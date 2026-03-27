// =============================================================================
// Cena Platform -- Firebase Admin SDK Wrapper
// BKD-002: Manages Firebase Auth users (create, update, disable, claims)
// =============================================================================

using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Firebase;

public interface IFirebaseAdminService
{
    Task<string> CreateUserAsync(string email, string fullName, string? password);
    Task UpdateEmailAsync(string uid, string newEmail);
    Task SetCustomClaimsAsync(string uid, Dictionary<string, object> claims);
    Task DisableUserAsync(string uid);
    Task EnableUserAsync(string uid);
    Task DeleteUserAsync(string uid);
    Task<string> GenerateSignInLinkAsync(string email);
}

public sealed class FirebaseAdminService : IFirebaseAdminService
{
    private readonly ILogger<FirebaseAdminService> _logger;
    private readonly bool _initialized;

    public FirebaseAdminService(IConfiguration configuration, ILogger<FirebaseAdminService> logger)
    {
        _logger = logger;

        if (FirebaseApp.DefaultInstance != null)
        {
            _initialized = true;
            return;
        }

        var credPath = configuration["Firebase:ServiceAccountKeyPath"];
        try
        {
            if (!string.IsNullOrEmpty(credPath) && File.Exists(credPath))
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromFile(credPath),
                    ProjectId = configuration["Firebase:ProjectId"]
                });
                _initialized = true;
            }
            else
            {
                // Try Application Default Credentials (ADC) — available in cloud environments
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.GetApplicationDefault(),
                    ProjectId = configuration["Firebase:ProjectId"]
                });
                _initialized = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Firebase Admin SDK not initialized — user management operations will use local-only mode. {Message}", ex.Message);
            _initialized = false;
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("Firebase Admin SDK is not initialized. Set Firebase:ServiceAccountKeyPath in configuration or configure Application Default Credentials.");
    }

    public async Task<string> CreateUserAsync(string email, string fullName, string? password)
    {
        var args = new UserRecordArgs
        {
            Email = email,
            DisplayName = fullName,
            EmailVerified = false,
            Disabled = false
        };

        if (!string.IsNullOrEmpty(password))
            args.Password = password;

        var userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(args);
        _logger.LogInformation("Created Firebase user {Uid} for {Email}", userRecord.Uid, email);
        return userRecord.Uid;
    }

    public async Task UpdateEmailAsync(string uid, string newEmail)
    {
        await FirebaseAuth.DefaultInstance.UpdateUserAsync(new UserRecordArgs
        {
            Uid = uid,
            Email = newEmail
        });
    }

    public async Task SetCustomClaimsAsync(string uid, Dictionary<string, object> claims)
    {
        await FirebaseAuth.DefaultInstance.SetCustomUserClaimsAsync(uid, claims);
        _logger.LogInformation("Updated custom claims for {Uid}: {Claims}",
            uid, string.Join(", ", claims.Select(c => $"{c.Key}={c.Value}")));
    }

    public async Task DisableUserAsync(string uid)
    {
        await FirebaseAuth.DefaultInstance.UpdateUserAsync(new UserRecordArgs
        {
            Uid = uid,
            Disabled = true
        });
        _logger.LogInformation("Disabled Firebase user {Uid}", uid);
    }

    public async Task EnableUserAsync(string uid)
    {
        await FirebaseAuth.DefaultInstance.UpdateUserAsync(new UserRecordArgs
        {
            Uid = uid,
            Disabled = false
        });
        _logger.LogInformation("Enabled Firebase user {Uid}", uid);
    }

    public async Task DeleteUserAsync(string uid)
    {
        await FirebaseAuth.DefaultInstance.DeleteUserAsync(uid);
        _logger.LogInformation("Deleted Firebase user {Uid}", uid);
    }

    public async Task<string> GenerateSignInLinkAsync(string email)
    {
        var link = await FirebaseAuth.DefaultInstance.GenerateEmailVerificationLinkAsync(email);
        return link;
    }
}
