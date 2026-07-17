namespace TownHall.UI.Services;

// Client-side implementations of the shared interfaces: each simply routes to the SignalR hub via
// TownHallClient, mirroring the server-side services' API.

public sealed class RoomsClient(TownHallClient client) : IRooms
{
    public IAsyncEnumerable<RoomView?> Get(string roomId, CancellationToken cancellationToken = default)
        => client.Stream<RoomView?>("Get", cancellationToken, roomId);

    public IAsyncEnumerable<ImmutableArray<string>> ListRooms(int limit, CancellationToken cancellationToken = default)
        => client.Stream<ImmutableArray<string>>("ListRooms", cancellationToken, limit);

    public IAsyncEnumerable<LobbyView> GetLobby(int limit, CancellationToken cancellationToken = default)
        => client.Stream<LobbyView>("GetLobby", cancellationToken, limit);

    public IAsyncEnumerable<RoomCard?> GetCard(string roomId, CancellationToken cancellationToken = default)
        => client.Stream<RoomCard?>("GetCard", cancellationToken, roomId);

    public Task<string?> GetOwnerToken(string roomId, CancellationToken cancellationToken = default)
        => client.Invoke<string?>("GetOwnerToken", cancellationToken, roomId);

    public Task<Room> Create(Rooms_Create command, CancellationToken cancellationToken = default)
        => client.Invoke<Room>("Create", cancellationToken, command);

    public Task ClaimOwnership(Rooms_ClaimOwnership command, CancellationToken cancellationToken = default)
        => client.Invoke("ClaimOwnership", cancellationToken, command);

    public Task SetLive(Rooms_SetLive command, CancellationToken cancellationToken = default)
        => client.Invoke("SetLive", cancellationToken, command);

    public Task SetIsPrivate(Rooms_SetIsPrivate command, CancellationToken cancellationToken = default)
        => client.Invoke("SetIsPrivate", cancellationToken, command);

    public Task SetTitle(Rooms_SetTitle command, CancellationToken cancellationToken = default)
        => client.Invoke("SetTitle", cancellationToken, command);

    public Task SetLink(Rooms_SetLink command, CancellationToken cancellationToken = default)
        => client.Invoke("SetLink", cancellationToken, command);

    public Task SetDescription(Rooms_SetDescription command, CancellationToken cancellationToken = default)
        => client.Invoke("SetDescription", cancellationToken, command);

    public Task AdjustDuration(Rooms_AdjustDuration command, CancellationToken cancellationToken = default)
        => client.Invoke("AdjustDuration", cancellationToken, command);
}

public sealed class QuestionsClient(TownHallClient client) : IQuestions
{
    public IAsyncEnumerable<ImmutableArray<QuestionView>> ListOpen(string roomId, CancellationToken cancellationToken = default)
        => client.Stream<ImmutableArray<QuestionView>>("ListOpen", cancellationToken, roomId);

    public IAsyncEnumerable<ImmutableArray<QuestionView>> ListResolved(string roomId, CancellationToken cancellationToken = default)
        => client.Stream<ImmutableArray<QuestionView>>("ListResolved", cancellationToken, roomId);

    public Task<Question> Post(Questions_Post command, CancellationToken cancellationToken = default)
        => client.Invoke<Question>("Post", cancellationToken, command);

    public Task Vote(Questions_Vote command, CancellationToken cancellationToken = default)
        => client.Invoke("Vote", cancellationToken, command);

    public Task Resolve(Questions_Resolve command, CancellationToken cancellationToken = default)
        => client.Invoke("Resolve", cancellationToken, command);

    public Task Delete(Questions_Delete command, CancellationToken cancellationToken = default)
        => client.Invoke("Delete", cancellationToken, command);
}

public sealed class RoomStatsClient(TownHallClient client) : IRoomStats
{
    public IAsyncEnumerable<ImmutableArray<TrendingQuestion>> ListTrending(string roomId, int limit, CancellationToken cancellationToken = default)
        => client.Stream<ImmutableArray<TrendingQuestion>>("ListTrending", cancellationToken, roomId, limit);
}

public sealed class MoodClient(TownHallClient client) : IMood
{
    public IAsyncEnumerable<MoodView> GetSummary(string roomId, CancellationToken cancellationToken = default)
        => client.Stream<MoodView>("GetSummary", cancellationToken, roomId);

    public Task SetMood(Mood_Set command, CancellationToken cancellationToken = default)
        => client.Invoke("SetMood", cancellationToken, command);
}

public sealed class UsersClient(TownHallClient client) : IUsers
{
    public IAsyncEnumerable<UserFull?> GetOwn(CancellationToken cancellationToken = default)
        => client.Stream<UserFull?>("GetOwn", cancellationToken);

    public Task SetName(Users_SetName command, CancellationToken cancellationToken = default)
        => client.Invoke("SetName", cancellationToken, command);
}

public sealed class AuthClient(TownHallClient client) : IAuth
{
    public Task<string> GetRegistrationOptions(string name, CancellationToken cancellationToken = default)
        => client.Invoke<string>("GetRegistrationOptions", cancellationToken, name);

    public Task<string> GetSignInOptions(CancellationToken cancellationToken = default)
        => client.Invoke<string>("GetSignInOptions", cancellationToken);

    public Task<UserFull> RegisterPasskey(Auth_RegisterPasskey command, CancellationToken cancellationToken = default)
        => client.Invoke<UserFull>("RegisterPasskey", cancellationToken, command);

    public Task<UserFull> SignIn(Auth_SignIn command, CancellationToken cancellationToken = default)
        => client.Invoke<UserFull>("SignIn", cancellationToken, command);

    public Task SignOut(CancellationToken cancellationToken = default)
        => client.Invoke("SignOut", cancellationToken);
}

public sealed class PresenceClient(TownHallClient client) : IPresence
{
    public Task Watch(Presence_Watch command, CancellationToken cancellationToken = default)
        => client.Invoke("Watch", cancellationToken, command);
}
