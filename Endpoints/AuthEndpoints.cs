using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TTLockBackend.Auth;
using TTLockBackend.Data;

namespace TTLockBackend.Endpoints;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, DateTime ExpiresAt, string Username);

public static class AuthEndpoints
{
    private static readonly PasswordHasher<AdminUser> Hasher = new();

    private static readonly AdminUser DummyAdmin = new()
    {
        Username = "",
        PasswordHash = "",
    };

    /// <summary>
    /// A real hash of a password nobody knows, checked when the username does
    /// not exist. See <see cref="Login"/> for why. Lazy, so the ~100 ms of
    /// hashing happens on first use rather than at startup.
    /// </summary>
    private static readonly Lazy<string> DummyHash = new(() =>
        Hasher.HashPassword(DummyAdmin, Guid.NewGuid().ToString()));

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", Login);
    }

    private static async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>> Login(
        LoginRequest request, AppDbContext db, TokenService tokens)
    {
        var username = AdminCommands.NormaliseUsername(request.Username);
        var password = request.Password ?? "";

        var admin = username is null
            ? null
            : await db.Admins.SingleOrDefaultAsync(a => a.Username == username);

        if (admin is null)
        {
            // Still do a full password check, against a dummy hash, and throw
            // the answer away. Checking a password takes ~100 ms on purpose;
            // skipping it for unknown names would make "no such user" visibly
            // faster than "wrong password", and the timing alone would tell an
            // attacker which usernames exist.
            Hasher.VerifyHashedPassword(DummyAdmin, DummyHash.Value, password);
            return TypedResults.Unauthorized();
        }

        var check = Hasher.VerifyHashedPassword(admin, admin.PasswordHash, password);
        if (check == PasswordVerificationResult.Failed)
        {
            // Identical to the unknown-user response: same status, no body.
            return TypedResults.Unauthorized();
        }

        if (check == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // The hash was made with older, weaker settings. The password is
            // correct and in hand right now, so upgrade the stored hash.
            admin.PasswordHash = Hasher.HashPassword(admin, password);
            await db.SaveChangesAsync();
        }

        var (token, expiresAt) = tokens.Issue(admin);
        return TypedResults.Ok(new LoginResponse(token, expiresAt, admin.Username));
    }
}
