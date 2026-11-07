using System.Security.Cryptography;
using System.Text;
using DwhLoader;
using Microsoft.Win32;
using Npgsql;

namespace DwhLoader.Tests;

[Collection("loader")]
public sealed class AcceptanceTests
{
    private const string RaceKey = "2026110805010211";
    private const string WatermarkSql = """
        SELECT dataspec, option, confirmed, resume_file_name, resume_timestamp, resume_skip_count, open_fromtime,
               last_success_utc, last_error_code, last_result, last_new_count, consecutive_failures, suspended_on,
               option1_rejected, updated_utc
        FROM watermark ORDER BY dataspec, option
        """;

    private readonly PgCluster _pg;

    public AcceptanceTests(PgCluster pg) => _pg = pg;

    [Fact]
    public void IncompleteRaw_IsNotLoaded_AndWatermarkStays()
    {
        _pg.Reset();
        using var lab = new LedgerLab();
        lab.Sql("""
            INSERT INTO watermark (
              dataspec, option, confirmed, resume_file_name, resume_timestamp, resume_skip_count, open_fromtime,
              last_success_utc, last_error_code, last_result, last_new_count, consecutive_failures, suspended_on,
              option1_rejected, updated_utc)
            VALUES (
              'RACE', 1, '20261108000000', 'file', '20261108000000', 3, '20261101000000',
              '2026-11-08T00:00:00.0000000+00:00', 0, 'ok', 4, 1, '20261108', 0,
              '2026-11-08T00:00:00.0000000+00:00')
            """);
        var before = lab.Dump(WatermarkSql);
        var hidden = lab.Add("RACE", false, [("RA", Samples.Race('7', "20261108", "2026110805010299", (byte)'1'))], Samples.T0);
        var shown = lab.Add("RACE", true, [("RA", Samples.Race('7', "20261108", RaceKey, (byte)'2'))], Samples.T0);
        var result = Cycle(lab);
        Assert.Equal(1, result.Wrote);
        Assert.Equal(before, lab.Dump(WatermarkSql));
        Assert.Equal("0", lab.Dump($"SELECT complete FROM raw_file WHERE id = {hidden.FileId}").Trim());
        Assert.Equal([RaceKey], _pg.Lines("SELECT btrim(race_key) FROM jv.race"));
        Assert.Equal([shown.FileId.ToString()], _pg.Lines("SELECT ledger_file_id::text FROM jv.ingest_file ORDER BY 1"));

        var missing = Path.Combine(Path.GetTempPath(), "keiba-no-ledger-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(missing);
        var session = new LoaderSession(
            new RollingFileLog(Path.Combine(missing, "logs")),
            Settings(missing),
            "loader-secret-value");
        var down = session.RunOnce();
        Assert.True(down.LedgerDown);
        Assert.Equal("台帳を開けません", session.Snapshot.Headline);
        Assert.Equal("確認していません", session.Snapshot.Warehouse);
        Assert.False(session.Snapshot.ShowList);
        Assert.False(File.Exists(Path.Combine(missing, "state", "ledger.sqlite")));
        Assert.Equal(1, _pg.Count("SELECT count(*) FROM jv.race"));
    }

    [Fact]
    public void Versions_Stay_AndKubunZeroLeavesTheView()
    {
        _pg.Reset();
        using var lab = new LedgerLab();
        lab.Add("RACE", true, [("RA", Samples.Race('2', "20261101", RaceKey, (byte)'1'))], Samples.T0);
        lab.Add("RACE", true, [("RA", Samples.Race('2', "20261108", RaceKey, (byte)'2'))], Samples.T0);
        lab.Add("RACE", true, [("RA", Samples.Race('2', "20261108", RaceKey, (byte)'3'))], Samples.T1);
        lab.Add("RACE", true, [("RA", Samples.Race('0', "20261109", RaceKey, (byte)'4'))], Samples.T1);
        var result = Cycle(lab);
        Assert.Equal(4, result.Wrote);
        Assert.Equal(4, _pg.Count("SELECT count(*) FROM jv.race"));
        Assert.Equal(1, _pg.Count("SELECT count(*) FROM jv.race WHERE data_kubun = '0'"));
        Assert.Equal(
            ["RACE|2|20261108|2026-11-08 01:00:00"],
            _pg.Lines("""
                SELECT btrim(src_dataspec) || '|' || data_kubun || '|' || make_ymd || '|' ||
                       to_char(collected_at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS')
                FROM jv.v_race_program
                """));
    }

    [Fact]
    public void Ob14_FollowsTheActiveGeneration_AndKeepsThePreviousCopyOnFailure()
    {
        _pg.Reset();
        using var lab = new LedgerLab();
        var first = lab.Add("0B14", true, [("WE", Samples.Weather((byte)'1'))], Samples.T0);
        var second = lab.Add("0B14", true, [("WE", Samples.Weather((byte)'2'))], Samples.T1);
        var gen1 = lab.Generation("20261108", 1, true, first.Records[0].IndexId);
        Cycle(lab);
        Assert.Equal(["1|" + Hex(first.Records[0].Sha256) + "|WE"], Active());
        Assert.Equal([Hex(first.Records[0].Sha256)], Weather());

        lab.Sql("UPDATE ob14_generation SET active = 0 WHERE id = $id", ("$id", gen1));
        lab.Generation("20261108", 2, true, second.Records[0].IndexId);
        Cycle(lab);
        Assert.Equal(["2|" + Hex(second.Records[0].Sha256) + "|WE"], Active());
        Assert.Equal([Hex(second.Records[0].Sha256)], Weather());

        Cycle(lab);
        Assert.Equal(["2|" + Hex(second.Records[0].Sha256) + "|WE"], Active());

        lab.Sql("UPDATE ob14_generation SET active = 0");
        lab.Generation("20261108", 3, true, first.Records[0].IndexId);
        Cycle(lab);
        Assert.Equal(["3|" + Hex(first.Records[0].Sha256) + "|WE"], Active());
        Assert.Equal([Hex(first.Records[0].Sha256)], Weather());

        lab.Sql("UPDATE ob14_generation SET meeting_day = '20260231' WHERE active = 1");
        var kept = Cycle(lab);
        Assert.True(kept.Ob14Kept);
        Assert.Equal(["3|" + Hex(first.Records[0].Sha256) + "|WE"], Active());

        lab.Sql("UPDATE ob14_generation SET active = 0");
        Cycle(lab);
        Assert.Empty(Active());
    }

    [Fact]
    public void OpenLedger_CanBeReadWithoutWriting()
    {
        _pg.Reset();
        using var lab = new LedgerLab();
        lab.Sql("""
            INSERT INTO watermark (
              dataspec, option, confirmed, resume_file_name, resume_timestamp, resume_skip_count, open_fromtime,
              last_success_utc, last_error_code, last_result, last_new_count, consecutive_failures, suspended_on,
              option1_rejected, updated_utc)
            VALUES (
              'RACE', 1, '20261108000000', 'file', '20261108000000', 0, '',
              '', 0, '', 0, 0, '', 0, '2026-11-08T00:00:00.0000000+00:00')
            """);
        var file = lab.Add("RACE", true, [("RA", Samples.Race('2', "20261108", RaceKey, (byte)'5'))], Samples.T0);
        var before = lab.Dump(WatermarkSql);
        var indexBefore = lab.Dump("SELECT count(*) FROM record_index");
        lab.HoldWriter();
        using (new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var result = Cycle(lab);
            Assert.Equal(1, result.Wrote);
        }

        Assert.Equal(before, lab.Dump(WatermarkSql));
        Assert.Equal(indexBefore, lab.Dump("SELECT count(*) FROM record_index"));
        lab.ReleaseWriter();
        lab.Sql("UPDATE watermark SET last_new_count = 9 WHERE dataspec = 'RACE'");
        Assert.Contains("9", lab.Dump(WatermarkSql), StringComparison.Ordinal);
        Assert.Contains("SqliteOpenMode.ReadOnly", File.ReadAllText(Source("DwhLoader.Storage", "LedgerReader.cs")));
        Assert.Contains("PRAGMA query_only=ON", File.ReadAllText(Source("DwhLoader.Storage", "LedgerReader.cs")));
    }

    [Fact]
    public void CompletedFile_IsLoadedWithoutRestarting_AndTheSameIdDoesNotGrow()
    {
        _pg.Reset();
        using var lab = new LedgerLab();
        var file = lab.Add("RACE", false, [("RA", Samples.Race('2', "20261108", RaceKey, (byte)'6'))], Samples.T0);
        using var session = Session(lab);
        using var cancel = new CancellationTokenSource();
        var seen = new ManualResetEventSlim(false);
        var stable = 0;
        var beforeRun = RegistryValues();
        var thread = new Thread(() =>
        {
            FollowLoop.Run(
                () =>
                {
                    var result = session.RunOnce(cancel.Token);
                    if (_pg.Count("SELECT count(*) FROM jv.race") >= 1 && Interlocked.Increment(ref stable) >= 2)
                    {
                        seen.Set();
                    }

                    return result;
                },
                _ => { },
                (delay, token) =>
                {
                    Assert.Equal(TimeSpan.FromSeconds(5), delay);
                    token.WaitHandle.WaitOne(30);
                },
                cancel.Token);
        })
        {
            IsBackground = true,
        };
        thread.Start();
        Assert.False(seen.Wait(400));
        lab.Complete(file.FileId);
        Assert.True(seen.Wait(TimeSpan.FromSeconds(15)));
        cancel.Cancel();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, _pg.Count("SELECT count(*) FROM jv.race"));
        Assert.Equal(beforeRun, RegistryValues());
        _pg.Exec("DELETE FROM jv.ingest_file");
        Assert.Equal(1, session.RunOnce().Wrote);
        Assert.Equal(1, _pg.Count("SELECT count(*) FROM jv.race"));
        Assert.Equal(1, _pg.Count("SELECT count(*) FROM jv.ingest_file"));
    }

