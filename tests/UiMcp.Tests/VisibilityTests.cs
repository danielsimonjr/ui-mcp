using System.Text.Json;
using FluentAssertions;
using UiMcp.Abstractions;

namespace UiMcp.Tests;

/// <summary>
/// Tests for the visibility system ported from <c>danielsimonjr/JSON-UI</c>
/// (<c>packages/core/src/visibility.ts</c>).
///
/// Covers three layers:
/// <list type="number">
///   <item><c>VisibilityCondition.Parse</c> — parsing from JSON into the condition type.</item>
///   <item><c>VisibilityEvaluator.IsVisible</c> — evaluation against data.</item>
///   <item><c>CatalogValidator</c> — the <c>visible</c> prop is accepted at the node level and
///     carried through to <c>ValidatedNode.Visible</c>.</item>
/// </list>
///
/// Every rejection test is paired with a positive control.
/// </summary>
public class VisibilityTests
{
    private static JsonElement D(string json) => JsonDocument.Parse(json).RootElement;
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;
    private static ValidatedNode Validate(string json) =>
        CatalogValidator.Validate(D(json));

    // ---- VisibilityCondition.Parse ----------------------------------------------------

    [Fact]
    public void Parse_True_ReturnsAlways()
        => VisibilityCondition.Parse(J("true"))
            .Should().BeOfType<VisibilityCondition.Always>();

    [Fact]
    public void Parse_False_ReturnsNever()
        => VisibilityCondition.Parse(J("false"))
            .Should().BeOfType<VisibilityCondition.Never>();

    [Fact]
    public void Parse_PathObject_ReturnsWhenPath()
    {
        var cond = VisibilityCondition.Parse(J("""{"path":"sections.live"}"""));
        cond.Should().BeOfType<VisibilityCondition.WhenPath>()
            .Which.Path.Should().Be("sections.live");
    }

