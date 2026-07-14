namespace TownHall.Host.Services;

public class TimeService : ITime
{
    [ComputeMethod(AutoInvalidationDelay = 1)]
    public virtual Task<DateTime> GetTime(CancellationToken cancellationToken = default)
        => Task.FromResult(DateTime.Now);
}
