using System.Text.Json;

namespace UiMcp.Abstractions;

// INTERNAL, not public. Both describe how the catalog is BUILT, and every use of either is inside
// this file - the catalog is a private static dictionary and nothing outside constructs one.
// Publishing them widened UiMcp.Abstractions' surface with two types no caller can act on: a
// consumer given a ComponentSpec cannot validate anything with it, because Validate() is the only
// entry point and it takes JSON. Surfaced by unused-analysis.md, which classified both as
// "referenced only within their own file" - correctly, and that is what a needlessly public type
// looks like from the outside.

/// <summary>A prop rule: how to validate it, and whether it may be omitted.</summary>
internal sealed record PropRule(Func<JsonElement, object> Validate, bool Optional = false);

/// <summary>What one catalog component accepts.</summary>
internal sealed record ComponentSpec(IReadOnlyDictionary<string, PropRule> Props, bool AcceptsChildren);

/// <summary>
/// The constrained component vocabulary, and the boundary that enforces it.
///
/// WHY A CATALOG AT ALL
///   This layer renders JSON an LLM produced. That is a prompt-injection surface unless the output
///   space is CLOSED. The pattern is borrowed from json-render: give the model a typed catalog and
///   validate at the boundary, so anything it emits is either schema-valid or rejected - nothing in
///   between. The tree is DATA TO VALIDATE, never INSTRUCTIONS TO OBEY.
///
/// ADDING A COMPONENT IS A DELIBERATE ACT. If it is not here, it cannot render.
///
/// Deliberately absent, each an injection vector and none needed to show machine telemetry:
/// raw html, model-supplied hrefs or image srcs, event handlers, style strings, class overrides.
/// </summary>
public static class CatalogValidator
{
    public const int MaxDepth = 12;
    public const int MaxChildren = 64;

    private static PropRule Req(Func<JsonElement, object> f) => new(f);
    private static PropRule Opt(Func<JsonElement, object> f) => new(f, Optional: true);

    private static readonly IReadOnlyDictionary<string, ComponentSpec> Catalog =
        new Dictionary<string, ComponentSpec>(StringComparer.Ordinal)
        {
            // Full-width status banner. The single most important thing on the page.
            ["StatusBanner"] = new(new Dictionary<string, PropRule>
            {
                ["label"] = Req(PropTypes.Text),
                ["tone"] = Req(PropTypes.Tone),
                ["detail"] = Opt(PropTypes.Text),
            }, AcceptsChildren: false),

            // A titled container. The workhorse.
            ["Panel"] = new(new Dictionary<string, PropRule>
            {
                ["title"] = Req(PropTypes.Text),
                ["tone"] = Opt(PropTypes.Tone),
            }, AcceptsChildren: true),

            // Layout: an N-column row. Children are distributed across columns.
            ["Row"] = new(new Dictionary<string, PropRule>
            {
                ["cols"] = Req(PropTypes.RowCols),
            }, AcceptsChildren: true),

            // One number with a label. Binds to data via valuePath.
            ["Metric"] = new(new Dictionary<string, PropRule>
            {
                ["label"] = Req(PropTypes.Text),
                ["valuePath"] = Req(PropTypes.Path),
                ["unit"] = Opt(PropTypes.Text),
                ["deltaPath"] = Opt(PropTypes.Path),
                ["tone"] = Opt(PropTypes.Tone),
            }, AcceptsChildren: false),

            // Label / value line. For things that are not numbers.
            ["Field"] = new(new Dictionary<string, PropRule>
            {
                ["label"] = Req(PropTypes.Text),
                ["valuePath"] = Req(PropTypes.Path),
                ["tone"] = Opt(PropTypes.Tone),
            }, AcceptsChildren: false),

            // Bounded bar.
            ["Gauge"] = new(new Dictionary<string, PropRule>
            {
                ["label"] = Req(PropTypes.Text),
                ["valuePath"] = Req(PropTypes.Path),
                ["maxPath"] = Opt(PropTypes.Path),
                ["tone"] = Opt(PropTypes.Tone),
            }, AcceptsChildren: false),

            // Repeats its children once per element of an array, with $item scope.
            ["Repeat"] = new(new Dictionary<string, PropRule>
            {
                ["fromPath"] = Req(PropTypes.Path),
                ["emptyText"] = Opt(PropTypes.Text),
            }, AcceptsChildren: true),

            // A table built from an array of objects.
            ["Table"] = new(new Dictionary<string, PropRule>
            {
                ["fromPath"] = Req(PropTypes.Path),
                ["columns"] = Req(PropTypes.Columns),
                ["emptyText"] = Opt(PropTypes.Text),
            }, AcceptsChildren: false),

            // Plain paragraph. Text only.
            ["Note"] = new(new Dictionary<string, PropRule>
            {
                ["text"] = Req(PropTypes.Text),
                ["tone"] = Opt(PropTypes.Tone),
            }, AcceptsChildren: false),
        };

    /// <summary>Catalog component names. Exposed so a test can pin the vocabulary against the spec.</summary>
    public static IReadOnlyCollection<string> ComponentNames => (IReadOnlyCollection<string>)Catalog.Keys;

    /// <summary>
    /// Validate a tree. Returns a normalised <see cref="ValidatedNode"/>, or throws
    /// <see cref="UiValidationException"/> naming the component and prop at fault.
    ///
    /// The caller renders ONLY if the WHOLE tree passed. A partially rendered invalid tree is worse
    /// than none: it looks like a working display while silently omitting whatever failed.
    /// </summary>
    public static ValidatedNode Validate(JsonElement node) => Validate(node, 0);

    private static ValidatedNode Validate(JsonElement node, int depth)
    {
        if (depth > MaxDepth) throw new UiValidationException($"tree too deep (max {MaxDepth})");
        if (node.ValueKind != JsonValueKind.Object) throw new UiValidationException("node must be an object");

        if (!node.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            throw new UiValidationException("unknown component: <missing type>");

        var type = typeEl.GetString()!;
        if (!Catalog.TryGetValue(type, out var spec))
            throw new UiValidationException($"unknown component: {type}");

        var given = node.TryGetProperty("props", out var p) && p.ValueKind == JsonValueKind.Object
            ? p.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        // Reject unknown props outright rather than ignoring them. Silent ignores hide typos, and a
        // typo in a security boundary is how things get through.
        foreach (var key in given.Keys)
            if (!spec.Props.ContainsKey(key))
                throw new UiValidationException($"{type}: unknown prop \"{key}\"");

        var props = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, rule) in spec.Props)
        {
            if (!given.TryGetValue(key, out var raw))
            {
                if (rule.Optional) continue;
                throw new UiValidationException($"{type}: missing required prop \"{key}\"");
            }

            try { props[key] = rule.Validate(raw); }
            catch (UiValidationException e)
            {
                // Re-wrapped so the message names the path to the fault. "expected string" alone is
                // unactionable in a 60-node tree.
                throw new UiValidationException($"{type}.{key}: {e.Message}", e);
            }
        }

        var children = Array.Empty<ValidatedNode>();
        if (node.TryGetProperty("children", out var kids) && kids.ValueKind != JsonValueKind.Null)
        {
            if (!spec.AcceptsChildren) throw new UiValidationException($"{type} does not accept children");
            if (kids.ValueKind != JsonValueKind.Array) throw new UiValidationException("children must be an array");

            var list = kids.EnumerateArray().ToList();
            if (list.Count > MaxChildren)
                throw new UiValidationException($"too many children (max {MaxChildren})");

            children = list.Select(c => Validate(c, depth + 1)).ToArray();
        }

        return new ValidatedNode(type, props, children);
    }
}
