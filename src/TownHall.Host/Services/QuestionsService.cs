namespace TownHall.Host.Services;

// Frontend questions service: reads open to guests; posting/voting require sign-in; resolve/delete
// require room ownership. Argument + room-status validation lives in IQuestionsBackend.
public class QuestionsService(IServiceProvider services) : IQuestions
{
    private IQuestionsBackend Backend => field ??= services.GetRequiredService<IQuestionsBackend>();
    private IRoomsBackend Rooms => field ??= services.GetRequiredService<IRoomsBackend>();
    private IUsersBackend Users => field ??= services.GetRequiredService<IUsersBackend>();
    private ICommander Commander => field ??= services.Commander();

    public virtual Task<Question?> Get(Session session, string roomId, long index, CancellationToken cancellationToken = default)
        => Backend.Get(roomId, index, cancellationToken);

    public virtual Task<ImmutableArray<long>> ListOpen(Session session, string roomId, CancellationToken cancellationToken = default)
        => Backend.ListOpen(roomId, cancellationToken);

    public virtual Task<ImmutableArray<long>> ListTopOpen(Session session, string roomId, int limit, CancellationToken cancellationToken = default)
        => Backend.ListTopOpen(roomId, limit, cancellationToken);

    public virtual Task<ImmutableArray<long>> ListResolved(Session session, string roomId, CancellationToken cancellationToken = default)
        => Backend.ListResolved(roomId, cancellationToken);

    public virtual Task<Resolution?> GetResolution(Session session, string roomId, long index, CancellationToken cancellationToken = default)
        => Backend.GetResolution(roomId, index, cancellationToken);

    public virtual Task<int> GetVoteCount(Session session, string roomId, long index, CancellationToken cancellationToken = default)
        => Backend.GetVoteCount(roomId, index, cancellationToken);

    public virtual async Task<bool> HasOwnVote(Session session, string roomId, long index, CancellationToken cancellationToken = default)
    {
        var userId = await Users.GetUserIdBySession(session.Id, cancellationToken).ConfigureAwait(false);
        return userId != null && await Backend.HasVote(roomId, index, userId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Question> OnPost(Questions_Post command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return null!;

        var (session, roomId, text) = command;
        var userId = (await GetOwnUserId(session, cancellationToken).ConfigureAwait(false)).RequireSignedIn();
        return await Commander.Call(new QuestionsBackend_Post(roomId, userId, text), true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnVote(Questions_Vote command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (session, roomId, index, value) = command;
        var userId = (await GetOwnUserId(session, cancellationToken).ConfigureAwait(false)).RequireSignedIn();
        await Commander.Call(new QuestionsBackend_Vote(roomId, index, userId, value), true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnResolve(Questions_Resolve command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (session, roomId, index, note) = command;
        await RequireOwner(session, roomId, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new QuestionsBackend_Resolve(roomId, index, note), true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnDelete(Questions_Delete command, CancellationToken cancellationToken = default)
    {
        if (Invalidation.IsActive)
            return;

        var (session, roomId, index) = command;
        await RequireOwner(session, roomId, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new QuestionsBackend_Delete(roomId, index), true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private Task<string?> GetOwnUserId(Session session, CancellationToken cancellationToken)
        => Users.GetUserIdBySession(session.Id, cancellationToken);

    private async Task RequireOwner(Session session, string roomId, CancellationToken cancellationToken)
    {
        var userId = (await GetOwnUserId(session, cancellationToken).ConfigureAwait(false)).RequireSignedIn();
        if (!await Rooms.IsOwner(roomId, userId, cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Only town hall owners can do this.");
    }
}
