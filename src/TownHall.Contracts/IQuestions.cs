using MessagePack;

namespace TownHall;

public interface IQuestions : IComputeService
{
    // Returns deleted questions as null, like never-existed ones
    [ComputeMethod]
    Task<Question?> Get(Session session, string roomId, long index, CancellationToken cancellationToken = default);

    // Open questions, newest first ("Recent" tab)
    [ComputeMethod]
    Task<ImmutableArray<long>> ListOpenIds(Session session, string roomId, CancellationToken cancellationToken = default);

    // Open questions sorted by active vote count desc, ties older-first ("Top" tab)
    [ComputeMethod]
    Task<ImmutableArray<long>> GetTopOpenIds(Session session, string roomId, int limit, CancellationToken cancellationToken = default);

    // Resolved questions, most recently resolved first ("Resolved" tab)
    [ComputeMethod]
    Task<ImmutableArray<long>> ListResolvedIds(Session session, string roomId, CancellationToken cancellationToken = default);

    // null for Open questions
    [ComputeMethod]
    Task<Resolution?> GetResolution(Session session, string roomId, long index, CancellationToken cancellationToken = default);

    [ComputeMethod]
    Task<int> GetVoteCount(Session session, string roomId, long index, CancellationToken cancellationToken = default);

    [ComputeMethod]
    Task<bool> HasOwnVote(Session session, string roomId, long index, CancellationToken cancellationToken = default);

    // Requires room Live. Text: trimmed 1..500 chars.
    [CommandHandler]
    Task<Question> OnPost(Questions_Post command, CancellationToken cancellationToken = default);

    // Requires room Live and question Open. Sets (Value=true) or clears (Value=false) this session's
    // vote. Idempotent: re-setting an existing vote refreshes its CastAt; clearing a non-existent
    // vote is a no-op. One vote per (session, question); voting for your own question is allowed.
    [CommandHandler]
    Task OnVote(Questions_Vote command, CancellationToken cancellationToken = default);

    // Owner-only, allowed while Paused or Live, rejected once Ended. Marks Open -> Resolved with
    // an optional note (trimmed 0..500 chars); resolving a Resolved question overwrites the note.
    [CommandHandler]
    Task OnResolve(Questions_Resolve command, CancellationToken cancellationToken = default);

    // Owner-only, same lifecycle gating as OnResolve. Hard delete: the question, its votes, and
    // its resolution disappear. Idempotent.
    [CommandHandler]
    Task OnDelete(Questions_Delete command, CancellationToken cancellationToken = default);
}

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Questions_Post(
    Session Session,
    string RoomId,
    string Text
) : ISessionCommand<Question>;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Questions_Vote(
    Session Session,
    string RoomId,
    long QuestionIndex,
    bool Value
) : ISessionCommand<Unit>;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Questions_Resolve(
    Session Session,
    string RoomId,
    long QuestionIndex,
    string Note
) : ISessionCommand<Unit>;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record Questions_Delete(
    Session Session,
    string RoomId,
    long QuestionIndex
) : ISessionCommand<Unit>;
