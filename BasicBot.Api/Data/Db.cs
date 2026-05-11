using Microsoft.Data.Sqlite;

namespace BasicBot.Api.Data;

public class Db
{
    private readonly string _connectionString;

    public Db(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Sqlite")
            ?? throw new InvalidOperationException("ConnectionStrings:Sqlite not configured");
    }

    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // busy_timeout is per-connection (resets on each Open). Wait up to 5s
        // on writer contention instead of throwing SQLITE_BUSY immediately.
        // WAL mode is file-level and set once in InitializeSchemaAsync.
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    public async Task InitializeSchemaAsync()
    {
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        // WAL is persistent at the file level — set once, sticks across
        // restarts. Lets readers and writers run concurrently (only
        // writer-writer is serialised through a small WAL file). 5-10x
        // throughput under bursty concurrent load vs. default DELETE mode.
        // Requires the SQLite file to live on local disk (not NFS/SMB).
        cmd.CommandText = "PRAGMA journal_mode = WAL;";
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS echo_records (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                message     TEXT    NOT NULL,
                received_at TEXT    NOT NULL
            );";
        await cmd.ExecuteNonQueryAsync();
    }
}
