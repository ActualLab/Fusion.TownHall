namespace TownHall.Db;

public static class MomentExt
{
    // Postgres "timestamp with time zone" keeps microseconds, while Moment has
    // 100ns ticks; stamping at DB precision keeps command results identical to
    // what a later re-read returns.
    public static Moment ToDbPrecision(this Moment moment)
        => new(moment.EpochOffsetTicks / TimeSpan.TicksPerMicrosecond * TimeSpan.TicksPerMicrosecond);
}