    [Fact]
    public void PredictionViews_AreReadableAfterTheCopy()
    {
        _pg.Reset();
        using var lab = new LedgerLab();
        lab.Add("RACE", true, [("RA", Samples.Race('2', "20261108", RaceKey, (byte)'7'))], Samples.T0);
        Assert.Equal(1, Cycle(lab).Wrote);
        foreach (var view in new[]
        {
            "v_race_program", "v_race_horse_program", "v_race_result", "v_race_horse_result",
            "v_jockey_change", "v_entry_cancel", "v_post_time_change", "v_weather", "v_course_change",
            "v_schedule", "v_horse", "v_trainer", "v_jockey", "v_owner", "v_breeder",
        })
        {
            _pg.Count($"SELECT count(*) FROM jv.{view}");
        }

        Assert.Equal(1, _pg.Count("SELECT count(*) FROM jv.v_race_program"));
        Assert.Equal(0, _pg.Count("SELECT count(*)::bigint FROM pg_roles WHERE rolname = 'keiba_loader'"));
    }

    [Fact]
    public void HashMismatch_DoesNotIngest_AndOtherFilesContinue()
    {
        _pg.Reset();
        using var lab = new LedgerLab();
        var bad = lab.Add("RACE", true, [("RA", Samples.Race('7', "20261108", RaceKey, (byte)'8'))], Samples.T0, corruptHash: true);
        var good = lab.Add("RACE", true, [("RA", Samples.Race('7', "20261108", "2026110805010212", (byte)'9'))], Samples.T0);
        var retry = new Dictionary<long, DateTimeOffset>();
        var now = Samples.T0;
        var first = Cycle(lab, retry, now);
        Assert.Equal(1, first.Wrote);
        Assert.Contains(first.Files, file => file.Id == bad.FileId && file.Result == "再試行待ち" && file.Reason.Contains("HashMismatchException"));
        Assert.Equal([good.FileId.ToString()], _pg.Lines("SELECT ledger_file_id::text FROM jv.ingest_file"));
        var second = Cycle(lab, retry, now.AddSeconds(4));
        Assert.Equal(0, second.Wrote);
        Assert.DoesNotContain(second.Files, file => file.Id == bad.FileId);
        var third = Cycle(lab, retry, now.AddSeconds(5));
        Assert.Contains(third.Files, file => file.Id == bad.FileId && file.Result == "再試行待ち");
        Assert.Equal([good.FileId.ToString()], _pg.Lines("SELECT ledger_file_id::text FROM jv.ingest_file"));
    }

