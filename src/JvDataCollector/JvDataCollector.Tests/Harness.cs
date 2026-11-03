using System.Security.Cryptography;
using JvDataCollector;
using Microsoft.Data.Sqlite;

namespace JvDataCollector.Tests;

public sealed class ManualClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 2, 5, 0, 0, TimeSpan.Zero);
}

public sealed class NoDelay : IDelay
{
    public void Wait(TimeSpan delay, CancellationToken cancel)
    {
    }
}

public sealed class MemoryConfig : IConfigStore
{
    public CollectorConfig Current { get; set; } = new()
    {
        Dataspecs = [.. DataspecCatalog.DefaultCodes],
        SettingsComplete = true,
        LaunchAtLogon = true,
        DifferentialEpoch = "20261102000000",
    };

    public CollectorConfig Load() => Current.Copy();

    public void Save(CollectorConfig config) => Current = config.Copy();
}

public sealed class MemorySecret : ISecretStore
{
    public string? Value { get; set; } = "test-service-key";

    public bool Exists => !string.IsNullOrEmpty(Value);

    public string? Load() => Value;

    public void Save(string serviceKey) => Value = serviceKey;
}

public sealed class FixedDisk : IDiskSpace
{
    public long Available { get; set; } = JvCodes.MinimumFreeBytes;

    public long AvailableBytes(string path) => Available;
}

public sealed class ListLog : IAppLog
{
    public List<string> Lines { get; } = [];

    public void Info(string message) => Lines.Add(message);

    public void Error(string message) => Lines.Add(message);

    public void NoteSecret(string? secret)
    {
    }
}

public sealed class FakeJv : IJvGateway
{
    public int OpenCode { get; set; }
    public int RtCode { get; set; }
    public string LastTimestamp { get; set; } = "20261102120000";
    public int DownloadCount { get; set; } = 1;
    public string? FileStamp { get; set; } = "20261102090000";
    public Queue<ReadResult> Reads { get; } = new();
    public List<string> Calls { get; } = [];
    public Action? OnRead { get; set; }

    public int Init(string softwareId)
    {
        Calls.Add("init");
        return 0;
    }

    public int SetServiceKey(string serviceKey)
    {
        Calls.Add("key");
        return 0;
    }

    public int SetSavePath(string path)
    {
        Calls.Add("path");
        return 0;
    }

    public int SetSaveFlag(int flag) => 0;

    public int SetUiProperties() => 0;

    public OpenResult Open(string dataspec, string fromTime, int option)
    {
        Calls.Add("open");
        return new OpenResult(OpenCode, 1, DownloadCount, LastTimestamp);
    }

    public OpenResult RtOpen(string dataspec, string key)
    {
        Calls.Add("rt");
        return new OpenResult(RtCode, 0, 0, null);
    }

    public int Status() => DownloadCount;

    public ReadResult Read()
    {
        Calls.Add("read");
        OnRead?.Invoke();
        return Reads.Count == 0 ? new ReadResult(0, null, null) : Reads.Dequeue();
    }

    public void Skip() => Calls.Add("skip");

    public void CancelDownload() => Calls.Add("cancel");

    public int Close()
    {
        Calls.Add("close");
        return 0;
    }

    public int DeleteFile(string fileName)
    {
        Calls.Add("delete:" + fileName);
        return 0;
    }

    public string? CurrentFileTimeStamp() => FileStamp;

    public void StartWatch(Action<RealtimeEventKind, string> onEvent)
    {
    }

    public void StopWatch()
    {
    }
}

public sealed class Harness : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "keiba-dwh-tests", Guid.NewGuid().ToString("N"));
    public LandingStore Store { get; }
    public FakeJv Jv { get; } = new();
    public ManualClock Clock { get; } = new();
    public MemoryConfig Config { get; } = new();
    public MemorySecret Secrets { get; } = new();
    public AttentionBoard Attention { get; } = new();
    public FixedDisk Disk { get; } = new();
    public CollectionCoordinator Coordinator { get; }

    public Harness(long? maxFileBytes = null)
    {
        Directory.CreateDirectory(Root);
        Config.Current.DataRoot = Root;
        Store = new LandingStore(Root, new NullLog(), maxFileBytes);
        Coordinator = new CollectionCoordinator(
            Config,
            Secrets,
            Store,
            Store,
            Jv,
            Clock,
            new ListLog(),
            Attention,
            Disk,
            new NoDelay());
    }

    public void Dispose()
    {
        Store.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public static class Payloads
{
    public static byte[] Record(string id, string body)
    {
        var text = id + body;
        return System.Text.Encoding.ASCII.GetBytes(text);
    }

    public static ReadResult Data(byte[] payload, string? file = null) => new(payload.Length, payload, file);

    public static int Published(string ledger, string dataspec)
    {
        using var connection = new SqliteConnection($"Data Source={ledger};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM published_record WHERE dataspec=$d";
        command.Parameters.AddWithValue("$d", dataspec);
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
