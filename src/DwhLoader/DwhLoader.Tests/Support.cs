using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using DwhLoader;
using Microsoft.Data.Sqlite;
using Npgsql;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DwhLoader.Tests;

public sealed class PgCluster : IDisposable
{
    private readonly string _bin;
    private readonly string _dir;
    private bool _started;

    public PgCluster()
    {
        Database = "loader_it";
        EmptyDatabase = "loader_empty";
        if (Database == "keiba" || EmptyDatabase == "keiba")
        {
            throw new InvalidOperationException("テストは keiba に接続しません");
        }

        _bin = @"C:\Program Files\PostgreSQL\16\bin";
        if (!File.Exists(Path.Combine(_bin, "initdb.exe")))
        {
            throw new InvalidOperationException("PostgreSQL 16 の initdb が見つかりません");
        }

        _dir = Path.Combine(Path.GetTempPath(), "keiba-dwh-loader-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
        Port = FreePort();
        Run("initdb.exe", $"-D \"{_dir}\" -U postgres -A trust -E UTF8 --locale=C --no-instructions");
        File.AppendAllText(Path.Combine(_dir, "postgresql.conf"), $"\nlisten_addresses = '127.0.0.1'\nport = {Port}\n");
        Run("pg_ctl.exe", $"start -D \"{_dir}\" -l \"{Path.Combine(_dir, "server.log")}\" -W");
        WaitUntilReady();
        _started = true;
        using (var admin = Open("postgres"))
        {
            Exec(admin, $"CREATE DATABASE {Database}");
            Exec(admin, $"CREATE DATABASE {EmptyDatabase}");
        }

        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "jv.sql"));
        using var conn = Open(Database);
        using var command = conn.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public int Port { get; }

    public string Database { get; }

    public string EmptyDatabase { get; }

    public NpgsqlConnection Open(string database = null)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = Port,
            Database = database ?? Database,
            Username = "postgres",
            Timeout = 15,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    public void Reset()
    {
        Exec("""
            DO $$ DECLARE name text;
            BEGIN
              FOR name IN SELECT tablename FROM pg_tables WHERE schemaname = 'jv' LOOP
                EXECUTE 'TRUNCATE TABLE jv.' || quote_ident(name) || ' CASCADE';
              END LOOP;
            END $$;
            """);
    }

