using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace DwhLoader;

public sealed class LedgerReader : ILedger
{
    private readonly string _root;

    public LedgerReader(string dataRoot) => _root = dataRoot;

    public IReadOnlyList<CompleteFile> ListComplete() =>
        Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, dataspec, path FROM raw_file WHERE complete = 1 ORDER BY id";
            using var reader = command.ExecuteReader();
            var files = new List<CompleteFile>();
            while (reader.Read())
            {
                files.Add(new CompleteFile(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
            }

            return (IReadOnlyList<CompleteFile>)files;
        });

    public IReadOnlyList<IndexedRecord> ReadIndex(CompleteFile file) =>
        Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT seq, record_id, sha256, "offset", length, acquired_utc
                FROM published_record
                WHERE raw_path = $path
                ORDER BY seq
                """;
            command.Parameters.AddWithValue("$path", file.Path);
            using var reader = command.ExecuteReader();
            var rows = new List<IndexedRecord>();
            while (reader.Read())
            {
                var acquired = reader.GetString(5);
                if (!DateTimeOffset.TryParse(acquired, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp))
                {
                    throw new RecordFormatException("値の形式が一致しません");
                }

                rows.Add(new IndexedRecord(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    (byte[])reader.GetValue(2),
                    reader.GetInt64(3),
                    reader.GetInt32(4),
                    stamp));
            }

            return (IReadOnlyList<IndexedRecord>)rows;
        });

    public byte[] ReadPayload(CompleteFile file, IndexedRecord record)
    {
        try
        {
            using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (record.Offset < 0 || record.Length < 0 || record.Offset > stream.Length || record.Length > stream.Length - record.Offset)
            {
                throw new PayloadOffsetException();
            }

            stream.Position = record.Offset;
            var buffer = new byte[record.Length];
            var read = 0;
            while (read < buffer.Length)
            {
                var count = stream.Read(buffer, read, buffer.Length - read);
                if (count == 0)
                {
                    throw new PayloadOffsetException();
                }

                read += count;
            }

            if (!SHA256.HashData(buffer).AsSpan().SequenceEqual(record.Sha256))
            {
                throw new HashMismatchException();
            }

            return buffer;
        }
        catch (Exception ex) when (ex is HashMismatchException or PayloadOffsetException or RecordFormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RecordFormatException("ファイルを読めません");
        }
    }

    public IReadOnlyList<Ob14Member> ReadActiveOb14() =>
        Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT a.meeting_day, a.generation, i.record_id, i.sha256
                FROM ob14_active a
                JOIN record_index i ON i.id = a.record_index_id
                """;
            using var reader = command.ExecuteReader();
            var rows = new List<Ob14Member>();
            while (reader.Read())
            {
                var day = FieldText.CalendarOrNull(reader.GetString(0));
                var recordId = reader.GetString(2);
                if (day is null || recordId.Length != 2)
                {
                    throw new Ob14CopyException("写しの値が不正です");
                }

                rows.Add(new Ob14Member(day.Value, reader.GetInt32(1), recordId, (byte[])reader.GetValue(3)));
            }

            return (IReadOnlyList<Ob14Member>)rows;
        });

    private T Read<T>(Func<SqliteConnection, T> body)
    {
        var path = Path.Combine(_root, "state", "ledger.sqlite");
        if (!File.Exists(path))
        {
            throw new LedgerUnavailableException("台帳を開けません");
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            };
            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            using (var timeout = connection.CreateCommand())
            {
                timeout.CommandText = "PRAGMA busy_timeout=5000";
                timeout.ExecuteNonQuery();
            }

            using (var queryOnly = connection.CreateCommand())
            {
                queryOnly.CommandText = "PRAGMA query_only=ON";
                queryOnly.ExecuteNonQuery();
            }

            return body(connection);
        }
        catch (Exception ex) when (ex is Ob14CopyException or RecordFormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SqliteException or IOException)
        {
            throw new LedgerUnavailableException("台帳を開けません");
        }
    }
}
