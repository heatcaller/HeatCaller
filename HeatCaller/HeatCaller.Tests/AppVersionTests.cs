using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The version number. It used to be typed into AppVersion.cs by hand while
/// the csproj carried no version at all, so the installer (which reads the
/// assembly) could ship a package numbered differently from the title bar.
/// The csproj now wins, and these tests hold the two together.
/// </summary>
public sealed class AppVersionTests
{
    [Fact]
    public void The_version_is_a_sensible_dotted_number()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppVersion.Number);
    }

    [Fact]
    public void The_version_is_the_one_the_assembly_was_built_with()
    {
        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        Assert.NotNull(informational);

        // The SDK can append "+<git sha>" to the informational version;
        // nobody reading a title bar wants that part.
        var plus = informational!.IndexOf('+');
        var expected = plus < 0 ? informational : informational[..plus];
        Assert.Equal(expected, AppVersion.Number);
    }

    [Fact]
    public void The_version_is_never_blank()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Number));
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Short));
        Assert.Contains(AppVersion.Number, AppVersion.Short);
    }

    [Theory]
    [InlineData("0.59.0+abc123", "0.59.0")]
    [InlineData("0.59.0", "0.59.0")]
    [InlineData("  0.59.0+abc123  ", "0.59.0")]
    [InlineData("0.59.0-beta.2+abc123", "0.59.0-beta.2")]
    [InlineData("+abc123", "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void The_git_sha_is_cut_off_the_version(string? informational, string expected)
    {
        Assert.Equal(expected, AppVersion.WithoutBuildMetadata(informational));
    }
}