    public long Count(string sql)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(sql, connection);
        return (long)command.ExecuteScalar()!;
    }

    public List<string> Lines(string sql)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(sql, connection);
        using var reader = command.ExecuteReader();
        var lines = new List<string>();
        while (reader.Read())
        {
            var parts = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                parts[i] = reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString();
            }

            lines.Add(string.Join("|", parts));
        }

        return lines;
    }

    public void Exec(string sql)
    {
        using var connection = Open();
        Exec(connection, sql);
    }

    public void Dispose()
    {
        NpgsqlConnection.ClearAllPools();
        if (_started)
        {
            try
            {
                Run("pg_ctl.exe", $"stop -D \"{_dir}\" -m immediate -w -t 30");
            }
            catch (InvalidOperationException)
            {
            }
        }

        for (var i = 0; i < 5; i++)
        {
            try
            {
                if (Directory.Exists(_dir))
                {
                    Directory.Delete(_dir, true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static void Exec(NpgsqlConnection connection, string sql)
    {
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private void WaitUntilReady()
    {
        var started = DateTime.UtcNow;
        Exception last = null;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(30))
        {
            try
            {
                using var connection = Open("postgres");
                return;
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException or SocketException)
            {
                last = ex;
                Thread.Sleep(200);
            }
        }

        var log = Path.Combine(_dir, "server.log");
        var tail = File.Exists(log) ? File.ReadAllText(log) : "";
        throw new InvalidOperationException("PostgreSQL が立ち上がりません" + Environment.NewLine + tail, last);
    }

    private void Run(string file, string args)
    {
        var info = new ProcessStartInfo(Path.Combine(_bin, file), args)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var process = Process.Start(info)!;
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(file);
        }

        var finished = Task.WaitAll([stdoutTask, stderrTask], 2000);
        var stdout = finished ? stdoutTask.Result : "";
        var stderr = finished ? stderrTask.Result : "";
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(file + " " + process.ExitCode + Environment.NewLine + stderr + stdout);
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public sealed class LedgerLab : IDisposable
{
    private readonly string _root;
    private SqliteConnection _hold;

    public LedgerLab()
    {
        _root = Path.Combine(Path.GetTempPath(), "keiba-dwh-ledger-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(_root, "state"));
        Directory.CreateDirectory(Path.Combine(_root, "raw"));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE watermark (
              dataspec TEXT NOT NULL,
              option INTEGER NOT NULL,
              confirmed TEXT,
              resume_file_name TEXT,
              resume_timestamp TEXT,
              resume_skip_count INTEGER NOT NULL DEFAULT 0,
              open_fromtime TEXT,
              last_success_utc TEXT,
              last_error_code INTEGER,
              last_result TEXT,
              last_new_count INTEGER,
              consecutive_failures INTEGER NOT NULL DEFAULT 0,
              suspended_on TEXT,
              option1_rejected INTEGER NOT NULL DEFAULT 0,
              updated_utc TEXT NOT NULL,
              PRIMARY KEY (dataspec, option)
            );
            CREATE TABLE raw_file (
              id INTEGER PRIMARY KEY,
              dataspec TEXT NOT NULL,
              path TEXT NOT NULL UNIQUE,
              first_seq INTEGER,
              last_seq INTEGER,
              complete INTEGER NOT NULL DEFAULT 0,
              byte_length INTEGER NOT NULL DEFAULT 0,
              created_utc TEXT NOT NULL,
              completed_utc TEXT
            );
            CREATE TABLE record_index (
              id INTEGER PRIMARY KEY,
              dataspec TEXT NOT NULL,
              record_id TEXT NOT NULL,
              sha256 BLOB NOT NULL,
              file_id INTEGER NOT NULL REFERENCES raw_file(id),
              seq INTEGER NOT NULL,
              offset INTEGER NOT NULL,
              length INTEGER NOT NULL,
              source_file TEXT,
              source_timestamp TEXT,
              acquired_utc TEXT NOT NULL,
              UNIQUE (dataspec, sha256)
            );
            CREATE TABLE ob14_generation (
              id INTEGER PRIMARY KEY,
              meeting_day TEXT NOT NULL,
              generation INTEGER NOT NULL,
              active INTEGER NOT NULL,
              created_utc TEXT NOT NULL,
              UNIQUE (meeting_day, generation)
            );
            CREATE TABLE ob14_generation_record (
              generation_id INTEGER NOT NULL REFERENCES ob14_generation(id) ON DELETE CASCADE,
              record_index_id INTEGER NOT NULL REFERENCES record_index(id),
              PRIMARY KEY (generation_id, record_index_id)
            );
            CREATE VIEW published_record AS
            SELECT r.id, r.dataspec, r.record_id, r.sha256, f.path AS raw_path, r.seq, r.offset, r.length,
                   r.source_file, r.source_timestamp, r.acquired_utc
            FROM record_index r
            JOIN raw_file f ON f.id = r.file_id
            WHERE f.complete = 1;
            CREATE VIEW ob14_active AS
            SELECT g.meeting_day, g.generation, r.record_index_id
            FROM ob14_generation g
            JOIN ob14_generation_record r ON r.generation_id = g.id
            JOIN record_index i ON i.id = r.record_index_id
            JOIN raw_file f ON f.id = i.file_id
            WHERE g.active = 1 AND f.complete = 1;
            """;
        command.ExecuteNonQuery();
    }

    public string Root => _root;

    public StoredFile Add(
        string dataspec,
        bool complete,
        IReadOnlyList<(string RecordId, byte[] Payload)> records,
        DateTimeOffset acquired,
        bool corruptHash = false,
        int pad = 8)
    {
        var path = Path.Combine(_root, "raw", Guid.NewGuid().ToString("n") + ".bin");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            stream.Write(new byte[pad]);
            foreach (var record in records)
            {
                stream.Write(record.Payload);
            }
        }

        var stamp = acquired.ToUniversalTime().ToString("O");
        using var connection = Open();
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO raw_file (dataspec, path, first_seq, last_seq, complete, byte_length, created_utc, completed_utc)
            VALUES ($d, $p, 1, $last, $c, $n, $t, $done);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$d", dataspec);
        insert.Parameters.AddWithValue("$p", path);
        insert.Parameters.AddWithValue("$last", records.Count);
        insert.Parameters.AddWithValue("$c", complete ? 1 : 0);
        insert.Parameters.AddWithValue("$n", new FileInfo(path).Length);
        insert.Parameters.AddWithValue("$t", stamp);
        insert.Parameters.AddWithValue("$done", complete ? stamp : (object)DBNull.Value);
        var fileId = (long)insert.ExecuteScalar()!;
        var stored = new List<StoredRecord>();
        var offset = (long)pad;
        for (var i = 0; i < records.Count; i++)
        {
            var sha = corruptHash ? new byte[32] : SHA256.HashData(records[i].Payload);
            using var index = connection.CreateCommand();
            index.CommandText = """
                INSERT INTO record_index (dataspec, record_id, sha256, file_id, seq, offset, length, acquired_utc)
                VALUES ($d, $r, $s, $f, $seq, $o, $n, $t);
                SELECT last_insert_rowid();
                """;
            index.Parameters.AddWithValue("$d", dataspec);
            index.Parameters.AddWithValue("$r", records[i].RecordId);
            index.Parameters.AddWithValue("$s", sha);
            index.Parameters.AddWithValue("$f", fileId);
            index.Parameters.AddWithValue("$seq", i + 1);
            index.Parameters.AddWithValue("$o", offset);
            index.Parameters.AddWithValue("$n", records[i].Payload.Length);
            index.Parameters.AddWithValue("$t", stamp);
            stored.Add(new StoredRecord((long)index.ExecuteScalar()!, records[i].RecordId, sha));
            offset += records[i].Payload.Length;
        }

        return new StoredFile(fileId, path, stored);
    }

    public void Complete(long fileId)
    {
        Sql("UPDATE raw_file SET complete = 1, completed_utc = created_utc WHERE id = $id", ("$id", fileId));
    }

    public long Generation(string meetingDay, int generation, bool active, long recordIndexId)
    {
        using var connection = Open();
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO ob14_generation (meeting_day, generation, active, created_utc)
            VALUES ($d, $g, $a, $t);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$d", meetingDay);
        insert.Parameters.AddWithValue("$g", generation);
        insert.Parameters.AddWithValue("$a", active ? 1 : 0);
        insert.Parameters.AddWithValue("$t", "2026-11-08T00:00:00.0000000+00:00");
        var id = (long)insert.ExecuteScalar()!;
        Link(id, recordIndexId);
        return id;
    }

    public void Link(long generationId, long recordIndexId)
    {
        Sql(
            "INSERT INTO ob14_generation_record (generation_id, record_index_id) VALUES ($g, $r)",
            ("$g", generationId),
            ("$r", recordIndexId));
    }

    public void Sql(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    public string Dump(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var builder = new StringBuilder();
        while (reader.Read())
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (i > 0)
                {
                    builder.Append('|');
                }

                builder.Append(reader.IsDBNull(i) ? "" : reader.GetValue(i));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    public void HoldWriter()
    {
        _hold = Open();
        using var command = _hold.CreateCommand();
        command.CommandText = "BEGIN";
        command.ExecuteNonQuery();
    }

    public void ReleaseWriter()
    {
        using (var command = _hold.CreateCommand())
        {
            command.CommandText = "COMMIT";
            command.ExecuteNonQuery();
        }

        _hold.Dispose();
        _hold = null;
    }

    public void Dispose()
    {
        _hold?.Dispose();
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, "state", "ledger.sqlite"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }
}

public sealed record StoredRecord(long IndexId, string RecordId, byte[] Sha256);

public sealed record StoredFile(long FileId, string Path, IReadOnlyList<StoredRecord> Records);

public static class Samples
{
    public static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-08T00:00:00.0000000+00:00");

    public static readonly DateTimeOffset T1 = DateTimeOffset.Parse("2026-11-08T01:00:00.0000000+00:00");

    public static byte[] Record(int length, string recordId, char kubun, string makeYmd, params (int At, string Text)[] fields)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, (byte)' ');
        Encoding.ASCII.GetBytes(recordId, 0, 2, bytes, 0);
        bytes[2] = (byte)kubun;
        Encoding.ASCII.GetBytes(makeYmd, 0, 8, bytes, 3);
        foreach (var (at, text) in fields)
        {
            Encoding.ASCII.GetBytes(text, 0, text.Length, bytes, at);
        }

        bytes[^2] = 13;
        bytes[^1] = 10;
        return bytes;
    }

    public static byte[] Race(char kubun, string makeYmd, string raceKey, byte mark)
    {
        var bytes = Record(1272, "RA", kubun, makeYmd, (11, raceKey));
        bytes[400] = mark;
        return bytes;
    }

    public static byte[] Weather(byte mark)
    {
        var bytes = Record(42, "WE", '1', "20261108", (11, "20261108050102"));
        bytes[34] = mark;
        return bytes;
    }

    public static byte[] Breeder(bool expanded, string makeYmd, string name)
    {
        if (expanded)
        {
            var bytes = Record(545, "BR", '1', makeYmd);
            Encoding.ASCII.GetBytes("010203  ", bytes.AsSpan(11));
            Encoding.ASCII.GetBytes(name, bytes.AsSpan(91));
            return bytes;
        }

        var old = Record(537, "BR", '1', makeYmd);
        Encoding.ASCII.GetBytes("010203", old.AsSpan(11));
        Encoding.ASCII.GetBytes(name, old.AsSpan(87));
        return old;
    }

    public static void Sta(Action body)
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}

[CollectionDefinition("loader")]
public sealed class LoaderCollection : ICollectionFixture<PgCluster>
{
}
