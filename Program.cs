using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TTLockBackend;
using TTLockBackend.Auth;
using TTLockBackend.Data;
using TTLockBackend.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// SQLite resolves a relative path against the current directory, which
// depends on where `dotnet run` was typed. Anchoring it to the content root
// means there is only ever one database, whichever folder you start from.
var dbPath = Path.Combine(
    builder.Environment.ContentRootPath,
    builder.Configuration["Database:Path"] ?? "ttlock.db");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// The keys that encrypt lockData in the database. They live in keys/
// (gitignored), apart from the database: a stolen database alone is useless.
//
// LOSING THESE KEYS LOSES EVERY LOCK. Without them nothing in the database
// can be decrypted, and each lock needs a physical factory reset. Back up
// keys/ together with ttlock.db, always as a pair.
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("TTLockBackend")
    .PersistKeysToFileSystem(
        new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "keys")));

if (OperatingSystem.IsWindows())
{
    // Also encrypt the key files themselves with Windows DPAPI, tied to this
    // Windows user. A copy of keys/ is useless on another machine or account.
    // A Linux server will need a different choice; decide that at deployment.
    dataProtection.ProtectKeysWithDpapi();
}

// One instance for the whole app: it holds the signing key, nothing per-request.
builder.Services.AddSingleton<TokenService>();

var app = builder.Build();

// `dotnet run -- add-admin <name>` and friends run here and exit, without
// ever starting the web server.
if (await AdminCommands.TryRunAsync(app, args) is int exitCode)
{
    return exitCode;
}

// Create the token service now, so a missing or weak signing key stops the
// server at startup with instructions, rather than at the first login.
_ = app.Services.GetRequiredService<TokenService>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Proves the server is up and reachable. Deliberately needs no login and
// touches no database, so it answers "is the backend there?" and nothing else.
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }))
    .WithName("Health");

app.MapAuthEndpoints();

// Not protected yet (step L3 adds that). Tolerable only because the server
// listens on localhost, so just this PC and a phone on `adb reverse` can reach it.
app.MapLockEndpoints();

app.Run();
return 0;
