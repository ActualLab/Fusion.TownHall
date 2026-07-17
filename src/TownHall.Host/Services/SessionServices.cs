namespace TownHall.Host.Services;

// The server-side frontend services bound to one connection's identity. The hub builds this per call
// from the shared singletons and the connection's resolved identity, then delegates to it.
public sealed class SessionServices(ServerShared shared, Identity identity)
{
    public RoomsService Rooms { get; } = new(shared, identity);
    public QuestionsService Questions { get; } = new(shared, identity);
    public RoomStatsService RoomStats { get; } = new(shared, identity);
    public MoodService Mood { get; } = new(shared, identity);
    public UsersService Users { get; } = new(shared, identity);
    public AuthService Auth { get; } = new(shared, identity);
    public PresenceService Presence { get; } = new(shared, identity);
}
