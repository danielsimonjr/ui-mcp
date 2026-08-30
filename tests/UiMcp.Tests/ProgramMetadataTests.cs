using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using UiMcp;

namespace UiMcp.Tests;

/// <summary>
/// Pins the MCP server identity strings that <see cref="Program"/> publishes through the SDK.
/// Wire-level <c>server/discover</c> is gated in CI against the shipped bundle; these tests keep
/// the composition root honest on every <c>dotnet test</c> run.
/// </summary>
public class ProgramMetadataTests
{
    private static string RepoRoot { get; } = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void ServerVersion_MatchesDirectoryBuildProps()
    {
        var propsPath = Path.Combine(RepoRoot, "Directory.Build.props");
        var expected = XDocument.Load(propsPath)
            .Descendants("Version")
            .Select(e => e.Value)
            .FirstOrDefault();

        expected.Should().NotBeNullOrWhiteSpace();
        Program.ServerVersion.Should().Be(expected);
    }

    [Fact]
    public void ServerInstructions_DescribeAllFourTools()
    {
        var instructions = typeof(Program)
            .GetField("ServerInstructions", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetRawConstantValue()
            as string;

        instructions.Should().NotBeNullOrWhiteSpace();
        instructions.Should().Contain("ui_open");
        instructions.Should().Contain("ui_render");
        instructions.Should().Contain("ui_status");
        instructions.Should().Contain("ui_close");
    }
}
