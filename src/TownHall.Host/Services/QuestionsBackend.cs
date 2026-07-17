using Microsoft.EntityFrameworkCore;
using TownHall.Db;

namespace TownHall.Host.Services;

public sealed class QuestionsBackend(
    IDbContextFactory<AppDbContext> dbContextFactory, ChangeTracker changes, IUsersBackend users)
    : BackendService(dbContextFactory, changes), IQuestionsBackend
{
    public async Task<ImmutableArray<QuestionView>> ReadList(string roomId, bool resolved, CancellationToken cancellationToken = default)
    {
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var query = dbContext.Questions.Where(q => q.RoomId == roomId);
        query = resolved
            ? query.Where(q => q.ResolvedAt != null).OrderByDescending(q => q.ResolvedAt)
            : query.Where(q => q.ResolvedAt == null).OrderByDescending(q => q.Index);
        var dbQuestions = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        if (dbQuestions.Count == 0)
            return [];

        var indexes = dbQuestions.Select(q => q.Index).ToArray();
        var voteCounts = await dbContext.Votes
            .Where(v => v.RoomId == roomId && indexes.Contains(v.QuestionIndex))
            .GroupBy(v => v.QuestionIndex)
            .Select(g => new { Index = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var countByIndex = voteCounts.ToDictionary(x => x.Index, x => x.Count);
        var nameById = await users.GetNames(dbQuestions.Select(q => q.AuthorId).ToArray(), cancellationToken).ConfigureAwait(false);

        return [
            ..dbQuestions.Select(q => new QuestionView(
                new Question(roomId, q.Index, q.AuthorId, q.Text, q.PostedAt.DefaultKind(DateTimeKind.Utc)),
                nameById.GetValueOrDefault(q.AuthorId, ""),
                countByIndex.GetValueOrDefault(q.Index),
                false,
                q.ResolvedAt is { } resolvedAt
                    ? new Resolution(q.ResolutionNote, resolvedAt.DefaultKind(DateTimeKind.Utc))
                    : null))
        ];
    }

    public async Task<Question> Post(QuestionsBackend_Post command, CancellationToken cancellationToken = default)
    {
        var (roomId, authorUserId, text, anonymous) = command;
        // Questions are single-paragraph: line feeds and whitespace runs collapse to single spaces
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length is < 1 or > 500)
            throw new ArgumentException("Question text must be 1..500 characters long.");

        // Anonymous posts are attributed to a per-(user, room) pseudonym; the real user id isn't stored
        var authorId = anonymous ? AnonId.Of(authorUserId, roomId) : authorUserId;
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        var now = Moment.Now.ToDbPrecision();
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
        await SaveAndNotify(dbContext, cancellationToken, $"room:{roomId}");
        return new Question(roomId, dbQuestion.Index, authorId, text, now);
    }

    public async Task Vote(QuestionsBackend_Vote command, CancellationToken cancellationToken = default)
    {
        var (roomId, index, userId, value) = command;
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        var now = Moment.Now.ToDbPrecision();
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
        await SaveAndNotify(dbContext, cancellationToken, $"room:{roomId}");
    }

    public async Task Resolve(QuestionsBackend_Resolve command, CancellationToken cancellationToken = default)
    {
        var (roomId, index, note) = command;
        // Single paragraph, like a question
        note = string.Join(" ", note.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (note.Length > 500)
            throw new ArgumentException("Resolution note must be at most 500 characters long.");

        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var now = Moment.Now.ToDbPrecision();
        var dbQuestion = await dbContext.Questions
            .FirstOrDefaultAsync(q => q.Key == DbQuestion.ComposeKey(roomId, index), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Question not found.");
        // Preserve the original resolution time when only the note is being edited
        dbQuestion.ResolvedAt ??= now;
        dbQuestion.ResolutionNote = note;
        await SaveAndNotify(dbContext, cancellationToken, $"room:{roomId}");
    }

    public async Task Delete(QuestionsBackend_Delete command, CancellationToken cancellationToken = default)
    {
        var (roomId, index) = command;
        var dbContext = await CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbRoom = await dbContext.GetRoom(roomId, cancellationToken).ConfigureAwait(false);
        var now = Moment.Now.ToDbPrecision();
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
        await SaveAndNotify(dbContext, cancellationToken, $"room:{roomId}");
    }
}
