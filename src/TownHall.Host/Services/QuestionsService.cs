using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

// Frontend questions service: reads open to guests; posting/voting require sign-in; resolve/delete
// require room ownership. Argument + room-status validation lives in IQuestionsBackend. Adds the
// caller's own-vote flag onto the backend's list.
public sealed class QuestionsService(ServerShared shared, Identity identity)
    : ServerService(shared, identity), IQuestions
{
    public IAsyncEnumerable<ImmutableArray<QuestionView>> ListOpen(string roomId, CancellationToken cancellationToken = default)
        => Stream([$"room:{roomId}", SessionScope], ct => ReadList(roomId, resolved: false, ct), cancellationToken);

    public IAsyncEnumerable<ImmutableArray<QuestionView>> ListResolved(string roomId, CancellationToken cancellationToken = default)
        => Stream([$"room:{roomId}", SessionScope], ct => ReadList(roomId, resolved: true, ct), cancellationToken);

    public async Task<Question> Post(Questions_Post command, CancellationToken cancellationToken = default)
    {
        var (roomId, text, anonymous) = command;
        var userId = await RequireUserId(cancellationToken).ConfigureAwait(false);
        return await Shared.Questions
            .Post(new QuestionsBackend_Post(roomId, userId, text, anonymous), cancellationToken).ConfigureAwait(false);
    }

    public async Task Vote(Questions_Vote command, CancellationToken cancellationToken = default)
    {
        var (roomId, index, value) = command;
        var userId = await RequireUserId(cancellationToken).ConfigureAwait(false);
        await Shared.Questions.Vote(new QuestionsBackend_Vote(roomId, index, userId, value), cancellationToken).ConfigureAwait(false);
    }

    public async Task Resolve(Questions_Resolve command, CancellationToken cancellationToken = default)
    {
        var (roomId, index, note) = command;
        await RequireOwner(roomId, cancellationToken).ConfigureAwait(false);
        await Shared.Questions.Resolve(new QuestionsBackend_Resolve(roomId, index, note), cancellationToken).ConfigureAwait(false);
    }

    public async Task Delete(Questions_Delete command, CancellationToken cancellationToken = default)
    {
        var (roomId, index) = command;
        await RequireOwner(roomId, cancellationToken).ConfigureAwait(false);
        await Shared.Questions.Delete(new QuestionsBackend_Delete(roomId, index), cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<(ImmutableArray<QuestionView> Value, TimeSpan? Wake)> ReadList(
        string roomId, bool resolved, CancellationToken cancellationToken)
    {
        var views = await Shared.Questions.ReadList(roomId, resolved, cancellationToken).ConfigureAwait(false);
        var userId = await GetUserId(cancellationToken).ConfigureAwait(false);
        if (views.Length == 0 || userId == null)
            return (views, null);

        var indexes = views.Select(v => v.Question.Index).ToArray();
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        var ownVotes = (await dbContext.Votes
            .Where(v => v.RoomId == roomId && v.UserId == userId && indexes.Contains(v.QuestionIndex))
            .Select(v => v.QuestionIndex)
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
        if (ownVotes.Count == 0)
            return (views, null);

        return ([..views.Select(v => ownVotes.Contains(v.Question.Index) ? v with { HasOwnVote = true } : v)], null);
    }

    private async Task RequireOwner(string roomId, CancellationToken cancellationToken)
    {
        var userId = await RequireUserId(cancellationToken).ConfigureAwait(false);
        if (!await Shared.Rooms.IsOwner(roomId, userId, cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Only town hall owners can do this.");
    }
}
