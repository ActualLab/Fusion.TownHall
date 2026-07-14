using MessagePack;

namespace TownHall;

public interface IParticipants : IComputeService
{
    [ComputeMethod]
    Task<ParticipantInfo> GetOwn(Session session, CancellationToken cancellationToken = default);

    // Trimmed length 1..30. Does NOT rewrite AuthorName snapshots on already-posted questions.
    [CommandHandler]
    Task OnSetName(Participants_SetName command, CancellationToken cancellationToken = default);
}

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Participants_SetName(
    Session Session,
    string Name
) : ISessionCommand<Unit>;
