using System.Security.Cryptography;
using System.Text;
using JvDataCollector;

namespace JvDataCollector.Tests;

public class FrameTests
{
    [Fact]
    public void RoundTripKeepsBytesAndHash()
    {
        var payload = Encoding.ASCII.GetBytes("RA" + new string('1', 16) + "rest");
        using var stream = new MemoryStream();
        var hash = RawFrameCodec.Write(stream, 1, payload);
        stream.Position = 0;
        Assert.True(RawFrameCodec.TryRead(stream, out var sequence, out _, out var length, out var stored, out var read));
        Assert.Equal(1, sequence);
        Assert.Equal(payload.Length, length);
        Assert.Equal(payload, read);
        Assert.Equal(SHA256.HashData(payload), hash);
        Assert.Equal(hash, stored);
        Assert.False(RawFrameCodec.TryRead(stream, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void PartialTailIsRejected()
    {
        var payload = Encoding.ASCII.GetBytes("YS20261108");
        using var stream = new MemoryStream();
        RawFrameCodec.Write(stream, 1, payload);
        stream.Write(Encoding.ASCII.GetBytes("JVRAW"));
        stream.Position = 0;
        Assert.True(RawFrameCodec.TryRead(stream, out _, out _, out _, out _, out _));
        var end = stream.Position;
        Assert.False(RawFrameCodec.TryRead(stream, out _, out _, out _, out _, out _));
        Assert.Equal(end, stream.Position);
    }
}

public class LandingTests
{
    [Fact]
    public void RepairDropsPartialTailAndRebuildsIndex()
    {
        using var harness = new Harness();
        var payload = Encoding.ASCII.GetBytes("UM" + new string('A', 20));
        harness.Store.Append("DIFF", harness.Clock.UtcNow, payload);
        var file = Directory.EnumerateFiles(Path.Combine(harness.Root, "raw"), "*.bin", SearchOption.AllDirectories).Single();
        File.AppendAllText(file, "JVRAWpartial");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={harness.Store.LedgerPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM record_index";
            command.ExecuteNonQuery();
        }

        harness.Store.Repair(RecordKeys.Classify);
        harness.Store.CompleteDataspec("DIFF");
        using var check = File.OpenRead(file);
        Assert.True(RawFrameCodec.TryRead(check, out _, out _, out _, out var hash, out var read));
        Assert.Equal(payload, read);
        Assert.Equal(SHA256.HashData(payload), hash);
        Assert.False(RawFrameCodec.TryRead(check, out _, out _, out _, out _, out _));
        Assert.Equal(check.Length, check.Position);
        Assert.Equal(1, Payloads.Published(harness.Store.LedgerPath, "DIFF"));
    }

    [Fact]
    public void RotationCompletesTheFullFile()
    {
        using var harness = new Harness(maxFileBytes: 80);
        var first = Encoding.ASCII.GetBytes("HC" + new string('1', 20));
        var second = Encoding.ASCII.GetBytes("WC" + new string('2', 20));
        harness.Store.Append("SLOP", harness.Clock.UtcNow, first);
        harness.Store.Append("SLOP", harness.Clock.UtcNow, second);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={harness.Store.LedgerPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM raw_file WHERE complete=1";
        Assert.Equal(1, Convert.ToInt32(command.ExecuteScalar()));
    }
}

public class SessionTests
{
    [Fact]
    public void NoDataKeepsWatermarkAndCloses()
    {
        using var harness = new Harness();
        harness.Jv.OpenCode = -1;
        harness.Coordinator.Execute(Stored("YSCH"), CancellationToken.None);
        var mark = harness.Store.GetWatermark("YSCH", 1);
        Assert.Null(mark.Confirmed);
        Assert.Equal("該当なし", mark.LastResult);
        Assert.Contains("close", harness.Jv.Calls);
        Assert.DoesNotContain("read", harness.Jv.Calls);
    }

    [Fact]
    public void SuccessStoresBytesOnceAndSecondPassIsDuplicate()
    {
        using var harness = new Harness();
        var payload = new byte[32];
        Encoding.ASCII.GetBytes("YS").CopyTo(payload, 0);
        Encoding.ASCII.GetBytes("20261108").CopyTo(payload, 11);
        harness.Jv.Reads.Enqueue(Payloads.Data(payload, "YS.jvd"));
        harness.Jv.Reads.Enqueue(new ReadResult(-1, null, "YS.jvd"));
        harness.Jv.Reads.Enqueue(new ReadResult(0, null, null));
        harness.Coordinator.Execute(Stored("YSCH"), CancellationToken.None);
        Assert.Equal("20261102120000", harness.Store.GetWatermark("YSCH", 1).Confirmed);
        Assert.Contains("20261108", harness.Store.MeetingDays());

        harness.Jv.Reads.Enqueue(Payloads.Data(payload, "YS.jvd"));
        harness.Jv.Reads.Enqueue(new ReadResult(0, null, null));
        harness.Coordinator.Execute(Stored("YSCH"), CancellationToken.None);
        Assert.Equal(1, Payloads.Published(harness.Store.LedgerPath, "YSCH"));
        var file = Directory.EnumerateFiles(Path.Combine(harness.Root, "raw"), "*.bin", SearchOption.AllDirectories).Single();
        using var stream = File.OpenRead(file);
        Assert.True(RawFrameCodec.TryRead(stream, out _, out _, out _, out var hash, out var read));
        Assert.Equal(payload, read);
        Assert.Equal(SHA256.HashData(payload), hash);
    }

    [Fact]
    public void CancelKeepsWatermarkAndRepairRemovesPartialTail()
    {
        using var harness = new Harness();
        var payload = Encoding.ASCII.GetBytes("RA" + new string('3', 30));
        using var cancel = new CancellationTokenSource();
        var reads = 0;
        harness.Jv.OnRead = () =>
        {
            reads++;
            if (reads == 2)
            {
                cancel.Cancel();
            }
        };
        harness.Jv.Reads.Enqueue(Payloads.Data(payload, "RA.jvd"));
        harness.Jv.Reads.Enqueue(new ReadResult(-1, null, "RA.jvd"));
        harness.Jv.Reads.Enqueue(new ReadResult(0, null, null));
        harness.Coordinator.Execute(Stored("RACE"), cancel.Token);
        Assert.Null(harness.Store.GetWatermark("RACE", 1).Confirmed);
        Assert.Equal("20261102090000", harness.Store.GetWatermark("RACE", 1).ResumeTimestamp);
        Assert.Contains("cancel", harness.Jv.Calls);
        var file = Directory.EnumerateFiles(Path.Combine(harness.Root, "raw"), "*.bin", SearchOption.AllDirectories).Single();
        File.AppendAllText(file, "TAIL");
        harness.Store.Repair(RecordKeys.Classify);
        using var stream = File.OpenRead(file);
        Assert.True(RawFrameCodec.TryRead(stream, out _, out _, out _, out _, out var read));
        Assert.Equal(payload, read);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void SetupResumeSkipsCompletedFiles()
    {
        using var harness = new Harness();
        harness.Store.SaveResume("RACE", 4, "done.jvd", "20260101000000", 2);
        harness.Jv.Reads.Enqueue(new ReadResult(0, null, null));
        harness.Coordinator.Execute(new CollectionJob
        {
            Kind = JobKind.Stored,
            Priority = JobPriority.User,
            Dataspec = "RACE",
            Option = 4,
            Group = JobGroups.Setup,
        }, CancellationToken.None);
        Assert.Equal(2, harness.Jv.Calls.Count(call => call == "skip"));
    }

    [Fact]
    public void CorruptFileIsDeletedAndRetried()
    {
        using var harness = new Harness();
        var payload = Encoding.ASCII.GetBytes("SE" + new string('4', 10));
        harness.Jv.Reads.Enqueue(new ReadResult(-402, null, "bad.jvd"));
        harness.Jv.Reads.Enqueue(Payloads.Data(payload, "ok.jvd"));
        harness.Jv.Reads.Enqueue(new ReadResult(0, null, null));
        harness.Coordinator.Execute(Stored("RACE"), CancellationToken.None);
        Assert.Contains("delete:bad.jvd", harness.Jv.Calls);
        Assert.Equal(1, Payloads.Published(harness.Store.LedgerPath, "RACE"));
    }

    [Fact]
    public void Ob14SecondGenerationReplacesTheFirstAndNoDataDoesNot()
    {
        using var harness = new Harness();
        var first = Encoding.ASCII.GetBytes("WE" + new string('A', 8));
        var second = Encoding.ASCII.GetBytes("AV" + new string('B', 8));
        var third = Encoding.ASCII.GetBytes("JC" + new string('C', 8));
        RunRealtime(harness, "0B14", "20261108", first);
        RunRealtime(harness, "0B14", "20261108", second, third);
        Assert.Equal(2, harness.Store.ActiveOb14Generation("20261108"));
        Assert.Equal(2, harness.Store.ActiveOb14MemberCount("20261108"));
        harness.Jv.RtCode = -1;
        harness.Coordinator.Execute(Realtime("0B14", "20261108"), CancellationToken.None);
        Assert.Equal(2, harness.Store.ActiveOb14Generation("20261108"));
    }

    [Fact]
    public void WeeklyRereadDoesNotMoveWatermarkBackward()
    {
        using var harness = new Harness();
        harness.Store.CommitWatermark("RACE", 1, "20261102000000", harness.Clock.UtcNow);
        harness.Jv.LastTimestamp = "20261001000000";
        harness.Jv.Reads.Enqueue(new ReadResult(0, null, null));
        harness.Coordinator.Execute(new CollectionJob
        {
            Kind = JobKind.Stored,
            Priority = JobPriority.Scheduled,
            Dataspec = "RACE",
            Option = 1,
            WeeklyReread = true,
        }, CancellationToken.None);
        Assert.Equal("20261102000000", harness.Store.GetWatermark("RACE", 1).Confirmed);
    }

    [Fact]
    public void AgreementErrorNeedsAttentionAndBlocksTheSchedule()
    {
        using var harness = new Harness();
        harness.Jv.OpenCode = -305;
        harness.Coordinator.Execute(Stored("RACE"), CancellationToken.None);
        Assert.True(harness.Attention.HasAttention);
        Assert.True(harness.Coordinator.ScheduleBlocked);
        var before = harness.Coordinator.Queue.Count;
        harness.Coordinator.CatchUp(harness.Clock.UtcNow);
        Assert.Equal(before, harness.Coordinator.Queue.Count);
    }

    [Fact]
    public void SixTransientFailuresSuspendTheDataspecForTheDay()
    {
        using var harness = new Harness();
        harness.Jv.OpenCode = -503;
        var job = Stored("WOOD");
        for (var i = 0; i < 6; i++)
        {
            harness.Coordinator.Execute(job, CancellationToken.None);
        }

        var opens = harness.Jv.Calls.Count(call => call == "open");
        harness.Coordinator.Execute(job, CancellationToken.None);
        Assert.Equal(opens, harness.Jv.Calls.Count(call => call == "open"));
        Assert.False(harness.Attention.HasAttention);
        Assert.Equal(Jst.Day(harness.Clock.UtcNow), harness.Store.GetWatermark("WOOD", 1).SuspendedOnDate);
    }

    [Fact]
    public void CommOption1RejectionDropsItFromTheSchedule()
    {
        using var harness = new Harness();
        harness.Jv.OpenCode = -116;
        harness.Coordinator.Execute(Stored("COMM"), CancellationToken.None);
        Assert.True(harness.Store.GetWatermark("COMM", 1).Option1Rejected);
        Assert.False(harness.Attention.HasAttention);
        harness.Coordinator.RequestUserFetch();
        var specs = new List<string>();
        while (harness.Coordinator.Queue.TakeDue(harness.Clock.UtcNow) is { } job)
        {
            specs.Add(job.Dataspec);
        }

        Assert.DoesNotContain("COMM", specs);
        Assert.Contains("RACE", specs);
    }

    [Fact]
    public void LowDiskStopsBeforeOpen()
    {
        using var harness = new Harness();
        harness.Disk.Available = 1024;
        harness.Coordinator.Execute(Stored("RACE"), CancellationToken.None);
        Assert.DoesNotContain("open", harness.Jv.Calls);
        Assert.True(harness.Attention.HasAttention);
    }

    private static void RunRealtime(Harness harness, string spec, string key, params byte[][] payloads)
    {
        foreach (var payload in payloads)
        {
            harness.Jv.Reads.Enqueue(Payloads.Data(payload));
        }

        harness.Jv.Reads.Enqueue(new ReadResult(0, null, null));
        harness.Coordinator.Execute(Realtime(spec, key), CancellationToken.None);
    }

    private static CollectionJob Stored(string spec) => new()
    {
        Kind = JobKind.Stored,
        Priority = JobPriority.Scheduled,
        Dataspec = spec,
        Option = 1,
    };

    private static CollectionJob Realtime(string spec, string key) => new()
    {
        Kind = JobKind.Realtime,
        Priority = JobPriority.RealtimeScheduled,
        Dataspec = spec,
        Key = key,
        MeetingDay = key,
    };
}

public class ScheduleTests
{
    [Fact]
    public void MissedMondaySlotsAreQueuedOnce()
    {
        using var harness = new Harness();
        var monday = new DateTimeOffset(2026, 11, 2, 5, 0, 0, TimeSpan.Zero);
        harness.Coordinator.CatchUp(monday);
        var count = harness.Coordinator.Queue.Count;
        Assert.Equal(DataspecCatalog.DefaultCodes.Count, count);
        harness.Coordinator.CatchUp(monday);
        Assert.Equal(count, harness.Coordinator.Queue.Count);
        var job = harness.Coordinator.Queue.TakeDue(monday);
        Assert.NotNull(job);
        Assert.True(job.WeeklyReread);
    }

    [Fact]
    public void EmptyScheduleTreatsWeekendAsMeetingDays()
    {
        var friday = new DateOnly(2026, 11, 6);
        Assert.True(MeetingCalendar.IsMeetingDay(friday, []));
        Assert.False(MeetingCalendar.IsMeetingDay(new DateOnly(2026, 11, 2), []));
        Assert.False(MeetingCalendar.IsMeetingDay(friday, ["20261108"]));
        Assert.True(MeetingCalendar.IsMeetingSide(friday, ["20261106"]));
        Assert.False(MeetingCalendar.IsMeetingSide(new DateOnly(2026, 11, 2), ["20261102"]));
        Assert.Equal(["20261106", "20261107", "20261108"], MeetingCalendar.UpcomingWeekend(new DateOnly(2026, 11, 5)).Select(day => day.ToString("yyyyMMdd")).ToArray());
    }

    [Fact]
    public void CustomStoredTimeReplacesTheDefaultSlots()
    {
        using var harness = new Harness();
        var monday = new DateTimeOffset(2026, 11, 2, 5, 0, 0, TimeSpan.Zero);
        harness.Config.Current.Dataspecs = ["RACE"];
        harness.Config.Current.StoredSchedule =
        [
            new StoredScheduleRow
            {
                Id = "c|race",
                Dataspec = "RACE",
                Enabled = true,
                Day = "毎日",
                Time = "01:00",
            },
        ];
        harness.Coordinator.Reload();
        harness.Coordinator.CatchUp(monday);
        Assert.Equal(1, harness.Coordinator.Queue.Count);
        var job = harness.Coordinator.Queue.TakeDue(monday);
        Assert.NotNull(job);
        Assert.Equal("RACE", job.Dataspec);
        Assert.False(job.WeeklyReread);
    }

    [Fact]
    public void EditorKeepsTheDefaultCaptionBesideANarrowSelection()
    {
        var rows = SchedulePlan.EditStored(new CollectorConfig { Dataspecs = ["RACE"] });
        Assert.Contains(rows, row => row.Dataspec == "SLOP" && !row.Enabled);
        Assert.Contains(rows, row => row.Dataspec == "RACE" && row.Enabled && ScheduleDefaults.StoredCaption(row.Id).Contains("13:30", StringComparison.Ordinal));
        Assert.Contains("取得する", ScheduleDefaults.StoredCaption(rows.First(row => row.Dataspec == "RACE").Id), StringComparison.Ordinal);
        var rules = SchedulePlan.EditRealtime(new CollectorConfig());
        var odds = rules.Single(row => row.Kind == ScheduleKind.OddsMeeting);
        Assert.Equal(3, odds.IntervalMinutes);
        Assert.Contains("09:00", ScheduleDefaults.RealtimeCaption(odds.Id), StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledRealtimeSpecIsLeftOutOfTheEvent()
    {
        using var harness = new Harness();
        harness.Config.Current.RealtimeData = DataspecCatalog.Realtime
            .Select(item => new RealtimeDataRow { Dataspec = item.Code, Enabled = item.Code != "0B11" })
            .ToList();
        harness.Coordinator.Reload();
        harness.Coordinator.OnRealtime(RealtimeEventKind.Weight, "2026110805010101");
        var specs = new List<string>();
        while (harness.Coordinator.Queue.TakeDue(harness.Clock.UtcNow) is { } job)
        {
            specs.Add(job.Dataspec);
        }

        Assert.Equal(["0B13", "0B17"], specs);
    }

    [Fact]
    public void OddsSweepKeepsTheConfiguredInterval()
    {
        using var harness = new Harness();
        var rules = ScheduleDefaults.RealtimeRows().Select(row => row.Copy()).ToList();
        rules.Single(row => row.Kind == ScheduleKind.OddsMeeting).IntervalMinutes = 10;
        harness.Config.Current.RealtimeSchedule = rules;
        harness.Coordinator.Reload();
        var start = new DateTimeOffset(2026, 11, 7, 1, 0, 0, TimeSpan.Zero);
        harness.Coordinator.MaybeEnqueueOdds(start);
        Assert.Equal(DataspecCatalog.OddsSweep.Length, harness.Coordinator.Queue.Count);
        while (harness.Coordinator.Queue.TakeDue(start) is { } job)
        {
            harness.Coordinator.OnJobFinished(job);
        }

        harness.Coordinator.MaybeEnqueueOdds(start.AddMinutes(4));
        Assert.Equal(0, harness.Coordinator.Queue.Count);
        harness.Coordinator.MaybeEnqueueOdds(start.AddMinutes(10));
        Assert.Equal(DataspecCatalog.OddsSweep.Length, harness.Coordinator.Queue.Count);
    }

    [Fact]
    public void WeightAndChangeEventsQueueTheRequiredSpecs()
    {
        var weight = RealtimePlanner.ForEvent(RealtimeEventKind.Weight, "2026110805010101");
        Assert.Equal(["0B11", "0B13", "0B17"], weight.Select(job => job.Dataspec).ToArray());
        var weather = RealtimePlanner.ForEvent(RealtimeEventKind.Weather, "2026110805010101");
        Assert.Equal(["0B16", "0B14", "0B15", "0B13", "0B17"], weather.Select(job => job.Dataspec).ToArray());
        Assert.Equal("20261108", weather.Single(job => job.Dataspec == "0B14").Key);
        var payoff = RealtimePlanner.ForEvent(RealtimeEventKind.Payoff, "2026110805010101");
        Assert.Equal(["0B12", "0B15"], payoff.Select(job => job.Dataspec).ToArray());
    }
}

public class QueueTests
{
    [Fact]
    public void UserFetchKeepsASingleWaitingRound()
    {
        var queue = new JobQueue();
        var jobs = new[]
        {
            Job("RACE"),
            Job("DIFF"),
        };
        Assert.True(queue.RequestRound(JobGroups.User, jobs));
        Assert.False(queue.RequestRound(JobGroups.User, jobs));
        var first = queue.TakeDue(DateTimeOffset.UtcNow);
        Assert.Equal("RACE", first!.Dataspec);
        queue.Complete(first, group => group == JobGroups.User ? jobs : []);
        var second = queue.TakeDue(DateTimeOffset.UtcNow);
        Assert.Equal("DIFF", second!.Dataspec);
        queue.Complete(second, group => group == JobGroups.User ? jobs : []);
        Assert.Equal("RACE", queue.TakeDue(DateTimeOffset.UtcNow)!.Dataspec);
    }

    [Fact]
    public void ShutdownPreemptsStoredJobs()
    {
        var queue = new JobQueue();
        queue.TryAdd(Job("RACE"));
        queue.TryAdd(new CollectionJob { Kind = JobKind.Shutdown, Priority = JobPriority.Shutdown });
        Assert.Equal(JobKind.Shutdown, queue.TakeDue(DateTimeOffset.UtcNow)!.Kind);
    }

    private static CollectionJob Job(string spec) => new()
    {
        Kind = JobKind.Stored,
        Priority = JobPriority.User,
        Group = JobGroups.User,
        Dataspec = spec,
        Option = 1,
    };
}

public class SecretTests
{
    [Fact]
    public void ConfigDoesNotContainTheServiceKeyAndTheLogScrubsIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keiba-dwh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configPath = Path.Combine(directory, "config.json");
            var store = new JsonConfigStore(configPath);
            store.Save(new CollectorConfig
            {
                DataRoot = directory,
                Dataspecs = ["RACE"],
                SettingsComplete = true,
                LaunchAtLogon = true,
                DifferentialEpoch = "20261102000000",
            });
            var json = File.ReadAllText(configPath);
            Assert.DoesNotContain("serviceKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("super-secret-key", json, StringComparison.Ordinal);

            var secretPath = Path.Combine(directory, "service-key.bin");
            var secrets = new DpapiSecretStore(secretPath);
            secrets.Save("super-secret-key");
            Assert.Equal("super-secret-key", secrets.Load());
            Assert.DoesNotContain("super-secret-key", Encoding.UTF8.GetString(File.ReadAllBytes(secretPath)), StringComparison.Ordinal);

            var log = new RollingFileLog(directory);
            log.NoteSecret("super-secret-key");
            log.Info("leak super-secret-key and " + SoftwareId.Value);
            var written = File.ReadAllText(Directory.EnumerateFiles(directory, "collector-*.log").Single());
            Assert.DoesNotContain("super-secret-key", written, StringComparison.Ordinal);
            Assert.DoesNotContain(SoftwareId.Value, written, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