    [Fact]
    public void UnknownRecord_GoesToUnparsed_AndAKnownRecordInTheSameFileIsKept()
    {
        _pg.Reset();
        using var lab = new LedgerLab();
        lab.Add("RACE", true, [
            ("RA", Samples.Race('2', "20261108", RaceKey, (byte)'A')),
            ("ZZ", Encoding.ASCII.GetBytes("ZZ1 ")),
        ], Samples.T0);
        lab.Add("RACE", true, [("CK", Encoding.ASCII.GetBytes("CK1     "))], Samples.T0);
        var bad = new byte[100];
        Array.Fill(bad, (byte)' ');
        bad[0] = (byte)'R';
        bad[1] = (byte)'A';
        var rejected = lab.Add("RACE", true, [("RA", bad)], Samples.T1);
        var result = Cycle(lab);
        Assert.Equal(2, result.Wrote);
        Assert.Contains(result.Files, file => file.Id == rejected.FileId && file.Reason.Contains("構造体長の不一致"));
        Assert.Equal(1, _pg.Count("SELECT count(*) FROM jv.race"));
        Assert.Equal(["CK", "ZZ"], _pg.Lines("SELECT record_id FROM jv.unparsed ORDER BY record_id"));
        Assert.Equal(2, _pg.Count("SELECT count(*) FROM jv.ingest_file"));
        Assert.Equal(0, _pg.Count($"SELECT count(*) FROM jv.ingest_file WHERE ledger_file_id = {rejected.FileId}"));
    }

