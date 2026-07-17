namespace TownHall.Tests;

public sealed class RoomsTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task CreateReturnsPausedRoomWithClampedDuration()
    {
        var session = await NewUser();
        var room = await For(session).Rooms.Create(new Rooms_Create("  Board Q&A  ", TimeSpan.FromSeconds(1)));
        Assert.Equal(RoomStatus.Paused, room.Status);
        Assert.Equal("Board Q&A", room.Title);
        Assert.Equal(TimeSpan.FromMinutes(5), room.EndsAt - room.CreatedAt);
        Assert.Equal(room, await GetRoom(session, room.Id));
        Assert.Contains(room.Id, await GetRoomIds(session));
    }

    [Fact]
    public async Task OwnershipIsPerSession()
    {
        var owner = await NewUser();
        var other = await NewUser();
        var room = await For(owner).Rooms.Create(new Rooms_Create("Ownership", TimeSpan.FromHours(1)));
        Assert.True((await GetView(owner, room.Id))!.IsOwner);
        Assert.NotNull(await For(owner).Rooms.GetOwnerToken(room.Id));
        Assert.False((await GetView(other, room.Id))!.IsOwner);
        Assert.Null(await For(other).Rooms.GetOwnerToken(room.Id));
    }

    [Fact]
    public async Task ClaimOwnershipValidatesToken()
    {
        var owner = await NewUser();
        var claimer = await NewUser();
        var room = await For(owner).Rooms.Create(new Rooms_Create("Claim", TimeSpan.FromHours(1)));
        var token = await For(owner).Rooms.GetOwnerToken(room.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => For(claimer).Rooms.ClaimOwnership(new Rooms_ClaimOwnership(room.Id, "wrong-token")));
        await For(claimer).Rooms.ClaimOwnership(new Rooms_ClaimOwnership(room.Id, token!));
        Assert.True((await GetView(claimer, room.Id))!.IsOwner);
    }

    [Fact]
    public async Task SetIsPrivateHidesRoomFromList()
    {
        var owner = await NewUser();
        var other = await NewUser();
        var room = await For(owner).Rooms.Create(new Rooms_Create("Private", TimeSpan.FromHours(1), IsPrivate: true));
        Assert.True(room.IsPrivate);
        Assert.DoesNotContain(room.Id, await GetRoomIds(owner));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => For(other).Rooms.SetIsPrivate(new Rooms_SetIsPrivate(room.Id, false)));
        await For(owner).Rooms.SetIsPrivate(new Rooms_SetIsPrivate(room.Id, false));
        Assert.Contains(room.Id, await GetRoomIds(owner));
        Assert.False((await GetRoom(owner, room.Id))!.IsPrivate);
    }

    [Fact]
    public async Task SetTitleRenamesRoom()
    {
        var owner = await NewUser();
        var other = await NewUser();
        var room = await For(owner).Rooms.Create(new Rooms_Create("Old title", TimeSpan.FromHours(1)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => For(other).Rooms.SetTitle(new Rooms_SetTitle(room.Id, "Hijacked")));
        await Assert.ThrowsAsync<ArgumentException>(
            () => For(owner).Rooms.SetTitle(new Rooms_SetTitle(room.Id, "  ")));
        await For(owner).Rooms.SetTitle(new Rooms_SetTitle(room.Id, "  New title "));
        Assert.Equal("New title", (await GetRoom(owner, room.Id))!.Title);
    }

    [Fact]
    public async Task AdjustDurationShiftsAndClampsEndsAt()
    {
        var owner = await NewUser();
        var room = await For(owner).Rooms.Create(new Rooms_Create("Duration", TimeSpan.FromHours(1)));
        await For(owner).Rooms.AdjustDuration(new Rooms_AdjustDuration(room.Id, TimeSpan.FromMinutes(5)));
        var extended = await GetRoom(owner, room.Id);
        Assert.Equal(room.EndsAt + TimeSpan.FromMinutes(5), extended!.EndsAt);
        await For(owner).Rooms.AdjustDuration(new Rooms_AdjustDuration(room.Id, TimeSpan.FromHours(100)));
        var clamped = await GetRoom(owner, room.Id);
        Assert.Equal(room.CreatedAt + TimeSpan.FromHours(24), clamped!.EndsAt);
    }

    [Fact]
    public async Task AdjustDurationResurrectsJustEndedRoom()
    {
        var owner = await NewUser();
        var room = await For(owner).Rooms.Create(new Rooms_Create("Grace", TimeSpan.FromHours(1)));
        await For(owner).Rooms.SetLive(new Rooms_SetLive(room.Id, true)); // Only a running hall can end
        await For(owner).Rooms.AdjustDuration(new Rooms_AdjustDuration(room.Id, TimeSpan.FromHours(-2)));
        var ended = await GetRoom(owner, room.Id);
        Assert.Equal(RoomStatus.Ended, ended!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => For(owner).Rooms.AdjustDuration(new Rooms_AdjustDuration(room.Id, TimeSpan.FromMinutes(-5))));
        await For(owner).Rooms.AdjustDuration(new Rooms_AdjustDuration(room.Id, TimeSpan.FromMinutes(5)));
        var revived = await GetRoom(owner, room.Id);
        Assert.NotEqual(RoomStatus.Ended, revived!.Status);
        Assert.True(revived.EndsAt > ended.EndsAt);
    }

    [Fact]
    public async Task SetLiveRequiresOwner()
    {
        var owner = await NewUser();
        var other = await NewUser();
        var room = await For(owner).Rooms.Create(new Rooms_Create("Live", TimeSpan.FromHours(1)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => For(other).Rooms.SetLive(new Rooms_SetLive(room.Id, true)));
        await For(owner).Rooms.SetLive(new Rooms_SetLive(room.Id, true));
        var live = await GetRoom(owner, room.Id);
        Assert.Equal(RoomStatus.Live, live!.Status);
    }

    [Fact]
    public async Task PauseFreezesTheTimerAndResumeShiftsEndsAt()
    {
        var owner = await NewUser();
        // Created Paused (not started): the timer is frozen at the full duration
        var room = await For(owner).Rooms.Create(new Rooms_Create("Timer", TimeSpan.FromHours(1)));
        Assert.Equal(RoomStatus.Paused, room.Status);
        Assert.NotNull(room.PausedAt);
        var pausedRemaining = room.EndsAt - room.PausedAt!.Value;
        Assert.True(pausedRemaining > TimeSpan.FromMinutes(59) && pausedRemaining <= TimeSpan.FromHours(1));

        // Resume → Live, PausedAt cleared, ~full duration still ahead
        await For(owner).Rooms.SetLive(new Rooms_SetLive(room.Id, true));
        var live = await GetRoom(owner, room.Id);
        Assert.Equal(RoomStatus.Live, live!.Status);
        Assert.Null(live.PausedAt);
        Assert.True(live.EndsAt - Moment.Now > TimeSpan.FromMinutes(59));

        // Pause again → the remaining time is frozen at EndsAt - PausedAt
        await For(owner).Rooms.SetLive(new Rooms_SetLive(room.Id, false));
        var paused = await GetRoom(owner, room.Id);
        Assert.Equal(RoomStatus.Paused, paused!.Status);
        Assert.NotNull(paused.PausedAt);
        Assert.True(paused.EndsAt - paused.PausedAt!.Value > TimeSpan.FromMinutes(59));
    }

    [Fact]
    public async Task CreateStoresLinkAndSingleParagraphDescription()
    {
        var owner = await NewUser();
        var room = await For(owner).Rooms.Create(new Rooms_Create("Event", TimeSpan.FromHours(1),
            Link: " https://zoom.us/j/123 ", Description: "  Line one\n\n  line two  "));
        Assert.Equal("https://zoom.us/j/123", room.Link);
        Assert.Equal("Line one line two", room.Description);
        Assert.Equal(room, await GetRoom(owner, room.Id));
        await Assert.ThrowsAsync<ArgumentException>(
            () => For(owner).Rooms.Create(new Rooms_Create("Bad", TimeSpan.FromHours(1), Link: "not-a-url")));
    }

    [Fact]
    public async Task TitleLinkAndDescriptionEditableAfterEnded()
    {
        var owner = await NewUser();
        var other = await NewUser();
        var room = await For(owner).Rooms.Create(new Rooms_Create("Editable", TimeSpan.FromHours(1)));
        await For(owner).Rooms.SetLive(new Rooms_SetLive(room.Id, true)); // Only a running hall can end
        await For(owner).Rooms.AdjustDuration(new Rooms_AdjustDuration(room.Id, TimeSpan.FromHours(-2)));
        var ended = await GetRoom(owner, room.Id);
        Assert.Equal(RoomStatus.Ended, ended!.Status);

        await For(owner).Rooms.SetTitle(new Rooms_SetTitle(room.Id, "New title"));
        await For(owner).Rooms.SetLink(new Rooms_SetLink(room.Id, "https://meet.google.com/abc"));
        await For(owner).Rooms.SetDescription(new Rooms_SetDescription(room.Id, "Added after the fact"));
        var updated = await GetRoom(owner, room.Id);
        Assert.Equal("New title", updated!.Title);
        Assert.Equal("https://meet.google.com/abc", updated.Link);
        Assert.Equal("Added after the fact", updated.Description);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => For(other).Rooms.SetLink(new Rooms_SetLink(room.Id, "https://evil.example")));
    }
}
