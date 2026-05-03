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
        return conn;
    }

    public async Task InitializeSchemaAsync()
    {
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS echo_records (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                message     TEXT    NOT NULL,
                received_at TEXT    NOT NULL
            );";
        await cmd.ExecuteNonQueryAsync();
    }
}
