using System.Globalization;
using System.Text.Json;

namespace UiMcp.Abstractions;

/// <summary>
/// Evaluates a <see cref="VisibilityCondition"/> against a data document.
///
/// Ported from <c>danielsimonjr/JSON-UI</c> — see <c>packages/core/src/visibility.ts</c>
/// (<c>evaluateVisibility</c>, <c>evaluateLogicExpression</c>).
///
/// Auth-based conditions (<c>{auth: "signedIn"}</c>) are not applicable in ui-mcp; the MCP server
/// has no session/auth model. <c>VisibilityCondition.Parse</c> has no auth branch, so a tree that
/// carries one is refused at validation ("unrecognised condition shape") and never reaches this
/// method. A view authored for the Neural Computer web app therefore fails loudly, not silently.
///
/// An unresolvable path is treated as falsy — matching the JS behaviour where a missing path
/// value is <c>undefined</c>, which is falsy in a boolean context.
///
/// A condition of an unknown subtype is hidden. The private constructor of
/// <see cref="VisibilityCondition"/> stops C# source outside the type from adding a subtype, and
/// <c>Parse</c> builds only the twelve nested records, so a validated tree never reaches this case.
/// <c>Not</c> negates the hidden result, so a <c>Not</c> of an unknown subtype shows the node.
/// </summary>
public static class VisibilityEvaluator
{
    /// <summary>
    /// Returns <c>true</c> if the element should be rendered.
    ///
    /// A <c>null</c> condition (the <c>visible</c> prop was absent) means always-visible,
    /// matching the JSON-UI spec default.
    /// </summary>
    public static bool IsVisible(VisibilityCondition? condition, JsonElement data)
    {
        if (condition is null) return true;

        return condition switch
        {
            VisibilityCondition.Always                   => true,
            VisibilityCondition.Never                    => false,
            VisibilityCondition.WhenPath(var path)       => IsTruthy(PathResolver.Resolve(data, path)),
            VisibilityCondition.And(var conds)           => conds.All(c => IsVisible(c, data)),
            VisibilityCondition.Or(var conds)            => conds.Any(c => IsVisible(c, data)),
            VisibilityCondition.Not(var inner)           => !IsVisible(inner, data),
            VisibilityCondition.Eq(var l, var r)         => object.Equals(ResolveOperand(l, data), ResolveOperand(r, data)),
            VisibilityCondition.Neq(var l, var r)        => !object.Equals(ResolveOperand(l, data), ResolveOperand(r, data)),
            VisibilityCondition.Gt(var l, var r)         => CompareNumbers(l, r, data) is > 0,
            VisibilityCondition.Gte(var l, var r)        => CompareNumbers(l, r, data) is >= 0,
            VisibilityCondition.Lt(var l, var r)         => CompareNumbers(l, r, data) is < 0,
            VisibilityCondition.Lte(var l, var r)        => CompareNumbers(l, r, data) is <= 0,
            _                                            => false  // unknown sub-type: hidden, never shown
        };
    }

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>
    /// Whether a resolved value is truthy.
    /// Matches JavaScript: false, null, undefined, 0, and empty string are falsy; everything else
    /// is truthy.
    /// </summary>
    private static bool IsTruthy(JsonElement? v)
    {
        if (v is null) return false;
        return v.Value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.False                           => false,
            JsonValueKind.Number                          => v.Value.TryGetDouble(out var d) && d != 0,
            JsonValueKind.String                          => (v.Value.GetString() ?? "") != "",
            _                                             => true   // true, array, object are truthy
        };
    }

    /// <summary>
    /// Resolve an operand to a canonical comparison value (boxed double for numbers,
    /// string for strings, boxed bool for booleans, null for missing/null). The caller compares
    /// two results with <c>object.Equals</c>: <c>==</c> on boxed values is reference equality.
    /// </summary>
    private static object? ResolveOperand(VisibilityOperand op, JsonElement data)
    {
        var el = op switch
        {
            VisibilityOperand.Literal(var v) => v,
            VisibilityOperand.PathRef(var p) => PathResolver.Resolve(data, p),
            _ => null
        };

        if (el is null) return null;
        return el.Value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True                            => true,
            JsonValueKind.False                           => false,
            JsonValueKind.Number when el.Value.TryGetDouble(out var d) => (object)d,
            JsonValueKind.String                          => (object)(el.Value.GetString() ?? ""),
            _                                             => null
        };
    }

    /// <summary>
    /// Numeric comparison. Returns <c>null</c> when either operand is non-numeric or unresolvable,
    /// so every ordering operator evaluates to <c>false</c> — matching JS where a non-numeric
    /// comparison produces <c>false</c>.
    /// </summary>
    private static int? CompareNumbers(VisibilityOperand left, VisibilityOperand right, JsonElement data)
    {
        if (ResolveOperand(left, data) is not double l) return null;
        if (ResolveOperand(right, data) is not double r) return null;
        return l.CompareTo(r);
    }
}
