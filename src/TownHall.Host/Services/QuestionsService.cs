using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using TownHall.Host.Db;

namespace TownHall.Host.Services;

public class QuestionsService(IServiceProvider services) : DbServiceBase<AppDbContext>(services), IQuestions
{
    private IDbEntityResolver<string, DbQuestion> QuestionResolver { get; }
        = services.DbEntityResolver<string, DbQuestion>();

    public virtual async Task<Question?> Get(Session session, string roomId, long index, CancellationToken cancellationToken = default)
        => await GetQuestion(roomId, index, cancellationToken).ConfigureAwait(false);

    public virtual async Task<ImmutableArray<long>> ListOpenIds(Session session, string roomId, CancellationToken cancellationToken = default)
        => await ListOpenQuestionIds(roomId, cancellationToken).ConfigureAwait(false);

    public virtual async Task<ImmutableArray<long>> GetTopOpenIds(Session session, string roomId, int limit, CancellationToken cancellationToken = default)
    {
        var openIds = await ListOpenQuestionIds(roomId, cancellationToken).ConfigureAwait(false);
        var counted = new List<(long Index, int Count)>(openIds.Length);
        foreach (var index in openIds)
            counted.Add((index, await GetQuestionVoteCount(roomId, index, cancellationToken).ConfigureAwait(false)));
        return [
            ..counted
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Index)
                .Take(limit)
                .Select(x => x.Index)
        ];
    }

    public virtual async Task<ImmutableArray<long>> ListResolvedIds(Session session, string roomId, CancellationToken cancellationToken = default)
        => await ListResolvedQuestionIds(roomId, cancellationToken).ConfigureAwait(false);

    public virtual async Task<Resolution?> GetResolution(Session session, string roomId, long index, CancellationToken cancellationToken = default)
        => await GetQuestionResolution(roomId, index, cancellationToken).ConfigureAwait(false);

    public virtual async Task<int> GetVoteCount(Session session, string roomId, long index, CancellationToken cancellationToken = default)
        => await GetQuestionVoteCount(roomId, index, cancellationToken).ConfigureAwait(false);

    public virtual async Task<bool> HasOwnVote(Session session, string roomId, long index, CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        string sessionId = session.Id;
        return await dbContext.Votes
            .AnyAsync(v => v.RoomId == roomId && v.QuestionIndex == index && v.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public virtual async Task<Question> OnPost(Questions_Post command, CancellationToken cancellationToken = default)
    {
        var (session, roomId, text) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var index = context.Operation.Items.Get<long>("Index");
            _ = GetQuestion(roomId, index, default);
            _ = ListOpenQuestionIds(roomId, default);
            return null!;
        }

        // Questions are single-paragraph: line feeds and whitespace runs collapse to single spaces
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length is < 1 or > 500)
            throw new ArgumentException("Question text must be 1..500 characters long.");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (dbRoom.GetStatus(now) != RoomStatus.Live)
            throw new InvalidOperationException("This town hall is not live.");

        var authorId = ParticipantId.Of(session);
        var questionIndex = dbRoom.NextQuestionIndex++;
        var dbQuestion = new DbQuestion {
            Key = DbQuestion.ComposeKey(roomId, questionIndex),
            RoomId = roomId,
            Index = questionIndex,
            AuthorId = authorId,
            Text = text,
            PostedAt = now,
        };
        dbContext.Add(dbQuestion);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.Set("Index", dbQuestion.Index);
        return new Question(roomId, dbQuestion.Index, authorId, text, now);
    }

    public virtual async Task OnVote(Questions_Vote command, CancellationToken cancellationToken = default)
    {
        var (session, roomId, index, value) = command;
        if (Invalidation.IsActive) {
            _ = GetQuestionVoteCount(roomId, index, default);
            _ = HasOwnVote(session, roomId, index, default);
            _ = PseudoVotes(roomId);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (dbRoom.GetStatus(now) != RoomStatus.Live)
            throw new InvalidOperationException("This town hall is not live.");

        var dbQuestion = await dbContext.Questions
            .FirstOrDefaultAsync(q => q.Key == DbQuestion.ComposeKey(roomId, index), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Question not found.");
        if (dbQuestion.ResolvedAt != null)
            throw new InvalidOperationException("Voting is closed for resolved questions.");

        string sessionId = session.Id;
        var dbVote = await dbContext.Votes
            .FirstOrDefaultAsync(v => v.RoomId == roomId && v.QuestionIndex == index && v.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (value) {
            if (dbVote == null)
                dbContext.Add(new DbVote { RoomId = roomId, QuestionIndex = index, SessionId = sessionId, CastAt = now });
            else
                dbVote.CastAt = now;
        }
        else {
            if (dbVote == null)
                return;

            dbContext.Remove(dbVote);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnResolve(Questions_Resolve command, CancellationToken cancellationToken = default)
    {
        var (session, roomId, index, note) = command;
        if (Invalidation.IsActive) {
            _ = ListOpenQuestionIds(roomId, default);
            _ = ListResolvedQuestionIds(roomId, default);
            _ = GetQuestionResolution(roomId, index, default);
            return;
        }

        note = note.Trim();
        if (note.Length > 500)
            throw new ArgumentException("Resolution note must be at most 500 characters long.");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        await dbContext.RequireRoomOwner(roomId, session.Id, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (dbRoom.GetStatus(now) == RoomStatus.Ended)
            throw new InvalidOperationException("This town hall has ended.");

        var dbQuestion = await dbContext.Questions
            .FirstOrDefaultAsync(q => q.Key == DbQuestion.ComposeKey(roomId, index), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Question not found.");
        dbQuestion.ResolvedAt = now;
        dbQuestion.ResolutionNote = note;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnDelete(Questions_Delete command, CancellationToken cancellationToken = default)
    {
        var (session, roomId, index) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            _ = GetQuestion(roomId, index, default);
            _ = ListOpenQuestionIds(roomId, default);
            _ = ListResolvedQuestionIds(roomId, default);
            _ = GetQuestionResolution(roomId, index, default);
            _ = GetQuestionVoteCount(roomId, index, default);
            _ = PseudoVotes(roomId);
            var voterIds = context.Operation.Items.Get<string[]>("VoterIds") ?? [];
            foreach (var voterId in voterIds)
                _ = HasOwnVote(new Session(voterId), roomId, index, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        await dbContext.RequireRoomOwner(roomId, session.Id, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (dbRoom.GetStatus(now) == RoomStatus.Ended)
            throw new InvalidOperationException("This town hall has ended.");

        var dbQuestion = await dbContext.Questions
            .FirstOrDefaultAsync(q => q.Key == DbQuestion.ComposeKey(roomId, index), cancellationToken)
            .ConfigureAwait(false);
        if (dbQuestion == null)
            return;

        var dbVotes = await dbContext.Votes
            .Where(v => v.RoomId == roomId && v.QuestionIndex == index)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.Remove(dbQuestion);
        dbContext.RemoveRange(dbVotes);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.Set("VoterIds", dbVotes.Select(v => v.SessionId).ToArray());
    }

    // Protected/internal methods

    // The session-less compute methods below are public (though not part of IQuestions),
    // so RoomStatsService can compose on top of them.

    [ComputeMethod]
    public virtual async Task<Question?> GetQuestion(string roomId, long index, CancellationToken cancellationToken = default)
    {
        var dbQuestion = await QuestionResolver.Get(DbQuestion.ComposeKey(roomId, index), cancellationToken).ConfigureAwait(false);
        return dbQuestion == null
            ? null
            : new Question(roomId, dbQuestion.Index, dbQuestion.AuthorId, dbQuestion.Text,
                dbQuestion.PostedAt.DefaultKind(DateTimeKind.Utc));
    }

    [ComputeMethod]
    public virtual async Task<ImmutableArray<long>> ListOpenQuestionIds(string roomId, CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var ids = await dbContext.Questions
            .Where(q => q.RoomId == roomId && q.ResolvedAt == null)
            .OrderByDescending(q => q.Index)
            .Select(q => q.Index)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [..ids];
    }

    [ComputeMethod]
    public virtual async Task<ImmutableArray<long>> ListResolvedQuestionIds(string roomId, CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var ids = await dbContext.Questions
            .Where(q => q.RoomId == roomId && q.ResolvedAt != null)
            .OrderByDescending(q => q.ResolvedAt)
            .Select(q => q.Index)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [..ids];
    }

    [ComputeMethod]
    public virtual async Task<Resolution?> GetQuestionResolution(string roomId, long index, CancellationToken cancellationToken = default)
    {
        var dbQuestion = await QuestionResolver.Get(DbQuestion.ComposeKey(roomId, index), cancellationToken).ConfigureAwait(false);
        return dbQuestion?.ResolvedAt is not { } resolvedAt
            ? null
            : new Resolution(dbQuestion.ResolutionNote, resolvedAt.DefaultKind(DateTimeKind.Utc));
    }

    [ComputeMethod]
    public virtual async Task<int> GetQuestionVoteCount(string roomId, long index, CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        return await dbContext.Votes
            .CountAsync(v => v.RoomId == roomId && v.QuestionIndex == index, cancellationToken)
            .ConfigureAwait(false);
    }

    // A pseudo dependency invalidated by any vote change in a room;
    // trending and total-vote-count reads depend on it.
    [ComputeMethod]
    public virtual Task<Unit> PseudoVotes(string roomId)
        => TaskExt.UnitTask;
}
