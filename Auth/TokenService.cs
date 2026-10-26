using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TTLockBackend.Data;

namespace TTLockBackend.Auth;

/// <summary>
/// Issues the signed tokens (JWTs) that the app sends with every request
/// after logging in.
/// </summary>
/// <remarks>
/// A JWT is three base64 parts joined by dots: a header, a payload of
/// "claims" (who this is, when it expires), and a signature over both. Anyone
/// can READ the payload. Nobody can CHANGE it without the signing key, because
/// the signature would no longer match. So never put secrets in a token, and
/// never let the signing key leave the server.
/// </remarks>
public sealed class TokenService
{
    public TokenService(IConfiguration configuration, IHostEnvironment environment)
    {
        var encodedKey = configuration["Jwt:SigningKey"];
        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(encodedKey ?? "");
        }
        catch (FormatException)
        {
            keyBytes = [];
        }

        // HMAC-SHA256 needs at least 256 bits. A short or missing key would make
        // every token forgeable, so the server refuses to start without one.
        if (keyBytes.Length < 32)
        {
            // The most common cause isn't a missing secret but the wrong
            // environment: user-secrets are only read in Development, so say
            // which environment this is.
            var hint = environment.IsDevelopment()
                ? "Store one with `dotnet user-secrets set Jwt:SigningKey <base64 of 64 random bytes>`."
                : $"This is running in the '{environment.EnvironmentName}' environment, and " +
                  "user-secrets are only loaded in Development. Start it with " +
                  "`dotnet run --launch-profile http`, which sets Development.";
            throw new InvalidOperationException(
                $"Jwt:SigningKey is missing or shorter than 32 bytes. {hint}");
        }

        SigningKey = new SymmetricSecurityKey(keyBytes);
        Issuer = configuration["Jwt:Issuer"] ?? "ttlock-backend";
        Audience = configuration["Jwt:Audience"] ?? "ttlock-app";
        Lifetime = TimeSpan.FromDays(configuration.GetValue("Jwt:LifetimeDays", 30));
    }

    /// <summary>Used both to sign tokens here and to check them on each request.</summary>
    public SymmetricSecurityKey SigningKey { get; }

    public string Issuer { get; }

    public string Audience { get; }

    public TimeSpan Lifetime { get; }

    public (string Token, DateTime ExpiresAt) Issue(AdminUser admin)
    {
        var expiresAt = DateTime.UtcNow.Add(Lifetime);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = expiresAt,
            Subject = new ClaimsIdentity(
            [
                // "sub" (subject): the account this token speaks for. The id,
                // not the name, so it stays correct even if a name is reused.
                new Claim(JwtRegisteredClaimNames.Sub, admin.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, admin.Username),
            ]),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }
}
