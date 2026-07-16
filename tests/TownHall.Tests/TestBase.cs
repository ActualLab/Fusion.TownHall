namespace TownHall.Tests;

/// <summary>
/// Base for test classes that run the same test methods against one of two access points:
/// the server DI container or a Fusion RPC client container.
/// </summary>
public abstract class TestBase(TestAppHost host) : IClassFixture<TestAppHost>
{
    protected static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    protected TestAppHost Host { get; } = host;
    protected abstract IServiceProvider TestServices { get; }
    protected IUsers Users => TestServices.GetRequiredService<IUsers>();
    protected IRooms Rooms => TestServices.GetRequiredService<IRooms>();
    protected IQuestions Questions => TestServices.GetRequiredService<IQuestions>();
    protected IRoomStats RoomStats => TestServices.GetRequiredService<IRoomStats>();
    protected IPresence Presence => TestServices.GetRequiredService<IPresence>();
    protected IMood Mood => TestServices.GetRequiredService<IMood>();
    protected ICommander Commander => TestServices.Commander();

    // Server-side command service proxies allow commands only via ICommander,
    // so tests use Call() at both access levels.
    protected Task<TResult> Call<TResult>(ICommand<TResult> command)
        => Commander.Call(command);

    // Signs a session in (guests can't act). Uses the server-side backend directly - there's no
    // headless passkey ceremony - so it works for both the server- and client-container test variants.
    protected async Task<string> SignIn(Session session, string name = "")
    {
        var serverCommander = Host.Services.Commander();
        var userId = await serverCommander.Call(new UsersBackend_Create(name));
        await serverCommander.Call(new UsersBackend_LinkSession(session.Id, userId));
        return userId;
    }

    // A fresh, signed-in session (the common case: a real participant).
    protected async Task<Session> NewUser(string name = "")
    {
        var session = Session.New();
        await SignIn(session, name);
        return session;
    }

    protected async Task<Room> CreateRoom(Session session, bool live = true)
    {
        var room = await Call(new Rooms_Create(session, "Test Room", TimeSpan.FromHours(1)));
        if (live)
            await Call(new Rooms_SetLive(session, room.Id, true));
        return room;
    }

    protected static async Task<T> ReadWhen<T>(Func<Task<T>> read, Func<T, bool> predicate)
    {
        // Waits till the computed value of read() satisfies the predicate; this absorbs the
        // client-side invalidation propagation delay when a cached value is re-read after a command
        using var cts = new CancellationTokenSource(WaitTimeout);
        var computed = await Computed.Capture(read, cts.Token);
        computed = await computed.When(predicate, cts.Token);
        return computed.Value;
    }
}
