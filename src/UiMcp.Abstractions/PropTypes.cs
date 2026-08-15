using System.Text.Json;
using System.Text.RegularExpressions;

namespace UiMcp.Abstractions;

/// <summary>
/// Prop validators. Each returns a COERCED SAFE VALUE or throws - never the raw input.
/// Ported from AdminLTE/JSON-UI/catalog.js; where the two disagree, the JS is right.
/// </summary>
public static class PropTypes
{
    public const int MaxTextLength = 500;
    public const int MaxTableColumns = 8;

    /// <summary>The closed tone set. A tone maps to a colour IN THE RENDERER; the tree never supplies one.</summary>
    public static readonly IReadOnlyList<string> Tones =
        new[] { "nominal", "attention", "critical", "degraded", "muted", "info" };

    /// <summary>Row.cols is a closed numeric set, not an arbitrary integer.</summary>
    public static readonly IReadOnlyList<int> RowColumnCounts = new[] { 1, 2, 3, 4 };

    // Anchored, and deliberately narrow: identifier chars, dots and array brackets only. Anything
    // that could escape into another syntax - quotes, slashes, semicolons, whitespace - is absent
    // by construction rather than blocked by a denylist.
    private static readonly Regex LegalPath = new(@"^[A-Za-z0-9_.\[\]]+$", RegexOptions.Compiled);

    // Checked as substrings, not as whole segments, because "a.__proto__.b" must fail too.
    private static readonly string[] ForbiddenPathTokens = { "__proto__", "constructor", "prototype" };

    public static object Text(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.String) throw new UiValidationException("expected string");
        var s = v.GetString()!;
        if (s.Length > MaxTextLength)
            throw new UiValidationException($"string too long (max {MaxTextLength})");
        return s;   // rendered as text only, never as markup
    }

    public static object Num(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || double.IsNaN(d) || double.IsInfinity(d))
            throw new UiValidationException("expected finite number");
        return d;
    }

    public static object Bool(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new UiValidationException("expected boolean")
    };

    public static object Path(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.String) throw new UiValidationException("expected string path");
        var s = v.GetString()!;

        // Prototype check FIRST. "__proto__" is legal under the charset rule, so checking the
        // charset first would let a valid-looking prototype path through on a future regex edit.
        // Ordering the stricter, security-bearing rule first makes that class of regression harder.
        foreach (var bad in ForbiddenPathTokens)
            if (s.Contains(bad, StringComparison.Ordinal))
                throw new UiValidationException("prototype access refused");

        if (!LegalPath.IsMatch(s)) throw new UiValidationException("illegal characters in path");
        return s;
    }

    public static object Tone(JsonElement v)
    {
        var s = v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (s is null || !Tones.Contains(s))
            throw new UiValidationException("tone must be one of " + string.Join("|", Tones));
        return s;
    }

    public static object RowCols(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n) || !RowColumnCounts.Contains(n))
            throw new UiValidationException("cols must be one of " + string.Join("|", RowColumnCounts));
        return n;
    }

    /// <summary>
    /// Table columns. Each column's valuePath goes through <see cref="Path"/>, so the
    /// prototype guard reaches inside the array rather than stopping at its edge - a nested path is
    /// exactly where a boundary check tends to be forgotten.
    /// </summary>
    public static object Columns(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Array) throw new UiValidationException("columns must be an array");
        var items = v.EnumerateArray().ToList();
        if (items.Count > MaxTableColumns)
            throw new UiValidationException($"max {MaxTableColumns} columns");

        var cols = new List<ValidatedColumn>(items.Count);
        foreach (var c in items)
        {
            if (c.ValueKind != JsonValueKind.Object) throw new UiValidationException("column must be an object");
            if (!c.TryGetProperty("header", out var h)) throw new UiValidationException("column missing \"header\"");
            if (!c.TryGetProperty("valuePath", out var p)) throw new UiValidationException("column missing \"valuePath\"");
            cols.Add(new ValidatedColumn((string)Text(h), (string)Path(p)));
        }
        return cols;
    }
}
