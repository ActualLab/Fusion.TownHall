namespace TownHall;

public interface ITime : IComputeService
{
    [ComputeMethod]
    Task<DateTime> GetTime(CancellationToken cancellationToken = default);
}
