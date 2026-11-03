using System.Security.Cryptography;

namespace JvDataCollector;

public enum SessionOutcomeKind
{
    Success,
    NoData,
    Cancelled,
    Failed,
}

public sealed record SessionOutcome(
    SessionOutcomeKind Kind,
    int Code,
    int RecordCount,
    int NewCount,
    int DuplicateCount,
    int ReadCount,
    int DownloadCount,
    string? LastFileTimestamp);

public sealed class SessionRunner
{
    private readonly IJvGateway _jv;
    private readonly IRawLog _raw;
    private readonly ILedger _ledger;
    private readonly IClock _clock;
    private readonly IAppLog _log;
    private readonly IDelay _delay;
    private readonly Action _pump;
    private readonly Action<string, int, int> _progress;

    public SessionRunner(
        IJvGateway jv,
        IRawLog raw,
        ILedger ledger,
        IClock clock,
        IAppLog log,
        IDelay delay,
        Action? pump = null,
        Action<string, int, int>? progress = null)
    {
        _jv = jv;
        _raw = raw;
        _ledger = ledger;
        _clock = clock;
        _log = log;
        _delay = delay;
        _pump = pump ?? delegate { };
        _progress = progress ?? delegate { };
    }

    public SessionOutcome Run(CollectionJob job, string serviceKey, string savePath, string fromTime, CancellationToken cancel)
    {
        var option = job.Kind == JobKind.Stored ? job.Option : 0;
        var init = _jv.Init(SoftwareId.Value);
        if (init != 0)
        {
            return Log(job, Failed(init));
        }

        var keyCode = _jv.SetServiceKey(serviceKey);
        if (keyCode != 0 && keyCode != -101)
        {
            return Log(job, Failed(keyCode));
        }

        var pathCode = _jv.SetSavePath(savePath);
        if (pathCode != 0)
        {
            return Log(job, Failed(pathCode));
        }

        var flagCode = _jv.SetSaveFlag(1);
        if (flagCode != 0)
        {
            return Log(job, Failed(flagCode));
        }

        if (job.Kind == JobKind.Stored)
        {
            _ledger.SaveOpenFromTime(job.Dataspec, option, fromTime);
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var skip = job.Option == 4 ? _ledger.GetWatermark(job.Dataspec, option).ResumeSkipCount : 0;
            var outcome = ReadOnce(job, option, fromTime, skip, cancel);
            if (outcome.Kind != SessionOutcomeKind.Failed || outcome.Code is not (-402 or -403) || attempt == 2)
            {
                return Log(job, outcome);
            }

            if (!string.IsNullOrEmpty(outcome.LastFileTimestamp))
            {
                _jv.DeleteFile(outcome.LastFileTimestamp);
            }

            _log.Info($"file repair dataspec={job.Dataspec} code={outcome.Code}");
        }

        return Log(job, Failed(-402));
    }

    private SessionOutcome ReadOnce(CollectionJob job, int option, string fromTime, int skip, CancellationToken cancel)
    {
        var opened = job.Kind is JobKind.Realtime or JobKind.Odds
            ? _jv.RtOpen(job.Dataspec, job.Key)
            : _jv.Open(job.Dataspec, fromTime, job.Option);
        if (opened.Code == -1)
        {
            _jv.Close();
            return new SessionOutcome(SessionOutcomeKind.NoData, -1, 0, 0, 0, opened.ReadCount, opened.DownloadCount, opened.LastFileTimestamp);
        }

        if (opened.Code == -2)
        {
            _jv.Close();
            return new SessionOutcome(SessionOutcomeKind.Cancelled, -2, 0, 0, 0, opened.ReadCount, opened.DownloadCount, opened.LastFileTimestamp);
        }

        if (opened.Code != 0)
        {
            _jv.Close();
            return Failed(opened.Code);
        }

        try
        {
            var waiting = WaitDownload(job, opened.DownloadCount, cancel);
            if (waiting is not null)
            {
                return waiting;
            }

            for (var i = 0; i < skip; i++)
            {
                if (cancel.IsCancellationRequested)
                {
                    _jv.CancelDownload();
                    return Cancelled();
                }

                _jv.Skip();
            }

            long? generation = null;
            if (job.Dataspec == "0B14")
            {
                var day = string.IsNullOrEmpty(job.MeetingDay) ? job.Key : job.MeetingDay;
                if (!string.IsNullOrEmpty(day))
                {
                    generation = _ledger.BeginOb14(day.Length >= 8 ? day[..8] : day, _clock.UtcNow);
                }
            }

            var read = ReadRecords(job, option, skip, generation, opened, cancel);
            if (read.Kind == SessionOutcomeKind.Success)
            {
                _raw.CompleteDataspec(job.Dataspec);
                if (job.Kind == JobKind.Stored)
                {
                    var mark = _ledger.GetWatermark(job.Dataspec, option);
                    _ledger.CommitWatermark(job.Dataspec, option, Newer(mark.Confirmed, opened.LastFileTimestamp), _clock.UtcNow);
                }
            }

            if (generation is long id)
            {
                if (read.Kind == SessionOutcomeKind.Success && read.RecordCount > 0)
                {
                    _ledger.ActivateOb14(id);
                }
                else
                {
                    _ledger.DropOb14(id);
                }
            }

            return read;
        }
        finally
        {
            _jv.Close();
        }
    }

