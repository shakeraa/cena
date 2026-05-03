// =============================================================================
// Cena Platform -- Firebase Auth Test Double (FIND-qa-011)
// Shared test infrastructure for Firebase JWT authentication.
// 
// Enables tests to generate valid-looking Firebase tokens without calling
// the real Firebase Auth service. Supports both unit tests and integration tests.
// =============================================================================

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace Cena.Infrastructure.Auth.Testing;

/// <summary>
/// Test double for Firebase Authentication.
/// Generates self-signed JWTs that match Firebase's token structure
/// without requiring network calls to Firebase.
/// </summary>
public sealed class FirebaseAuthTestDouble
{
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _securityKey;
    private readonly SigningCredentials _signingCredentials;
    private readonly string _projectId;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    public FirebaseAuthTestDouble(string projectId = "cena-test-project")
    {
        _projectId = projectId;
        _rsa = RSA.Create(2048);
        _securityKey = new RsaSecurityKey(_rsa);
        _signingCredentials = new SigningCredentials(_securityKey, SecurityAlgorithms.RsaSha256);
        _tokenHandler = new JwtSecurityTokenHandler();
    }

    /// <summary>
    /// Generates a test Firebase JWT token with the specified claims.
    /// </summary>
    public string GenerateTestToken(TestFirebaseUser user)
    {
        var now = DateTimeOffset.UtcNow;
        
        var claims = new List<Claim>
        {
            new Claim("sub", user.UserId),
            new Claim("user_id", user.UserId),
            new Claim("iss", $"https://securetoken.google.com/{_projectId}"),
            new Claim("aud", _projectId),
            new Claim("iat", now.ToUnixTimeSeconds().ToString()),
            new Claim("exp", now.AddHours(1).ToUnixTimeSeconds().ToString()),
            new Claim("auth_time", now.ToUnixTimeSeconds().ToString()),
        };

        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim("email", user.Email));
            claims.Add(new Claim("email_verified", user.EmailVerified.ToString().ToLower()));
        }

        if (!string.IsNullOrEmpty(user.Role))
        {
            claims.Add(new Claim(ClaimTypes.Role, user.Role));
            claims.Add(new Claim("role", user.Role));
        }

        if (!string.IsNullOrEmpty(user.SchoolId))
        {
            claims.Add(new Claim("school_id", user.SchoolId));
        }

        if (user.Claims != null)
        {
            foreach (var (key, value) in user.Claims)
            {
                claims.Add(new Claim(key, value));
            }
        }

        var token = new JwtSecurityToken(
            issuer: $"https://securetoken.google.com/{_projectId}",
            audience: _projectId,
            claims: claims,
            notBefore: now.DateTime,
            expires: now.AddHours(1).DateTime,
            signingCredentials: _signingCredentials);

        return _tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Creates the JWKS (JSON Web Key Set) that would be returned by Firebase's
    /// public key endpoint. Use this to configure the test JWT validation.
    /// </summary>
    public JsonWebKeySet GetJwks()
    {
        var rsaParams = _rsa.ExportParameters(false);
        var jwk = new JsonWebKey
        {
            Kty = "RSA",
            Use = "sig",
            Kid = "test-key-1",
            N = Base64UrlEncoder.Encode(rsaParams.Modulus),
            E = Base64UrlEncoder.Encode(rsaParams.Exponent),
            Alg = "RS256"
        };

        return new JsonWebKeySet { Keys = { jwk } };
    }

    /// <summary>
    /// Returns the public key in PEM format for manual test setup.
    /// </summary>
    public string GetPublicKeyPem() => _rsa.ExportRSAPublicKeyPem();

    /// <summary>
    /// Disposes the RSA key.
    /// </summary>
    public void Dispose() => _rsa.Dispose();
}

/// <summary>
/// Test user configuration for Firebase Auth test double.
/// </summary>
public sealed record TestFirebaseUser(
    string UserId,
    string? Email = null,
    bool EmailVerified = true,
    string? Role = null,
    string? SchoolId = null,
    Dictionary<string, string>? Claims = null);

/// <summary>
/// Extension methods for configuring Firebase auth in integration tests.
/// </summary>
public static class FirebaseAuthTestExtensions
{
    /// <summary>
    /// Adds Firebase Auth test double to the service collection for integration tests.
    /// This bypasses real Firebase validation and uses the test double's public key.
    /// </summary>
    public static IServiceCollection AddFirebaseAuthTestDouble(
        this IServiceCollection services,
        FirebaseAuthTestDouble testDouble,
        string projectId = "cena-test-project")
    {
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
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.Name,
                    // FIND-qa-011: Use test double's signing key for validation
                    IssuerSigningKeyResolver = (token, secToken, kid, validationParams) =>
                    {
                        return testDouble.GetJwks().GetSigningKeys();
                    }
                };
            });

        services.AddTransient<IClaimsTransformation, CenaClaimsTransformer>();

        return services;
    }
}
