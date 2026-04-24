// =============================================================================
// Cena Platform -- Firebase Auth Test Double Tests (FIND-qa-011)
// Demonstrates and validates the Firebase Auth test double functionality.
// =============================================================================

using Cena.Infrastructure.Auth.Testing;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Cena.Infrastructure.Tests.Auth;

/// <summary>
/// FIND-qa-011: Tests for the Firebase Auth test double.
/// </summary>
public class FirebaseAuthTestDoubleTests : IDisposable
{
    private readonly FirebaseAuthTestDouble _authDouble;

    public FirebaseAuthTestDoubleTests()
    {
        _authDouble = new FirebaseAuthTestDouble("cena-test");
    }

    public void Dispose() => _authDouble.Dispose();

    [Fact]
    public void GenerateTestToken_CreatesValidJwtStructure()
    {
        var user = new TestFirebaseUser("test-user-123", "test@example.com", true, "student");
        var token = _authDouble.GenerateTestToken(user);

        Assert.NotNull(token);
        Assert.Contains(".", token);
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void GenerateTestToken_ContainsExpectedClaims()
    {
        var user = new TestFirebaseUser(
            UserId: "student-456",
            Email: "student@test.com",
            EmailVerified: true,
            Role: "student",
            SchoolId: "school-789");

        var token = _authDouble.GenerateTestToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("student-456", jwt.Claims.First(c => c.Type == "sub").Value);
        Assert.Equal("student@test.com", jwt.Claims.First(c => c.Type == "email").Value);
        Assert.Equal("student", jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal("school-789", jwt.Claims.First(c => c.Type == "school_id").Value);
    }

    [Fact]
    public void GetJwks_ReturnsValidSigningKey()
    {
        var jwks = _authDouble.GetJwks();

        Assert.NotNull(jwks);
        Assert.Single(jwks.Keys);
        
        var key = jwks.Keys.First();
        Assert.Equal("RSA", key.Kty);
        Assert.Equal("RS256", key.Alg);
    }

    [Fact]
    public void GenerateTestToken_CanBeValidatedWithJwks()
    {
        var user = new TestFirebaseUser("test-789");
        var token = _authDouble.GenerateTestToken(user);
        var signingKey = _authDouble.GetJwks().GetSigningKeys().First();

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://securetoken.google.com/cena-test",
            ValidateAudience = true,
            ValidAudience = "cena-test",
            IssuerSigningKey = signingKey
        };

        var principal = new JwtSecurityTokenHandler().ValidateToken(token, validationParams, out _);

        Assert.NotNull(principal);
        Assert.Equal("test-789", principal.FindFirst("sub")?.Value);
    }
}
