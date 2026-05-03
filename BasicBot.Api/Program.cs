using System.Security.Cryptography;
using System.Text;
using BasicBot.Api.Data;
using BasicBot.Api.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<EchoRepository>();
builder.Services.AddSingleton<EchoService>();

builder.Services.AddOpenApi();

// Cap request body size at the server level to prevent memory / disk DoS via
// oversized payloads. 256 KB covers any reasonable echo + room for headroom;
// per-bot overrides should bump this only as far as needed.
const long MaxRequestBodyBytes = 256L * 1024L;
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
});

var app = builder.Build();

// Bootstrap SQLite schema
var db = app.Services.GetRequiredService<Db>();
await db.InitializeSchemaAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Optional X-API-Key middleware. Off by default — boilerplate ships with both
// containers on a private docker network with no published ports. If you expose
// this API publicly, set BASICBOT_API_KEY (or ApiKey in configuration) and the
// middleware below will require X-API-Key on every non-/health request.
var apiKey = builder.Configuration["ApiKey"]
    ?? Environment.GetEnvironmentVariable("BASICBOT_API_KEY");

if (!string.IsNullOrEmpty(apiKey))
{
    var expectedBytes = Encoding.UTF8.GetBytes(apiKey);
    app.Use(async (ctx, next) =>
    {
        // /health stays open so liveness/readiness probes don't need the key.
        if (ctx.Request.Path == "/health")
        {
            await next();
            return;
        }
        if (!ctx.Request.Headers.TryGetValue("X-API-Key", out var provided))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("unauthorized");
            return;
        }
        var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());
        if (providedBytes.Length != expectedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("unauthorized");
            return;
        }
        await next();
    });
    app.Logger.LogInformation("X-API-Key middleware enabled.");
}
else
{
    app.Logger.LogWarning(
        "BASICBOT_API_KEY not set — endpoints accept all callers. " +
        "This is the boilerplate default and is safe ONLY when the API stays on a private docker network. " +
        "Set BASICBOT_API_KEY to require X-API-Key on every non-/health route.");
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    time = DateTime.UtcNow.ToString("O")
}));

// Hard cap on the message field. Keeps SQLite rows bounded and rejects pathological
// payloads even if upstream validation is bypassed.
const int MaxMessageLength = 10_000;

app.MapPost("/echo", async (EchoRequest req, EchoService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Message))
        return Results.BadRequest(new { error = "message is required" });
    if (req.Message.Length > MaxMessageLength)
        return Results.BadRequest(new { error = $"message exceeds {MaxMessageLength} character limit" });
    var record = await svc.RecordAsync(req.Message);
    return Results.Ok(record);
});

app.MapGet("/echo/{id:long}", async (long id, EchoService svc) =>
{
    var record = await svc.GetAsync(id);
    return record is null ? Results.NotFound() : Results.Ok(record);
});

app.Run();

public record EchoRequest(string Message);
