using System.Text.Json;
using FluentAssertions;
using UiMcp.Abstractions;

namespace UiMcp.Tests;

/// <summary>
/// Ported from AdminLTE/JSON-UI/catalog.js, which is the proven implementation. Where this suite
/// and that file disagree, the JS is right and this is a transcription error.
///
/// EVERY REJECTION TEST IS PAIRED WITH A POSITIVE CONTROL (SPEC section 7). A validator that
/// refuses everything passes a naive rejection suite perfectly, so each "must throw" case has a
/// sibling "must pass" case that differs in exactly the offending detail. Without the pair, the
/// suite cannot distinguish "correctly refused" from "refuses everything".
/// </summary>
public class CatalogValidatorTests
{
    private static JsonElement N(string json) => JsonDocument.Parse(json).RootElement;

    private static ValidatedNode Validate(string json) => CatalogValidator.Validate(N(json));

    private static Action Validating(string json) => () => CatalogValidator.Validate(N(json));

    // A minimal valid node reused as the baseline for many controls.
    private const string ValidNote = """{"type":"Note","props":{"text":"hello"}}""";

    // ---- component identity -------------------------------------------------------------------

    [Fact]
    public void UnknownComponent_IsRejected()
        => Validating("""{"type":"Script","props":{}}""")
            .Should().Throw<UiValidationException>().WithMessage("*unknown component*");

    [Fact]
    public void KnownComponent_IsAccepted_PositiveControl()
        => Validate(ValidNote).Type.Should().Be("Note");

    [Fact]
    public void AllNineCatalogComponents_AreRegistered()
        => CatalogValidator.ComponentNames.Should().BeEquivalentTo(new[]
        {
            "StatusBanner", "Panel", "Row", "Metric", "Field", "Gauge", "Repeat", "Table", "Note"
        });

    // ---- props --------------------------------------------------------------------------------

    [Fact]
    public void UnknownProp_IsRejected_NotIgnored()
        => Validating("""{"type":"Note","props":{"text":"hi","onclick":"x"}}""")
            .Should().Throw<UiValidationException>().WithMessage("*unknown prop*");

    [Fact]
    public void MissingRequiredProp_IsRejected()
        => Validating("""{"type":"Note","props":{}}""")
            .Should().Throw<UiValidationException>().WithMessage("*missing required prop*");

    [Fact]
    public void OptionalPropMayBeOmitted_PositiveControl()
        => Validate(ValidNote).Props.Should().NotContainKey("tone");

    [Fact]
    public void OptionalPropIsKeptWhenSupplied_PositiveControl()
        => Validate("""{"type":"Note","props":{"text":"hi","tone":"nominal"}}""")
            .Props["tone"].Should().Be("nominal");

    // ---- prop types ---------------------------------------------------------------------------

    [Fact]
    public void TextProp_RejectsNonString()
        => Validating("""{"type":"Note","props":{"text":42}}""")
            .Should().Throw<UiValidationException>().WithMessage("*expected string*");

    [Fact]
    public void TextProp_RejectsOver500Chars()
        => Validating($$$"""{"type":"Note","props":{"text":"{{{new string('a', 501)}}}"}}""")
            .Should().Throw<UiValidationException>().WithMessage("*too long*");

    [Fact]
    public void TextProp_Accepts500Chars_BoundaryPositiveControl()
        => Validate($$$"""{"type":"Note","props":{"text":"{{{new string('a', 500)}}}"}}""")
            .Props["text"].Should().Be(new string('a', 500));

    [Fact]
    public void ToneProp_RejectsValueOutsideClosedSet()
        => Validating("""{"type":"Note","props":{"text":"hi","tone":"chartreuse"}}""")
            .Should().Throw<UiValidationException>().WithMessage("*tone*");

    [Theory]
    [InlineData("nominal")]
    [InlineData("attention")]
    [InlineData("critical")]
    [InlineData("degraded")]
    [InlineData("muted")]
    [InlineData("info")]
    public void ToneProp_AcceptsEveryClosedSetMember_PositiveControl(string tone)
        => Validate($$$"""{"type":"Note","props":{"text":"hi","tone":"{{{tone}}}"}}""")
            .Props["tone"].Should().Be(tone);

    // ---- paths: the prototype-pollution guard --------------------------------------------------

    [Theory]
    [InlineData("__proto__")]
    [InlineData("a.__proto__.b")]
    [InlineData("constructor")]
    [InlineData("x.prototype.y")]
    public void PathProp_RefusesPrototypeAccess(string path)
        => Validating($$$"""{"type":"Field","props":{"label":"L","valuePath":"{{{path}}}"}}""")
            .Should().Throw<UiValidationException>().WithMessage("*prototype access refused*");

