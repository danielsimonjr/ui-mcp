using System.Text.Json;
using FluentAssertions;
using UiMcp.Abstractions;
using UiMcp.Hosting;

namespace UiMcp.Tests;

/// <summary>
/// <see cref="UiSurface"/> - the real WPF surface, previously untested.
///
/// The threading underneath it (<see cref="UiThreadHost"/>) is thoroughly covered already, and the
/// tool layer is covered against a spy, so what was missing is specifically the WPF-object handling
/// in between: window lifetime, idempotent open, auto-open on render, and whether `Status` tells
/// the truth.
///
/// SPLIT DELIBERATELY INTO TWO CLASSES:
///
///   - Everything that does NOT need a window lives here and runs everywhere. That includes the
///     single most important assertion in the file - that a surface which has never rendered
///     reports NULLS, not zeros - which is the UNKNOWN-not-zero invariant applied to the server's
///     own introspection.
///
///   - Everything that must actually SHOW a window lives in <see cref="UiSurfaceWindowTests"/>,
///     which briefly opens and closes real windows.
///
/// The split is not squeamishness about the hard case: it is that these two groups fail for
/// different reasons. A failure here is a logic bug; a failure there may be an environment without
/// a desktop, and a suite that cannot tell those apart teaches you to ignore it.
/// </summary>
public class UiSurfaceTests
{
    [Fact]
    public void ASurfaceThatHasNeverRenderedReportsNulls_NotZeros()
    {
        // THE assertion this class exists for. `nodeCount: 0` and `nodeCount: UNKNOWN` are
        // different claims - "I rendered an empty tree" versus "I have never rendered" - and
        // ui_status turns each null below into UNKNOWN precisely so the second cannot masquerade
        // as the first. A status board that answers 0 when it means "never measured" is lying in
        // the most convincing possible way.
        using var surface = new UiSurface();
        var status = surface.Status;

        status.WindowAlive.Should().BeFalse();
        status.Title.Should().BeNull();
        status.NodeCount.Should().BeNull("never rendered is not a node count of zero");
        status.TreeHash.Should().BeNull();
        status.LastRenderUtc.Should().BeNull();
        status.LastFault.Should().BeNull("no fault is not the same as a fault with no message");
    }

    [Fact]
    public void ClosingASurfaceThatWasNeverOpenedIsSilent()
    {
        // Reached from the orderly MCP exit path as well as from error paths. A teardown that
        // throws when there is nothing to tear down turns cleanup into a new fault.
        using var surface = new UiSurface();
        var close = surface.Close;
        close.Should().NotThrow();
    }

    [Fact]
    public void DisposingTwiceIsSilent()
    {
        var surface = new UiSurface();
        surface.Dispose();
        var again = surface.Dispose;
        again.Should().NotThrow();
    }

    [Fact]
    public void StatusIsStillAnswerableAfterDispose()
    {
        // ui_status must never be the call that throws. A disposed surface is a degraded display,
        // and the tool's job is to REPORT that, not to fail alongside it.
        var surface = new UiSurface();
        surface.Dispose();

        var read = () => surface.Status;
        read.Should().NotThrow();
        surface.Status.WindowAlive.Should().BeFalse();
    }
}

/// <summary>
/// The half that needs a real window. These briefly show and close one.
///
/// They run on any interactive session; on a host with no desktop they would fail at
/// <see cref="UiSurface.Open"/>, which is the correct and informative failure - see SPEC 10.1,
/// where the desktop assumption is retired for the interactive path and explicitly NOT retired for
/// S4U. That is a real environmental constraint, and encoding it as a passing skip would hide it.
/// </summary>
[Collection("ui-window")]
public class UiSurfaceWindowTests
{
    private static JsonElement Data(string json) => JsonDocument.Parse(json).RootElement;

    private static ValidatedNode Tree(string json)
        => CatalogValidator.Validate(Data(json));

    private const string OneNote = """{"type":"Note","props":{"text":"hello"}}""";

    private const string ThreeNodes = """
        {"type":"Panel","props":{"title":"p"},
         "children":[{"type":"Note","props":{"text":"a"}},{"type":"Note","props":{"text":"b"}}]}
        """;

