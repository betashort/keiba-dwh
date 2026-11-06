using System.Diagnostics;
using System.Globalization;
using System.Text;
using JvDataCollector;
using Xunit.Abstractions;

namespace JvDataCollector.Tests;

public class SetupWindowTests
{
    [Fact]
    public void RaceSetupOpensEachYearAndSealsOnce()
    {
        using var harness = new Harness();
        harness.Coordinator.Execute(Setup("RACE"), CancellationToken.None);
        var year = Jst.Now(harness.Clock.UtcNow).Year;
        Assert.Equal(year - 1986 + 1, harness.Jv.Opened.Count);
        Assert.Equal("19860101000000-19870101000000", harness.Jv.Opened[0].FromTime);
        Assert.Equal("20260101000000", harness.Jv.Opened[^1].FromTime);
        Assert.DoesNotContain(harness.Jv.Opened, item => item.FromTime.EndsWith("1231235959", StringComparison.Ordinal));
        Assert.Equal("20261102120000", harness.Store.GetWatermark("RACE", 4).Confirmed);
    }

    [Fact]
    public void FailedYearLeavesEarlierRecordsUnpublished()
    {
        using var harness = new Harness();
        var payload = Race(1);
        harness.Jv.OpenCodes = new Queue<int>([0, -503]);
        harness.Jv.Reads.Enqueue(Payloads.Data(payload, "RA.jvd"));
        harness.Jv.Reads.Enqueue(new ReadResult(-1, null, "RA.jvd"));
        harness.Jv.Reads.Enqueue(new ReadResult(0, null, null));
        harness.Coordinator.Execute(Setup("RACE"), CancellationToken.None);
        Assert.Equal(2, harness.Jv.Opened.Count);
        Assert.Null(harness.Store.GetWatermark("RACE", 4).Confirmed);
        Assert.Equal(0, Payloads.Published(harness.Store.LedgerPath, "RACE"));
        var file = Directory.EnumerateFiles(Path.Combine(harness.Root, "raw"), "*.bin", SearchOption.AllDirectories).Single();
        Assert.True(new FileInfo(file).Length > payload.Length);
    }

    [Fact]
    public void OtherDataspecsStayOnOneOpen()
    {
        using var harness = new Harness();
        harness.Coordinator.Execute(Setup("DIFF"), CancellationToken.None);
        Assert.Equal(["19860101000000"], harness.Jv.Opened.Select(item => item.FromTime).ToArray());
    }

    [Fact]
    public void WindowsResumeAtTheStoredRange()
    {
        var windows = SetupCalendar.Windows("20190101000000-20200101000000", 2026);
        Assert.Equal("20190101000000-20200101000000", windows[0]);
        Assert.Equal("20200101000000-20210101000000", windows[1]);
        Assert.Equal("20260101000000", windows[^1]);
        Assert.Equal(8, windows.Count);
    }

    [Fact]
    public void CancelBeforeBoundaryDropsTheOpenFile()
    {
        using var harness = new Harness();
        using var cancel = new CancellationTokenSource();
        var reads = 0;
        harness.Jv.OnRead = () =>
        {
            reads++;
            if (reads == 1)
            {
                cancel.Cancel();
            }
        };
        harness.Jv.Reads.Enqueue(Payloads.Data(Race(2), "RA.jvd"));
        harness.Coordinator.Execute(Stored("RACE"), cancel.Token);
        Assert.Null(harness.Store.GetWatermark("RACE", 1).ResumeTimestamp);
        Assert.Null(harness.Store.GetWatermark("RACE", 1).Confirmed);
        var file = Directory.EnumerateFiles(Path.Combine(harness.Root, "raw"), "*.bin", SearchOption.AllDirectories).Single();
        Assert.Equal(0, new FileInfo(file).Length);
    }

    [Fact]
    public void OversizedGetsBufferStoresOnlyTheRecord()
    {
        using var harness = new Harness();
        var payload = new byte[80];
        Encoding.ASCII.GetBytes("YS").CopyTo(payload, 0);
        Encoding.ASCII.GetBytes("20261108").CopyTo(payload, 11);
        harness.Jv.Reads.Enqueue(new ReadResult(32, payload, "YS.jvd"));
        harness.Jv.Reads.Enqueue(new ReadResult(-1, null, "YS.jvd"));
        harness.Jv.Reads.Enqueue(new ReadResult(0, null, null));
        harness.Coordinator.Execute(Stored("YSCH"), CancellationToken.None);
        var file = Directory.EnumerateFiles(Path.Combine(harness.Root, "raw"), "*.bin", SearchOption.AllDirectories).Single();
        using var stream = File.OpenRead(file);
        Assert.True(RawFrameCodec.TryRead(stream, out _, out _, out var length, out _, out var read));
        Assert.Equal(32, length);
        Assert.Equal(payload.AsSpan(0, 32).ToArray(), read);
    }

