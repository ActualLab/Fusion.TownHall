namespace TownHall;

public interface IQuestions
{
    // Open questions with their live vote counts, own-vote flags and author names. The UI orders this
    // set for both the "Recent" (newest first) and "Top" (most votes first) tabs from one stream.
    IAsyncEnumerable<ImmutableArray<QuestionView>> ListOpen(string roomId, CancellationToken cancellationToken = default);

    // Resolved questions, most recently resolved first, each carrying its Resolution
    IAsyncEnumerable<ImmutableArray<QuestionView>> ListResolved(string roomId, CancellationToken cancellationToken = default);

    // Requires room Live. Text: trimmed 1..500 chars. When Anonymous, the question is attributed to a
    // per-(user, room) pseudonym instead of the poster's real user (see AnonId).
    Task<Question> Post(Questions_Post command, CancellationToken cancellationToken = default);

    // Requires room Live and question Open. Sets (Value=true) or clears (Value=false) this session's
    // vote. Idempotent: re-setting an existing vote refreshes its CastAt; clearing a non-existent
    // vote is a no-op. One vote per (session, question); voting for your own question is allowed.
    Task Vote(Questions_Vote command, CancellationToken cancellationToken = default);

    // Owner-only. Marks Open -> Resolved with an optional single-paragraph note (<=500 chars);
    // re-resolving overwrites the note but preserves the original resolution time. Allowed even
    // after Ended, so notes stay editable (one owner resolves, another can add/edit the note later).
    Task Resolve(Questions_Resolve command, CancellationToken cancellationToken = default);

    // Owner-only, allowed while Paused or Live, rejected once Ended. Hard delete: the question,
    // its votes, and its resolution disappear. Idempotent.
    Task Delete(Questions_Delete command, CancellationToken cancellationToken = default);
}

// ReSharper disable once InconsistentNaming
public sealed record Questions_Post(
    string RoomId,
    string Text,
    bool Anonymous = false
);

// ReSharper disable once InconsistentNaming
public sealed record Questions_Vote(
    string RoomId,
    long QuestionIndex,
    bool Value
);

// ReSharper disable once InconsistentNaming
public sealed record Questions_Resolve(
    string RoomId,
    long QuestionIndex,
    string Note
);

// ReSharper disable once InconsistentNaming
public sealed record Questions_Delete(
    string RoomId,
    long QuestionIndex
);
