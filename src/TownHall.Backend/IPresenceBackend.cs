namespace TownHall;

// Backend presence tracker (host-local, in-memory - never touches the DB). Keyed by user id: only
// signed-in users are present, so the audience is the set of present users. Watch is a plain
// memory-only heartbeat (no operation log), so a busy room doesn't spam the DB.
public interface IPresenceBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<int> GetAudienceCount(string roomId, CancellationToken cancellationToken = default);

    [ComputeMethod(ConsolidationDelay = 0.1)]
    Task<PresentUsers> GetPresentUsers(string roomId);

    [ComputeMethod]
    Task<PresentUsers> GetPresentUsersRaw(string roomId);

    Task Watch(string roomId, string userId);
}

// The set of user ids present in a room, with value (sequence) equality so a steady stream of
// heartbeats that doesn't change the set is swallowed by GetPresentUsers' consolidation.
public sealed class PresentUsers(string[] userIds) : IEquatable<PresentUsers>
{
    public static readonly PresentUsers Empty = new([]);

    public string[] UserIds { get; } = userIds;
    public int Count => UserIds.Length;

    public bool Contains(string userId) => Array.IndexOf(UserIds, userId) >= 0;

    public bool Equals(PresentUsers? other)
        => other is not null && UserIds.AsSpan().SequenceEqual(other.UserIds);
    public override bool Equals(object? obj) => Equals(obj as PresentUsers);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var id in UserIds)
            hash.Add(id, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
