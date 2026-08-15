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
        {
            RenderCalls++;
            LastTree = tree;
            // A real surface records the hash of what it drew. Modelling that here is what lets the
            // one-source-of-truth test below mean something.
            Status = Status with { TreeHash = "surface-computed-hash", NodeCount = 1 };
        }

        public void Close() => CloseCalls++;
    }

    private const string ValidTree = """{"type":"Note","props":{"text":"all nominal"}}""";
    private const string SomeData = """{"x":1}""";

    // ---- ui_render: the tree may arrive as an object OR as a JSON string -----------------------

    // Found by driving the DEPLOYED plugin over stdio, not by any unit test. `tree` was declared
    // `string`, so a caller passing an actual JSON OBJECT - the natural reading of the tool's own
    // description, "The UI tree as JSON" - failed during SDK parameter binding, BEFORE the method
    // ran. The caller saw only "An error occurred invoking 'ui_render'"; the real cause
    // (System.Text.Json: "The JSON value could not be converted to System.String") went to stderr,
    // where an agent calling this tool cannot see it. Refusing WITH A REASON is this server's whole
    // posture, and that path could not refuse with anything.
    //
    // SPEC 4 calls `tree` "catalog JSON" and does not require a stringified form, so accepting both
    // is the contract rather than a loosening of it. Both shapes are pinned because either alone
    // would let the other regress.

    [Fact]
    public void Render_TreePassedAsJsonObject_IsAccepted()
    {
        var spy = new SpySurface();
        var tools = new UiTools(spy);

        var result = tools.Render(AsJson(ValidTree));

        result.Should().NotContain("rejected");
        spy.RenderCalls.Should().Be(1, "an object-shaped tree is the natural way to call this");
        spy.LastTree!.Type.Should().Be("Note");
    }

    [Fact]
    public void Render_TreePassedAsJsonString_IsStillAccepted_PositiveControl()
    {
        var spy = new SpySurface();
        var tools = new UiTools(spy);

        // The string form is what every existing caller sends; accepting objects must not drop it.
        var result = tools.Render(AsJsonStringValue(ValidTree));

        result.Should().NotContain("rejected");
        spy.RenderCalls.Should().Be(1);
        spy.LastTree!.Type.Should().Be("Note");
    }

    [Fact]
    public void Render_DataPassedAsJsonObject_IsAccepted()
    {
        var spy = new SpySurface();
        var tools = new UiTools(spy);

        var result = tools.Render(AsJson(ValidTree), AsJson(SomeData));

        result.Should().NotContain("rejected");
        spy.RenderCalls.Should().Be(1);
    }

    /// <summary>The tree as a JSON object - the way an agent would naturally send it.</summary>
    private static JsonElement AsJson(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>The tree wrapped as a JSON *string* value - the legacy caller shape.</summary>
    private static JsonElement AsJsonStringValue(string json) =>
        JsonDocument.Parse(JsonSerializer.Serialize(json)).RootElement;

    // ---- ui_render: refuse first, render second ------------------------------------------------

    [Fact]
    public void Render_UnknownComponent_IsRejected_AndNeverReachesTheSurface()
    {
        var spy = new SpySurface();
        var tools = new UiTools(spy);

        var result = tools.Render(AsJson("""{"type":"Script","props":{}}"""));

        result.Should().Contain("rejected");
        spy.RenderCalls.Should().Be(0, "an invalid tree must never touch the window");
    }

    [Fact]
    public void Render_ValidTree_ReachesTheSurfaceExactlyOnce_PositiveControl()
    {
        var spy = new SpySurface();
        var tools = new UiTools(spy);

        var result = tools.Render(AsJson(ValidTree));

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

        tools.Render(AsJson(tree)).Should().Contain("rejected");
        spy.RenderCalls.Should().Be(0, "a partially valid tree is refused whole, not drawn in part");
    }

    [Fact]
    public void Render_RejectionMessage_NamesTheOffendingComponentAndProp()
    {
        var tools = new UiTools(new SpySurface());
        var result = tools.Render(AsJson("""{"type":"Note","props":{"text":"hi","onclick":"x"}}"""));
        result.Should().Contain("Note").And.Contain("onclick");
    }

    [Fact]
    public void Render_MalformedJson_IsRejectedNotThrown()
    {
        var spy = new SpySurface();
        var tools = new UiTools(spy);

        var act = () => tools.Render(AsJsonStringValue("{ this is not json"));

        act.Should().NotThrow("a bad payload is a refusal, not a server fault");
        spy.RenderCalls.Should().Be(0);
    }

    [Fact]
    public void Render_MalformedData_IsRejected_AndNothingIsDrawn()
    {
        var spy = new SpySurface();
        var tools = new UiTools(spy);

        tools.Render(AsJson(ValidTree), AsJsonStringValue("{ not json either")).Should().Contain("rejected");
        spy.RenderCalls.Should().Be(0);
    }

    /// <summary>
    /// ONE SOURCE OF TRUTH FOR THE TREE HASH. Found by running both tools in one live session:
    /// ui_render reported 7d4ef2048c4b while ui_status reported febd32fc836e for the same render,
    /// because the tool hashed the raw JSON text and the surface hashed the structure. Same field
    /// name, two functions, two answers. ui_render now reads the value BACK from the surface, so
    /// they cannot disagree - deleting the duplicate rather than syncing it, because syncing
    /// re-arms the drift.
    /// </summary>
    [Fact]
    public void RenderAndStatus_ReportTheSameTreeHash()
    {
        var spy = new SpySurface();
        var tools = new UiTools(spy);

        var rendered = tools.Render(AsJson(ValidTree));
        var status = tools.Status();

        rendered.Should().Contain("surface-computed-hash");
        status.Should().Contain("surface-computed-hash");
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
