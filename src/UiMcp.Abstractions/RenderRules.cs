using System.Globalization;
using System.Text.Json;

namespace UiMcp.Abstractions;

/// <summary>
/// The renderer's decisions, with no WPF in sight.
///
/// Everything here is a judgement about what a value MEANS - whether a unit should be shown, whether
/// a delta exists, how long a bar should be - as opposed to how it is drawn. Keeping the two apart
/// is what lets the interesting half be tested on any runner, and leaves the WPF layer thin enough
/// to be checked by reading it.
///
/// Ported from AdminLTE/JSON-UI/render.js. Where they disagree, the JS is right.
/// </summary>
public static class RenderRules
{
    /// <summary>Matches the proven renderer's <c>arr.slice(0, 64)</c>.</summary>
    public const int MaxRepeatItems = 64;

    /// <summary>Matches the proven renderer's <c>arr.slice(0, 200)</c>.</summary>
    public const int MaxTableRows = 200;

    /// <summary>Missing, or present-but-null. Both are blind spots; a real zero is not.</summary>
    public static bool IsUnknown(JsonElement? v)
        => v is null || v.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    /// <summary>
    /// The metric's visible text. The unit is appended ONLY when there is a value to attach it to:
    /// "UNKNOWN live" reads as a measured quantity in some unit, when the truth is that there is no
    /// quantity at all.
    /// </summary>
    public static string MetricText(JsonElement? value, string? unit)
    {
        var text = PathResolver.Display(value);
        if (IsUnknown(value) || string.IsNullOrWhiteSpace(unit)) return text;
        return $"{text} {unit}";
    }

    /// <summary>
    /// Signed delta, or null when there is no NUMERIC delta to show.
    /// Null and zero are different claims - "unchanged" versus "not measured" - so a non-numeric or
    /// missing delta renders nothing at all rather than a misleading 0.
    /// </summary>
    public static string? DeltaText(JsonElement? delta)
    {
        if (!TryNumber(delta, out var d)) return null;
        var s = d.ToString("R", CultureInfo.InvariantCulture);
        return d >= 0 ? "+" + s : s;
    }

    /// <summary>
    /// Bar length, 0-100, clamped. Max defaults to 100 when no maxPath was supplied.
    ///
    /// A missing value gives 0 because a bar must have SOME length and zero is the only honest
    /// choice. The distinction between "empty bar" and "no reading" is carried by the LABEL, which
    /// shows UNKNOWN - a zero-length bar with no UNKNOWN beside it is the green-zero failure again.
    /// </summary>
    public static double GaugePercent(JsonElement? value, JsonElement? max)
    {
        if (!TryNumber(value, out var v)) return 0;

        var m = 100d;
        if (max is not null && !TryNumber(max, out m)) return 0;
        if (m <= 0) return 0;

        return Math.Clamp(v / m * 100d, 0d, 100d);
    }

    /// <summary>What an empty Repeat or Table says. Never nothing - a blank area reads as a bug.</summary>
    public static string EmptyText(string? supplied)
        => string.IsNullOrWhiteSpace(supplied) ? "none" : supplied;

    private static bool TryNumber(JsonElement? e, out double value)
    {
        value = 0;
        if (e is null || e.Value.ValueKind != JsonValueKind.Number) return false;
        if (!e.Value.TryGetDouble(out var d) || double.IsNaN(d) || double.IsInfinity(d)) return false;
        value = d;
        return true;
    }
}
