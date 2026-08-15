using System.Text.Json;
using FluentAssertions;
using UiMcp.Abstractions;
using UiMcp.Hosting;
using UiMcp.Tools;

namespace UiMcp.Tests;

/// <summary>
/// The tool layer, tested against a spy surface so none of this needs a desktop.
///
/// THE TEST THAT MATTERS MOST IS A NEGATIVE: an invalid tree must be refused BEFORE anything
/// touches the window. SPEC section 4 - "ui_render validates the whole tree first and renders only
/// if all of it passed. A partially rendered invalid tree is worse than none: it looks like a
/// working display while silently omitting what failed."
///
/// A spy can assert that negative ("Render was never called"), which a screenshot cannot. That is
/// why the seam exists at all.
/// </summary>
public class UiToolsTests
{
    private sealed class SpySurface : IUiSurface
    {
        public int RenderCalls { get; private set; }
        public int OpenCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public ValidatedNode? LastTree { get; private set; }
        public string? LastTitle { get; private set; }
        public UiSurfaceStatus Status { get; set; } =
            new(WindowAlive: false, Title: null, NodeCount: null, TreeHash: null,
                LastRenderUtc: null, LastFault: null);

        public void Open(string title, bool topmost, double width, double height)
        { OpenCalls++; LastTitle = title; }

        public void Render(ValidatedNode tree, JsonElement data)
        { RenderCalls++; LastTree = tree; }

        public void Close() => CloseCalls++;
    }

    private const string ValidTree = """{"type":"Note","props":{"text":"all nominal"}}""";

    // ---- ui_render: refuse first, render second ------------------------------------------------

    [Fact]
    public void Render_UnknownComponent_IsRejected_AndNeverReachesTheSurface()
    {
        var spy = new SpySurface();
        var tools = new UiTools(spy);

        var result = tools.Render("""{"type":"Script","props":{}}""");

        result.Should().Contain("rejected");
        spy.RenderCalls.Should().Be(0, "an invalid tree must never touch the window");
    }

    [Fact]
    public void Render_ValidTree_ReachesTheSurfaceExactlyOnce_PositiveControl()
    {
        var spy = new SpySurface();
        var tools = new UiTools(spy);

        var result = tools.Render(ValidTree);

        result.Should().NotContain("rejected");
        spy.RenderCalls.Should().Be(1);
        spy.LastTree!.Type.Should().Be("Note");
    }

    [Fact]
    public void Render_PartiallyInvalidTree_RendersNothingAtAll()
    {
        var spy = new SpySurface();
        var tools = new UiTools(spy);

        // First child is fine; the second is not. All-or-nothing, so neither is drawn.
        var tree = """
        {"type":"Panel","props":{"title":"T"},"children":[
          {"type":"Note","props":{"text":"good"}},
          {"type":"Note","props":{"text":"bad","onclick":"evil"}}
        ]}
        """;

        tools.Render(tree).Should().Contain("rejected");
        spy.RenderCalls.Should().Be(0, "a partially valid tree is refused whole, not drawn in part");
    }

    [Fact]
    public void Render_RejectionMessage_NamesTheOffendingComponentAndProp()
    {
        var tools = new UiTools(new SpySurface());
        var result = tools.Render("""{"type":"Note","props":{"text":"hi","onclick":"x"}}""");
        result.Should().Contain("Note").And.Contain("onclick");
    }

    [Fact]
    public void Render_MalformedJson_IsRejectedNotThrown()
    {
        var spy = new SpySurface();
        var tools = new UiTools(spy);

        var act = () => tools.Render("{ this is not json");

        act.Should().NotThrow("a bad payload is a refusal, not a server fault");
        spy.RenderCalls.Should().Be(0);
    }

    [Fact]
    public void Render_MalformedData_IsRejected_AndNothingIsDrawn()
    {
        var spy = new SpySurface();
        var tools = new UiTools(spy);

        tools.Render(ValidTree, "{ not json either").Should().Contain("rejected");
        spy.RenderCalls.Should().Be(0);
    }

    // ---- ui_status: UNKNOWN, never a fabricated zero --------------------------------------------

    [Fact]
    public void Status_WithNoWindow_ReportsUnknownRatherThanZero()
    {
        var tools = new UiTools(new SpySurface());
        var status = tools.Status();

        status.Should().Contain("UNKNOWN");
        status.Should().NotContain("\"nodeCount\": 0", "never measured is not the same as measured zero");
    }

    [Fact]
    public void Status_AfterARender_ReportsRealNumbers_PositiveControl()
    {
        var spy = new SpySurface
        {
            Status = new(WindowAlive: true, Title: "Starship", NodeCount: 12,
                         TreeHash: "abc123", LastRenderUtc: DateTimeOffset.UnixEpoch, LastFault: null)
        };
        var status = new UiTools(spy).Status();

        status.Should().Contain("Starship").And.Contain("12").And.Contain("abc123");
    }

    [Fact]
    public void Status_ReportsAnAbsorbedFault_RatherThanHidingIt()
    {
        var spy = new SpySurface
        {
            Status = new(WindowAlive: true, Title: "T", NodeCount: 1, TreeHash: "h",
                         LastRenderUtc: DateTimeOffset.UnixEpoch, LastFault: "window handler blew up")
        };
        new UiTools(spy).Status().Should().Contain("window handler blew up");
    }

    // ---- ui_open / ui_close ---------------------------------------------------------------------

    [Fact]
    public void Open_PassesTheTitleThrough()
    {
        var spy = new SpySurface();
        new UiTools(spy).Open("Starship Console");
        spy.OpenCalls.Should().Be(1);
        spy.LastTitle.Should().Be("Starship Console");
    }

    [Fact]
    public void Close_ClosesTheWindow()
    {
        var spy = new SpySurface();
        new UiTools(spy).Close();
        spy.CloseCalls.Should().Be(1);
    }
}
