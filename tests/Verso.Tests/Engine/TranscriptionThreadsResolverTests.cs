using Verso.Core.Engine;

namespace Verso.Tests.Engine;

public class TranscriptionThreadsResolverTests
{
    [Fact]
    public void Resolve_WhenEnvVarValid_TakesPrecedenceOverSetting()
    {
        WithEnvVar("4", () =>
        {
            Assert.Equal(4, TranscriptionThreadsResolver.Resolve(settingsMaxThreads: 8));
        });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData(null)]
    public void Resolve_WhenEnvVarInvalidOrAbsent_FallsBackToSettingWhenPositive(string? envValue)
    {
        WithEnvVar(envValue, () =>
        {
            Assert.Equal(8, TranscriptionThreadsResolver.Resolve(settingsMaxThreads: 8));
        });
    }

    [Fact]
    public void Resolve_WhenNoEnvVarAndSettingIsZero_ReturnsZeroForAutomatic()
    {
        WithEnvVar(null, () =>
        {
            Assert.Equal(0, TranscriptionThreadsResolver.Resolve(settingsMaxThreads: 0));
        });
    }

    private static void WithEnvVar(string? value, Action assertion)
    {
        var previous = Environment.GetEnvironmentVariable(TranscriptionThreadsResolver.EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(TranscriptionThreadsResolver.EnvVarName, value);
            assertion();
        }
        finally
        {
            Environment.SetEnvironmentVariable(TranscriptionThreadsResolver.EnvVarName, previous);
        }
    }
}
