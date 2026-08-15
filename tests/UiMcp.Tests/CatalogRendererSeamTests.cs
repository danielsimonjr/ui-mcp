using System.Text.Json;
using FluentAssertions;
using UiMcp.Abstractions;
using UiMcp.Rendering;

namespace UiMcp.Tests;

/// <summary>
/// The SEAM between the validator and the renderer - where every defect this system has actually
/// suffered was found, and where neither component's own suite could see it.
///
/// Both defects found by running the real thing lived exactly here:
///
///   1. `$item` paths were REFUSED by `PropTypes.Path` while `PathResolver` implemented them.
///      `CatalogValidatorTests` was right that the validator did what it said. `PathResolverTests`
///      was right that the resolver did what it said. They disagreed about what a legal path IS,
///      and no test asked them the same question.
///
///   2. `ui_render` and `ui_status` hashed different things under one name.
///
/// The lesson generalises past those two instances: a suite organised per component tests each
/// side of every seam and neither seam itself. This file asks ONE question of every catalog
/// component - "does the thing the validator accepts actually render?" - which is the question no
/// per-component suite is shaped to ask.
///
/// It is deliberately EXHAUSTIVE over the catalog rather than sampled: the whole point is that the
/// gap was in the component nobody thought to check, so a sample would have been just as blind.
/// `EveryCatalogComponentIsCovered` fails if a component is ever added without a case here.
/// </summary>
public class CatalogRendererSeamTests : IClassFixture<StaFixture>
{
    private readonly StaFixture _sta;
    public CatalogRendererSeamTests(StaFixture sta) => _sta = sta;

    /// <summary>
    /// One realistic tree per component, each exercising EVERY prop the catalog allows - including
    /// the optional ones, because an optional prop the validator accepts and the renderer mishandles
    /// is the same defect wearing a smaller hat.
    /// </summary>
    public static TheoryData<string, string> EveryComponent() => new()
    {
        { "StatusBanner", """{"type":"StatusBanner","props":{"label":"ALL NOMINAL","tone":"nominal","detail":"14 agents"}}""" },
        { "Panel",        """{"type":"Panel","props":{"title":"Roster","tone":"info"},"children":[{"type":"Note","props":{"text":"n"}}]}""" },
        { "Row",          """{"type":"Row","props":{"cols":3},"children":[{"type":"Note","props":{"text":"a"}}]}""" },
        { "Metric",       """{"type":"Metric","props":{"label":"live","valuePath":"roster.live","unit":"agents","deltaPath":"roster.delta","tone":"nominal"}}""" },
        { "Field",        """{"type":"Field","props":{"label":"host","valuePath":"host","tone":"muted"}}""" },
        { "Gauge",        """{"type":"Gauge","props":{"label":"disk","valuePath":"disk.used","maxPath":"disk.total","tone":"attention"}}""" },
        { "Repeat",       """{"type":"Repeat","props":{"fromPath":"drives","emptyText":"no drives"},"children":[{"type":"Field","props":{"label":"free","valuePath":"$item.freeGb"}}]}""" },
        { "Table",        """{"type":"Table","props":{"fromPath":"drives","emptyText":"no drives","columns":[{"header":"name","valuePath":"$item.name"},{"header":"free","valuePath":"$item.freeGb"}]}}""" },
        { "Note",         """{"type":"Note","props":{"text":"a note","tone":"degraded"}}""" },
    };

    private const string RealisticData = """
        {
          "host": "ZBOOK",
          "roster": { "live": 14, "delta": -2 },
          "disk":   { "used": 380, "total": 1000 },
          "drives": [ { "name": "C", "freeGb": 120 }, { "name": "D", "freeGb": 44 } ]
        }
        """;

