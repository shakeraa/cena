// =============================================================================
// Cena Platform — CasGateModeProvider Tests (RDY-036 §12 / RDY-037)
//
// Pins the env + config resolution behavior so deploys can rely on a
// predictable Off/Shadow/Enforce resolution without surprise defaults.
// =============================================================================

using Cena.Actors.Cas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Cas;

public class CasGateModeTests : IDisposable
{
    private const string EnvKey = "CENA_CAS_GATE_MODE";
    private readonly string? _originalEnv;

    public CasGateModeTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable(EnvKey);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvKey, _originalEnv);
    }

    private static IConfiguration Config(string? configValue)
    {
        var kv = configValue is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["Cas:GateMode"] = configValue };
        return new ConfigurationBuilder().AddInMemoryCollection(kv).Build();
    }

    [Fact]
    public void Default_NoEnv_NoConfig_IsEnforce()
    {
        Environment.SetEnvironmentVariable(EnvKey, null);
        var provider = new CasGateModeProvider(Config(null), NullLogger<CasGateModeProvider>.Instance);
        Assert.Equal(CasGateMode.Enforce, provider.CurrentMode);
    }

    [Theory]
    [InlineData("off", CasGateMode.Off)]
    [InlineData("OFF", CasGateMode.Off)]
    [InlineData("Off", CasGateMode.Off)]
    [InlineData(" off ", CasGateMode.Off)]
    [InlineData("shadow", CasGateMode.Shadow)]
    [InlineData("Shadow", CasGateMode.Shadow)]
    [InlineData("enforce", CasGateMode.Enforce)]
    [InlineData("ENFORCE", CasGateMode.Enforce)]
    public void EnvVar_ParsesCaseAndWhitespaceInsensitively(string envValue, CasGateMode expected)
    {
        Environment.SetEnvironmentVariable(EnvKey, envValue);
        var provider = new CasGateModeProvider(Config(null), NullLogger<CasGateModeProvider>.Instance);
        Assert.Equal(expected, provider.CurrentMode);
    }

    [Fact]
    public void EnvVar_TakesPrecedenceOverConfig()
    {
        Environment.SetEnvironmentVariable(EnvKey, "off");
        var provider = new CasGateModeProvider(Config("enforce"), NullLogger<CasGateModeProvider>.Instance);
        Assert.Equal(CasGateMode.Off, provider.CurrentMode);
    }

    [Fact]
    public void ConfigValue_AppliesWhenNoEnv()
    {
        Environment.SetEnvironmentVariable(EnvKey, null);
        var provider = new CasGateModeProvider(Config("shadow"), NullLogger<CasGateModeProvider>.Instance);
        Assert.Equal(CasGateMode.Shadow, provider.CurrentMode);
    }

    [Fact]
    public void UnknownValue_FallsBackToEnforce_NotSilentOff()
    {
        // Defensive default: anything we don't recognise must be safe
        // (Enforce), not a silent disable.
        Environment.SetEnvironmentVariable(EnvKey, "banana");
        var provider = new CasGateModeProvider(Config(null), NullLogger<CasGateModeProvider>.Instance);
        Assert.Equal(CasGateMode.Enforce, provider.CurrentMode);
    }

    [Fact]
    public void CurrentMode_IsStableAcrossReads()
    {
        Environment.SetEnvironmentVariable(EnvKey, "shadow");
        var provider = new CasGateModeProvider(Config(null), NullLogger<CasGateModeProvider>.Instance);

        // Change the env after construction — provider must not re-resolve.
        Environment.SetEnvironmentVariable(EnvKey, "off");

        Assert.Equal(CasGateMode.Shadow, provider.CurrentMode);
        Assert.Equal(CasGateMode.Shadow, provider.CurrentMode);
    }
}
