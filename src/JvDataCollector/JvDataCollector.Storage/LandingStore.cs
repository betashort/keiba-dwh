using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace JvDataCollector;

public sealed class LandingStore : IRawLog, ILedger, IWriteBatch, IDisposable
{
    public const string PublishedRecordView = "published_record";
    public const string ActiveOb14View = "ob14_active";

    private readonly object _gate = new();
    private readonly object _repairGate = new();
    private SqliteConnection _connection;
    private string _root;
    private readonly long _maxFileBytes;
    private readonly IAppLog _log;
    private SqliteTransaction? _batch;
    private FileStream? _stream;
    private long _streamFileId;
    private string? _streamPath;
    private long _streamLength;
    private readonly Dictionary<string, long> _touched = new(StringComparer.OrdinalIgnoreCase);

    public LandingStore(string dataRoot, IAppLog log, long? maxFileBytes = null)
    {
        _root = dataRoot;
        _log = log;
        _maxFileBytes = maxFileBytes ?? RawFrameCodec.MaxFileBytes;
        Directory.CreateDirectory(Path.Combine(_root, "jvlink"));
        Directory.CreateDirectory(Path.Combine(_root, "raw"));
        Directory.CreateDirectory(Path.Combine(_root, "state"));
        var path = Path.Combine(_root, "state", "ledger.sqlite");
        _connection = new SqliteConnection($"Data Source={path};Mode=ReadWriteCreate;Cache=Shared");
        _connection.Open();
        using (var journal = _connection.CreateCommand())
        {
            journal.CommandText = "PRAGMA journal_mode=WAL;";
            journal.ExecuteScalar();
        }

        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        CreateSchema();
    }

