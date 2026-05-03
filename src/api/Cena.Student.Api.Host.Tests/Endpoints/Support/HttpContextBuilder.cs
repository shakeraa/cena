// =============================================================================
// Cena Platform — HttpContext test helper for endpoint unit tests
//
// Builds DefaultHttpContext instances with a populated ClaimsPrincipal so
// endpoint handlers can be invoked directly without spinning up a TestHost
// pipeline. Used by AuthOnFirstSignInTests; reusable for any future endpoint
// test that needs a quick "this caller is authenticated as X" setup.
// =============================================================================

using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Cena.Student.Api.Host.Tests.Endpoints.Support;

internal static class HttpContextBuilder
{
    public static HttpContext WithEmptyPrincipal()
    {
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity()),
        };
    }

    public static HttpContext WithUid(string uid, string? email)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, uid),
            new("sub", uid),
        };
        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim("email", email));
            claims.Add(new Claim(ClaimTypes.Email, email));
        }
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth")),
        };
    }
}
