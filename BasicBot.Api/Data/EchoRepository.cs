using System.Globalization;
using BasicBot.Api.Models;
using Microsoft.Data.Sqlite;

namespace BasicBot.Api.Data;

public class EchoRepository
{
    private readonly Db _db;

    public EchoRepository(Db db) => _db = db;

    public async Task<long> InsertAsync(string message, DateTime receivedAtUtc)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO echo_records (message, received_at)
            VALUES ($message, $receivedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$message", message);
        cmd.Parameters.AddWithValue("$receivedAt", receivedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        var id = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        return id;
    }

    public async Task<EchoRecord?> GetByIdAsync(long id)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, message, received_at FROM echo_records WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new EchoRecord(
            reader.GetInt64(0),
            reader.GetString(1),
            DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    public async Task<(long Count, DateTime? LastReceivedAtUtc)> GetStatusAsync()
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), MAX(received_at) FROM echo_records;";
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (0, null);
        var count = reader.GetInt64(0);
        DateTime? lastAt = reader.IsDBNull(1)
            ? null
            : DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return (count, lastAt);
    }
}
