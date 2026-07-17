using TownHall.Host.Services;

namespace TownHall.Tests;

/// <summary>
/// Base for the service tests: helpers to spin up sessions and read the current value of a reactive
/// stream (each stream yields the latest committed state immediately on subscribe).
/// </summary>
public abstract class TestBase(TestAppHost host) : IClassFixture<TestAppHost>
{
    protected static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    protected TestAppHost Host { get; } = host;

    protected static string NewSession()
        => Guid.NewGuid().ToString("N");

    protected SessionServices For(string sessionId)
        => Host.For(sessionId);

    // Signs a session in (guests can't act). Uses the server-side backend directly - there's no
    // headless passkey ceremony.
    protected async Task<string> SignIn(string sessionId, string name = "")
    {
        var userId = await Host.Shared.Users.Create(new UsersBackend_Create(name));
        await Host.Shared.Users.LinkSession(new UsersBackend_LinkSession(sessionId, userId));
        return userId;
    }

    // A fresh, signed-in session (the common case: a real participant).
    protected async Task<string> NewUser(string name = "")
    {
        var sessionId = NewSession();
        await SignIn(sessionId, name);
        return sessionId;
    }

    protected async Task<Room> CreateRoom(string sessionId, bool live = true)
    {
        var svc = For(sessionId);
        var room = await svc.Rooms.Create(new Rooms_Create("Test Room", TimeSpan.FromHours(1)));
        if (live)
            await svc.Rooms.SetLive(new Rooms_SetLive(room.Id, true));
        return room;
    }

    protected static async Task<T> First<T>(IAsyncEnumerable<T> stream)
    {
        using var cts = new CancellationTokenSource(WaitTimeout);
        await foreach (var value in stream.WithCancellation(cts.Token))
            return value;
        throw new InvalidOperationException("Stream produced no value.");
    }

    // Current-value read helpers
    protected async Task<RoomView?> GetView(string s, string roomId)
        => await First(For(s).Rooms.Get(roomId));
    protected async Task<Room?> GetRoom(string s, string roomId)
        => (await GetView(s, roomId))?.Room;
    protected async Task<ImmutableArray<string>> GetRoomIds(string s, int limit = 1000)
        => await First(For(s).Rooms.ListRooms(limit));
    protected async Task<RoomCard?> GetRoomCard(string s, string roomId)
        => await First(For(s).Rooms.GetCard(roomId));
    protected async Task<ImmutableArray<QuestionView>> GetOpen(string s, string roomId)
        => await First(For(s).Questions.ListOpen(roomId));
    protected async Task<ImmutableArray<QuestionView>> GetResolved(string s, string roomId)
        => await First(For(s).Questions.ListResolved(roomId));
    protected async Task<QuestionView?> GetQuestion(string s, string roomId, long index)
    {
        var view = (await GetOpen(s, roomId)).FirstOrDefault(v => v.Question.Index == index)
            ?? (await GetResolved(s, roomId)).FirstOrDefault(v => v.Question.Index == index);
        return view;
    }
    protected async Task<MoodView> GetMood(string s, string roomId)
        => await First(For(s).Mood.GetSummary(roomId));
    protected async Task<ImmutableArray<TrendingQuestion>> GetTrending(string s, string roomId, int limit)
        => await First(For(s).RoomStats.ListTrending(roomId, limit));
    protected async Task<UserFull?> GetOwn(string s)
        => await First(For(s).Users.GetOwn());
    protected async Task<int> GetAudience(string s, string roomId)
        => (await GetView(s, roomId))!.Stats.AudienceCount;
}