    [Fact]
    public void OpenBringsTheWindowAlive()
    {
        using var surface = new UiSurface();
        surface.Open("probe-open", topmost: false, width: 320, height: 200);

        surface.Status.WindowAlive.Should().BeTrue();
        surface.Status.Title.Should().Be("probe-open");
    }

    [Fact]
    public void OpenIsIdempotent_ASecondCallRetitlesRatherThanOpeningASecondWindow()
    {
        using var surface = new UiSurface();
        surface.Open("first", topmost: false, width: 320, height: 200);
        surface.Open("second", topmost: false, width: 320, height: 200);

        surface.Status.WindowAlive.Should().BeTrue();
        surface.Status.Title.Should().Be("second",
            "the existing window is retitled and focused; a second window would strand the first");
    }

    [Fact]
    public void RenderAutoOpensWhenNothingWasOpenedFirst()
    {
        // An agent that renders without opening meant to display something. Failing on a ceremony
        // step would be pedantry, not safety.
        using var surface = new UiSurface();
        surface.Render(Tree(OneNote), Data("{}"));

        surface.Status.WindowAlive.Should().BeTrue();
        surface.Status.NodeCount.Should().Be(1);
    }

    [Fact]
    public void RenderRecordsTheNodeCountAndATimestamp()
    {
        using var surface = new UiSurface();
        surface.Render(Tree(ThreeNodes), Data("{}"));

        surface.Status.NodeCount.Should().Be(3, "one Panel plus two Notes");
        surface.Status.LastRenderUtc.Should().NotBeNull();
    }

    [Fact]
    public void TheTreeHashIsSTRUCTURAL_NotAHashOfTheRawJson()
    {
        // The defect that produced two different hashes under one name was exactly a disagreement
        // about WHAT is hashed. Pinning the structural property stops a future "optimisation" from
        // quietly reintroducing raw-JSON hashing: identical structure with different DATA must hash
        // the same, because the hash answers "is the same view up?", not "did the numbers move?".
        using var a = new UiSurface();
        using var b = new UiSurface();

        var tree = """{"type":"Field","props":{"label":"l","valuePath":"v"}}""";
        a.Render(Tree(tree), Data("""{"v":1}"""));
        b.Render(Tree(tree), Data("""{"v":9999}"""));

        a.Status.TreeHash.Should().Be(b.Status.TreeHash);
        a.Status.TreeHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ADifferentTreeStructureHashesDifferently()
    {
        using var a = new UiSurface();
        using var b = new UiSurface();

        a.Render(Tree(OneNote), Data("{}"));
        b.Render(Tree(ThreeNodes), Data("{}"));

        a.Status.TreeHash.Should().NotBe(b.Status.TreeHash);
    }

    [Fact]
    public void CloseTakesTheWindowDownButLeavesTheSurfaceAnswering()
    {
        // "A closed display is not a stopped service." The window goes; ui_status must keep
        // working and must report the truth about it.
        using var surface = new UiSurface();
        surface.Open("probe-close", topmost: false, width: 320, height: 200);
        surface.Status.WindowAlive.Should().BeTrue();

        surface.Close();

        // Close is fire-and-forget by design, so the window teardown is asynchronous. Poll rather
        // than sleep a fixed amount: a fixed sleep is either flaky or slow, and this suite has no
        // business being either.
        var gone = SpinWait.SpinUntil(() => !surface.Status.WindowAlive, TimeSpan.FromSeconds(10));

        gone.Should().BeTrue("Close must actually close the window");
        surface.Status.Title.Should().Be("probe-close", "the last title is still a fact worth reporting");
    }

    [Fact]
    public void RenderingAfterACloseOpensAFreshWindow()
    {
        using var surface = new UiSurface();
        surface.Open("probe-reopen", topmost: false, width: 320, height: 200);
        surface.Close();
        SpinWait.SpinUntil(() => !surface.Status.WindowAlive, TimeSpan.FromSeconds(10));

        surface.Render(Tree(OneNote), Data("{}"));

        surface.Status.WindowAlive.Should().BeTrue("the surface recovers rather than staying dead");
    }
}
