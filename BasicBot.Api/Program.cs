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
    // Strip the "Server: Kestrel" banner (P43) so responses don't disclose the
    // framework. Lift this line into every portfolio bot.
    options.AddServerHeader = false;
});

var app = builder.Build();

// Bootstrap SQLite schema
var db = app.Services.GetRequiredService<Db>();
await db.InitializeSchemaAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Security headers (P31). Defence-in-depth at the app layer for when Caddy
// terminates TLS but doesn't emit these. JSON-only API, so CSP `default-src
// 'none'; frame-ancestors 'none'` is correct for any non-HTML response.
// HSTS is intentionally NOT set here — Caddy emits it at the TLS edge and the
// bot listens HTTP-only on the docker bridge, so app-layer HSTS would be a no-op.
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var p = ctx.Request.Path.Value ?? string.Empty;
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["Referrer-Policy"]        = "no-referrer";
        ctx.Response.Headers["X-Frame-Options"]        = "DENY";
        ctx.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        if (!p.StartsWith("/v1/resources/", StringComparison.Ordinal) && p != "/health")
            ctx.Response.Headers["Cache-Control"] = "no-store";
        return Task.CompletedTask;
    });
    await next();
});

// X-API-Key middleware. Required in any non-Development environment — a fail-
// open default plus a bad .env deploy or env-load failure would silently expose
// every endpoint. In Development the bot is still allowed to start without a
// key, with a loud warning, so local clones don't need configuration to boot.
var apiKey = builder.Configuration["ApiKey"]
    ?? Environment.GetEnvironmentVariable("BASICBOT_API_KEY");

if (string.IsNullOrEmpty(apiKey))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "BASICBOT_API_KEY is required in non-Development environments. " +
            $"Current environment: {app.Environment.EnvironmentName}. Set the env var " +
            "(or `ApiKey` in configuration) to a high-entropy random string.");
    }
    app.Logger.LogWarning(
        "BASICBOT_API_KEY not set — Development mode only. " +
        "Endpoints accept all callers. Set the env var before any non-local deployment.");
}
else
{
    var expectedBytes = Encoding.UTF8.GetBytes(apiKey);
    app.Use(async (ctx, next) =>
    {
        // /health stays open so liveness/readiness probes don't need the key.
        // /v1/resources/* stays open so buyer / orchestrator agents (Butler etc.)
        // can introspect the bot pre-hire — that's the whole point of Resources.
        var path = ctx.Request.Path.Value ?? string.Empty;
        if (path == "/health" || path.StartsWith("/v1/resources/", StringComparison.Ordinal))
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

// ACP v2 Resources — public, free, parameterised endpoints mirrored
// 1:1 with entries in acp-v2/src/resources.ts. Buyer / orchestrator agents
// (Butler etc.) call these BEFORE paying for an offering, so handlers must
// be cheap, side-effect-free, and stable. Add new routes here in lockstep
// with new entries in acp-v2/src/resources.ts; run `npm run print-resources`
// in acp-v2/ and paste each block into app.virtuals.io's Resources tab.
//
// Resources stay reachable even when the X-API-Key middleware is on —
// the middleware above whitelists /v1/resources/* alongside /health.
app.MapGet("/v1/resources/echoStatus", async (EchoRepository repo) =>
{
    var (count, lastAt) = await repo.GetStatusAsync();
    return Results.Ok(new
    {
        count,
        lastEchoAt = lastAt?.ToString("O")
    });
});

app.Run();

public record EchoRequest(string Message);
