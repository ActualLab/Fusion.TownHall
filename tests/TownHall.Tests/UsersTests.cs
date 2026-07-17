namespace TownHall.Tests;

public abstract class UsersTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task GuestHasNoUser()
    {
        var session = Session.New();
        Assert.Null(await Users.GetOwn(session));
    }

    [Fact]
    public async Task SignInCreatesUserWithGeneratedName()
    {
        var session = await NewUser();
        var user = await Users.GetOwn(session);
        Assert.NotNull(user);
        Assert.Matches(@"^\w+ \w+$", user!.Name);
        // Get(userId) returns the public projection anyone can read
        Assert.Equal(user.Name, (await Users.Get(session, user.Id))!.Name);
    }

    [Fact]
    public async Task SetNameChangesName()
    {
        var session = await NewUser();
        await Call(new Users_SetName(session, "  Alice  "));
        var user = await ReadWhen(() => Users.GetOwn(session), u => u?.Name == "Alice");
        Assert.Equal("Alice", user!.Name);
        await Assert.ThrowsAsync<ArgumentException>(
            () => Call(new Users_SetName(session, "   ")));
        await Assert.ThrowsAsync<ArgumentException>(
            () => Call(new Users_SetName(session, new string('x', 31))));
    }

    [Fact]
    public async Task GuestCannotActButCanRead()
    {
        var guest = Session.New();
        var owner = await NewUser();
        var room = await CreateRoom(owner);

        // Every write path rejects a guest
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Call(new Users_SetName(guest, "Nope")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Call(new Rooms_Create(guest, "Nope", TimeSpan.FromHours(1))));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Call(new Questions_Post(guest, room.Id, "Nope?")));

        // A guest can read, and doesn't get counted as present
        Assert.NotNull(await Rooms.Get(guest, room.Id));
        await Call(new Presence_Watch(guest, room.Id));
        Assert.Equal(0, await Presence.GetAudienceCount(guest, room.Id));
    }
}

public sealed class UsersServerTests(TestAppHost host) : UsersTests(host)
{
    protected override IServiceProvider TestServices => Host.Services;
}

public sealed class UsersClientTests(TestAppHost host) : UsersTests(host)
{
    protected override IServiceProvider TestServices => Host.ClientServices;
}
