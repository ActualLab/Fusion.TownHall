using MessagePack;

namespace TownHall;

public interface IParticipants : IComputeService
{
    [ComputeMethod]
    Task<ParticipantInfo> GetOwn(Session session, CancellationToken cancellationToken = default);

    // The display name behind a public participant id; shared across sessions (no Session parameter),
    // so it's cached once and invalidated for everyone when the owner renames.
    [ComputeMethod]
    Task<string> GetName(string participantId, CancellationToken cancellationToken = default);

    // Trimmed length 1..30. Renames the participant everywhere it's shown (GetOwn + GetName).
    [CommandHandler]
    Task OnSetName(Participants_SetName command, CancellationToken cancellationToken = default);
}

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Participants_SetName(
    Session Session,
    string Name
) : ISessionCommand<Unit>;