    [Theory]
    [InlineData("a b")]
    [InlineData("a-b")]
    [InlineData("a/b")]
    [InlineData("a;b")]
    public void PathProp_RefusesIllegalCharacters(string path)
        => Validating($$$"""{"type":"Field","props":{"label":"L","valuePath":"{{{path}}}"}}""")
            .Should().Throw<UiValidationException>().WithMessage("*illegal characters*");

    [Theory]
    [InlineData("zbook")]
    [InlineData("zbook.sections.disk")]
    [InlineData("zbook.sections.disk[0].freeGb")]
    [InlineData("a_b.c1")]
    public void PathProp_AcceptsLegalPaths_PositiveControl(string path)
        => Validate($$$"""{"type":"Field","props":{"label":"L","valuePath":"{{{path}}}"}}""")
            .Props["valuePath"].Should().Be(path);

    // ---- children -----------------------------------------------------------------------------

    [Fact]
    public void ChildrenOnLeafComponent_AreRejected()
        => Validating($$$"""{"type":"Note","props":{"text":"hi"},"children":[{{{ValidNote}}}]}""")
            .Should().Throw<UiValidationException>().WithMessage("*does not accept children*");

    [Fact]
    public void ChildrenOnContainerComponent_AreAccepted_PositiveControl()
        => Validate($$$"""{"type":"Panel","props":{"title":"T"},"children":[{{{ValidNote}}}]}""")
            .Children.Should().HaveCount(1);

    [Fact]
    public void OverSixtyFourChildren_AreRejected()
    {
        var kids = string.Join(",", Enumerable.Repeat(ValidNote, 65));
        Validating($$$"""{"type":"Panel","props":{"title":"T"},"children":[{{{kids}}}]}""")
            .Should().Throw<UiValidationException>().WithMessage("*too many children*");
    }

    [Fact]
    public void ExactlySixtyFourChildren_AreAccepted_BoundaryPositiveControl()
    {
        var kids = string.Join(",", Enumerable.Repeat(ValidNote, 64));
        Validate($$$"""{"type":"Panel","props":{"title":"T"},"children":[{{{kids}}}]}""")
            .Children.Should().HaveCount(64);
    }

    // ---- depth --------------------------------------------------------------------------------

    private static string Nest(int depth)
    {
        var s = ValidNote;
        for (var i = 0; i < depth; i++)
            s = $$$"""{"type":"Panel","props":{"title":"T"},"children":[{{{s}}}]}""";
        return s;
    }

    [Fact]
    public void OverDepthTwelve_IsRejected()
        => Validating(Nest(13))
            .Should().Throw<UiValidationException>().WithMessage("*too deep*");

    [Fact]
    public void WithinDepthTwelve_IsAccepted_BoundaryPositiveControl()
        => Validate(Nest(11)).Type.Should().Be("Panel");

    // ---- Table columns ------------------------------------------------------------------------

    [Fact]
    public void TableColumns_RejectMoreThanEight()
    {
        var cols = string.Join(",", Enumerable.Range(0, 9)
            .Select(i => $$$"""{"header":"H{{{i}}}","valuePath":"a{{{i}}}"}"""));
        Validating($$$"""{"type":"Table","props":{"fromPath":"rows","columns":[{{{cols}}}]}}""")
            .Should().Throw<UiValidationException>().WithMessage("*max 8 columns*");
    }

    [Fact]
    public void TableColumns_AcceptExactlyEight_BoundaryPositiveControl()
    {
        var cols = string.Join(",", Enumerable.Range(0, 8)
            .Select(i => $$$"""{"header":"H{{{i}}}","valuePath":"a{{{i}}}"}"""));
        Validate($$$"""{"type":"Table","props":{"fromPath":"rows","columns":[{{{cols}}}]}}""")
            .Props.Should().ContainKey("columns");
    }

    [Fact]
    public void TableColumns_RejectPrototypePathInsideAColumn()
        => Validating("""{"type":"Table","props":{"fromPath":"rows","columns":[{"header":"H","valuePath":"__proto__"}]}}""")
            .Should().Throw<UiValidationException>().WithMessage("*prototype access refused*");

    // ---- Row.cols is a closed numeric set ------------------------------------------------------

    [Fact]
    public void RowCols_RejectsValueOutsideSet()
        => Validating("""{"type":"Row","props":{"cols":5}}""")
            .Should().Throw<UiValidationException>();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RowCols_AcceptsEverySetMember_PositiveControl(int cols)
        => Validate($$$"""{"type":"Row","props":{"cols":{{{cols}}}}}""")
            .Props["cols"].Should().Be(cols);

    // ---- the error message must name the offending node and prop -------------------------------

    [Fact]
    public void ErrorMessage_NamesComponentAndProp()
        => Validating("""{"type":"Metric","props":{"label":"L","valuePath":"a b"}}""")
            .Should().Throw<UiValidationException>()
            .WithMessage("*Metric*valuePath*");
}
