using BasicBot.Api.Data;
using BasicBot.Api.Models;

namespace BasicBot.Api.Services;

public class EchoService
{
    private readonly EchoRepository _repo;

    public EchoService(EchoRepository repo) => _repo = repo;

    public async Task<EchoRecord> RecordAsync(string message)
    {
        var receivedAt = DateTime.UtcNow;
        var id = await _repo.InsertAsync(message, receivedAt);
        return new EchoRecord(id, message, receivedAt);
    }

    public Task<EchoRecord?> GetAsync(long id) => _repo.GetByIdAsync(id);
}
