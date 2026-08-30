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
/// has no session/auth model. An auth condition always evaluates to <c>false</c> (hidden) so that
/// a view authored for the Neural Computer web app degrades safely rather than inadvertently
/// showing content the condition was meant to guard.
///
/// An unresolvable path is treated as falsy — matching the JS behaviour where a missing path
/// value is <c>undefined</c>, which is falsy in a boolean context.
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
            VisibilityCondition.Eq(var l, var r)         => ResolveOperand(l, data) == ResolveOperand(r, data),
            VisibilityCondition.Neq(var l, var r)        => ResolveOperand(l, data) != ResolveOperand(r, data),
            VisibilityCondition.Gt(var l, var r)         => CompareNumbers(l, r, data) > 0,
            VisibilityCondition.Gte(var l, var r)        => CompareNumbers(l, r, data) >= 0,
            VisibilityCondition.Lt(var l, var r)         => CompareNumbers(l, r, data) < 0,
            VisibilityCondition.Lte(var l, var r)        => CompareNumbers(l, r, data) <= 0,
            _                                            => true   // unknown sub-type: show by default
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
    /// string for strings/booleans, null for missing/null).
    /// </summary>
    private static object? ResolveOperand(VisibilityOperand op, JsonElement data)
    {
        var el = op switch
        {
            VisibilityOperand.Literal(var v) => v,
            VisibilityOperand.PathRef(var p) => PathResolver.Resolve(data, p) ?? (JsonElement?)default,
            _ => default
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
    /// Numeric comparison. Returns 0 when either operand is non-numeric or unresolvable,
    /// which means the condition evaluates to <c>false</c> — matching JS where a non-numeric
    /// comparison produces <c>false</c>.
    /// </summary>
    private static int CompareNumbers(VisibilityOperand left, VisibilityOperand right, JsonElement data)
    {
        if (ResolveOperand(left, data) is not double l) return 0;
        if (ResolveOperand(right, data) is not double r) return 0;
        return l.CompareTo(r);
    }
}
