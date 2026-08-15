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
/// Ported from AdminLTE/JSON-UI/render.js. Where they disagree, the JS is right.
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