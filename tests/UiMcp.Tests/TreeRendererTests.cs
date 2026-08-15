using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FluentAssertions;
using UiMcp.Abstractions;
using UiMcp.Rendering;

namespace UiMcp.Tests;

/// <summary>
/// The renderer's ASSEMBLY, which until now had no tests at all.
///
/// The design intent was that every JUDGEMENT lives in <see cref="RenderRules"/> - tested
/// thoroughly, no WPF - leaving TreeRenderer "thin enough to check by reading". At 248 lines across
/// nine component builders it is not thin, and the two defects ever found by running this system
/// both lived in this gap: the `$item` validator/resolver seam, and a duplicated treeHash. Neither
/// was visible to any unit test.
///
/// What is pinned here is specifically what reading CANNOT settle:
///   - that the nine-way switch reaches the right builder,
///   - that the truncation caps are APPLIED, not merely declared as constants,
///   - that `$item` scope actually reaches a Repeat's children and a Table's cells,
///   - that a missing value is visually distinct, not just textually.
///
/// TWO THREADING RULES MAKE THIS FILE WORK, AND BOTH ARE EASY TO GET WRONG:
///
/// 1. Building WPF objects needs an STA thread, so every render goes through the same
///    <see cref="UiThreadHost"/> production uses - not a bespoke thread, for the same reason one
///    tests the shipped artifact rather than a rehearsal of it.
///
/// 2. WPF objects have THREAD AFFINITY. Reading `Panel.Children` from the test thread throws
///    "The calling thread cannot access this object because a different thread owns it" - which is
///    what the first version of this file did, failing all 34 tests for one reason that looked like
///    34. So every query runs INSIDE the STA call and returns plain values (strings, ints, doubles)
///    that cross the boundary safely. The `Probe` helpers below exist for exactly that.
///
/// Nothing here needs a desktop: the logical tree is fully inspectable without showing a window.
/// </summary>
public class TreeRendererTests : IClassFixture<StaFixture>
{
    private readonly StaFixture _sta;
    public TreeRendererTests(StaFixture sta) => _sta = sta;

    private static JsonElement Data(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>A node built THROUGH the validator - the only shape production ever renders.</summary>
    private static ValidatedNode Valid(string json) => CatalogValidator.Validate(Data(json));

    /// <summary>A Note carrying one tone. Built by replacement rather than raw-string
    /// interpolation, whose brace counting fights JSON at every closing brace.</summary>
    private static string NoteJson(string tone)
        => """{"type":"Note","props":{"text":"x","tone":"TONE"}}""".Replace("TONE", tone);

    /// <summary>A node built directly, for the branches a validated tree cannot reach.</summary>
    private static ValidatedNode Raw(string type, Dictionary<string, object> props)
        => new(type, props, Array.Empty<ValidatedNode>());

    // ---- probes: render AND inspect on the owning thread, return plain values ---------------------

    private T Probe<T>(ValidatedNode node, string data, Func<UIElement, T> read)
        => _sta.On(() => read(TreeRenderer.Render(node, Data(data))));

    private T Probe<T>(string nodeJson, string data, Func<UIElement, T> read)
        => Probe(Valid(nodeJson), data, read);

    private List<string> Texts(string nodeJson, string data = "{}")
        => Probe(nodeJson, data, el => AllTexts(el));

    private List<string> Texts(ValidatedNode node, string data = "{}")
        => Probe(node, data, el => AllTexts(el));

    private Type ElementType(string nodeJson, string data = "{}")
        => Probe(nodeJson, data, el => el.GetType());

    private string BrushKey(ValidatedNode node, string data = "{}")
        => Probe(node, data, el => Key(All<TextBlock>(el).First().Foreground));

    private string BrushKey(string nodeJson, string data = "{}") => BrushKey(Valid(nodeJson), data);

    private double GaugeValue(string nodeJson, string data)
        => Probe(nodeJson, data, el => All<ProgressBar>(el).Single().Value);

    // These run ON the STA thread, called from inside Probe.
    private static List<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new List<T>();
        if (root is T self) found.Add(self);
        foreach (var d in Descend(root).OfType<T>()) found.Add(d);
        return found;
    }