    [Fact]
    public void ResultPrefersRaceKubun7_AndProgramPrefersTheSameKubunFrom0B15()
    {
        _pg.Reset();
        using var lab = new LedgerLab();
        lab.Add("RACE", true, [("RA", Samples.Race('7', "20261101", RaceKey, (byte)'B'))], Samples.T0);
        lab.Add("0B15", true, [("RA", Samples.Race('7', "20261108", RaceKey, (byte)'C'))], Samples.T1);
        lab.Add("RACE", true, [("RA", Samples.Race('2', "20261108", RaceKey, (byte)'D'))], Samples.T1);
        lab.Add("0B15", true, [("RA", Samples.Race('2', "20261101", RaceKey, (byte)'E'))], Samples.T0);
        Assert.Equal(4, Cycle(lab).Wrote);
        Assert.Equal(
            ["RACE|7|20261101"],
            _pg.Lines("SELECT btrim(src_dataspec) || '|' || data_kubun || '|' || make_ymd FROM jv.v_race_result"));
        Assert.Equal(
            ["0B15|2|20261101"],
            _pg.Lines("SELECT btrim(src_dataspec) || '|' || data_kubun || '|' || make_ymd FROM jv.v_race_program"));
    }

    [Fact]
    public void ExpandedAndPreviousLayouts_ShareColumns()
    {
        _pg.Reset();
        using var lab = new LedgerLab();
        var oldBytes = Samples.Breeder(false, "20261101", "OLDNAME");
        var newBytes = Samples.Breeder(true, "20261102", "NEWNAME");
        var oldRow = RecordExpander.Expand("BR", "DIFF", oldBytes, SHA256.HashData(oldBytes), Samples.T0).Single();
        var newRow = RecordExpander.Expand("BR", "DIFF", newBytes, SHA256.HashData(newBytes), Samples.T1).Single();
        Assert.Equal((short)0, oldRow.Columns.Single(column => column.Name == "layout_rev").Value);
        Assert.Equal((short)1, newRow.Columns.Single(column => column.Name == "layout_rev").Value);
        Assert.Equal("OLDNAME", oldRow.Columns.Single(column => column.Name == "breeder_name").Value);
        Assert.Equal("NEWNAME", newRow.Columns.Single(column => column.Name == "breeder_name").Value);
        lab.Add("DIFF", true, [("BR", oldBytes)], Samples.T0);
        lab.Add("DIFF", true, [("BR", newBytes)], Samples.T1);
        Assert.Equal(2, Cycle(lab).Wrote);
        Assert.Equal(
            ["0|010203|OLDNAME", "1|010203|NEWNAME"],
            _pg.Lines("SELECT layout_rev::text || '|' || btrim(breeder_code) || '|' || breeder_name FROM jv.breeder ORDER BY layout_rev"));
    }

