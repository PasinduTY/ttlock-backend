using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

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

// No login yet: until step C (encryption) and login exist, use fake lockData
// only. The server listens on localhost, so only this PC and a phone on
// `adb reverse` can reach it.
app.MapLockEndpoints();

app.Run();