    private static IEnumerable<DependencyObject> Descend(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Descend(child)) yield return d;
        }
    }

    private static List<string> AllTexts(DependencyObject root)
        => All<TextBlock>(root).Select(t => t.Text).ToList();

    private static string Key(Brush b) => b is SolidColorBrush s ? s.Color.ToString() : b.ToString()!;

    // ---- the nine-way switch reaches the right builder --------------------------------------------

    [Theory]
    [InlineData("""{"type":"StatusBanner","props":{"label":"L","tone":"nominal"}}""", typeof(Border))]
    [InlineData("""{"type":"Panel","props":{"title":"T"}}""", typeof(Border))]
    [InlineData("""{"type":"Row","props":{"cols":2}}""", typeof(UniformGrid))]
    [InlineData("""{"type":"Metric","props":{"label":"L","valuePath":"a"}}""", typeof(StackPanel))]
    [InlineData("""{"type":"Field","props":{"label":"L","valuePath":"a"}}""", typeof(DockPanel))]
    [InlineData("""{"type":"Gauge","props":{"label":"L","valuePath":"a"}}""", typeof(StackPanel))]
    [InlineData("""{"type":"Repeat","props":{"fromPath":"a"}}""", typeof(StackPanel))]
    [InlineData("""{"type":"Note","props":{"text":"hi"}}""", typeof(TextBlock))]
    public void EachComponentRendersItsOwnElementType(string json, Type expected)
        => ElementType(json).Should().Be(expected);

    [Fact]
    public void AnEmptyTableRendersTheEmptyTextRatherThanAGrid()
        => ElementType("""{"type":"Table","props":{"fromPath":"rows","columns":[{"header":"H","valuePath":"$item.x"}]}}""")
            .Should().Be(typeof(TextBlock));

    [Fact]
    public void APopulatedTableRendersAGrid()
        => ElementType("""{"type":"Table","props":{"fromPath":"rows","columns":[{"header":"H","valuePath":"$item.x"}]}}""",
                       """{"rows":[{"x":1}]}""")
            .Should().Be(typeof(Grid));

    [Fact]
    public void AnUnknownComponentRendersALoudMarkerRatherThanThrowing()
    {
        // Unreachable through the validator, which is exactly why it must not throw: a future
        // catalog addition that forgets the switch should degrade one node, not take the display
        // down. Reachable here because Render is public and ValidatedNode is constructible.
        Texts(Raw("Hologram", new Dictionary<string, object>()))
            .Should().ContainSingle().Which.Should().Contain("no renderer for Hologram");
    }

    // ---- the truncation caps are APPLIED, not merely declared -------------------------------------

    [Fact]
    public void RepeatRendersAtMost64Items()
    {
        // RenderRules.MaxRepeatItems is already tested as a CONSTANT. Nothing asserted that the
        // renderer applies it, which is the half that can actually regress.
        var items = string.Join(",", Enumerable.Range(0, 70).Select(i => $$"""{"n":{{i}}}"""));
        var count = Probe(
            """{"type":"Repeat","props":{"fromPath":"items"},"children":[{"type":"Note","props":{"text":"x"}}]}""",
            $$"""{"items":[{{items}}]}""",
            el => ((StackPanel)el).Children.Count);

        RenderRules.MaxRepeatItems.Should().Be(64, "the JS original slices at 64");
        count.Should().Be(64, "70 items were supplied; 6 must be dropped");
    }

    [Fact]
    public void TableRendersAtMost200Rows()
    {
        var rows = string.Join(",", Enumerable.Range(0, 250).Select(i => $$"""{"x":{{i}}}"""));
        var (rowDefs, cells) = Probe(
            """{"type":"Table","props":{"fromPath":"rows","columns":[{"header":"H","valuePath":"$item.x"}]}}""",
            $$"""{"rows":[{{rows}}]}""",
            el => (((Grid)el).RowDefinitions.Count, All<TextBlock>(el).Count));

        RenderRules.MaxTableRows.Should().Be(200, "the JS original slices at 200");
        rowDefs.Should().Be(201, "one header row plus the capped data rows");
        cells.Should().Be(201, "1 header cell + 200 data cells");
    }

    // ---- $item scope actually reaches where it must ------------------------------------------------

    [Fact]
    public void RepeatPassesEachItemAsScopeToItsChildren()
    {
        // THE seam that already produced one real defect. A Repeat whose children cannot see
        // $item renders the same UNKNOWN for every row, which reads as "no data" rather than as
        // "the scope never arrived".
        var texts = Texts("""
            {"type":"Repeat","props":{"fromPath":"drives"},
             "children":[{"type":"Field","props":{"label":"free","valuePath":"$item.freeGb"}}]}
            """,
            """{"drives":[{"freeGb":11},{"freeGb":22},{"freeGb":33}]}""");

        texts.Should().Contain(new[] { "11", "22", "33" });
        texts.Should().NotContain("UNKNOWN");
    }

    [Fact]
    public void RepeatChildrenCanStillReachTheRootDataAlongsideItemScope()
    {
        // A plain path inside a Repeat resolves against `data`, not the item - matching the JS,
        // which passes (data, item) rather than (item, item).
        Texts("""
            {"type":"Repeat","props":{"fromPath":"drives"},
             "children":[{"type":"Field","props":{"label":"host","valuePath":"host"}}]}
            """,
            """{"host":"ZBOOK","drives":[{"freeGb":11}]}""")
            .Should().Contain("ZBOOK");
    }

    [Fact]
    public void TableResolvesEachColumnAgainstItsOwnRow()
        => Texts("""
            {"type":"Table","props":{"fromPath":"rows",
             "columns":[{"header":"name","valuePath":"$item.name"},
                        {"header":"gb","valuePath":"$item.gb"}]}}
            """,
            """{"rows":[{"name":"C","gb":1},{"name":"D","gb":2}]}""")
            .Should().Contain(new[] { "name", "gb", "C", "1", "D", "2" });

    [Fact]
    public void ATableCellWhosePathMissesThatRowIsUnknownForThatRowOnly()
    {
        var texts = Texts("""
            {"type":"Table","props":{"fromPath":"rows",
             "columns":[{"header":"gb","valuePath":"$item.gb"}]}}
            """,
            """{"rows":[{"gb":1},{"other":2}]}""");

        texts.Should().Contain("1");
        texts.Should().Contain("UNKNOWN", "the second row has no gb, and a blind spot is not a zero");
        texts.Should().NotContain("0");
    }

    // ---- Row distributes across the requested column count -----------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RowUsesTheRequestedColumnCount(int cols)
    {
        var json = """
            {"type":"Row","props":{"cols":COLS},
             "children":[{"type":"Note","props":{"text":"a"}},{"type":"Note","props":{"text":"b"}}]}
            """.Replace("COLS", cols.ToString());

        var (columns, children) = Probe(json, "{}", el => (((UniformGrid)el).Columns, ((UniformGrid)el).Children.Count));

        columns.Should().Be(cols);
        children.Should().Be(2, "every child gets a cell regardless of the column count");
    }

    // ---- empty collections say something ------------------------------------------------------------

    [Fact]
    public void AnEmptyRepeatSaysNoneByDefault()
        => Texts("""{"type":"Repeat","props":{"fromPath":"nope"}}""")
            .Should().ContainSingle().Which.Should().Be("none");

    [Fact]
    public void AnEmptyRepeatUsesTheSuppliedEmptyText()
        => Texts("""{"type":"Repeat","props":{"fromPath":"nope","emptyText":"no drives"}}""")
            .Should().ContainSingle().Which.Should().Be("no drives");

    [Fact]
    public void ARepeatOverANonArraySaysEmptyRatherThanRenderingRows()
        => Texts("""{"type":"Repeat","props":{"fromPath":"a"}}""", """{"a":"not an array"}""")
            .Should().ContainSingle().Which.Should().Be("none");

    [Fact]
    public void AnEmptyTableUsesTheSuppliedEmptyText()
        => Texts("""
            {"type":"Table","props":{"fromPath":"nope","emptyText":"no rows",
             "columns":[{"header":"H","valuePath":"$item.x"}]}}
            """)
            .Should().ContainSingle().Which.Should().Be("no rows");

    // ---- a missing value is VISUALLY distinct, not merely textually ---------------------------------

    [Fact]
    public void AMetricWithAnUnresolvableValueIsPaintedDifferentlyFromOneThatResolved()
    {
        // "UNKNOWN must LOOK wrong" is the renderer's own stated rule. Text alone is not enough:
        // on a wall display the colour is what carries at a glance.
        const string node = """{"type":"Metric","props":{"label":"live","valuePath":"a","tone":"nominal"}}""";

        var resolved = Probe(node, """{"a":3}""", el => BrushOfTextContaining(el, "3"));
        var missing = Probe(node, "{}", el => BrushOfTextContaining(el, "UNKNOWN"));

        resolved.Should().NotBe(missing);
    }

    [Fact]
    public void AMetricSuppressesItsUnitWhenTheValueIsMissing()
        => Texts("""{"type":"Metric","props":{"label":"l","valuePath":"a","unit":"GB"}}""", "{}")
            .Should().Contain("UNKNOWN").And.NotContain("UNKNOWN GB");

    [Fact]
    public void AMetricShowsNoDeltaWhenTheDeltaIsNotNumeric()
        => Texts("""{"type":"Metric","props":{"label":"l","valuePath":"a","deltaPath":"d"}}""",
                 """{"a":5,"d":"nope"}""")
            .Should().NotContain(t => t.StartsWith('+') || t.StartsWith('-'));

    [Fact]
    public void AMetricShowsASignedDeltaWhenItIsNumeric()
        => Texts("""{"type":"Metric","props":{"label":"l","valuePath":"a","deltaPath":"d"}}""",
                 """{"a":5,"d":2}""")
            .Should().Contain("+2");

    // ---- the gauge must not invent a scale ----------------------------------------------------------

    [Fact]
    public void AGaugeWhoseRequestedMaxDidNotResolveShowsAnEmptyBar()
        // The defect this test was written to catch, end to end through the renderer rather than
        // only through RenderRules: an unresolvable maxPath silently defaulted to 100, so 50
        // against an unreadable maximum drew a HALF FULL bar - a confident measurement against a
        // scale nobody supplied.
        => GaugeValue("""{"type":"Gauge","props":{"label":"disk","valuePath":"used","maxPath":"total"}}""",
                      """{"used":50}""")
            .Should().Be(0, "an unreadable maximum is not a maximum of 100");

    [Fact]
    public void AGaugeUsesItsResolvedMax()
        => GaugeValue("""{"type":"Gauge","props":{"label":"disk","valuePath":"used","maxPath":"total"}}""",
                      """{"used":50,"total":200}""")
            .Should().Be(25);

    [Fact]
    public void AGaugeWithNoMaxPathStillDefaultsTo100()
        => GaugeValue("""{"type":"Gauge","props":{"label":"disk","valuePath":"used"}}""",
                      """{"used":42}""")
            .Should().Be(42);

    [Fact]
    public void AGaugeLabelCarriesTheValueSoAnEmptyBarIsNotMistakenForAMeasuredZero()
        => Texts("""{"type":"Gauge","props":{"label":"disk","valuePath":"used"}}""", "{}")
            .Should().ContainSingle().Which.Should().Contain("UNKNOWN");

    // ---- tone is renderer-owned, and the two fallthrough cases are different ------------------------

    [Theory]
    [InlineData("nominal")]
    [InlineData("attention")]
    [InlineData("critical")]
    [InlineData("degraded")]
    [InlineData("info")]
    [InlineData("muted")]
    public void EveryToneInTheClosedSetPaintsSomething(string tone)
        => BrushKey(NoteJson(tone)).Should().NotBeNullOrEmpty();

    [Fact]
    public void TheSixTonesAreSixDistinctColours()
        => new[] { "nominal", "attention", "critical", "degraded", "info", "muted" }
            .Select(t => BrushKey(NoteJson(t)))
            .Should().OnlyHaveUniqueItems("a tone that cannot be told apart from another conveys nothing");

    [Fact]
    public void AToneOutsideTheClosedSetIsMutedRatherThanAlarming()
    {
        // Previously this returned the ATTENTION colour, contradicting both the method's own
        // summary and the JS it is ported from (`TONE_CLASS[t] || 'stx-muted'`). Rendering a tone
        // the renderer does not understand as alarm MANUFACTURES urgency out of a parse failure.
        // Unreachable from a validated tree - the catalog's tone is a closed enum - but Render is
        // public, and an unreachable branch that behaves wrongly is a trap for whoever makes it
        // reachable.
        var unknown = BrushKey(Raw("Note", new Dictionary<string, object> { ["text"] = "x", ["tone"] = "chartreuse" }));

        unknown.Should().Be(BrushKey(NoteJson("muted")));
        unknown.Should().NotBe(BrushKey(NoteJson("attention")));
    }

    [Fact]
    public void NoToneSuppliedKeepsTheDefaultAccent()
        // The other fallthrough, and deliberately NOT muted: the JS emits no tone class at all
        // here, leaving the element's own styling. Pinned so the muted fix above cannot later be
        // over-applied to the absent case and repaint every untoned panel grey.
        => BrushKey("""{"type":"Note","props":{"text":"x"}}""")
            .Should().NotBe(BrushKey(NoteJson("muted")));

    // ---- the security posture, asserted rather than assumed ------------------------------------------

    [Fact]
    public void TextReachesTheUiThroughTextBlockTextWhichParsesNoMarkup()
    {
        // The structural equivalent of the JS renderer's "textContent, never innerHTML" rule. If
        // this ever became markup-parsing, the string below would stop surviving verbatim.
        const string markup = "<Button Content='pwn'/> & <b>bold</b>";
        var json = JsonSerializer.Serialize(new { type = "Note", props = new { text = markup } });

        var (text, inlineCount, runText, buttons) = Probe(json, "{}", el =>
        {
            var block = (TextBlock)el;
            var run = block.Inlines.FirstInline as System.Windows.Documents.Run;
            return (block.Text, block.Inlines.Count, run?.Text, All<Button>(el).Count);
        });

        text.Should().Be(markup, "the string must survive verbatim, uninterpreted");

        // Setting TextBlock.Text produces exactly ONE Run holding the literal string - that IS the
        // inert representation. Markup parsing is what produces SEVERAL inlines (a Run, a Bold,
        // another Run...), so "one Run carrying the string verbatim" is the discriminating
        // assertion, and a stronger one than "no inlines" would have been.
        inlineCount.Should().Be(1, "several inlines would mean the markup was parsed into elements");
        runText.Should().Be(markup);
        buttons.Should().Be(0, "no element may be constructed from a string in the tree");
    }

    [Fact]
    public void NestingRendersChildrenOfChildren()
        => Texts("""
            {"type":"Panel","props":{"title":"outer"},
             "children":[{"type":"Panel","props":{"title":"inner"},
                          "children":[{"type":"Note","props":{"text":"deep"}}]}]}
            """)
            .Should().Contain(new[] { "outer", "inner", "deep" });

    /// <summary>Runs ON the STA thread: the colour of the first TextBlock containing the text.</summary>
    private static string BrushOfTextContaining(DependencyObject root, string text)
        => All<TextBlock>(root)
            .Where(t => t.Text.Contains(text, StringComparison.Ordinal))
            .Select(t => Key(t.Foreground))
            .First();
}