    [Fact]
    public void Parse_AndObject_ReturnsAnd()
    {
        var cond = VisibilityCondition.Parse(
            J("""{"and":[{"path":"a"},{"path":"b"}]}"""));
        var and = cond.Should().BeOfType<VisibilityCondition.And>().Subject;
        and.Conditions.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_OrObject_ReturnsOr()
    {
        var cond = VisibilityCondition.Parse(
            J("""{"or":[true,false]}"""));
        cond.Should().BeOfType<VisibilityCondition.Or>()
            .Which.Conditions.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_NotObject_ReturnsNot()
    {
        var cond = VisibilityCondition.Parse(J("""{"not":true}"""));
        cond.Should().BeOfType<VisibilityCondition.Not>()
            .Which.Condition.Should().BeOfType<VisibilityCondition.Always>();
    }

    [Fact]
    public void Parse_EqObject_ReturnsEq()
    {
        var cond = VisibilityCondition.Parse(
            J("""{"eq":[{"path":"x"},1]}"""));
        cond.Should().BeOfType<VisibilityCondition.Eq>();
    }

    [Fact]
    public void Parse_NeqObject_ReturnsNeq()
        => VisibilityCondition.Parse(J("""{"neq":[0,1]}"""))
            .Should().BeOfType<VisibilityCondition.Neq>();

    [Fact]
    public void Parse_GtObject_ReturnsGt()
        => VisibilityCondition.Parse(J("""{"gt":[2,1]}"""))
            .Should().BeOfType<VisibilityCondition.Gt>();

    [Fact]
    public void Parse_GteObject_ReturnsGte()
        => VisibilityCondition.Parse(J("""{"gte":[1,1]}"""))
            .Should().BeOfType<VisibilityCondition.Gte>();

    [Fact]
    public void Parse_LtObject_ReturnsLt()
        => VisibilityCondition.Parse(J("""{"lt":[1,2]}"""))
            .Should().BeOfType<VisibilityCondition.Lt>();

    [Fact]
    public void Parse_LteObject_ReturnsLte()
        => VisibilityCondition.Parse(J("""{"lte":[1,1]}"""))
            .Should().BeOfType<VisibilityCondition.Lte>();

    [Fact]
    public void Parse_Number_Throws()
        => ((Action)(() => VisibilityCondition.Parse(J("42"))))
            .Should().Throw<UiValidationException>();

    [Fact]
    public void Parse_UnrecognisedObject_Throws()
        => ((Action)(() => VisibilityCondition.Parse(J("""{"unknown":true}"""))))
            .Should().Throw<UiValidationException>().WithMessage("*unrecognised*");

    [Fact]
    public void Parse_PathMustBeString()
        => ((Action)(() => VisibilityCondition.Parse(J("""{"path":42}"""))))
            .Should().Throw<UiValidationException>();

    [Fact]
    public void Parse_AndMustBeArray()
        => ((Action)(() => VisibilityCondition.Parse(J("""{"and":true}"""))))
            .Should().Throw<UiValidationException>();

    [Fact]
    public void Parse_EqMustBeTwoElements()
        => ((Action)(() => VisibilityCondition.Parse(J("""{"eq":[1]}"""))))
            .Should().Throw<UiValidationException>();

    // ---- VisibilityEvaluator.IsVisible ------------------------------------------------

    private static bool V(VisibilityCondition? cond, string dataJson)
        => VisibilityEvaluator.IsVisible(cond, D(dataJson));

    [Fact]
    public void IsVisible_NullCondition_IsAlwaysTrue()
        => V(null, "{}").Should().BeTrue();

    [Fact]
    public void IsVisible_Always_IsTrue()
        => V(new VisibilityCondition.Always(), "{}").Should().BeTrue();

    [Fact]
    public void IsVisible_Never_IsFalse()
        => V(new VisibilityCondition.Never(), "{}").Should().BeFalse();

    [Fact]
    public void IsVisible_WhenPath_TruthyValue_IsTrue()
        => V(new VisibilityCondition.WhenPath("live"), """{"live":1}""")
            .Should().BeTrue();

    [Fact]
    public void IsVisible_WhenPath_FalsyValue_IsFalse()
        => V(new VisibilityCondition.WhenPath("live"), """{"live":0}""")
            .Should().BeFalse();

    [Fact]
    public void IsVisible_WhenPath_AbsentKey_IsFalse()
        => V(new VisibilityCondition.WhenPath("nope"), "{}").Should().BeFalse();

    [Fact]
    public void IsVisible_WhenPath_NullValue_IsFalse()
        => V(new VisibilityCondition.WhenPath("x"), """{"x":null}""")
            .Should().BeFalse();

    [Fact]
    public void IsVisible_WhenPath_EmptyString_IsFalse()
        => V(new VisibilityCondition.WhenPath("s"), """{"s":""}""")
            .Should().BeFalse();

    [Fact]
    public void IsVisible_WhenPath_NonEmptyString_IsTrue()
        => V(new VisibilityCondition.WhenPath("s"), """{"s":"hi"}""")
            .Should().BeTrue();

    [Fact]
    public void IsVisible_And_AllTrue_IsTrue()
        => V(new VisibilityCondition.And(
                [new VisibilityCondition.Always(), new VisibilityCondition.Always()]),
            "{}").Should().BeTrue();

    [Fact]
    public void IsVisible_And_OneFalse_IsFalse()
        => V(new VisibilityCondition.And(
                [new VisibilityCondition.Always(), new VisibilityCondition.Never()]),
            "{}").Should().BeFalse();

    [Fact]
    public void IsVisible_Or_AllFalse_IsFalse()
        => V(new VisibilityCondition.Or(
                [new VisibilityCondition.Never(), new VisibilityCondition.Never()]),
            "{}").Should().BeFalse();

    [Fact]
    public void IsVisible_Or_OneTrue_IsTrue()
        => V(new VisibilityCondition.Or(
                [new VisibilityCondition.Never(), new VisibilityCondition.Always()]),
            "{}").Should().BeTrue();

    [Fact]
    public void IsVisible_Not_NegatesTrue()
        => V(new VisibilityCondition.Not(new VisibilityCondition.Always()), "{}")
            .Should().BeFalse();

    [Fact]
    public void IsVisible_Not_NegatesFalse()
        => V(new VisibilityCondition.Not(new VisibilityCondition.Never()), "{}")
            .Should().BeTrue();

    [Fact]
    public void IsVisible_Eq_EqualValues_IsTrue()
        => V(new VisibilityCondition.Eq(
                new VisibilityOperand.Literal(D("1")),
                new VisibilityOperand.Literal(D("1"))),
            "{}").Should().BeTrue();

    [Fact]
    public void IsVisible_Eq_UnequalValues_IsFalse()
        => V(new VisibilityCondition.Eq(
                new VisibilityOperand.Literal(D("1")),
                new VisibilityOperand.Literal(D("2"))),
            "{}").Should().BeFalse();

    [Fact]
    public void IsVisible_Neq_UnequalValues_IsTrue()
        => V(new VisibilityCondition.Neq(
                new VisibilityOperand.Literal(D("1")),
                new VisibilityOperand.Literal(D("2"))),
            "{}").Should().BeTrue();

    [Fact]
    public void IsVisible_Gt_GreaterLeft_IsTrue()
        => V(new VisibilityCondition.Gt(
                new VisibilityOperand.Literal(D("5")),
                new VisibilityOperand.Literal(D("3"))),
            "{}").Should().BeTrue();

    [Fact]
    public void IsVisible_Gt_SameValues_IsFalse()
        => V(new VisibilityCondition.Gt(
                new VisibilityOperand.Literal(D("3")),
                new VisibilityOperand.Literal(D("3"))),
            "{}").Should().BeFalse();

    [Fact]
    public void IsVisible_Gte_EqualValues_IsTrue()
        => V(new VisibilityCondition.Gte(
                new VisibilityOperand.Literal(D("3")),
                new VisibilityOperand.Literal(D("3"))),
            "{}").Should().BeTrue();

    [Fact]
    public void IsVisible_Lt_SmallerLeft_IsTrue()
        => V(new VisibilityCondition.Lt(
                new VisibilityOperand.Literal(D("1")),
                new VisibilityOperand.Literal(D("2"))),
            "{}").Should().BeTrue();

    [Fact]
    public void IsVisible_Lte_EqualValues_IsTrue()
        => V(new VisibilityCondition.Lte(
                new VisibilityOperand.Literal(D("2")),
                new VisibilityOperand.Literal(D("2"))),
            "{}").Should().BeTrue();

    [Fact]
    public void IsVisible_PathRef_Operand_ResolvesFromData()
        => V(new VisibilityCondition.Gt(
                new VisibilityOperand.PathRef("count"),
                new VisibilityOperand.Literal(D("0"))),
            """{"count":5}""").Should().BeTrue();

    [Fact]
    public void IsVisible_Gt_NonNumericOperand_IsFalse_NoBoom()
        => V(new VisibilityCondition.Gt(
                new VisibilityOperand.Literal(D("\"hello\"")),
                new VisibilityOperand.Literal(D("0"))),
            "{}").Should().BeFalse();

    // ---- CatalogValidator round-trip --------------------------------------------------

    [Fact]
    public void Validator_AcceptsVisibleTrue_OnLeafComponent()
    {
        var node = Validate("""{"type":"Note","props":{"text":"hi"},"visible":true}""");
        node.Visible.Should().BeOfType<VisibilityCondition.Always>();
    }

    [Fact]
    public void Validator_AcceptsVisibleFalse_OnLeafComponent()
    {
        var node = Validate("""{"type":"Note","props":{"text":"hi"},"visible":false}""");
        node.Visible.Should().BeOfType<VisibilityCondition.Never>();
    }

    [Fact]
    public void Validator_AcceptsPathCondition_OnComponent()
    {
        var node = Validate(
            """{"type":"Note","props":{"text":"hi"},"visible":{"path":"x.y"}}""");
        node.Visible.Should().BeOfType<VisibilityCondition.WhenPath>()
            .Which.Path.Should().Be("x.y");
    }

    [Fact]
    public void Validator_AbsentVisible_LeavesNullOnNode()
        => Validate("""{"type":"Note","props":{"text":"hi"}}""")
            .Visible.Should().BeNull();

    [Fact]
    public void Validator_NullVisible_LeavesNullOnNode()
        => Validate("""{"type":"Note","props":{"text":"hi"},"visible":null}""")
            .Visible.Should().BeNull();

    [Fact]
    public void Validator_InvalidVisibleShape_Throws()
        => ((Action)(() => Validate(
                """{"type":"Note","props":{"text":"hi"},"visible":42}""")))
            .Should().Throw<UiValidationException>().WithMessage("*Note.visible*");

    [Fact]
    public void Validator_AcceptsVisibleOnContainerComponent()
    {
        var node = Validate(
            """{"type":"Panel","props":{"title":"t"},"visible":false,"children":[]}""");
        node.Visible.Should().BeOfType<VisibilityCondition.Never>();
    }

    [Fact]
    public void Validator_AcceptsLogicExpression_OnComponent()
    {
        var node = Validate(
            """{"type":"Note","props":{"text":"hi"},"visible":{"and":[true,{"path":"x"}]}}""");
        var and = node.Visible.Should().BeOfType<VisibilityCondition.And>().Subject;
        and.Conditions.Should().HaveCount(2);
    }
}
