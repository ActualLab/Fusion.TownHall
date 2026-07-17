using Microsoft.AspNetCore.SignalR;
using TownHall.Host.Services;

namespace TownHall.Host;

// The single SignalR endpoint the client talks to. Reactive reads are server-to-client streams
// (IAsyncEnumerable); commands are one-shot invocations. Identity is resolved from the connection
// (session cookie, or a ?session= override outside Production) and bound to the per-call services -
// the raw session id never travels to the client.
public sealed class TownHallHub(ServerShared shared, IHostEnvironment env) : Hub
{
    public const string SessionCookieName = "th_sess";

    public override Task OnConnectedAsync()
    {
        Context.Items[nameof(Identity)] = Identity.Of(ResolveSessionId(Context.GetHttpContext(), env));
        return base.OnConnectedAsync();
    }

    // Reactive reads (server-to-client streams)
    public IAsyncEnumerable<RoomView?> Get(string roomId, CancellationToken cancellationToken)
        => Services().Rooms.Get(roomId, cancellationToken);
    public IAsyncEnumerable<ImmutableArray<string>> ListRooms(int limit, CancellationToken cancellationToken)
        => Services().Rooms.ListRooms(limit, cancellationToken);
    public IAsyncEnumerable<LobbyView> GetLobby(int limit, CancellationToken cancellationToken)
        => Services().Rooms.GetLobby(limit, cancellationToken);
    public IAsyncEnumerable<RoomCard?> GetCard(string roomId, CancellationToken cancellationToken)
        => Services().Rooms.GetCard(roomId, cancellationToken);
    public IAsyncEnumerable<ImmutableArray<QuestionView>> ListOpen(string roomId, CancellationToken cancellationToken)
        => Services().Questions.ListOpen(roomId, cancellationToken);
    public IAsyncEnumerable<ImmutableArray<QuestionView>> ListResolved(string roomId, CancellationToken cancellationToken)
        => Services().Questions.ListResolved(roomId, cancellationToken);
    public IAsyncEnumerable<ImmutableArray<TrendingQuestion>> ListTrending(string roomId, int limit, CancellationToken cancellationToken)
        => Services().RoomStats.ListTrending(roomId, limit, cancellationToken);
    public IAsyncEnumerable<MoodView> GetSummary(string roomId, CancellationToken cancellationToken)
        => Services().Mood.GetSummary(roomId, cancellationToken);
    public IAsyncEnumerable<UserFull?> GetOwn(CancellationToken cancellationToken)
        => Services().Users.GetOwn(cancellationToken);

    // One-shot read
    public Task<string?> GetOwnerToken(string roomId)
        => Services().Rooms.GetOwnerToken(roomId);

    // Commands
    public Task<Room> Create(Rooms_Create command)
        => Services().Rooms.Create(command);
    public Task ClaimOwnership(Rooms_ClaimOwnership command)
        => Services().Rooms.ClaimOwnership(command);
    public Task SetLive(Rooms_SetLive command)
        => Services().Rooms.SetLive(command);
    public Task SetIsPrivate(Rooms_SetIsPrivate command)
        => Services().Rooms.SetIsPrivate(command);
    public Task SetTitle(Rooms_SetTitle command)
        => Services().Rooms.SetTitle(command);
    public Task SetLink(Rooms_SetLink command)
        => Services().Rooms.SetLink(command);
    public Task SetDescription(Rooms_SetDescription command)
        => Services().Rooms.SetDescription(command);
    public Task AdjustDuration(Rooms_AdjustDuration command)
        => Services().Rooms.AdjustDuration(command);
    public Task<Question> Post(Questions_Post command)
        => Services().Questions.Post(command);
    public Task Vote(Questions_Vote command)
        => Services().Questions.Vote(command);
    public Task Resolve(Questions_Resolve command)
        => Services().Questions.Resolve(command);
    public Task Delete(Questions_Delete command)
        => Services().Questions.Delete(command);
    public Task SetMood(Mood_Set command)
        => Services().Mood.SetMood(command);
    public Task SetName(Users_SetName command)
        => Services().Users.SetName(command);
    public Task Watch(Presence_Watch command)
        => Services().Presence.Watch(command);

    // Passkey (WebAuthn) authentication
    public Task<string> GetRegistrationOptions(string name)
        => Services().Auth.GetRegistrationOptions(name);
    public Task<string> GetSignInOptions()
        => Services().Auth.GetSignInOptions();
    public Task<UserFull> RegisterPasskey(Auth_RegisterPasskey command)
        => Services().Auth.RegisterPasskey(command);
    public Task<UserFull> SignIn(Auth_SignIn command)
        => Services().Auth.SignIn(command);
    public Task SignOut()
        => Services().Auth.SignOut();

    // Private methods

    private SessionServices Services()
        => new(shared, (Identity)Context.Items[nameof(Identity)]!);

    private static string ResolveSessionId(HttpContext? httpContext, IHostEnvironment env)
    {
        if (httpContext == null)
            return Guid.NewGuid().ToString("N");

        // A ?session=<id> override lets one browser act as several users while testing (disabled in Production)
        if (!env.IsProduction()) {
            var overrideId = httpContext.Request.Query["session"].ToString();
            if (overrideId.Length >= 8)
                return overrideId;
        }
        var cookie = httpContext.Request.Cookies[SessionCookieName];
        return cookie is { Length: >= 8 } ? cookie : Guid.NewGuid().ToString("N");
    }
}
