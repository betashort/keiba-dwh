namespace DwhLoader;

public interface ICycleProgress
{
    void Writing(CompleteFile file);

    void Finished(FileOutcome outcome);
}

public sealed class FileOutcome
{
    public FileOutcome(long id, string dataspec, string result, int? count, string reason, DateTimeOffset? retryUntil)
    {
        Id = id;
        Dataspec = dataspec;
        Result = result;
        Count = count;
        Reason = reason;
        RetryUntil = retryUntil;
    }

    public long Id { get; }

    public string Dataspec { get; }

    public string Result { get; }

    public int? Count { get; }

    public string Reason { get; }

    public DateTimeOffset? RetryUntil { get; }
}

public sealed class CycleResult
{
    public bool SchemaMissing { get; set; }

    public bool LedgerDown { get; set; }

    public bool WarehouseDown { get; set; }

    public bool Stopped { get; set; }

    public int Wrote { get; set; }

    public bool Ob14Kept { get; set; }

    public IReadOnlyList<Ob14Member> Ob14Members { get; set; } = [];

    public List<FileOutcome> Files { get; } = [];

    public bool WroteAny => Wrote > 0;
}

public static class FollowLoop
{
    public static void Run(
        Func<CycleResult> cycle,
        Action<CycleResult> publish,
        Action<TimeSpan, CancellationToken> sleep,
        CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            var result = cycle();
            publish(result);
            if (result.Stopped || cancel.IsCancellationRequested)
            {
                return;
            }

            if (result.SchemaMissing || result.LedgerDown || result.WarehouseDown || !result.WroteAny)
            {
                sleep(LoadTiming.Interval, cancel);
            }
        }
    }
}

public static class LoadCycle
{
    public static CycleResult Execute(
        ILedger ledger,
        IWarehouse warehouse,
        Dictionary<long, DateTimeOffset> retryAfter,
        DateTimeOffset now,
        CancellationToken cancel,
        ILoadLog log,
        ICycleProgress progress)
    {
        log ??= new NullLoadLog();
        var ingested = warehouse.IngestedIds();
        var files = ledger.ListComplete()
            .Where(file => !ingested.Contains(file.Id))
            .Where(file => !retryAfter.TryGetValue(file.Id, out var until) || until <= now)
            .OrderBy(file => file.Id)
            .ToList();
        var result = new CycleResult();
        foreach (var file in files)
        {
            if (cancel.IsCancellationRequested)
            {
                warehouse.RollbackFile();
                result.Stopped = true;
                return result;
            }

            progress?.Writing(file);
            if (!TryFile(ledger, warehouse, retryAfter, now, cancel, log, progress, result, file))
            {
                return result;
            }
        }

        try
        {
            var members = ledger.ReadActiveOb14();
            warehouse.ReplaceOb14(members);
            result.Ob14Members = members;
        }
        catch (Ob14CopyException ex)
        {
            log.Info($"{ex.GetType().Name} {ex.Message}");
            result.Ob14Kept = true;
        }
        catch (LedgerUnavailableException)
        {
            result.LedgerDown = true;
            result.Ob14Kept = true;
        }

        return result;
    }

    private static bool TryFile(
        ILedger ledger,
        IWarehouse warehouse,
        Dictionary<long, DateTimeOffset> retryAfter,
        DateTimeOffset now,
        CancellationToken cancel,
        ILoadLog log,
        ICycleProgress progress,
        CycleResult result,
        CompleteFile file)
    {
        var began = false;
        try
        {
            var index = ledger.ReadIndex(file);
            warehouse.BeginFile();
            began = true;
            foreach (var record in index)
            {
                if (cancel.IsCancellationRequested)
                {
                    warehouse.RollbackFile();
                    result.Stopped = true;
                    return false;
                }

                var payload = ledger.ReadPayload(file, record);
                var rows = RecordExpander.Expand(
                    record.RecordId,
                    file.Dataspec,
                    payload,
                    record.Sha256,
                    record.AcquiredUtc);
                foreach (var row in rows)
                {
                    warehouse.Write(row);
                }
            }

            warehouse.MarkFile(file.Id, file.Dataspec, file.Path);
            warehouse.CommitFile();
            began = false;
            retryAfter.Remove(file.Id);
            result.Wrote++;
            var outcome = new FileOutcome(file.Id, file.Dataspec, "書けた", index.Count, "", null);
            result.Files.Add(outcome);
            progress?.Finished(outcome);
            log.Info($"{file.Dataspec} id {file.Id} {index.Count} 件");
            return true;
        }
        catch (Exception ex) when (ex is HashMismatchException or PayloadOffsetException or RecordFormatException or SqlFailureException)
        {
            if (began)
            {
                warehouse.RollbackFile();
            }

            if (cancel.IsCancellationRequested)
            {
                result.Stopped = true;
                return false;
            }

            if (ex is SqlFailureException sql && sql.Disconnect)
            {
                result.WarehouseDown = true;
                return false;
            }

            var reason = Reason(ex);
            var until = now + LoadTiming.Interval;
            retryAfter[file.Id] = until;
            var outcome = new FileOutcome(file.Id, file.Dataspec, "再試行待ち", null, reason, until);
            result.Files.Add(outcome);
            progress?.Finished(outcome);
            log.FileIssue(file.Id, file.Dataspec, null, "", reason);
            return true;
        }
        catch (WarehouseUnavailableException)
        {
            if (began)
            {
                warehouse.RollbackFile();
            }

            result.WarehouseDown = true;
            return false;
        }
    }

    public static string Reason(Exception ex)
    {
        if (ex is SqlFailureException sql)
        {
            return $"{ex.GetType().Name} {sql.SqlState} {ex.Message}";
        }

        return $"{ex.GetType().Name} {ex.Message}";
    }
}

public sealed class SqlFailureException : Exception
{
    public SqlFailureException(string sqlState, string message, bool disconnect) : base(message)
    {
        SqlState = sqlState ?? "";
        Disconnect = disconnect;
    }

    public string SqlState { get; }

    public bool Disconnect { get; }
}