    public void Reopen(string dataRoot)
    {
        lock (_repairGate)
        {
            lock (_gate)
            {
                if (string.Equals(_root, dataRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                DiscardWriteBatchUnlocked();
                CloseStream();
                _connection.Dispose();
                _root = dataRoot;
                Directory.CreateDirectory(Path.Combine(_root, "jvlink"));
                Directory.CreateDirectory(Path.Combine(_root, "raw"));
                Directory.CreateDirectory(Path.Combine(_root, "state"));
                var path = Path.Combine(_root, "state", "ledger.sqlite");
                _connection = new SqliteConnection($"Data Source={path};Mode=ReadWriteCreate;Cache=Shared");
                _connection.Open();
                using var pragma = _connection.CreateCommand();
                pragma.CommandText = "PRAGMA journal_mode=WAL;";
                pragma.ExecuteScalar();
                using var rest = _connection.CreateCommand();
                rest.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
                rest.ExecuteNonQuery();
                CreateSchema();
            }

            RepairIncomplete(RecordKeys.Classify);
        }
    }

    public string LedgerPath => Path.Combine(_root, "state", "ledger.sqlite");

    public void Dispose()
    {
        lock (_gate)
        {
            DiscardWriteBatchUnlocked();
            CloseStream();
            _connection.Dispose();
        }
    }

    public void BeginWriteBatch()
    {
        lock (_gate)
        {
            _batch ??= _connection.BeginTransaction();
        }
    }

    public void CommitWriteBatch()
    {
        lock (_gate)
        {
            if (_batch is null)
            {
                return;
            }

            _stream?.Flush(flushToDisk: true);
            _batch.Commit();
            _batch.Dispose();
            _batch = null;
            _touched.Clear();
        }
    }

    public void DiscardWriteBatch()
    {
        lock (_gate)
        {
            DiscardWriteBatchUnlocked();
        }
    }

    public void ReleaseWriter()
    {
        lock (_gate)
        {
            if (_batch is null)
            {
                CloseStream();
            }
        }
    }

    public FrameWritten Append(string dataspec, DateTimeOffset acquiredUtc, ReadOnlySpan<byte> payload) =>
        Append(dataspec, acquiredUtc, payload, SHA256.HashData(payload));

    public FrameWritten Append(string dataspec, DateTimeOffset acquiredUtc, ReadOnlySpan<byte> payload, byte[] sha256)
    {
        lock (_gate)
        {
            var hash = sha256.Length == RawFrameCodec.HashSize ? sha256 : SHA256.HashData(payload);
            var day = DateOnly.FromDateTime(Jst.Now(acquiredUtc).DateTime);
            var frameSize = RawFrameCodec.FrameSize(payload.Length);
            var current = PickFile(dataspec, day, frameSize);
            if (current is null)
            {
                current = CreateFile(dataspec, day);
            }

            EnsureStream(current);
            if (_batch is not null && !_touched.ContainsKey(current.Path))
            {
                _touched[current.Path] = _streamLength;
            }

            var sequence = current.LastSeq + 1;
            var offset = _streamLength + RawFrameCodec.HeaderSize;
            RawFrameCodec.Write(_stream!, sequence, payload, hash);
            _streamLength += frameSize;
            Execute(
                "UPDATE raw_file SET last_seq=$s, byte_length=$n WHERE id=$id",
                ("$s", sequence),
                ("$n", _streamLength),
                ("$id", current.Id));
            if (_batch is null)
            {
                _stream!.Flush(flushToDisk: true);
                CloseStream();
            }

            return new FrameWritten(current.Path, sequence, offset, payload.Length, hash);
        }
    }

    public void CompleteDataspec(string dataspec)
    {
        lock (_gate)
        {
            Execute(
                "UPDATE raw_file SET complete=1, completed_utc=$t WHERE dataspec=$d AND complete=0",
                ("$t", Stamp(DateTimeOffset.UtcNow)),
                ("$d", dataspec));
        }
    }

    public void Repair(Func<ReadOnlySpan<byte>, FrameClassification> classify)
    {
        lock (_repairGate)
        {
            RepairIncomplete(classify);
        }
    }

    private void RepairIncomplete(Func<ReadOnlySpan<byte>, FrameClassification> classify)
    {
        foreach (var file in IncompleteFiles())
        {
            RepairOpenFile(file, classify);
        }
    }

    private List<OpenRaw> IncompleteFiles()
    {
        lock (_gate)
        {
            using var command = NewCommand();
            command.CommandText = "SELECT id, path, dataspec FROM raw_file WHERE complete=0";
            var files = new List<OpenRaw>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                files.Add(new OpenRaw(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
            }

            return files;
        }
    }

    public WatermarkState GetWatermark(string dataspec, int option)
    {
        lock (_gate)
        {
            using var command = NewCommand();
            command.CommandText = """
                SELECT confirmed, resume_file_name, resume_timestamp, resume_skip_count, open_fromtime,
                       consecutive_failures, suspended_on, option1_rejected, last_result, last_new_count, last_error_code
                FROM watermark WHERE dataspec=$d AND option=$o
                """;
            Add(command, ("$d", dataspec), ("$o", option));
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return new WatermarkState();
            }

            return new WatermarkState
            {
                Confirmed = ReadString(reader, 0),
                ResumeFileName = ReadString(reader, 1),
                ResumeTimestamp = ReadString(reader, 2),
                ResumeSkipCount = reader.GetInt32(3),
                OpenFromTime = ReadString(reader, 4),
                ConsecutiveFailures = reader.GetInt32(5),
                SuspendedOnDate = ReadString(reader, 6),
                Option1Rejected = reader.GetInt32(7) != 0,
                LastResult = ReadString(reader, 8),
                LastNewCount = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                LastErrorCode = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            };
        }
    }

    public void SaveOpenFromTime(string dataspec, int option, string fromTime)
    {
        lock (_gate)
        {
            EnsureWatermark(dataspec, option);
            Execute(
                "UPDATE watermark SET open_fromtime=$f, updated_utc=$t WHERE dataspec=$d AND option=$o",
                ("$f", fromTime),
                ("$t", Stamp(DateTimeOffset.UtcNow)),
                ("$d", dataspec),
                ("$o", option));
        }
    }

    public void SaveResume(string dataspec, int option, string? fileName, string? fileTimestamp, int skipCount)
    {
        lock (_gate)
        {
            EnsureWatermark(dataspec, option);
            var current = GetWatermarkUnlocked(dataspec, option);
            var timestamp = string.IsNullOrEmpty(fileTimestamp) ? current.ResumeTimestamp : fileTimestamp;
            Execute(
                """
                UPDATE watermark
                SET resume_file_name=$n, resume_timestamp=$s, resume_skip_count=$c, updated_utc=$t
                WHERE dataspec=$d AND option=$o
                """,
                ("$n", fileName),
                ("$s", timestamp),
                ("$c", skipCount),
                ("$t", Stamp(DateTimeOffset.UtcNow)),
                ("$d", dataspec),
                ("$o", option));
        }
    }

    public void CommitWatermark(string dataspec, int option, string? lastFileTimestamp, DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            EnsureWatermark(dataspec, option);
            Execute(
                """
                UPDATE watermark
                SET confirmed=$c, resume_file_name=NULL, resume_timestamp=NULL, resume_skip_count=0, updated_utc=$t
                WHERE dataspec=$d AND option=$o
                """,
                ("$c", lastFileTimestamp),
                ("$t", Stamp(utcNow)),
                ("$d", dataspec),
                ("$o", option));
        }
    }

    public void NoteSuccess(string dataspec, int option, string result, int newCount, DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            EnsureWatermark(dataspec, option);
            Execute(
                """
                UPDATE watermark
                SET last_result=$r, last_new_count=$n, last_success_utc=$t, last_error_code=NULL,
                    consecutive_failures=0, suspended_on=NULL, updated_utc=$t
                WHERE dataspec=$d AND option=$o
                """,
                ("$r", result),
                ("$n", newCount),
                ("$t", Stamp(utcNow)),
                ("$d", dataspec),
                ("$o", option));
        }
    }

