namespace TownHall.Tests;

public sealed class UsersTests(TestAppHost host) : TestBase(host)
{
    [Fact]
    public async Task GuestHasNoUser()
    {
        var session = NewSession();
        Assert.Null(await GetOwn(session));
    }

    [Fact]
    public async Task SignInCreatesUserWithGeneratedName()
    {
        var session = await NewUser();
        var user = await GetOwn(session);
        Assert.NotNull(user);
        Assert.Matches(@"^\w+ \w+$", user!.Name);
    }

    [Fact]
    public async Task SetNameChangesName()
    {
        var session = await NewUser();
        await For(session).Users.SetName(new Users_SetName("  Alice  "));
        var user = await GetOwn(session);
        Assert.Equal("Alice", user!.Name);
        await Assert.ThrowsAsync<ArgumentException>(
            () => For(session).Users.SetName(new Users_SetName("   ")));
        await Assert.ThrowsAsync<ArgumentException>(
            () => For(session).Users.SetName(new Users_SetName(new string('x', 31))));
    }

    [Fact]
    public async Task GuestCannotActButCanRead()
    {
        var guest = NewSession();
        var owner = await NewUser();
        var room = await CreateRoom(owner);

        // Every write path rejects a guest
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => For(guest).Users.SetName(new Users_SetName("Nope")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => For(guest).Rooms.Create(new Rooms_Create("Nope", TimeSpan.FromHours(1))));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => For(guest).Questions.Post(new Questions_Post(room.Id, "Nope?")));

        // A guest can read, and doesn't get counted as present
        Assert.NotNull(await GetRoom(guest, room.Id));
        await For(guest).Presence.Watch(new Presence_Watch(room.Id));
        Assert.Equal(0, await GetAudience(guest, room.Id));
    }
}
