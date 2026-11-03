using System.Text.Json.Serialization;

namespace JvDataCollector;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IDelay
{
    void Wait(TimeSpan delay, CancellationToken cancel);
}

public sealed class ThreadDelay : IDelay
{
    public void Wait(TimeSpan delay, CancellationToken cancel)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        cancel.WaitHandle.WaitOne(delay);
    }
}

public interface IAppLog
{
    void Info(string message);
    void Error(string message);
    void NoteSecret(string? secret);
}

public interface IDiskSpace
{
    long AvailableBytes(string path);
}

public interface IAttention
{
    void Raise(AttentionReason reason, string message);
    void Clear(AttentionReason reason);
    bool HasAttention { get; }
    string? Message { get; }
}

public enum AttentionReason
{
    JvLinkMissing,
    ServiceKey,
    Agreement,
    SavePath,
    DiskSpace,
    JvRejected,
}

public interface IConfigStore
{
    CollectorConfig Load();
    void Save(CollectorConfig config);
}

public interface ISecretStore
{
    bool Exists { get; }
    string? Load();
    void Save(string serviceKey);
}

public interface IJvGateway
{
    int Init(string softwareId);
    int SetServiceKey(string serviceKey);
    int SetSavePath(string path);
    int SetSaveFlag(int flag);
    int SetUiProperties();
    OpenResult Open(string dataspec, string fromTime, int option);
    OpenResult RtOpen(string dataspec, string key);
    int Status();
    ReadResult Read();
    void Skip();
    void CancelDownload();
    int Close();
    int DeleteFile(string fileName);
    string? CurrentFileTimeStamp();
    void StartWatch(Action<RealtimeEventKind, string> onEvent);
    void StopWatch();
}

public readonly record struct OpenResult(int Code, int ReadCount, int DownloadCount, string? LastFileTimestamp);

public readonly record struct ReadResult(int Code, byte[]? Payload, string? FileName);

public enum RealtimeEventKind
{
    Payoff,
    JockeyChange,
    Weather,
    CourseChange,
    Scratch,
    PostTime,
    Weight,
}

public readonly record struct FrameClassification(string RecordId, string? MeetingDay, string? RaceKey);

public readonly record struct FrameWritten(string Path, long Sequence, long Offset, int Length, byte[] Sha256);

public interface IRawLog
{
    FrameWritten Append(string dataspec, DateTimeOffset acquiredUtc, ReadOnlySpan<byte> payload);
    void CompleteDataspec(string dataspec);
    void Repair(Func<ReadOnlySpan<byte>, FrameClassification> classify);
}

public interface ILedger
{
    WatermarkState GetWatermark(string dataspec, int option);
    void SaveOpenFromTime(string dataspec, int option, string fromTime);
    void SaveResume(string dataspec, int option, string? fileName, string? fileTimestamp, int skipCount);
    void CommitWatermark(string dataspec, int option, string? lastFileTimestamp, DateTimeOffset utcNow);
    void NoteSuccess(string dataspec, int option, string result, int newCount, DateTimeOffset utcNow);
    void NoteFailure(string dataspec, int option, int code, bool suspendToday, string today, DateTimeOffset utcNow);
    void RejectOption1(string dataspec);
    long? FindByHash(string dataspec, byte[] sha256);
    long IndexRecord(IndexDraft draft);
    void AssignSource(IReadOnlyList<long> ids, string? sourceFile, string? sourceTimestamp);
    void RecordRun(RunDraft run);
    long BeginOb14(string meetingDay, DateTimeOffset utcNow);
    void AttachOb14(long generationId, long recordId);
    void ActivateOb14(long generationId);
    void DropOb14(long generationId);
    int? ActiveOb14Generation(string meetingDay);
    int ActiveOb14MemberCount(string meetingDay);
    IReadOnlyList<string> MeetingDays();
    IReadOnlyList<string> RaceKeysForDays(IReadOnlyCollection<string> days);
    IReadOnlyList<string> StoredDataspecs();
    bool WasFired(string jobKey);
    void MarkFired(string jobKey, DateTimeOffset utcNow);
}

public sealed class WatermarkState
{
    public string? Confirmed { get; init; }
    public string? ResumeFileName { get; init; }
    public string? ResumeTimestamp { get; init; }
    public int ResumeSkipCount { get; init; }
    public string? OpenFromTime { get; init; }
    public int ConsecutiveFailures { get; init; }
    public string? SuspendedOnDate { get; init; }
    public bool Option1Rejected { get; init; }
    public string? LastResult { get; init; }
    public int? LastNewCount { get; init; }
    public int? LastErrorCode { get; init; }
}

public sealed record IndexDraft(
    string Dataspec,
    string Path,
    long Sequence,
    long Offset,
    int Length,
    byte[] Sha256,
    string RecordId,
    string? MeetingDay,
    string? RaceKey,
    DateTimeOffset AcquiredUtc);

public sealed record RunDraft(
    string Dataspec,
    int Option,
    string? RealtimeKey,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    int RecordCount,
    int NewCount,
    int DuplicateCount,
    int JvReadCount,
    int JvDownloadCount,
    string? LastFileTimestamp,
    int ReturnCode,
    string Outcome);

public sealed class CollectorConfig
{
    public string? DataRoot { get; set; }
    public List<string> Dataspecs { get; set; } = [];
    public bool LaunchAtLogon { get; set; }
    public bool SettingsComplete { get; set; }
    public string? DifferentialEpoch { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StoredScheduleRow>? StoredSchedule { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<RealtimeDataRow>? RealtimeData { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<RealtimeScheduleRow>? RealtimeSchedule { get; set; }

    public CollectorConfig Copy() => new()
    {
        DataRoot = DataRoot,
        Dataspecs = [.. Dataspecs],
        LaunchAtLogon = LaunchAtLogon,
        SettingsComplete = SettingsComplete,
        DifferentialEpoch = DifferentialEpoch,
        StoredSchedule = StoredSchedule?.Select(row => row.Copy()).ToList(),
        RealtimeData = RealtimeData?.Select(row => row.Copy()).ToList(),
        RealtimeSchedule = RealtimeSchedule?.Select(row => row.Copy()).ToList(),
    };
}