    public void NoteFailure(string dataspec, int option, int code, bool suspendToday, string today, DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            EnsureWatermark(dataspec, option);
            Execute(
                """
                UPDATE watermark
                SET last_result=$r, last_error_code=$c, consecutive_failures=consecutive_failures+1,
                    suspended_on=CASE WHEN $s=1 THEN $day ELSE suspended_on END, updated_utc=$t
                WHERE dataspec=$d AND option=$o
                """,
                ("$r", "失敗"),
                ("$c", code),
                ("$s", suspendToday ? 1 : 0),
                ("$day", today),
                ("$t", Stamp(utcNow)),
                ("$d", dataspec),
                ("$o", option));
        }
    }

    public void RejectOption1(string dataspec)
    {
        lock (_gate)
        {
            EnsureWatermark(dataspec, 1);
            Execute(
                "UPDATE watermark SET option1_rejected=1, updated_utc=$t WHERE dataspec=$d AND option=1",
                ("$t", Stamp(DateTimeOffset.UtcNow)),
                ("$d", dataspec));
        }
    }

    public long? FindByHash(string dataspec, byte[] sha256)
    {
        lock (_gate)
        {
            using var command = NewCommand();
            command.CommandText = "SELECT id FROM record_index WHERE dataspec=$d AND sha256=$h";
            Add(command, ("$d", dataspec));
            command.Parameters.Add("$h", SqliteType.Blob).Value = sha256;
            var value = command.ExecuteScalar();
            return value is long id ? id : null;
        }
    }

    public long IndexRecord(IndexDraft draft)
    {
        lock (_gate)
        {
            var existing = FindByHashUnlocked(draft.Dataspec, draft.Sha256);
            if (existing is long found)
            {
                RememberKeys(draft.MeetingDay, draft.RaceKey);
                return found;
            }

            var fileId = FileId(draft.Path, draft.Dataspec);
            using var command = NewCommand();
            command.CommandText = """
                INSERT INTO record_index
                    (dataspec, record_id, sha256, file_id, seq, offset, length, acquired_utc)
                VALUES ($d, $r, $h, $f, $s, $o, $n, $t)
                """;
            Add(command,
                ("$d", draft.Dataspec),
                ("$r", draft.RecordId),
                ("$f", fileId),
                ("$s", draft.Sequence),
                ("$o", draft.Offset),
                ("$n", draft.Length),
                ("$t", Stamp(draft.AcquiredUtc)));
            command.Parameters.Add("$h", SqliteType.Blob).Value = draft.Sha256;
            command.ExecuteNonQuery();
            var id = LastId();
            RememberKeys(draft.MeetingDay, draft.RaceKey);
            return id;
        }
    }

    public void AssignSource(IReadOnlyList<long> ids, string? sourceFile, string? sourceTimestamp)
    {
        lock (_gate)
        {
            foreach (var id in ids)
            {
                Execute(
                    "UPDATE record_index SET source_file=$f, source_timestamp=COALESCE($s, source_timestamp) WHERE id=$id",
                    ("$f", sourceFile),
                    ("$s", string.IsNullOrEmpty(sourceTimestamp) ? null : sourceTimestamp),
                    ("$id", id));
            }
        }
    }

    public void RecordRun(RunDraft run)
    {
        lock (_gate)
        {
            Execute(
                """
                INSERT INTO run_history
                    (dataspec, option, realtime_key, started_utc, finished_utc, record_count, new_count, duplicate_count,
                     jv_readcount, jv_downloadcount, lastfiletimestamp, return_code, outcome)
                VALUES ($d, $o, $k, $a, $b, $rc, $nc, $dc, $jr, $jd, $l, $c, $u)
                """,
                ("$d", run.Dataspec),
                ("$o", run.Option),
                ("$k", run.RealtimeKey),
                ("$a", Stamp(run.StartedUtc)),
                ("$b", Stamp(run.FinishedUtc)),
                ("$rc", run.RecordCount),
                ("$nc", run.NewCount),
                ("$dc", run.DuplicateCount),
                ("$jr", run.JvReadCount),
                ("$jd", run.JvDownloadCount),
                ("$l", run.LastFileTimestamp),
                ("$c", run.ReturnCode),
                ("$u", run.Outcome));
        }
    }

    public long BeginOb14(string meetingDay, DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            using var command = NewCommand();
            command.CommandText = """
                INSERT INTO ob14_generation (meeting_day, generation, active, created_utc)
                VALUES (
                    $d,
                    COALESCE((SELECT MAX(generation) FROM ob14_generation WHERE meeting_day=$d), 0) + 1,
                    0,
                    $t)
                """;
            Add(command, ("$d", meetingDay), ("$t", Stamp(utcNow)));
            command.ExecuteNonQuery();
            return LastId();
        }
    }

    public void AttachOb14(long generationId, long recordId)
    {
        lock (_gate)
        {
            Execute(
                "INSERT OR IGNORE INTO ob14_generation_record (generation_id, record_index_id) VALUES ($g, $r)",
                ("$g", generationId),
                ("$r", recordId));
        }
    }

    public void ActivateOb14(long generationId)
    {
        lock (_gate)
        {
            Execute(
                """
                UPDATE ob14_generation SET active=0
                WHERE meeting_day=(SELECT meeting_day FROM ob14_generation WHERE id=$id);
                UPDATE ob14_generation SET active=1 WHERE id=$id;
                """,
                ("$id", generationId));
        }
    }

    public void DropOb14(long generationId)
    {
        lock (_gate)
        {
            Execute("DELETE FROM ob14_generation WHERE id=$id AND active=0", ("$id", generationId));
        }
    }

    public int? ActiveOb14Generation(string meetingDay)
    {
        lock (_gate)
        {
            using var command = NewCommand();
            command.CommandText = "SELECT generation FROM ob14_generation WHERE meeting_day=$d AND active=1";
            Add(command, ("$d", meetingDay));
            var value = command.ExecuteScalar();
            return value is long generation ? (int)generation : null;
        }
    }

    public int ActiveOb14MemberCount(string meetingDay)
    {
        lock (_gate)
        {
            using var command = NewCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM ob14_generation_record r
                JOIN ob14_generation g ON g.id = r.generation_id
                WHERE g.meeting_day=$d AND g.active=1
                """;
            Add(command, ("$d", meetingDay));
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public IReadOnlyList<string> MeetingDays()
    {
        lock (_gate)
        {
            return Strings("SELECT yyyymmdd FROM meeting_day ORDER BY yyyymmdd");
        }
    }

    public IReadOnlyList<string> RaceKeysForDays(IReadOnlyCollection<string> days)
    {
        lock (_gate)
        {
            if (days.Count == 0)
            {
                return [];
            }

            using var command = NewCommand();
            var names = new List<string>();
            var index = 0;
            foreach (var day in days)
            {
                var name = "$d" + index++;
                names.Add(name);
                command.Parameters.AddWithValue(name, day);
            }

            command.CommandText = $"SELECT race_key FROM race_key WHERE meeting_day IN ({string.Join(", ", names)}) ORDER BY race_key";
            var list = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(reader.GetString(0));
            }

            return list;
        }
    }

    public IReadOnlyList<string> StoredDataspecs()
    {
        lock (_gate)
        {
            return Strings("SELECT DISTINCT dataspec FROM watermark ORDER BY dataspec");
        }
    }

    public bool WasFired(string jobKey)
    {
        lock (_gate)
        {
            using var command = NewCommand();
            command.CommandText = "SELECT 1 FROM scheduler_fire WHERE job_key=$k";
            Add(command, ("$k", jobKey));
            return command.ExecuteScalar() is not null;
        }
    }

    public void MarkFired(string jobKey, DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            Execute(
                "INSERT OR IGNORE INTO scheduler_fire (job_key, fired_utc) VALUES ($k, $t)",
                ("$k", jobKey),
                ("$t", Stamp(utcNow)));
        }
    }

    private void RepairOpenFile(OpenRaw file, Func<ReadOnlySpan<byte>, FrameClassification> classify)
    {
        if (!RawFrameCodec.StartsWithMagic(file.Path))
        {
            _log.Info($"repair skip path={file.Path}");
            return;
        }

        var known = KnownSequences(file.Id);
        long validLength = 0;
        long lastSeq = 0;
        using (var stream = new FileStream(file.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            while (RawFrameCodec.TryRead(stream, out var sequence, out var offset, out var length, out var hash, out var payload))
            {
                validLength = stream.Position;
                lastSeq = sequence;
                if (known.Contains(sequence))
                {
                    continue;
                }

                IndexMissing(file, sequence, offset, length, hash, classify(payload));
            }

            if (stream.Length != validLength)
            {
                stream.SetLength(validLength);
                _log.Info($"repair truncate path={file.Path} length={validLength}");
            }
        }

        CommitRepair(file.Id, validLength, lastSeq);
    }

    private HashSet<long> KnownSequences(long fileId)
    {
        lock (_gate)
        {
            using var command = NewCommand();
            command.CommandText = "SELECT seq FROM record_index WHERE file_id=$f";
            Add(command, ("$f", fileId));
            var known = new HashSet<long>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                known.Add(reader.GetInt64(0));
            }

            return known;
        }
    }

    private void IndexMissing(OpenRaw file, long sequence, long offset, int length, byte[] hash, FrameClassification classified)
    {
        lock (_gate)
        {
            if (FindBySequence(file.Id, sequence) || FindByHashUnlocked(file.Dataspec, hash) is not null)
            {
                return;
            }

            using var command = NewCommand();
            command.CommandText = """
                INSERT INTO record_index
                    (dataspec, record_id, sha256, file_id, seq, offset, length, acquired_utc)
                VALUES ($d, $r, $h, $f, $s, $o, $n, $t)
                """;
            Add(command,
                ("$d", file.Dataspec),
                ("$r", classified.RecordId),
                ("$f", file.Id),
                ("$s", sequence),
                ("$o", offset),
                ("$n", length),
                ("$t", Stamp(DateTimeOffset.UtcNow)));
            command.Parameters.Add("$h", SqliteType.Blob).Value = hash;
            command.ExecuteNonQuery();
            RememberKeys(classified.MeetingDay, classified.RaceKey);
        }
    }

    private void CommitRepair(long fileId, long length, long lastSeq)
    {
        lock (_gate)
        {
            Execute(
                "UPDATE raw_file SET byte_length=$n, last_seq=$s WHERE id=$id",
                ("$n", length),
                ("$s", lastSeq),
                ("$id", fileId));
            Execute(
                "DELETE FROM record_index WHERE file_id=$f AND offset>=$n",
                ("$f", fileId),
                ("$n", length));
        }
    }

    private FileRow? PickFile(string dataspec, DateOnly day, int frameSize)
    {
        using var command = NewCommand();
        command.CommandText = """
            SELECT id, path, byte_length, last_seq
            FROM raw_file WHERE dataspec=$d AND complete=0 ORDER BY id DESC
            """;
        Add(command, ("$d", dataspec));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new FileRow(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3));
            if (row.Length + frameSize > _maxFileBytes)
            {
                reader.Close();
                Execute(
                    "UPDATE raw_file SET complete=1, completed_utc=$t WHERE id=$id",
                    ("$t", Stamp(DateTimeOffset.UtcNow)),
                    ("$id", row.Id));
                return PickFile(dataspec, day, frameSize);
            }

            if (PathMatchesDay(row.Path, day))
            {
                return row;
            }
        }

        return null;
    }

    private FileRow CreateFile(string dataspec, DateOnly day)
    {
        var directory = Path.Combine(
            _root,
            "raw",
            dataspec,
            day.ToString("yyyy", CultureInfo.InvariantCulture),
            day.ToString("MM", CultureInfo.InvariantCulture),
            day.ToString("dd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        var sequence = 1;
        if (Directory.Exists(directory))
        {
            foreach (var existing in Directory.EnumerateFiles(directory, "*.bin"))
            {
                var name = Path.GetFileNameWithoutExtension(existing);
                if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                {
                    sequence = Math.Max(sequence, parsed + 1);
                }
            }
        }

        var path = Path.Combine(directory, sequence.ToString("D6", CultureInfo.InvariantCulture) + ".bin");
        File.WriteAllBytes(path, []);
        using var command = NewCommand();
        command.CommandText = """
            INSERT INTO raw_file (dataspec, path, first_seq, last_seq, complete, byte_length, created_utc)
            VALUES ($d, $p, 1, 0, 0, 0, $t)
            """;
        Add(command, ("$d", dataspec), ("$p", path), ("$t", Stamp(DateTimeOffset.UtcNow)));
        command.ExecuteNonQuery();
        var id = LastId();
        return new FileRow(id, path, 0, 0);
    }

    private long EnsureFileRow(string path, string dataspec, long length, long lastSeq, bool complete)
    {
        using var command = NewCommand();
        command.CommandText = "SELECT id FROM raw_file WHERE path=$p";
        Add(command, ("$p", path));
        var existing = command.ExecuteScalar();
        if (existing is long id)
        {
            Execute(
                "UPDATE raw_file SET byte_length=$n, last_seq=$s WHERE id=$id",
                ("$n", length),
                ("$s", lastSeq),
                ("$id", id));
            return id;
        }

        using var insert = NewCommand();
        insert.CommandText = """
            INSERT INTO raw_file (dataspec, path, first_seq, last_seq, complete, byte_length, created_utc)
            VALUES ($d, $p, 1, $s, $c, $n, $t)
            """;
        Add(insert,
            ("$d", dataspec),
            ("$p", path),
            ("$s", lastSeq),
            ("$c", complete ? 1 : 0),
            ("$n", length),
            ("$t", Stamp(DateTimeOffset.UtcNow)));
        insert.ExecuteNonQuery();
        return LastId();
    }

    private long FileId(string path, string dataspec)
    {
        using var command = NewCommand();
        command.CommandText = "SELECT id FROM raw_file WHERE path=$p";
        Add(command, ("$p", path));
        var value = command.ExecuteScalar();
        if (value is long id)
        {
            return id;
        }

        return EnsureFileRow(path, dataspec, new FileInfo(path).Length, 0, false);
    }

    private bool FindBySequence(long fileId, long sequence)
    {
        using var command = NewCommand();
        command.CommandText = "SELECT 1 FROM record_index WHERE file_id=$f AND seq=$s";
        Add(command, ("$f", fileId), ("$s", sequence));
        return command.ExecuteScalar() is not null;
    }

    private long? FindByHashUnlocked(string dataspec, byte[] sha256)
    {
        using var command = NewCommand();
        command.CommandText = "SELECT id FROM record_index WHERE dataspec=$d AND sha256=$h";
        Add(command, ("$d", dataspec));
        command.Parameters.Add("$h", SqliteType.Blob).Value = sha256;
        var value = command.ExecuteScalar();
        return value is long id ? id : null;
    }

    private void RememberKeys(string? meetingDay, string? raceKey)
    {
        if (!string.IsNullOrEmpty(meetingDay))
        {
            Execute(
                "INSERT OR IGNORE INTO meeting_day (yyyymmdd, updated_utc) VALUES ($d, $t)",
                ("$d", meetingDay),
                ("$t", Stamp(DateTimeOffset.UtcNow)));
        }

        if (!string.IsNullOrEmpty(raceKey) && raceKey.Length >= 8)
        {
            Execute(
                "INSERT OR IGNORE INTO race_key (race_key, meeting_day, updated_utc) VALUES ($k, $d, $t)",
                ("$k", raceKey),
                ("$d", raceKey[..8]),
                ("$t", Stamp(DateTimeOffset.UtcNow)));
        }
    }

    private void EnsureWatermark(string dataspec, int option)
    {
        Execute(
            "INSERT OR IGNORE INTO watermark (dataspec, option, updated_utc) VALUES ($d, $o, $t)",
            ("$d", dataspec),
            ("$o", option),
            ("$t", Stamp(DateTimeOffset.UtcNow)));
    }

    private WatermarkState GetWatermarkUnlocked(string dataspec, int option)
    {
        using var command = NewCommand();
        command.CommandText = "SELECT resume_timestamp FROM watermark WHERE dataspec=$d AND option=$o";
        Add(command, ("$d", dataspec), ("$o", option));
        var value = command.ExecuteScalar() as string;
        return new WatermarkState { ResumeTimestamp = value };
    }

    private List<string> Strings(string sql)
    {
        using var command = NewCommand();
        command.CommandText = sql;
        var list = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }

        return list;
    }

    private long LastId()
    {
        using var command = NewCommand();
        command.CommandText = "SELECT last_insert_rowid();";
        return (long)command.ExecuteScalar()!;
    }

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = NewCommand();
        command.CommandText = sql;
        Add(command, parameters);
        command.ExecuteNonQuery();
    }

    private SqliteCommand NewCommand()
    {
        var command = _connection.CreateCommand();
        if (_batch is not null)
        {
            command.Transaction = _batch;
        }

        return command;
    }

    private void EnsureStream(FileRow current)
    {
        if (_stream is not null && _streamFileId == current.Id)
        {
            return;
        }

        if (_stream is not null)
        {
            _stream.Flush(flushToDisk: _batch is not null);
            CloseStream();
        }

        _stream = new FileStream(
            current.Path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite,
            1024 * 1024,
            FileOptions.SequentialScan);
        _stream.Position = _stream.Length;
        _streamLength = _stream.Position;
        _streamFileId = current.Id;
        _streamPath = current.Path;
    }

    private void CloseStream()
    {
        _stream?.Dispose();
        _stream = null;
        _streamPath = null;
        _streamFileId = 0;
    }

    private void DiscardWriteBatchUnlocked()
    {
        if (_batch is not null)
        {
            _batch.Rollback();
            _batch.Dispose();
            _batch = null;
        }

        if (_stream is not null && _streamPath is not null && _touched.TryGetValue(_streamPath, out var current))
        {
            _stream.SetLength(current);
            _streamLength = current;
            _stream.Flush(flushToDisk: true);
        }

        foreach (var (path, length) in _touched)
        {
            if (string.Equals(path, _streamPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var other = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            other.SetLength(length);
            other.Flush(flushToDisk: true);
        }

        _touched.Clear();
    }

    private static void Add(SqliteCommand command, params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private static string? ReadString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string Stamp(DateTimeOffset utc) =>
        utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static bool PathMatchesDay(string path, DateOnly day)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var raw = Array.IndexOf(parts, "raw");
        if (raw < 0 || raw + 4 >= parts.Length)
        {
            return false;
        }

        return parts[raw + 2] == day.ToString("yyyy", CultureInfo.InvariantCulture)
            && parts[raw + 3] == day.ToString("MM", CultureInfo.InvariantCulture)
            && parts[raw + 4] == day.ToString("dd", CultureInfo.InvariantCulture);
    }

    private void CreateSchema()
    {
        using var command = NewCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS watermark (
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
            CREATE TABLE IF NOT EXISTS run_history (
              id INTEGER PRIMARY KEY,
              dataspec TEXT NOT NULL,
              option INTEGER,
              realtime_key TEXT,
              started_utc TEXT NOT NULL,
              finished_utc TEXT,
              record_count INTEGER,
              new_count INTEGER,
              duplicate_count INTEGER,
              jv_readcount INTEGER,
              jv_downloadcount INTEGER,
              lastfiletimestamp TEXT,
              return_code INTEGER,
              outcome TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS raw_file (
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
            CREATE TABLE IF NOT EXISTS record_index (
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
            CREATE INDEX IF NOT EXISTS ix_record_file ON record_index(file_id);
            CREATE TABLE IF NOT EXISTS ob14_generation (
              id INTEGER PRIMARY KEY,
              meeting_day TEXT NOT NULL,
              generation INTEGER NOT NULL,
              active INTEGER NOT NULL,
              created_utc TEXT NOT NULL,
              UNIQUE (meeting_day, generation)
            );
            CREATE TABLE IF NOT EXISTS ob14_generation_record (
              generation_id INTEGER NOT NULL REFERENCES ob14_generation(id) ON DELETE CASCADE,
              record_index_id INTEGER NOT NULL REFERENCES record_index(id),
              PRIMARY KEY (generation_id, record_index_id)
            );
            CREATE TABLE IF NOT EXISTS meeting_day (
              yyyymmdd TEXT PRIMARY KEY,
              updated_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS race_key (
              race_key TEXT PRIMARY KEY,
              meeting_day TEXT NOT NULL,
              updated_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS scheduler_fire (
              job_key TEXT PRIMARY KEY,
              fired_utc TEXT NOT NULL
            );
            CREATE VIEW IF NOT EXISTS published_record AS
            SELECT r.id, r.dataspec, r.record_id, r.sha256, f.path AS raw_path, r.seq, r.offset, r.length,
                   r.source_file, r.source_timestamp, r.acquired_utc
            FROM record_index r
            JOIN raw_file f ON f.id = r.file_id
            WHERE f.complete = 1;
            CREATE VIEW IF NOT EXISTS ob14_active AS
            SELECT g.meeting_day, g.generation, r.record_index_id
            FROM ob14_generation g
            JOIN ob14_generation_record r ON r.generation_id = g.id
            JOIN record_index i ON i.id = r.record_index_id
            JOIN raw_file f ON f.id = i.file_id
            WHERE g.active = 1 AND f.complete = 1;
            """;
        command.ExecuteNonQuery();
    }

    private readonly record struct OpenRaw(long Id, string Path, string Dataspec);

    private sealed record FileRow(long Id, string Path, long Length, long LastSeq);
}
