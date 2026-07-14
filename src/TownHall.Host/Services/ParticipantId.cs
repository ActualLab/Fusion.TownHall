namespace TownHall.Host.Services;

/// <summary>
/// Derives a public, stable participant id from a session. The raw session id authorizes votes,
/// moods and ownership, so it must never leave the server; this hash can be shared freely.
/// </summary>
public static class ParticipantId
{
    public static string Of(Session session)
    {
        string sessionId = session.Id;
        return sessionId.GetXxHash3L().ToString("x16");
    }
}
