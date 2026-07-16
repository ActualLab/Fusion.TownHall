using MessagePack;

namespace TownHall;

// Backend questions + votes store. Id-based; the frontend enforces sign-in / room-live / ownership.
public interface IQuestionsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Question?> Get(string roomId, long index, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<ImmutableArray<long>> ListOpen(string roomId, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<ImmutableArray<long>> ListTopOpen(string roomId, int limit, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<ImmutableArray<long>> ListResolved(string roomId, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<Resolution?> GetResolution(string roomId, long index, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<int> GetVoteCount(string roomId, long index, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<bool> HasVote(string roomId, long index, string userId, CancellationToken cancellationToken = default);
    // A pseudo dependency invalidated by any vote change in a room; trending / total-vote reads depend on it.
    [ComputeMethod]
    Task<Unit> PseudoVotes(string roomId, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task<Question> OnPost(QuestionsBackend_Post command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnVote(QuestionsBackend_Vote command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnResolve(QuestionsBackend_Resolve command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnDelete(QuestionsBackend_Delete command, CancellationToken cancellationToken = default);
}

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record QuestionsBackend_Post(
    string RoomId,
    string AuthorUserId,
    string Text,
    bool Anonymous = false
) : ICommand<Question>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record QuestionsBackend_Vote(
    string RoomId,
    long QuestionIndex,
    string UserId,
    bool Value
) : ICommand<Unit>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record QuestionsBackend_Resolve(
    string RoomId,
    long QuestionIndex,
    string Note
) : ICommand<Unit>, IBackendCommand;

[MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed record QuestionsBackend_Delete(
    string RoomId,
    long QuestionIndex
) : ICommand<Unit>, IBackendCommand;
