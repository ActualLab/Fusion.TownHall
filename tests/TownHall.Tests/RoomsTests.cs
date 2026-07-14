namespace TownHall.Tests;

public abstract class RoomsTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task OnCreateReturnsPausedRoomWithClampedDuration()
    {
        var session = Session.New();
        var room = await Call(new Rooms_Create(session, "  Board Q&A  ", TimeSpan.FromSeconds(1)));
        Assert.Equal(RoomStatus.Paused, room.Status);
        Assert.Equal("Board Q&A", room.Title);
        Assert.Equal(TimeSpan.FromMinutes(5), room.EndsAt - room.CreatedAt);
        Assert.Equal(room, await Rooms.Get(session, room.Id));
        Assert.Contains(room.Id, await Rooms.ListActive(session));
    }

    [Fact]
    public async Task OwnershipIsPerSession()
    {
        var owner = Session.New();
        var other = Session.New();
        var room = await Call(new Rooms_Create(owner, "Ownership", TimeSpan.FromHours(1)));
        Assert.True(await Rooms.IsOwner(owner, room.Id));
        Assert.NotNull(await Rooms.GetOwnerToken(owner, room.Id));
        Assert.False(await Rooms.IsOwner(other, room.Id));
        Assert.Null(await Rooms.GetOwnerToken(other, room.Id));
    }

    [Fact]
    public async Task OnClaimOwnershipValidatesToken()
    {
        var owner = Session.New();
        var claimer = Session.New();
        var room = await Call(new Rooms_Create(owner, "Claim", TimeSpan.FromHours(1)));
        var token = await Rooms.GetOwnerToken(owner, room.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Call(new Rooms_ClaimOwnership(claimer, room.Id, "wrong-token")));
        await Call(new Rooms_ClaimOwnership(claimer, room.Id, token!));
        Assert.True(await Rooms.IsOwner(claimer, room.Id));
    }

    [Fact]
    public async Task OnSetIsPrivateHidesRoomFromList()
    {
        var owner = Session.New();
        var other = Session.New();
        var room = await Call(new Rooms_Create(owner, "Private", TimeSpan.FromHours(1), IsPrivate: true));
        Assert.True(room.IsPrivate);
        Assert.DoesNotContain(room.Id, await Rooms.ListActive(owner));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Call(new Rooms_SetIsPrivate(other, room.Id, false)));
        await Call(new Rooms_SetIsPrivate(owner, room.Id, false));
        var ids = await ReadWhen(() => Rooms.ListActive(owner), x => x.Contains(room.Id));
        Assert.Contains(room.Id, ids);
        var updated = await ReadWhen(() => Rooms.Get(owner, room.Id), r => r?.IsPrivate == false);
        Assert.False(updated!.IsPrivate);
    }

    [Fact]
    public async Task OnSetTitleRenamesRoom()
    {
        var owner = Session.New();
        var other = Session.New();
        var room = await Call(new Rooms_Create(owner, "Old title", TimeSpan.FromHours(1)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Call(new Rooms_SetTitle(other, room.Id, "Hijacked")));
        await Assert.ThrowsAsync<ArgumentException>(
            () => Call(new Rooms_SetTitle(owner, room.Id, "  ")));
        await Call(new Rooms_SetTitle(owner, room.Id, "  New title "));
        Assert.Equal("New title", (await Rooms.Get(owner, room.Id))!.Title);
    }

    [Fact]
    public async Task OnAdjustDurationShiftsAndClampsEndsAt()
    {
        var owner = Session.New();
        var room = await Call(new Rooms_Create(owner, "Duration", TimeSpan.FromHours(1)));
        await Call(new Rooms_AdjustDuration(owner, room.Id, TimeSpan.FromMinutes(5)));
        var extended = await ReadWhen(() => Rooms.Get(owner, room.Id),
            r => r!.EndsAt == room.EndsAt + TimeSpan.FromMinutes(5));
        Assert.Equal(room.EndsAt + TimeSpan.FromMinutes(5), extended!.EndsAt);
        await Call(new Rooms_AdjustDuration(owner, room.Id, TimeSpan.FromHours(100)));
        var clamped = await ReadWhen(() => Rooms.Get(owner, room.Id),
            r => r!.EndsAt == room.CreatedAt + TimeSpan.FromHours(24));
        Assert.Equal(room.CreatedAt + TimeSpan.FromHours(24), clamped!.EndsAt);
    }

    [Fact]
    public async Task OnAdjustDurationResurrectsJustEndedRoom()
    {
        var owner = Session.New();
        var room = await Call(new Rooms_Create(owner, "Grace", TimeSpan.FromHours(1)));
        await Call(new Rooms_SetLive(owner, room.Id, true)); // Only a running hall can end
        await Call(new Rooms_AdjustDuration(owner, room.Id, TimeSpan.FromHours(-2)));
        var ended = await ReadWhen(() => Rooms.Get(owner, room.Id), r => r!.Status == RoomStatus.Ended);
        Assert.Equal(RoomStatus.Ended, ended!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Call(new Rooms_AdjustDuration(owner, room.Id, TimeSpan.FromMinutes(-5))));
        await Call(new Rooms_AdjustDuration(owner, room.Id, TimeSpan.FromMinutes(5)));
        var revived = await ReadWhen(() => Rooms.Get(owner, room.Id), r => r!.Status != RoomStatus.Ended);
        Assert.NotEqual(RoomStatus.Ended, revived!.Status);
        Assert.True(revived.EndsAt > ended.EndsAt);
    }

    [Fact]
    public async Task OnSetLiveRequiresOwner()
    {
        var owner = Session.New();
        var other = Session.New();
        var room = await Call(new Rooms_Create(owner, "Live", TimeSpan.FromHours(1)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Call(new Rooms_SetLive(other, room.Id, true)));
        await Call(new Rooms_SetLive(owner, room.Id, true));
        var live = await Rooms.Get(owner, room.Id);
        Assert.Equal(RoomStatus.Live, live!.Status);
    }

    [Fact]
    public async Task PauseFreezesTheTimerAndResumeShiftsEndsAt()
    {
        var owner = Session.New();
        // Created Paused (not started): the timer is frozen at the full duration
        var room = await Call(new Rooms_Create(owner, "Timer", TimeSpan.FromHours(1)));
        Assert.Equal(RoomStatus.Paused, room.Status);
        Assert.NotNull(room.PausedAt);
        var pausedRemaining = room.EndsAt - room.PausedAt!.Value;
        Assert.True(pausedRemaining > TimeSpan.FromMinutes(59) && pausedRemaining <= TimeSpan.FromHours(1));

        // Resume → Live, PausedAt cleared, ~full duration still ahead
        await Call(new Rooms_SetLive(owner, room.Id, true));
        var live = await ReadWhen(() => Rooms.Get(owner, room.Id), r => r!.Status == RoomStatus.Live);
        Assert.Null(live!.PausedAt);
        Assert.True(live.EndsAt - Moment.Now > TimeSpan.FromMinutes(59));

        // Pause again → the remaining time is frozen at EndsAt - PausedAt
        await Call(new Rooms_SetLive(owner, room.Id, false));
        var paused = await ReadWhen(() => Rooms.Get(owner, room.Id), r => r!.Status == RoomStatus.Paused);
        Assert.NotNull(paused!.PausedAt);
        Assert.True(paused.EndsAt - paused.PausedAt!.Value > TimeSpan.FromMinutes(59));
    }

    [Fact]
    public async Task OnCreateStoresLinkAndSingleParagraphDescription()
    {
        var owner = Session.New();
        var room = await Call(new Rooms_Create(owner, "Event", TimeSpan.FromHours(1),
            Link: " https://zoom.us/j/123 ", Description: "  Line one\n\n  line two  "));
        Assert.Equal("https://zoom.us/j/123", room.Link);
        Assert.Equal("Line one line two", room.Description);
        Assert.Equal(room, await Rooms.Get(owner, room.Id));
        await Assert.ThrowsAsync<ArgumentException>(
            () => Call(new Rooms_Create(owner, "Bad", TimeSpan.FromHours(1), Link: "not-a-url")));
    }

    [Fact]
    public async Task TitleLinkAndDescriptionEditableAfterEnded()
    {
        var owner = Session.New();
        var other = Session.New();
        var room = await Call(new Rooms_Create(owner, "Editable", TimeSpan.FromHours(1)));
        await Call(new Rooms_SetLive(owner, room.Id, true)); // Only a running hall can end
        await Call(new Rooms_AdjustDuration(owner, room.Id, TimeSpan.FromHours(-2)));
        var ended = await ReadWhen(() => Rooms.Get(owner, room.Id), r => r!.Status == RoomStatus.Ended);
        Assert.Equal(RoomStatus.Ended, ended!.Status);

        await Call(new Rooms_SetTitle(owner, room.Id, "New title"));
        await Call(new Rooms_SetLink(owner, room.Id, "https://meet.google.com/abc"));
        await Call(new Rooms_SetDescription(owner, room.Id, "Added after the fact"));
        var updated = await ReadWhen(() => Rooms.Get(owner, room.Id),
            r => r!.Title == "New title" && r.Link.Length > 0 && r.Description.Length > 0);
        Assert.Equal("New title", updated!.Title);
        Assert.Equal("https://meet.google.com/abc", updated.Link);
        Assert.Equal("Added after the fact", updated.Description);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Call(new Rooms_SetLink(other, room.Id, "https://evil.example")));
    }
}

public sealed class RoomsServerTests(TestAppHost host) : RoomsTests(host)
{
    protected override IServiceProvider TestServices => Host.Services;
}

public sealed class RoomsClientTests(TestAppHost host) : RoomsTests(host)
{
    protected override IServiceProvider TestServices => Host.ClientServices;
}
