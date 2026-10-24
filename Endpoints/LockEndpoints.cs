using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TTLockBackend.Data;

namespace TTLockBackend.Endpoints;

// DTOs ("data transfer objects"): the shapes the API accepts and returns.
// Deliberately separate from LockRecord, so the database can change without
// changing what the app sees, and the app can never set fields it shouldn't,
// such as CreatedAt.

/// <summary>A lock as the API returns it.</summary>
public record LockDto(
    string Mac,
    string Name,
    string LockData,
    string LockVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Body of <c>POST /locks</c>.</summary>
public record CreateLockRequest(
    string Mac,
    string Name,
    string LockData,
    string LockVersion);

/// <summary>Body of <c>PUT /locks/{mac}</c>. The MAC comes from the URL.</summary>
public record UpdateLockRequest(
    string Name,
    string LockData,
    string LockVersion);

public static class LockEndpoints
{
    private static readonly Regex MacPattern =
        new("^([0-9A-F]{2}:){5}[0-9A-F]{2}$", RegexOptions.Compiled);

    public static void MapLockEndpoints(this IEndpointRouteBuilder app)
    {
        // A route group: every endpoint below lives under /locks.
        var group = app.MapGroup("/locks");

        group.MapGet("/", GetAll);
        group.MapPost("/", Create);
        group.MapPut("/{mac}", Update);
        group.MapDelete("/{mac}", Delete);
    }

    private static async Task<Ok<List<LockDto>>> GetAll(AppDbContext db)
    {
        var records = await db.Locks.OrderBy(l => l.Name).ToListAsync();
        return TypedResults.Ok(records.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<LockDto>, Conflict<string>, ValidationProblem>>
        Create(CreateLockRequest request, AppDbContext db)
    {
        var mac = NormaliseMac(request.Mac);
        var errors = Validate(request.Name, request.LockData, request.LockVersion);
        if (mac is null)
        {
            errors["mac"] = ["Must look like AA:BB:CC:DD:EE:FF."];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Refuse rather than overwrite: replacing a lock's credential is a
        // deliberate act, and that is what PUT is for.
        if (await db.Locks.AnyAsync(l => l.Mac == mac))
        {
            return TypedResults.Conflict($"Lock {mac} is already registered.");
        }

        var now = DateTime.UtcNow;
        var record = new LockRecord
        {
            Mac = mac!,
            Name = request.Name.Trim(),
            LockData = request.LockData,
            LockVersion = request.LockVersion,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Locks.Add(record);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two requests for the same MAC raced past the check above, and
            // the other one won. Same outcome as if it had arrived first.
            if (await db.Locks.AnyAsync(l => l.Mac == mac))
            {
                return TypedResults.Conflict($"Lock {mac} is already registered.");
            }

            // Some other database failure: not ours to hide.
            throw;
        }

        return TypedResults.Created($"/locks/{mac}", ToDto(record));
    }

    private static async Task<Results<Ok<LockDto>, NotFound, ValidationProblem>>
        Update(string mac, UpdateLockRequest request, AppDbContext db)
    {
        var record = await Find(db, mac);
        if (record is null)
        {
            return TypedResults.NotFound();
        }

        var errors = Validate(request.Name, request.LockData, request.LockVersion);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        record.Name = request.Name.Trim();
        record.LockData = request.LockData;
        record.LockVersion = request.LockVersion;
        record.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return TypedResults.Ok(ToDto(record));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(string mac, AppDbContext db)
    {
        var record = await Find(db, mac);
        if (record is null)
        {
            return TypedResults.NotFound();
        }

        db.Locks.Remove(record);
        await db.SaveChangesAsync();

        return TypedResults.NoContent();
    }

    private static async Task<LockRecord?> Find(AppDbContext db, string mac)
    {
        var normalised = NormaliseMac(mac);
        // A malformed MAC cannot match any row, so it is simply "not found".
        return normalised is null ? null : await db.Locks.FindAsync(normalised);
    }

    /// <summary>
    /// Upper-cases and checks the format, so "b0:94:a1:e1:31:8f" and
    /// "B0:94:A1:E1:31:8F" are the same lock. Returns null if it is not a MAC.
    /// </summary>
    private static string? NormaliseMac(string? mac)
    {
        var candidate = mac?.Trim().ToUpperInvariant();
        return candidate is not null && MacPattern.IsMatch(candidate) ? candidate : null;
    }

    /// <summary>
    /// Checks the fields shared by create and update. A missing JSON field
    /// arrives as null at runtime even though the type says string, so every
    /// check tolerates null.
    /// </summary>
    private static Dictionary<string, string[]> Validate(string? name, string? lockData, string? lockVersion)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Required."];
        }
        else if (name.Trim().Length > 100)
        {
            errors["name"] = ["At most 100 characters."];
        }

        if (string.IsNullOrWhiteSpace(lockData))
        {
            errors["lockData"] = ["Required."];
        }

        if (string.IsNullOrWhiteSpace(lockVersion))
        {
            errors["lockVersion"] = ["Required."];
        }

        return errors;
    }

    private static LockDto ToDto(LockRecord r) =>
        new(r.Mac, r.Name, r.LockData, r.LockVersion, r.CreatedAt, r.UpdatedAt);
}
