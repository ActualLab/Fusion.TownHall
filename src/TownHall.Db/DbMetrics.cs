using System.Data.Common;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TownHall.Db;

// Publishes DB query and fetched-row counters (the dashboard derives per-second rates from them) by
// intercepting EF Core command execution. Register it on AppDbContext via AddInterceptors; the meter
// is static, so a single instance is enough. Complements Npgsql's own pool/duration metrics.
public sealed class DbMetrics : DbCommandInterceptor
{
    public const string MeterName = "TownHall.Db";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Queries =
        Meter.CreateCounter<long>("townhall.db.queries", "{query}", "DB commands executed");
    private static readonly Counter<long> Rows =
        Meter.CreateCounter<long>("townhall.db.rows", "{row}", "Rows read from result sets");

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Queries.Add(1);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Queries.Add(1);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Queries.Add(1);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Queries.Add(1);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Queries.Add(1);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Queries.Add(1);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult DataReaderDisposing(
        DbCommand command, DataReaderDisposingEventData eventData, InterceptionResult result)
    {
        // ReadCount is the number of rows actually read from this reader
        Rows.Add(eventData.ReadCount);
        return base.DataReaderDisposing(command, eventData, result);
    }
}
