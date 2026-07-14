using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using TownHall.Host.Db;

namespace TownHall.Host.Services;

public class ParticipantsService(IServiceProvider services) : DbServiceBase<AppDbContext>(services), IParticipants
{
    private IDbEntityResolver<string, DbParticipant> ParticipantResolver { get; }
        = services.DbEntityResolver<string, DbParticipant>();

    public virtual async Task<ParticipantInfo> GetOwn(Session session, CancellationToken cancellationToken = default)
    {
        var dbParticipant = await ParticipantResolver.Get(session.Id, cancellationToken).ConfigureAwait(false);
        return new ParticipantInfo(dbParticipant?.Name ?? NameGenerator.New(session.Id));
    }

    public virtual async Task OnSetName(Participants_SetName command, CancellationToken cancellationToken = default)
    {
        var (session, name) = command;
        if (Invalidation.IsActive) {
            _ = GetOwn(session, default);
            return;
        }

        name = name.Trim();
        if (name.Length is < 1 or > 30)
            throw new ArgumentException("Name must be 1..30 characters long.");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        string sessionId = session.Id;
        var dbParticipant = await dbContext.Participants
            .FirstOrDefaultAsync(p => p.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (dbParticipant == null)
            dbContext.Add(new DbParticipant { SessionId = sessionId, Name = name });
        else
            dbParticipant.Name = name;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
