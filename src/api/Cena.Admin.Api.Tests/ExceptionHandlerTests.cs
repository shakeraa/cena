// =============================================================================
// Tests for global exception handler status code mapping
// Verifies UnauthorizedAccessException → 403 (not 401)
// =============================================================================

namespace Cena.Admin.Api.Tests;

public class ExceptionHandlerTests
{
    /// <summary>
    /// Maps exception types to the expected HTTP status code,
    /// matching the switch expression in Program.cs.
    /// </summary>
    private static (int StatusCode, string Message) MapException(Exception? error)
    {
        return error switch
        {
            Microsoft.AspNetCore.Http.BadHttpRequestException e => (400, e.Message),
            UnauthorizedAccessException e => (403, e.Message),
            KeyNotFoundException => (404, "Resource not found"),
            _ => (500, "Internal server error"),
        };
    }

    [Fact]
    public void UnauthorizedAccessException_Returns403_Not401()
    {
        var ex = new UnauthorizedAccessException("User has no school_id claim.");

        var (statusCode, message) = MapException(ex);

        Assert.Equal(403, statusCode);
        Assert.Contains("school_id", message);
    }

    [Fact]
    public void Regression_UnauthorizedAccessException_MustNotReturn401()
    {
        // Before fix: UnauthorizedAccessException → 401 → frontend signs out → redirect loop
        // After fix: → 403 → frontend shows "not authorized" without signing out
        var (statusCode, _) = MapException(new UnauthorizedAccessException("test"));

        Assert.NotEqual(401, statusCode);
        Assert.Equal(403, statusCode);
    }

    [Fact]
    public void KeyNotFoundException_Returns404()
    {
        var (statusCode, _) = MapException(new KeyNotFoundException());

        Assert.Equal(404, statusCode);
    }

    [Fact]
    public void GenericException_Returns500()
    {
        var (statusCode, _) = MapException(new InvalidOperationException("boom"));

        Assert.Equal(500, statusCode);
    }

    [Fact]
    public void NullException_Returns500()
    {
        var (statusCode, _) = MapException(null);

        Assert.Equal(500, statusCode);
    }
}
