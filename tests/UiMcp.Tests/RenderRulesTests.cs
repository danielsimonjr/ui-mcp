using System.Text.Json;
using FluentAssertions;
using UiMcp.Abstractions;

namespace UiMcp.Tests;

/// <summary>
/// The renderer's DECISIONS, separated from its drawing. Every bug worth catching here is a
/// judgement - does a unit get appended to a value that does not exist, does a gauge clamp, does an
/// empty list say something - and none of it needs a window. The WPF layer that consumes these is
/// then thin enough to be obviously correct by reading it.
///
/// Ported from danielsimonjr/JSON-UI/render.js. Where they disagree, the JS is right.
/// </summary>
public class RenderRulesTests
{
    private static JsonElement? V(string json) => JsonDocument.Parse(json).RootElement;

    // ---- Metric text: the unit must not survive a missing value ---------------------------------

    [Fact]
    public void MetricText_AppendsUnit_WhenTheValueResolved()
        => RenderRules.MetricText(V("3"), "live").Should().Be("3 live");

    [Fact]
    public void MetricText_OmitsUnit_WhenTheValueIsMissing()
        => RenderRules.MetricText(null, "live").Should().Be("UNKNOWN",
            "\"UNKNOWN live\" implies a measured quantity in some unit; there is no quantity");

    [Fact]
    public void MetricText_NoUnitSupplied_IsJustTheValue()
        => RenderRules.MetricText(V("3"), null).Should().Be("3");

    [Fact]
    public void MetricText_RealZero_IsStillZero_NotUnknown()
        => RenderRules.MetricText(V("0"), "GB").Should().Be("0 GB");

    // ---- Delta: only a number earns a sign ------------------------------------------------------

    [Fact]
    public void DeltaText_PositiveGetsAPlus()
        => RenderRules.DeltaText(V("5")).Should().Be("+5");

    [Fact]
    public void DeltaText_NegativeKeepsItsMinus()
        => RenderRules.DeltaText(V("-3")).Should().Be("-3");

    [Fact]
    public void DeltaText_ZeroIsSignedPositive_MatchingTheJs()
        => RenderRules.DeltaText(V("0")).Should().Be("+0");

    [Fact]
    public void DeltaText_MissingIsNoDeltaAtAll_NotZero()
        => RenderRules.DeltaText(null).Should().BeNull("no delta and a delta of zero are different claims");

    [Fact]
    public void DeltaText_NonNumericIsNoDelta()
        => RenderRules.DeltaText(V("\"lots\"")).Should().BeNull();

    // ---- Gauge percent: clamped, and never invented ---------------------------------------------

    [Fact]
    public void GaugePercent_HalfOfMax()
        => RenderRules.GaugePercent(V("50"), V("100")).Should().Be(50);

    [Fact]
    public void GaugePercent_DefaultsMaxTo100_WhenNoMaxPathGiven()
        => RenderRules.GaugePercent(V("42"), null).Should().Be(42);

    [Fact]
    public void GaugePercent_ClampsAbove100()
        => RenderRules.GaugePercent(V("150"), V("100")).Should().Be(100);

    [Fact]
    public void GaugePercent_ClampsBelowZero()
        => RenderRules.GaugePercent(V("-20"), V("100")).Should().Be(0);

    [Fact]
    public void GaugePercent_ZeroMaxIsZero_NotInfinity()
        => RenderRules.GaugePercent(V("5"), V("0")).Should().Be(0);

    [Fact]
    public void GaugePercent_MissingValueIsZeroBar_ButTheLabelMustSayUNKNOWN()
    {
        // The BAR has to be some length and 0 is the only honest one. The TEXT is what carries the
        // distinction, which is why Display is asserted alongside it here rather than separately -
        // a zero-length bar with no "UNKNOWN" beside it is the green-zero failure all over again.
        RenderRules.GaugePercent(null, V("100")).Should().Be(0);
        PathResolver.Display(null).Should().Be("UNKNOWN");
    }

    [Fact]
    public void GaugePercent_NonNumericIsZero()
        => RenderRules.GaugePercent(V("\"full\""), V("100")).Should().Be(0);

    // ---- empty collections say something ---------------------------------------------------------

    [Fact]
    public void EmptyText_DefaultsToNone()
        => RenderRules.EmptyText(null).Should().Be("none");

    [Fact]
    public void EmptyText_UsesTheSuppliedTextWhenGiven()
        => RenderRules.EmptyText("no drives attached").Should().Be("no drives attached");

    // ---- caps ------------------------------------------------------------------------------------

    [Fact]
    public void Caps_MatchTheProvenRenderer()
    {
        RenderRules.MaxRepeatItems.Should().Be(64);
        RenderRules.MaxTableRows.Should().Be(200);
    }

    [Fact]
    public void IsUnknown_TrueForMissing_AndForJsonNull()
    {
        RenderRules.IsUnknown(null).Should().BeTrue();
        RenderRules.IsUnknown(V("null")).Should().BeTrue();
    }

