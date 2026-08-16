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
    /// <param name="maxWasRequested">
    /// Whether the tree supplied a <c>maxPath</c> at all.
    ///
    /// THIS PARAMETER EXISTS BECAUSE TWO DIFFERENT CLAIMS ARRIVE HERE AS THE SAME NULL. "No maximum
    /// was asked for" (default 100, correct) and "a maximum WAS asked for and could not be
    /// resolved" (there is no scale, and nothing honest to draw) are not the same thing, and the
    /// caller is the only one that can tell them apart.
    ///
    /// Without it, an unresolvable maxPath silently defaulted to 100, so a value of 50 against an
    /// unreadable maximum drew a half-full bar - a confident measurement against a scale nobody
    /// supplied. That is the green-zero failure wearing a progress bar. The JS original gets this
    /// right and is explicit about it (render.js): an unresolvable maxPath yields undefined, fails
    /// the `typeof max === 'number'` test, and the bar stays at 0.
    ///
    /// Defaults to false so every existing two-argument call keeps its exact previous behaviour.
    /// </param>
    public static double GaugePercent(JsonElement? value, JsonElement? max, bool maxWasRequested = false)
    {
        if (!TryNumber(value, out var v)) return 0;

        var m = 100d;
        if (maxWasRequested || max is not null)
        {
            if (!TryNumber(max, out m)) return 0;
        }
        if (m <= 0) return 0;

        return Math.Clamp(v / m * 100d, 0d, 100d);
    }

    /// <summary>What an empty Repeat or Table says. Never nothing - a blank area reads as a bug.</summary>
    public static string EmptyText(string? supplied)
        => string.IsNullOrWhiteSpace(supplied) ? "none" : supplied;

    /// <summary>
    /// Normalises a Table COLUMN path to be row-relative, which is what a column means.
    ///
    /// Without this, a column path was resolved against the data ROOT unless it began with "$item",
    /// so the natural path "name" looked for a top-level "name", found nothing, and rendered
    /// UNKNOWN. Observed 2026-08-16 on the HTML console that shares these semantics: every row of
    /// every table read UNKNOWN while the row COUNTS were correct, because fromPath resolved and the
    /// column paths did not. That combination is the tell.
    ///
    /// The default was the defect, not the views. Of the ten column paths written for that console,
    /// NINE were bare and one carried the prefix - when the author reaches for the "wrong" form nine
    /// times in ten, the surprising form is what needs changing. An explicit "$item." prefix still
    /// works and means the same thing, so no existing view breaks.
    ///
    /// TABLE COLUMNS ONLY. Inside a Repeat, a bare path resolving against the root is meaningful - a
    /// global value displayed beside each row - and is left alone.
    /// </summary>
    public static string ColumnPath(string valuePath)
        => valuePath.StartsWith("$item", StringComparison.Ordinal) ? valuePath : "$item." + valuePath;

    private static bool TryNumber(JsonElement? e, out double value)
    {
        value = 0;
        if (e is null || e.Value.ValueKind != JsonValueKind.Number) return false;
        if (!e.Value.TryGetDouble(out var d) || double.IsNaN(d) || double.IsInfinity(d)) return false;
        value = d;
        return true;
    }
}
