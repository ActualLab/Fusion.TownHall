namespace TownHall.Host.Services;

// The current caller's identity, bound to a hub connection and captured by the per-connection service
// instances. SessionId is the server-only secret; the frontend services resolve it to a user id.
public sealed record Identity(string SessionId)
{
    public static Identity Of(string sessionId) => new(sessionId);
}
