namespace TownHall;

// Backend questions + votes store. Id-based; the frontend enforces sign-in / room-live / ownership and
// fills in the caller's own-vote flag (ReadList leaves QuestionView.HasOwnVote false).
public interface IQuestionsBackend
{
    Task<ImmutableArray<QuestionView>> ReadList(string roomId, bool resolved, CancellationToken cancellationToken = default);

    Task<Question> Post(QuestionsBackend_Post command, CancellationToken cancellationToken = default);
    Task Vote(QuestionsBackend_Vote command, CancellationToken cancellationToken = default);
    Task Resolve(QuestionsBackend_Resolve command, CancellationToken cancellationToken = default);
    Task Delete(QuestionsBackend_Delete command, CancellationToken cancellationToken = default);
}

// ReSharper disable once InconsistentNaming
public sealed record QuestionsBackend_Post(
    string RoomId,
    string AuthorUserId,
    string Text,
    bool Anonymous = false
);

// ReSharper disable once InconsistentNaming
public sealed record QuestionsBackend_Vote(
    string RoomId,
    long QuestionIndex,
    string UserId,
    bool Value
);

// ReSharper disable once InconsistentNaming
public sealed record QuestionsBackend_Resolve(
    string RoomId,
    long QuestionIndex,
    string Note
);

// ReSharper disable once InconsistentNaming
public sealed record QuestionsBackend_Delete(
    string RoomId,
    long QuestionIndex
);
