var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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

app.Run();