    private static CollectionJob Setup(string spec) => new()
    {
        Kind = JobKind.Stored,
        Priority = JobPriority.User,
        Group = JobGroups.Setup,
        Dataspec = spec,
        Option = 4,
    };

    private static CollectionJob Stored(string spec) => new()
    {
        Kind = JobKind.Stored,
        Priority = JobPriority.Scheduled,
        Dataspec = spec,
        Option = 1,
    };

    private static byte[] Race(int seed)
    {
        var payload = new byte[64];
        Encoding.ASCII.GetBytes("RA").CopyTo(payload, 0);
        Encoding.ASCII.GetBytes("20260101" + seed.ToString("D8", CultureInfo.InvariantCulture)).CopyTo(payload, 11);
        return payload;
    }
}

public class SetupTimingTests
{
    private readonly ITestOutputHelper _output;

    public SetupTimingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void BoundaryBatchIsFasterThanPerRecordSync()
    {
        const int baselineCount = 200;
        const int batchCount = 4_000;
        const int recordsPerFile = 80;

        using var harness = new Harness();
        var baseline = TimePerRecord(index =>
        {
            var payload = Payload(index);
            var hash = System.Security.Cryptography.SHA256.HashData(payload);
            var frame = harness.Store.Append("RACE", harness.Clock.UtcNow, payload, hash);
            var id = harness.Store.IndexRecord(new IndexDraft(
                "RACE", frame.Path, frame.Sequence, frame.Offset, frame.Length, frame.Sha256,
                "RA", "20260101", "20260101" + index.ToString("D8", CultureInfo.InvariantCulture), harness.Clock.UtcNow));
            harness.Store.AssignSource([id], "base.jvd", "20260101000000");
        }, baselineCount);

        var batchWatch = Stopwatch.StartNew();
        var runner = new SessionRunner(harness.Jv, harness.Store, harness.Store, harness.Clock, new ListLog(), new NoDelay());
        for (var file = 0; file < batchCount / recordsPerFile; file++)
        {
            for (var row = 0; row < recordsPerFile; row++)
            {
                harness.Jv.Reads.Enqueue(Payloads.Data(Payload(baselineCount + file * recordsPerFile + row), "RA.jvd"));
            }

            harness.Jv.Reads.Enqueue(new ReadResult(-1, null, "RA.jvd"));
        }

        harness.Jv.Reads.Enqueue(new ReadResult(0, null, null));
        runner.Run(new CollectionJob
        {
            Kind = JobKind.Stored,
            Priority = JobPriority.User,
            Dataspec = "RACE",
            Option = 1,
        }, "test-service-key", Path.Combine(harness.Root, "jvlink"), "20260101000000", CancellationToken.None);
        batchWatch.Stop();
        var batch = batchWatch.Elapsed.TotalMilliseconds / batchCount;

        const double raceRecords = 5_000_000d;
        const double allRecords = 40_000_000d;
        var line = string.Join(Environment.NewLine,
        [
            $"baseline {baseline:F3} ms/record  ->  RACE {Hours(baseline, raceRecords):F1} h, all {Hours(baseline, allRecords):F1} h",
            $"batched  {batch:F3} ms/record ({recordsPerFile} records/file)  ->  RACE {Hours(batch, raceRecords):F2} h, all {Hours(batch, allRecords):F2} h",
            $"speedup  {baseline / batch:F1}x",
        ]);
        _output.WriteLine(line);
        Console.WriteLine(line);
        Assert.True(batch < baseline, line);
    }

    private static double TimePerRecord(Action<int> write, int count)
    {
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            write(i);
        }

        watch.Stop();
        return watch.Elapsed.TotalMilliseconds / count;
    }

    private static double Hours(double millisecondsPerRecord, double records) =>
        millisecondsPerRecord * records / 1000d / 3600d;

    private static byte[] Payload(int index)
    {
        var payload = new byte[256];
        Encoding.ASCII.GetBytes("RA").CopyTo(payload, 0);
        var key = "20260101" + (index % 10_000_000).ToString("D8", CultureInfo.InvariantCulture);
        Encoding.ASCII.GetBytes(key).CopyTo(payload, 11);
        BitConverter.TryWriteBytes(payload.AsSpan(40), index);
        return payload;
    }
}
