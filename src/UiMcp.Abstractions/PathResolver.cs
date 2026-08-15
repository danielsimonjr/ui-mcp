using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UiMcp.Abstractions;

/// <summary>
/// Resolves a dotted path such as "sections.roster.live" or "sections.disk.drives[0].freeGb"
/// against a data document, and formats the result for display.
///
/// Ported from AdminLTE/JSON-UI/render.js. Where the two disagree, the JS is right.
///
/// THE LOAD-BEARING RULE: an unresolvable path returns MISSING (null), and missing formats as
/// "UNKNOWN" - never 0, never "". SPEC section 6 names this the most expensive recurring failure
/// on this machine: a section that could not be measured, displayed as a green zero, reads as
/// health. So the resolver never substitutes a default, and <see cref="Display"/> keeps "no value"
/// and "the value zero" in visibly different shapes.
///
/// It also never throws. A missing value is a display problem, not a crash - one bad path in a
/// sixty-node tree must not take the whole console down.
/// </summary>
public static class PathResolver
{
    private const string ItemPrefix = "$item";

    /// <summary>Rendered for anything that could not be resolved. Deliberately loud.</summary>
    public const string Unknown = "UNKNOWN";

    // A segment is an optional name followed by any number of [n] indices: "drives", "drives[0]",
    // "grid[1][0]", or bare "[2]". Anchored, so a segment that is not exactly this shape resolves
    // to missing rather than being partially interpreted.
    private static readonly Regex Segment = new(@"^([^\[]+)?((?:\[\d+\])*)$", RegexOptions.Compiled);
    private static readonly Regex Indices = new(@"\d+", RegexOptions.Compiled);

    private static readonly string[] ForbiddenPathTokens = { "__proto__", "constructor", "prototype" };

    /// <summary>
    /// Resolve <paramref name="path"/> against <paramref name="data"/>, or against
    /// <paramref name="scope"/> when the path begins with "$item".
    /// Returns null when the path cannot be resolved for ANY reason - absent key, index out of
    /// range, indexing a non-array, a key on a scalar, a refused path, or no scope for "$item".
    /// </summary>
    public static JsonElement? Resolve(JsonElement data, string? path, JsonElement? scope = null)
    {
        if (path is null) return null;

        var p = path;
        JsonElement? root = data;

        if (p.StartsWith(ItemPrefix, StringComparison.Ordinal))
        {
            // "$item" with no scope is a template error, not a lookup that happens to fail. It still
            // returns missing rather than throwing, so one bad binding degrades to UNKNOWN in place
            // instead of taking the render down.
            if (scope is null) return null;
            root = scope;
            p = p[ItemPrefix.Length..].TrimStart('.');
        }

        if (p.Length == 0) return root;

        // Defence in depth. The catalog already refuses these at validation, but the resolver is
        // reachable from the renderer's own bindings too, and a guard that exists in only one of two
        // entry points is a guard with a door left open.
        foreach (var bad in ForbiddenPathTokens)
            if (p.Contains(bad, StringComparison.Ordinal))
                return null;

        var current = root;
        foreach (var part in p.Split('.'))
        {
            if (current is null) return null;

            var m = Segment.Match(part);
            if (!m.Success) return null;

            if (m.Groups[1].Success)
            {
                var name = m.Groups[1].Value;
                if (current.Value.ValueKind != JsonValueKind.Object) return null;
                if (!current.Value.TryGetProperty(name, out var next)) return null;
                current = next;
            }

            if (m.Groups[2].Success && m.Groups[2].Value.Length > 0)
            {
                foreach (Match idx in Indices.Matches(m.Groups[2].Value))
                {
                    if (current is null) return null;
                    if (current.Value.ValueKind != JsonValueKind.Array) return null;
                    var i = int.Parse(idx.Value, CultureInfo.InvariantCulture);
                    if (i >= current.Value.GetArrayLength()) return null;
                    current = current.Value[i];
                }
            }
        }

        return current;
    }

    /// <summary>
    /// The one place a value becomes visible text.
    ///
    /// "UNKNOWN" is deliberate and load-bearing. Note that JSON null formats as UNKNOWN too: a key
    /// present but null carries no more information than an absent key, and pretending otherwise
    /// puts a confident-looking blank on a status board.
    /// </summary>
    public static string Display(JsonElement? v)
    {
        if (v is null) return Unknown;

        return v.Value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => Unknown,
            JsonValueKind.True => "YES",
            JsonValueKind.False => "NO",
            // "R" round-trips, so 258.5 stays 258.5 and does not acquire trailing noise.
            // InvariantCulture matters: a comma decimal separator on a differently-configured
            // machine would silently change every number on the display.
            JsonValueKind.Number => v.Value.GetDouble().ToString("R", CultureInfo.InvariantCulture),
            JsonValueKind.String => v.Value.GetString() ?? Unknown,
            JsonValueKind.Array => v.Value.GetArrayLength().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.Object => "OBJ",
            _ => Unknown
        };
    }
}
