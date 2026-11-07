using Npgsql;
using NpgsqlTypes;

namespace DwhLoader;

public sealed class PostgresWarehouse : IWarehouse, IDisposable
{
    private readonly NpgsqlConnection _connection;
    private NpgsqlTransaction _transaction;

    public PostgresWarehouse(string host, int port, string database, string username, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            Timeout = 15,
            CommandTimeout = 0,
            SearchPath = "jv",
            IncludeErrorDetail = false,
            Pooling = false,
        };
        _connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            _connection.Open();
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            throw new WarehouseUnavailableException(ex.GetType().Name);
        }
    }

    public bool SchemaReady() =>
        Run(command =>
        {
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = 'jv' AND table_name = 'ingest_file')
                """;
            return (bool)command.ExecuteScalar()!;
        });

    public IReadOnlySet<long> IngestedIds() =>
        Run(command =>
        {
            command.CommandText = "SELECT ledger_file_id FROM jv.ingest_file";
            using var reader = command.ExecuteReader();
            var ids = new HashSet<long>();
            while (reader.Read())
            {
                ids.Add(reader.GetInt64(0));
            }

            return (IReadOnlySet<long>)ids;
        });

    public void BeginFile()
    {
        RollbackFile();
        try
        {
            _transaction = _connection.BeginTransaction();
        }
        catch (PostgresException ex)
        {
            throw Failure(ex);
        }
        catch (NpgsqlException ex)
        {
            throw new WarehouseUnavailableException(ex.GetType().Name);
        }
    }

    public void Write(DbRow row)
    {
        RequireIdent(row.Table);
        var names = new string[row.Columns.Count];
        var places = new string[row.Columns.Count];
        for (var i = 0; i < row.Columns.Count; i++)
        {
            RequireIdent(row.Columns[i].Name);
            names[i] = row.Columns[i].Name;
            places[i] = "$" + (i + 1);
        }

        Run(command =>
        {
            command.CommandText =
                $"INSERT INTO jv.{row.Table} ({string.Join(", ", names)}) VALUES ({string.Join(", ", places)}) ON CONFLICT DO NOTHING";
            foreach (var column in row.Columns)
            {
                command.Parameters.Add(Parameter(column.Value));
            }

            command.ExecuteNonQuery();
            return 0;
        });
    }

    public void MarkFile(long id, string dataspec, string path)
    {
        Run(command =>
        {
            command.CommandText = "INSERT INTO jv.ingest_file (ledger_file_id, dataspec, path) VALUES ($1, $2, $3)";
            command.Parameters.Add(new NpgsqlParameter { Value = id });
            command.Parameters.Add(new NpgsqlParameter { Value = dataspec });
            command.Parameters.Add(new NpgsqlParameter { Value = path });
            command.ExecuteNonQuery();
            return 0;
        });
    }

    public void CommitFile()
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            _transaction.Commit();
        }
        catch (PostgresException ex)
        {
            throw Failure(ex);
        }
        catch (NpgsqlException ex)
        {
            throw new WarehouseUnavailableException(ex.GetType().Name);
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public void RollbackFile()
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            _transaction.Rollback();
        }
        catch (NpgsqlException)
        {
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public void ReplaceOb14(IReadOnlyList<Ob14Member> members)
    {
        using var transaction = _connection.BeginTransaction();
        try
        {
            using (var delete = _connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandTimeout = 0;
                delete.CommandText = "DELETE FROM jv.ob14_active";
                delete.ExecuteNonQuery();
            }

            foreach (var member in members)
            {
                using var insert = _connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandTimeout = 0;
                insert.CommandText = """
                    INSERT INTO jv.ob14_active (meeting_day, generation, raw_sha256, record_id)
                    VALUES ($1, $2, $3, $4)
                    """;
                insert.Parameters.Add(Parameter(member.MeetingDay));
                insert.Parameters.Add(Parameter(member.Generation));
                insert.Parameters.Add(Parameter(member.Sha256));
                insert.Parameters.Add(Parameter(member.RecordId));
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (PostgresException ex)
        {
            throw new Ob14CopyException($"{ex.GetType().Name} {ex.SqlState}");
        }
        catch (NpgsqlException ex)
        {
            throw new WarehouseUnavailableException(ex.GetType().Name);
        }
    }

    public void Dispose()
    {
        RollbackFile();
        _connection.Dispose();
    }

    private T Run<T>(Func<NpgsqlCommand, T> body)
    {
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandTimeout = 0;
            if (_transaction is not null)
            {
                command.Transaction = _transaction;
            }

            return body(command);
        }
        catch (PostgresException ex)
        {
            throw Failure(ex);
        }
        catch (NpgsqlException ex)
        {
            throw new WarehouseUnavailableException(ex.GetType().Name);
        }
    }

    private static SqlFailureException Failure(PostgresException ex)
    {
        var disconnect = ex.SqlState is not null && (ex.SqlState.StartsWith("08", StringComparison.Ordinal) || ex.SqlState.StartsWith("57", StringComparison.Ordinal));
        return new SqlFailureException(ex.SqlState, ex.MessageText ?? "", disconnect);
    }

    private static NpgsqlParameter Parameter(object value)
    {
        var parameter = new NpgsqlParameter { Value = value ?? DBNull.Value };
        switch (value)
        {
            case DateOnly date:
                parameter.NpgsqlDbType = NpgsqlDbType.Date;
                parameter.Value = date;
                break;
            case DateTimeOffset stamp:
                parameter.NpgsqlDbType = NpgsqlDbType.TimestampTz;
                parameter.Value = stamp;
                break;
            case int?[] numbers:
                parameter.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer;
                parameter.Value = numbers;
                break;
            case string[] texts:
                parameter.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text;
                parameter.Value = texts;
                break;
            case byte[] bytes:
                parameter.NpgsqlDbType = NpgsqlDbType.Bytea;
                parameter.Value = bytes;
                break;
        }

        return parameter;
    }

    private static void RequireIdent(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException("列名が空です");
        }

        foreach (var value in name)
        {
            if (value is not (>= 'a' and <= 'z' or '_' or >= '0' and <= '9'))
            {
                throw new InvalidOperationException("列名が不正です");
            }
        }
    }
}
