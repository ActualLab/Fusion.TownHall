using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

public class QuestionsBackend(IServiceProvider services) : DbServiceBase<AppDbContext>(services), IQuestionsBackend
{
    private IDbEntityResolver<string, DbQuestion> QuestionResolver { get; }
        = services.DbEntityResolver<string, DbQuestion>();

    [ComputeMethod]
    public virtual async Task<Question?> Get(string roomId, long index, CancellationToken cancellationToken = default)
    {
        var dbQuestion = await QuestionResolver.Get(DbQuestion.ComposeKey(roomId, index), cancellationToken).ConfigureAwait(false);
        return dbQuestion == null
            ? null
            : new Question(roomId, dbQuestion.Index, dbQuestion.AuthorId, dbQuestion.Text,
                dbQuestion.PostedAt.DefaultKind(DateTimeKind.Utc));
    }

    [ComputeMethod]
    public virtual async Task<ImmutableArray<long>> ListOpen(string roomId, CancellationToken cancellationToken = default)
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
    public virtual async Task<ImmutableArray<long>> ListTopOpen(string roomId, int limit, CancellationToken cancellationToken = default)
    {
        var openIds = await ListOpen(roomId, cancellationToken).ConfigureAwait(false);
        var counted = new List<(long Index, int Count)>(openIds.Length);
        foreach (var index in openIds)
            counted.Add((index, await GetVoteCount(roomId, index, cancellationToken).ConfigureAwait(false)));
        return [
            ..counted
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Index)
                .Take(limit)
                .Select(x => x.Index)
        ];
    }

    [ComputeMethod]
    public virtual async Task<ImmutableArray<long>> ListResolved(string roomId, CancellationToken cancellationToken = default)
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
    public virtual async Task<Resolution?> GetResolution(string roomId, long index, CancellationToken cancellationToken = default)
    {
        var dbQuestion = await QuestionResolver.Get(DbQuestion.ComposeKey(roomId, index), cancellationToken).ConfigureAwait(false);
        return dbQuestion?.ResolvedAt is not { } resolvedAt
            ? null
            : new Resolution(dbQuestion.ResolutionNote, resolvedAt.DefaultKind(DateTimeKind.Utc));
    }

    [ComputeMethod]
    public virtual async Task<int> GetVoteCount(string roomId, long index, CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        return await dbContext.Votes
            .CountAsync(v => v.RoomId == roomId && v.QuestionIndex == index, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> HasVote(string roomId, long index, string userId, CancellationToken cancellationToken = default)
    {
        if (userId.Length == 0)
            return false;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        return await dbContext.Votes
            .AnyAsync(v => v.RoomId == roomId && v.QuestionIndex == index && v.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
    }

    // A pseudo dependency invalidated by any vote change in a room;
    // trending and total-vote-count reads depend on it.
    [ComputeMethod]
    public virtual Task<Unit> PseudoVotes(string roomId, CancellationToken cancellationToken = default)
        => TaskExt.UnitTask;

    public virtual async Task<Question> Post(QuestionsBackend_Post command, CancellationToken cancellationToken = default)
    {
        var (roomId, authorUserId, text, anonymous) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var index = context.Operation.Items.Get<long>("Index");
            _ = Get(roomId, index, default);
            _ = ListOpen(roomId, default);
            return null!;
        }

        // Questions are single-paragraph: line feeds and whitespace runs collapse to single spaces
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length is < 1 or > 500)
            throw new ArgumentException("Question text must be 1..500 characters long.");

        // Anonymous posts are attributed to a per-(user, room) pseudonym; the real user id isn't stored
        var authorId = anonymous ? AnonId.Of(authorUserId, roomId) : authorUserId;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now.ToDbPrecision();
        if (dbRoom.GetStatus(now) != RoomStatus.Live)
            throw new InvalidOperationException("This town hall is not live.");

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

    public virtual async Task Vote(QuestionsBackend_Vote command, CancellationToken cancellationToken = default)
    {
        var (roomId, index, userId, value) = command;
        if (Invalidation.IsActive) {
            _ = GetVoteCount(roomId, index, default);
            _ = HasVote(roomId, index, userId, default);
            _ = PseudoVotes(roomId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now.ToDbPrecision();
        if (dbRoom.GetStatus(now) != RoomStatus.Live)
            throw new InvalidOperationException("This town hall is not live.");

        var dbQuestion = await dbContext.Questions
            .FirstOrDefaultAsync(q => q.Key == DbQuestion.ComposeKey(roomId, index), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Question not found.");
        if (dbQuestion.ResolvedAt != null)
            throw new InvalidOperationException("Voting is closed for resolved questions.");

        var dbVote = await dbContext.Votes
            .FirstOrDefaultAsync(v => v.RoomId == roomId && v.QuestionIndex == index && v.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (value) {
            if (dbVote == null)
                dbContext.Add(new DbVote { RoomId = roomId, QuestionIndex = index, UserId = userId, CastAt = now });
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

    public virtual async Task Resolve(QuestionsBackend_Resolve command, CancellationToken cancellationToken = default)
    {
        var (roomId, index, note) = command;
        if (Invalidation.IsActive) {
            _ = ListOpen(roomId, default);
            _ = ListResolved(roomId, default);
            _ = GetResolution(roomId, index, default);
            return;
        }

        // Single paragraph, like a question
        note = string.Join(" ", note.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (note.Length > 500)
            throw new ArgumentException("Resolution note must be at most 500 characters long.");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var now = Clocks.SystemClock.Now.ToDbPrecision();
        var dbQuestion = await dbContext.Questions
            .FirstOrDefaultAsync(q => q.Key == DbQuestion.ComposeKey(roomId, index), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Question not found.");
        // Preserve the original resolution time when only the note is being edited
        dbQuestion.ResolvedAt ??= now;
        dbQuestion.ResolutionNote = note;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task Delete(QuestionsBackend_Delete command, CancellationToken cancellationToken = default)
    {
        var (roomId, index) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            _ = Get(roomId, index, default);
            _ = ListOpen(roomId, default);
            _ = ListResolved(roomId, default);
            _ = GetResolution(roomId, index, default);
            _ = GetVoteCount(roomId, index, default);
            _ = PseudoVotes(roomId, default);
            var voterIds = context.Operation.Items.Get<string[]>("VoterIds") ?? [];
            foreach (var voterId in voterIds)
                _ = HasVote(roomId, index, voterId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now.ToDbPrecision();
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
        context.Operation.Items.Set("VoterIds", dbVotes.Select(v => v.UserId).ToArray());
    }
}