    [Fact]
    public void IsUnknown_FalseForARealZero_PositiveControl()
        => RenderRules.IsUnknown(V("0")).Should().BeFalse("zero is a measurement, not a blind spot");

    // ---- Table column paths are ROW-relative by definition ---------------------------------------
    //
    // Found 2026-08-16 on the HTML console, which shares this renderer's semantics. Every row of
    // every table read UNKNOWN while the row COUNTS were correct - fromPath resolved, the column
    // paths did not. Cause: a column path was resolved against the DATA ROOT unless it began with
    // "$item", so the natural path "name" looked for a top-level "name" and found nothing.
    //
    // A column in a table over an array IS row-relative; that is what a column means. The evidence
    // that the default was wrong rather than the view: of the ten column paths written for that
    // console, NINE were bare and one carried the prefix. When the author reaches for the
    // "incorrect" form nine times out of ten, the surprising form is the defect.
    //
    // Scoped to Table columns ONLY. Inside a Repeat, a bare path resolving against the root is
    // meaningful - a global value shown beside each row - so that behaviour is unchanged.

    [Fact]
    public void ColumnPath_BarePathBecomesItemRelative()
        => RenderRules.ColumnPath("name").Should().Be("$item.name");

    [Fact]
    public void ColumnPath_NestedBarePathBecomesItemRelative()
        => RenderRules.ColumnPath("disk.freeGb").Should().Be("$item.disk.freeGb");

    [Fact]
    public void ColumnPath_ExplicitItemPrefixIsLeftAlone_PositiveControl()
        => RenderRules.ColumnPath("$item.name").Should().Be("$item.name",
            "an explicit prefix already means row-relative; double-prefixing would break it");

    [Fact]
    public void ColumnPath_BareItemIsLeftAlone()
        => RenderRules.ColumnPath("$item").Should().Be("$item");

    /// <summary>
    /// The end-to-end claim, not just the string rewrite: a BARE column path must actually resolve
    /// against the row. Asserting only the rewrite would pass even if the resolver ignored it.
    /// </summary>
    [Fact]
    public void ColumnPath_BarePath_ActuallyResolvesAgainstTheRow()
    {
        var data = JsonDocument.Parse("""{"name":"ROOT-VALUE","rows":[{"name":"ROW-VALUE"}]}""").RootElement;
        var row = data.GetProperty("rows")[0];

        var resolved = PathResolver.Resolve(data, RenderRules.ColumnPath("name"), row);

        PathResolver.Display(resolved).Should().Be("ROW-VALUE",
            "the row wins; resolving against the root is what produced UNKNOWN everywhere");
    }

    [Fact]
    public void WithoutTheFix_ABareColumnPathWouldHitTheRoot_RegressionWitness()
    {
        // Pins the OLD behaviour so the bug is documented as a behaviour, not just as a comment.
        // If someone "simplifies" ColumnPath away, this shows exactly what returns.
        var data = JsonDocument.Parse("""{"name":"ROOT-VALUE","rows":[{"name":"ROW-VALUE"}]}""").RootElement;
        var row = data.GetProperty("rows")[0];

        PathResolver.Display(PathResolver.Resolve(data, "name", row)).Should().Be("ROOT-VALUE");
    }

    // ---- Gauge: a REQUESTED max that did not resolve is not a max of 100 -------------------------
    //
    // The JS original is explicit about this (render.js):
    //     const max = p.maxPath ? resolve(data, p.maxPath, scope) : 100;
    //     if (typeof v === 'number' && typeof max === 'number' && max > 0) { ... }
    // An unresolvable maxPath yields undefined, fails the typeof test, and the bar stays at 0.
    //
    // "No maxPath supplied" and "a maxPath was supplied and could not be resolved" are different
    // claims that both arrive here as a null max. Collapsing them defaults the second to 100 and
    // draws a CONFIDENT bar against a scale nobody supplied - a value of 50 against an unreadable
    // maximum shows as half full. That is the green-zero failure wearing a progress bar, and it is
    // the exact class of defect this codebase exists to prevent.

    [Fact]
    public void GaugePercent_IsZero_WhenAMaxWasRequestedButDidNotResolve()
        => RenderRules.GaugePercent(V("50"), null, maxWasRequested: true).Should().Be(0,
            "an unreadable maximum is not a maximum of 100");

    [Fact]
    public void GaugePercent_IsZero_WhenARequestedMaxResolvedToSomethingNonNumeric()
        => RenderRules.GaugePercent(V("50"), V("\"lots\""), maxWasRequested: true).Should().Be(0);

    [Fact]
    public void GaugePercent_UsesTheMax_WhenARequestedMaxDidResolve()
        => RenderRules.GaugePercent(V("50"), V("200"), maxWasRequested: true).Should().Be(25);

    [Fact]
    public void GaugePercent_StillDefaultsTo100_WhenNoMaxWasRequested()
        // The regression guard for the fix: the existing two-argument behaviour is unchanged.
        => RenderRules.GaugePercent(V("42"), null).Should().Be(42);
}