    private SessionOutcome? WaitDownload(CollectionJob job, int downloadCount, CancellationToken cancel)
    {
        while (true)
        {
            _pump();
            if (cancel.IsCancellationRequested)
            {
                _jv.CancelDownload();
                return Cancelled();
            }

            var status = _jv.Status();
            if (status < 0)
            {
                return Failed(status);
            }

            _progress(job.Dataspec, status, downloadCount);
            if (downloadCount == 0 || status >= downloadCount)
            {
                return null;
            }

            _delay.Wait(TimeSpan.FromMilliseconds(300), cancel);
        }
    }

    private SessionOutcome ReadRecords(
        CollectionJob job,
        int option,
        int skip,
        long? generation,
        OpenResult opened,
        CancellationToken cancel)
    {
        var records = 0;
        var added = 0;
        var duplicates = 0;
        var sinceBoundary = new List<long>();
        while (true)
        {
            _pump();
            if (cancel.IsCancellationRequested)
            {
                _jv.CancelDownload();
                return Cancelled();
            }

            var read = _jv.Read();
            if (read.Code > 0)
            {
                if (read.Payload is null || read.Payload.Length < read.Code)
                {
                    return Failed(-1_000_001);
                }

                var payload = read.Payload.Length == read.Code
                    ? read.Payload
                    : read.Payload.AsSpan(0, read.Code).ToArray();
                var classified = RecordKeys.Classify(payload);
                var hash = SHA256.HashData(payload);
                var existing = _ledger.FindByHash(job.Dataspec, hash);
                long id;
                if (existing is long found)
                {
                    duplicates++;
                    id = found;
                }
                else
                {
                    var frame = _raw.Append(job.Dataspec, _clock.UtcNow, payload);
                    id = _ledger.IndexRecord(new IndexDraft(
                        job.Dataspec,
                        frame.Path,
                        frame.Sequence,
                        frame.Offset,
                        frame.Length,
                        frame.Sha256,
                        classified.RecordId,
                        classified.MeetingDay,
                        classified.RaceKey,
                        _clock.UtcNow));
                    added++;
                }

                records++;
                sinceBoundary.Add(id);
                if (generation is long generationId)
                {
                    _ledger.AttachOb14(generationId, id);
                }

                continue;
            }

            if (read.Code == -3)
            {
                _delay.Wait(TimeSpan.FromMilliseconds(300), cancel);
                continue;
            }

            if (read.Code == -1)
            {
                var timestamp = _jv.CurrentFileTimeStamp();
                _ledger.AssignSource(sinceBoundary, read.FileName, timestamp);
                sinceBoundary.Clear();
                if (job.Kind == JobKind.Stored)
                {
                    if (job.Option == 4)
                    {
                        skip++;
                    }

                    _ledger.SaveResume(
                        job.Dataspec,
                        option,
                        read.FileName,
                        timestamp,
                        job.Option == 4 ? skip : 0);
                }

                continue;
            }

            if (read.Code == 0)
            {
                return new SessionOutcome(
                    SessionOutcomeKind.Success,
                    0,
                    records,
                    added,
                    duplicates,
                    opened.ReadCount,
                    opened.DownloadCount,
                    opened.LastFileTimestamp);
            }

            if (read.Code is -402 or -403)
            {
                return new SessionOutcome(
                    SessionOutcomeKind.Failed,
                    read.Code,
                    records,
                    added,
                    duplicates,
                    opened.ReadCount,
                    opened.DownloadCount,
                    read.FileName);
            }

            return new SessionOutcome(
                SessionOutcomeKind.Failed,
                read.Code,
                records,
                added,
                duplicates,
                opened.ReadCount,
                opened.DownloadCount,
                opened.LastFileTimestamp);
        }
    }

    private SessionOutcome Log(CollectionJob job, SessionOutcome outcome)
    {
        _log.Info($"session dataspec={job.Dataspec} option={job.Option} kind={outcome.Kind} code={outcome.Code} new={outcome.NewCount} dup={outcome.DuplicateCount}");
        return outcome;
    }

    private static string? Newer(string? current, string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return current;
        }

        if (string.IsNullOrEmpty(current) || string.CompareOrdinal(candidate, current) > 0)
        {
            return candidate;
        }

        return current;
    }

    private static SessionOutcome Failed(int code) =>
        new(SessionOutcomeKind.Failed, code, 0, 0, 0, 0, 0, null);

    private static SessionOutcome Cancelled() =>
        new(SessionOutcomeKind.Cancelled, 0, 0, 0, 0, 0, 0, null);
}
