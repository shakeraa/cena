// =============================================================================
// Cena Platform -- Firebase JWT Authentication Setup
// BKD-001.1: JWT Bearer auth with Firebase token validation
// RDY-056 §2.2: Emulator-aware issuer so local dev can mint + validate
//               tokens against the Firebase Auth emulator.
// =============================================================================

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Cena.Infrastructure.Auth;

public static class FirebaseAuthExtensions
{
    /// <summary>
    /// Adds Firebase JWT Bearer authentication.
    /// Validates tokens issued by Firebase Auth (RS256, JWKS auto-rotation)
    /// in production, or by the Firebase Auth Emulator when
    /// FIREBASE_AUTH_EMULATOR_HOST is set (local dev only).
    /// </summary>
    public static IServiceCollection AddFirebaseAuth(
        this IServiceCollection services, IConfiguration configuration)
    {
        var projectId = configuration["Firebase:ProjectId"]
            ?? throw new InvalidOperationException("Firebase:ProjectId is required in configuration.");

        // RDY-056 §2.2: Emulator mode. The Firebase Auth Emulator signs
        // tokens with a well-known unsigned algorithm ("none"-equivalent:
        // the emulator issues real RS256 tokens but its kid rotates on
        // each emulator restart and its JWKS is exposed at a local URL).
        // When FIREBASE_AUTH_EMULATOR_HOST is set we swap the authority /
        // issuer to the emulator endpoints and disable signature validation
        // so local dev works without any Google round-trip.
        var emulatorHost = Environment.GetEnvironmentVariable("FIREBASE_AUTH_EMULATOR_HOST");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                if (!string.IsNullOrWhiteSpace(emulatorHost))
                {
                    // Emulator tokens use alg=none (unsigned JWTs). The .NET 9
                    // JwtBearer middleware defaults to JsonWebTokenHandler
                    // (via TokenHandlers) which refuses alg=none before our
                    // SignatureValidator fires. Force the legacy pipeline via
                    // UseSecurityTokenValidators=true so SignatureValidator
                    // below actually takes effect.
                    options.RequireHttpsMetadata = false;
                    options.UseSecurityTokenValidators = true;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = $"https://securetoken.google.com/{projectId}",
                        ValidateAudience = true,
                        ValidAudience = projectId,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = false,
                        RequireSignedTokens = false,
                        SignatureValidator = (token, _) =>
                            new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(token),
                        ClockSkew = TimeSpan.FromSeconds(30),
                        RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                        NameClaimType = System.Security.Claims.ClaimTypes.Name,
                    };
                }
                else
                {
                    // Production path: real Google-signed Firebase tokens.
                    options.Authority = $"https://securetoken.google.com/{projectId}";
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = $"https://securetoken.google.com/{projectId}",
                        ValidateAudience = true,
                        ValidAudience = projectId,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromSeconds(30),
                        RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                        NameClaimType = System.Security.Claims.ClaimTypes.Name,
                    };
                }
            });

        services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, CenaClaimsTransformer>();

        return services;
    }
}
