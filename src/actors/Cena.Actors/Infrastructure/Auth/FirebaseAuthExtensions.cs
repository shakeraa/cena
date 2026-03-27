// =============================================================================
// Cena Platform -- Firebase JWT Authentication Setup
// BKD-001.1: JWT Bearer auth with Firebase token validation
// =============================================================================

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Cena.Actors.Infrastructure.Auth;

public static class FirebaseAuthExtensions
{
    /// <summary>
    /// Adds Firebase JWT Bearer authentication.
    /// Validates tokens issued by Firebase Auth (RS256, JWKS auto-rotation).
    /// </summary>
    public static IServiceCollection AddFirebaseAuth(
        this IServiceCollection services, IConfiguration configuration)
    {
        var projectId = configuration["Firebase:ProjectId"]
            ?? throw new InvalidOperationException("Firebase:ProjectId is required in configuration.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = $"https://securetoken.google.com/{projectId}";
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = $"https://securetoken.google.com/{projectId}",
                    ValidateAudience = true,
                    ValidAudience = projectId,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, CenaClaimsTransformer>();

        return services;
    }
}