    [Theory]
    [MemberData(nameof(EveryComponent))]
    public void WhatTheValidatorAcceptsTheRendererCanDraw(string component, string json)
    {
        // Both halves in ONE test on purpose. Splitting them back into "the validator accepts it"
        // and "the renderer draws it" is precisely the shape that missed the $item defect.
        var validated = CatalogValidator.Validate(JsonDocument.Parse(json).RootElement);
        validated.Type.Should().Be(component);

        var drawn = _sta.On(() =>
            TreeRenderer.Render(validated, JsonDocument.Parse(RealisticData).RootElement) is not null);

        drawn.Should().BeTrue($"{component} validated, so it must also render");
    }

    [Theory]
    [MemberData(nameof(EveryComponent))]
    public void NoComponentRendersUnknownWhenItsPathsAllResolve(string component, string json)
    {
        // The stronger question, and the one that would have caught the $item bug from the other
        // direction: not merely "did it draw" but "did the data actually arrive". Every path in
        // every tree above resolves against RealisticData, so any UNKNOWN here is a binding that
        // silently failed.
        var validated = CatalogValidator.Validate(JsonDocument.Parse(json).RootElement);

        var texts = _sta.On(() =>
        {
            var el = TreeRenderer.Render(validated, JsonDocument.Parse(RealisticData).RootElement);
            return TextHarvest.From(el);
        });

        texts.Should().NotContain(PathResolver.Unknown,
            $"every path in the {component} tree resolves, so nothing should read UNKNOWN");
    }

    [Fact]
    public void EveryCatalogComponentIsCovered()
    {
        // The guard that keeps this file honest as the catalog grows. Without it, adding a tenth
        // component leaves a seam untested and every test here still passes - which is how a
        // "comprehensive" suite quietly stops being one.
        var covered = EveryComponent().Select(row => (string)row[0]!).ToHashSet();
        covered.Should().BeEquivalentTo(CatalogValidator.ComponentNames,
            "a component added to the catalog without a seam case is an untested seam");
    }

    [Fact]
    public void ItemScopeIsAcceptedByTheValidatorAndImplementedByTheResolver()
    {
        // The exact defect, pinned from both sides at once. The JS original still carries it:
        // AdminLTE/JSON-UI/view.json line 332 uses "$item", which its own catalog.js refuses.
        var accepted = () => PropTypes.Path(JsonDocument.Parse("\"$item.freeGb\"").RootElement);
        accepted.Should().NotThrow("the validator must accept what the resolver implements");

        var item = JsonDocument.Parse("""{"freeGb":44}""").RootElement;
        PathResolver.Resolve(default, "$item.freeGb", item).Should().NotBeNull();
        PathResolver.Display(PathResolver.Resolve(default, "$item.freeGb", item)).Should().Be("44");
    }

    [Fact]
    public void ABareItemIsTheItemItself_InBothTheValidatorAndTheResolver()
    {
        var accepted = () => PropTypes.Path(JsonDocument.Parse("\"$item\"").RootElement);
        accepted.Should().NotThrow();

        var item = JsonDocument.Parse("\"just a string\"").RootElement;
        PathResolver.Display(PathResolver.Resolve(default, "$item", item)).Should().Be("just a string");
    }

    [Theory]
    [InlineData("$other")]
    [InlineData("$")]
    [InlineData("a$b")]
    [InlineData("$item$")]
    public void OnlyItemIsBlessed_NotEveryUseOfTheDollarSign(string path)
    {
        // Why the fix was a literal prefix and not `$` added to the charset: widening the charset
        // would have been one character of diff and would have legalised all of these too.
        var attempt = () => PropTypes.Path(JsonDocument.Parse(JsonSerializer.Serialize(path)).RootElement);
        attempt.Should().Throw<UiValidationException>();
    }
}

/// <summary>Harvests TextBlock text. Runs ON the UI thread - see <see cref="StaFixture"/>.</summary>
internal static class TextHarvest
{
    public static List<string> From(System.Windows.DependencyObject root)
    {
        var found = new List<string>();
        Walk(root, found);
        return found;
    }

    private static void Walk(System.Windows.DependencyObject node, List<string> into)
    {
        if (node is System.Windows.Controls.TextBlock t) into.Add(t.Text);
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(node)
                     .OfType<System.Windows.DependencyObject>())
            Walk(child, into);
    }
}