    [Fact]
    public void MissingSchema_DoesNotCreateTables_AndAClosedPortDoesNotFailTheFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "keiba-empty-root-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        var missing = new LoaderSession(
            new RollingFileLog(Path.Combine(root, "logs")),
            new LoaderSettings
            {
                DataRoot = root,
                Host = "127.0.0.1",
                Port = _pg.Port,
                Database = _pg.EmptyDatabase,
                Username = "postgres",
            },
            "unused");
        var schema = missing.RunOnce();
        Assert.True(schema.SchemaMissing);
        Assert.Equal("スキーマ未適用", missing.Snapshot.Headline);
        Assert.Equal("接続できている", missing.Snapshot.Warehouse);
        Assert.False(missing.Snapshot.ShowList);
        using (var connection = _pg.Open(_pg.EmptyDatabase))
        using (var command = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'jv'", connection))
        {
            Assert.Equal(0L, (long)command.ExecuteScalar()!);
        }

        _pg.Reset();
        using var lab = new LedgerLab();
        lab.Add("RACE", true, [("RA", Samples.Race('2', "20261108", RaceKey, (byte)'F'))], Samples.T0);
        var down = new LoaderSession(
            new RollingFileLog(Path.Combine(lab.Root, "logs")),
            new LoaderSettings
            {
                DataRoot = lab.Root,
                Host = "127.0.0.1",
                Port = 1,
                Database = _pg.Database,
                Username = "postgres",
            },
            "loader-secret-value");
        var failed = down.RunOnce();
        Assert.True(failed.WarehouseDown);
        Assert.Equal("PostgreSQL に届きません", down.Snapshot.Headline);
        Assert.Equal("届かない", down.Snapshot.Warehouse);
        Assert.Equal(0, _pg.Count("SELECT count(*) FROM jv.ingest_file"));
        Assert.Equal(1, Session(lab).RunOnce().Wrote);
    }

    private List<string> Active() =>
        _pg.Lines("SELECT generation::text || '|' || encode(raw_sha256, 'hex') || '|' || btrim(record_id) FROM jv.ob14_active ORDER BY generation");

    private List<string> Weather() =>
        _pg.Lines("SELECT encode(raw_sha256, 'hex') FROM jv.v_weather");

    private CycleResult Cycle(LedgerLab lab, Dictionary<long, DateTimeOffset> retry = null, DateTimeOffset? now = null)
    {
        using var warehouse = new PostgresWarehouse("127.0.0.1", _pg.Port, _pg.Database, "postgres", "unused");
        return LoadCycle.Execute(
            new LedgerReader(lab.Root),
            warehouse,
            retry ?? [],
            now ?? Samples.T0,
            CancellationToken.None,
            new NullLoadLog(),
            null);
    }

    private LoaderSession Session(LedgerLab lab) =>
        new(new RollingFileLog(Path.Combine(lab.Root, "logs")), Settings(lab.Root), "loader-secret-value");

    private LoaderSettings Settings(string root) => new()
    {
        DataRoot = root,
        Host = "127.0.0.1",
        Port = _pg.Port,
        Database = _pg.Database,
        Username = "postgres",
    };

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static string Source(string project, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, project, file);
            if (File.Exists(path))
            {
                return path;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(file);
    }

    private static string RegistryValues()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (key is null)
        {
            return "";
        }

        return string.Join("\n", key.GetValueNames().Order().Select(name => name + "=" + key.GetValue(name)));
    }
}
