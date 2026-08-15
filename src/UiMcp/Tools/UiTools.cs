using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using UiMcp.Abstractions;
using UiMcp.Hosting;

namespace UiMcp.Tools;

/// <summary>
/// The MCP tool surface. Any agent connected to the host can draw; nothing else can.
///
/// THE ORDER OF OPERATIONS IN <see cref="Render"/> IS THE SAFETY PROPERTY. Validate the WHOLE tree,
/// then draw - never interleaved. A partially rendered invalid tree is worse than none: it looks
/// like a working display while silently omitting whatever failed, which is the same
/// "silence reads as health" defect that UNKNOWN exists to prevent elsewhere.
/// </summary>
[McpServerToolType]
public sealed class UiTools
{
    private readonly IUiSurface _surface;

    public UiTools(IUiSurface surface) => _surface = surface;

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    [McpServerTool(Name = "ui_open"), Description(
        "Open the shared UI window, or focus it if already open. Idempotent - a second call does not " +
        "create a second window.")]
    public string Open(
        [Description("Window title")] string title = "Starship Console",
        [Description("Keep the window above others")] bool topmost = false,
        [Description("Window width in device-independent pixels")] double width = 1100,
        [Description("Window height in device-independent pixels")] double height = 800)
    {
        _surface.Open(title, topmost, width, height);
        return Json(new { ok = true, title });
    }

    [McpServerTool(Name = "ui_render"), Description(
        "Render a catalog-constrained JSON UI tree, replacing whatever is displayed. The tree is " +
        "VALIDATED IN FULL FIRST and drawn only if all of it passed; anything outside the catalog is " +
        "refused rather than ignored. Components: StatusBanner, Panel, Row, Metric, Field, Gauge, " +
        "Repeat, Table, Note. Bind values with dotted paths into `data`; an unresolvable path renders " +
        "as UNKNOWN, never as 0.")]
    public string Render(
        [Description("The UI tree, as a JSON object (a JSON string containing it is also accepted)")]
        JsonElement tree,
        [Description("Optional data the tree's paths resolve against, as a JSON object (a JSON string is also accepted)")]
        JsonElement? data = null)
    {
        // `JsonElement`, not `string`. When this took `string`, a caller passing an actual JSON
        // OBJECT - the natural reading of "The UI tree as JSON" - failed inside the SDK's parameter
        // binding, BEFORE this method ran, so none of the refusal paths below could report anything.
        // All the caller got was "An error occurred invoking 'ui_render'", with the real cause
        // (System.Text.Json: "The JSON value could not be converted to System.String") on stderr,
        // which an agent calling the tool cannot read. Found by driving the deployed plugin, not by
        // a unit test. SPEC 4 says "catalog JSON" and does not require a stringified form.
        if (!TryUnwrap(tree, out var treeEl, out var treeError))
            return Rejected($"tree is not valid JSON: {treeError}");

        JsonElement dataEl = default;
        if (data is { } rawData && rawData.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            if (!TryUnwrap(rawData, out dataEl, out var dataError))
                return Rejected($"data is not valid JSON: {dataError}");
        }

        ValidatedNode validated;
        try { validated = CatalogValidator.Validate(treeEl); }
        catch (UiValidationException e) { return Rejected(e.Message); }

        // Only past this line does anything reach the UI.
        _surface.Render(validated, dataEl);

        // Read the hash BACK from the surface rather than computing a second one here. The first
        // version hashed the raw JSON string while the surface hashed the structure, so ui_render
        // and ui_status reported different values under the same name - caught by running both in
        // one session. Two functions for one fact is the drift defect; deleting one beats syncing
        // them, because syncing re-arms it.
        return Json(new
        {
            ok = true,
            nodeCount = Count(validated),
            treeHash = _surface.Status.TreeHash ?? PathResolver.Unknown
        });
    }

    [McpServerTool(Name = "ui_status"), Description(
        "Report the window state, the last render, and any absorbed UI fault. Anything that cannot " +
        "be measured is reported as UNKNOWN rather than as a zero.")]
    public string Status()
    {
        var s = _surface.Status;
        return Json(new
        {
            windowAlive = s.WindowAlive,
            title = s.Title ?? PathResolver.Unknown,
            nodeCount = s.NodeCount?.ToString() ?? PathResolver.Unknown,
            treeHash = s.TreeHash ?? PathResolver.Unknown,
            lastRenderUtc = s.LastRenderUtc?.ToString("o") ?? PathResolver.Unknown,
            lastFault = s.LastFault ?? "none"
        });
    }

    [McpServerTool(Name = "ui_close"), Description(
        "Close the window. The SERVER stays up and keeps serving tools - a closed display is not a " +
        "stopped service.")]
    public string Close()
    {
        _surface.Close();
        return Json(new { ok = true });
    }

    /// <summary>
    /// Accept a JSON value that is either the payload itself or a JSON string containing it.
    ///
    /// Both shapes are real: an agent naturally sends an object, while every caller written against
    /// the previous <c>string</c> signature sends a string. Refusing either would break somebody,
    /// and refusing the object shape was the defect this exists to close.
    ///
    /// Returns false with a reason instead of throwing, so the caller gets a rejection it can read
    /// rather than a generic invocation error.
    /// </summary>
    private static bool TryUnwrap(JsonElement value, out JsonElement parsed, out string error)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            parsed = value;
            error = string.Empty;
            return true;
        }

        try
        {
            // Not disposed deliberately: RootElement must outlive this scope, and the document is
            // collected once nothing references it - the same lifetime the previous string-parsing
            // path relied on.
            parsed = JsonDocument.Parse(value.GetString() ?? string.Empty).RootElement;
            error = string.Empty;
            return true;
        }
        catch (JsonException e)
        {
            parsed = default;
            error = e.Message;
            return false;
        }
    }

    private static string Rejected(string reason) => Json(new { ok = false, rejected = reason });

    private static string Json(object o) => JsonSerializer.Serialize(o, Pretty);

    private static int Count(ValidatedNode n) => 1 + n.Children.Sum(Count);
}
