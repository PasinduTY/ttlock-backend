using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TTLockBackend.Data;

namespace TTLockBackend.Auth;

public static class AuthSetup
{
    /// <summary>
    /// Makes ASP.NET check the token on every request to a protected endpoint,
    /// before any of our code runs: signature, expiry, issuer and audience, all
    /// against the same settings <see cref="TokenService"/> signs with.
    /// </summary>
    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Configured through TokenService rather than inline, so signing and
        // checking can never drift apart: there is one key, read in one place.
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TokenService>((options, tokens) =>
            {
                // Keep claim names exactly as issued ("sub", "name"). By default
                // ASP.NET renames them to long legacy URIs.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = tokens.SigningKey,
                    ValidateIssuer = true,
                    ValidIssuer = tokens.Issuer,
                    ValidateAudience = true,
                    ValidAudience = tokens.Audience,
                    ValidateLifetime = true,
                    // Tolerance for clocks that disagree slightly. The default is
                    // 5 minutes; 1 is plenty between a phone and a server.
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = JwtRegisteredClaimNames.Name,
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = RejectIfAccountRemoved,
                };
            });

        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// A valid signature only proves the token was issued; it says nothing
    /// about whether the account still exists. Tokens last 30 days, so without
    /// this check a removed admin would keep access until theirs expired.
    /// One cheap lookup per request makes <c>remove-admin</c> take effect at once.
    /// </summary>
    /// <remarks>
    /// Safe to look up by id: Admins.Id is AUTOINCREMENT, which SQLite never
    /// reuses, so a removed admin's token can't match a newer account.
    /// </remarks>
    private static async Task RejectIfAccountRemoved(TokenValidatedContext context)
    {
        var subject = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

        if (!int.TryParse(subject, out var adminId) ||
            !await db.Admins.AnyAsync(a => a.Id == adminId))
        {
            context.Fail("This account no longer exists.");
        }
    }
}
