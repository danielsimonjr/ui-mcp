using System.Text.Json;

namespace UiMcp.Abstractions;

/// <summary>
/// A validated visibility condition from the JSON-UI spec.
///
/// JSON-UI defines <c>visible?</c> as a discriminated union on every element:
/// <list type="bullet">
///   <item><c>true</c> / <c>false</c> — always visible or always hidden.</item>
///   <item><c>{ "path": "..." }</c> — visible when the path resolves to a truthy value in the
///     render data.</item>
///   <item>Logic expressions: <c>and</c>, <c>or</c>, <c>not</c>, <c>eq</c>, <c>neq</c>,
///     <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</item>
/// </list>
///
/// The absent case (no <c>visible</c> prop) is treated as always-visible, matching the JS behaviour.
/// An unresolvable path is treated as falsy (hidden), so a visibility condition that cannot be
/// evaluated never silently shows content that should be hidden.
///
/// Ported from <c>danielsimonjr/JSON-UI</c> — see <c>packages/core/src/visibility.ts</c> and
/// <c>packages/core/src/types.ts</c>.
/// </summary>
public abstract record VisibilityCondition
{
    private VisibilityCondition() { }

    /// <summary>Always visible (literal <c>true</c>).</summary>
    public sealed record Always : VisibilityCondition;

    /// <summary>Always hidden (literal <c>false</c>).</summary>
    public sealed record Never : VisibilityCondition;

    /// <summary>Visible when the resolved path is truthy in the render data.</summary>
    public sealed record WhenPath(string Path) : VisibilityCondition;

    /// <summary>Visible when ALL sub-conditions are true.</summary>
    public sealed record And(IReadOnlyList<VisibilityCondition> Conditions) : VisibilityCondition;

    /// <summary>Visible when ANY sub-condition is true.</summary>
    public sealed record Or(IReadOnlyList<VisibilityCondition> Conditions) : VisibilityCondition;

    /// <summary>Negates a sub-condition.</summary>
    public sealed record Not(VisibilityCondition Condition) : VisibilityCondition;

    /// <summary>Visible when two operands are equal.</summary>
    public sealed record Eq(VisibilityOperand Left, VisibilityOperand Right) : VisibilityCondition;

    /// <summary>Visible when two operands are not equal.</summary>
    public sealed record Neq(VisibilityOperand Left, VisibilityOperand Right) : VisibilityCondition;

    /// <summary>Visible when left &gt; right (numeric).</summary>
    public sealed record Gt(VisibilityOperand Left, VisibilityOperand Right) : VisibilityCondition;

    /// <summary>Visible when left &gt;= right (numeric).</summary>
    public sealed record Gte(VisibilityOperand Left, VisibilityOperand Right) : VisibilityCondition;

    /// <summary>Visible when left &lt; right (numeric).</summary>
    public sealed record Lt(VisibilityOperand Left, VisibilityOperand Right) : VisibilityCondition;

    /// <summary>Visible when left &lt;= right (numeric).</summary>
    public sealed record Lte(VisibilityOperand Left, VisibilityOperand Right) : VisibilityCondition;

    // ---------------------------------------------------------------------------
    // Parsing
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Parse a raw JSON element into a <see cref="VisibilityCondition"/>, or throw
    /// <see cref="UiValidationException"/> if the shape is unrecognised.
    /// </summary>
    public static VisibilityCondition Parse(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.True) return new Always();
        if (el.ValueKind == JsonValueKind.False) return new Never();

        if (el.ValueKind != JsonValueKind.Object)
            throw new UiValidationException("visible must be true, false, or an object");

        if (el.TryGetProperty("path", out var pathEl))
        {
            if (pathEl.ValueKind != JsonValueKind.String)
                throw new UiValidationException("visible.path must be a string");
            return new WhenPath(pathEl.GetString()!);
        }

        if (el.TryGetProperty("and", out var andEl))
            return new And(ParseArray(andEl, "and"));

        if (el.TryGetProperty("or", out var orEl))
            return new Or(ParseArray(orEl, "or"));

        if (el.TryGetProperty("not", out var notEl))
            return new Not(Parse(notEl));

        if (el.TryGetProperty("eq", out var eqEl))
        {
            var (l, r) = ParsePair(eqEl, "eq");
            return new Eq(l, r);
        }

        if (el.TryGetProperty("neq", out var neqEl))
        {
            var (l, r) = ParsePair(neqEl, "neq");
            return new Neq(l, r);
        }

        if (el.TryGetProperty("gt", out var gtEl))
        {
            var (l, r) = ParsePair(gtEl, "gt");
            return new Gt(l, r);
        }

        if (el.TryGetProperty("gte", out var gteEl))
        {
            var (l, r) = ParsePair(gteEl, "gte");
            return new Gte(l, r);
        }

        if (el.TryGetProperty("lt", out var ltEl))
        {
            var (l, r) = ParsePair(ltEl, "lt");
            return new Lt(l, r);
        }

        if (el.TryGetProperty("lte", out var lteEl))
        {
            var (l, r) = ParsePair(lteEl, "lte");
            return new Lte(l, r);
        }

        throw new UiValidationException("visible: unrecognised condition shape");
    }

    private static IReadOnlyList<VisibilityCondition> ParseArray(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Array)
            throw new UiValidationException($"visible.{key} must be an array");
        return el.EnumerateArray().Select(Parse).ToList();
    }

    private static (VisibilityOperand, VisibilityOperand) ParsePair(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Array)
            throw new UiValidationException($"visible.{key} must be a two-element array");
        var items = el.EnumerateArray().ToList();
        if (items.Count != 2)
            throw new UiValidationException($"visible.{key} must have exactly 2 elements");
        return (VisibilityOperand.Parse(items[0]), VisibilityOperand.Parse(items[1]));
    }
}

/// <summary>
/// One side of a comparison operator in a visibility condition.
/// Either a literal value or a data-path reference.
/// </summary>
public abstract record VisibilityOperand
{
    private VisibilityOperand() { }

    /// <summary>A literal JSON value (string, number, boolean, or null).</summary>
    public sealed record Literal(JsonElement Value) : VisibilityOperand;

    /// <summary>A data-path reference; resolved at evaluation time.</summary>
    public sealed record PathRef(string Path) : VisibilityOperand;

    /// <summary>Parse a raw JSON element into an operand.</summary>
    public static VisibilityOperand Parse(JsonElement el)
    {
        // {path: "..."} is a reference; everything else is a literal.
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("path", out var p)
            && p.ValueKind == JsonValueKind.String)
            return new PathRef(p.GetString()!);

        return new Literal(el);
    }
}
