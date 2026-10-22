using System.Reflection;
using System.Reflection.Emit;
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
    private static ValidatedNode Validate(string json) =>
        CatalogValidator.Validate(D(json));

    // ---- VisibilityCondition.Parse ----------------------------------------------------

    [Fact]
    public void Parse_True_ReturnsAlways()
        => VisibilityCondition.Parse(D("true"))
            .Should().BeOfType<VisibilityCondition.Always>();

    [Fact]
    public void Parse_False_ReturnsNever()
        => VisibilityCondition.Parse(D("false"))
            .Should().BeOfType<VisibilityCondition.Never>();

    [Fact]
    public void Parse_PathObject_ReturnsWhenPath()
    {
        var cond = VisibilityCondition.Parse(D("""{"path":"sections.live"}"""));
        cond.Should().BeOfType<VisibilityCondition.WhenPath>()
            .Which.Path.Should().Be("sections.live");
    }

    [Fact]
    public void Parse_AndObject_ReturnsAnd()
    {
        var cond = VisibilityCondition.Parse(
            D("""{"and":[{"path":"a"},{"path":"b"}]}"""));
        var and = cond.Should().BeOfType<VisibilityCondition.And>().Subject;
        and.Conditions.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_OrObject_ReturnsOr()
    {
        var cond = VisibilityCondition.Parse(
            D("""{"or":[true,false]}"""));
        cond.Should().BeOfType<VisibilityCondition.Or>()
            .Which.Conditions.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_NotObject_ReturnsNot()
    {
        var cond = VisibilityCondition.Parse(D("""{"not":true}"""));
        cond.Should().BeOfType<VisibilityCondition.Not>()
            .Which.Condition.Should().BeOfType<VisibilityCondition.Always>();
    }

    [Fact]
    public void Parse_EqObject_ReturnsEq()
    {
        var cond = VisibilityCondition.Parse(
            D("""{"eq":[{"path":"x"},1]}"""));
        cond.Should().BeOfType<VisibilityCondition.Eq>();
    }

    [Fact]
    public void Parse_NeqObject_ReturnsNeq()
        => VisibilityCondition.Parse(D("""{"neq":[0,1]}"""))
            .Should().BeOfType<VisibilityCondition.Neq>();

    [Fact]
    public void Parse_GtObject_ReturnsGt()
        => VisibilityCondition.Parse(D("""{"gt":[2,1]}"""))
            .Should().BeOfType<VisibilityCondition.Gt>();

    [Fact]
    public void Parse_GteObject_ReturnsGte()
        => VisibilityCondition.Parse(D("""{"gte":[1,1]}"""))
            .Should().BeOfType<VisibilityCondition.Gte>();

    [Fact]
    public void Parse_LtObject_ReturnsLt()
        => VisibilityCondition.Parse(D("""{"lt":[1,2]}"""))
            .Should().BeOfType<VisibilityCondition.Lt>();

    [Fact]
    public void Parse_LteObject_ReturnsLte()
        => VisibilityCondition.Parse(D("""{"lte":[1,1]}"""))
            .Should().BeOfType<VisibilityCondition.Lte>();

    [Fact]
    public void Parse_Number_Throws()
        => ((Action)(() => VisibilityCondition.Parse(D("42"))))
            .Should().Throw<UiValidationException>();

    [Fact]
    public void Parse_UnrecognisedObject_Throws()
        => ((Action)(() => VisibilityCondition.Parse(D("""{"unknown":true}"""))))
            .Should().Throw<UiValidationException>().WithMessage("*unrecognised*");

    [Fact]
    public void Parse_PathMustBeString()
        => ((Action)(() => VisibilityCondition.Parse(D("""{"path":42}"""))))
            .Should().Throw<UiValidationException>();

    [Fact]
    public void Parse_AndMustBeArray()
        => ((Action)(() => VisibilityCondition.Parse(D("""{"and":true}"""))))
            .Should().Throw<UiValidationException>();

    [Fact]
    public void Parse_EqMustBeTwoElements()
        => ((Action)(() => VisibilityCondition.Parse(D("""{"eq":[1]}"""))))
            .Should().Throw<UiValidationException>();

    // ---- a condition object holds exactly one form key ------------------------------------------
    //
    // The node validator refuses a key that it does not know. A condition object did not: the object
    // {"path":"a","pth":1} was accepted and `pth` was dropped, so a typo hid nothing and failed nothing.
    // Each of the ten object forms has a refusal test and a positive control that parses the same form
    // without the extra key.

    public static TheoryData<string, string> FormObjects => new()
    {
        { "path", "\"a\"" },
        { "and", "[true]" },
        { "or", "[true]" },
        { "not", "true" },
        { "eq", "[1,1]" },
        { "neq", "[1,2]" },
        { "gt", "[2,1]" },
        { "gte", "[1,1]" },
        { "lt", "[1,2]" },
        { "lte", "[1,1]" },
    };

    private static string Obj(string form, string value, string? extra = null, bool extraFirst = false)
    {
        var main = "\"" + form + "\":" + value;
        if (extra is null) return "{" + main + "}";
        return extraFirst ? "{" + extra + "," + main + "}" : "{" + main + "," + extra + "}";
    }

    [Fact]
    public void FormObjects_ListsEveryObjectForm_OfTheUnion()
        // Always and Never are the literals true and false. Every other nested record has an object form,
        // and the theories below must list it, or a new form would escape the one-key rule untested.
        => typeof(VisibilityCondition).GetNestedTypes().Length.Should().Be(FormObjects.Count() + 2);

    [Theory, MemberData(nameof(FormObjects))]
    public void Parse_FormWithOnlyItsKey_IsAccepted(string form, string value)
        => ((Action)(() => VisibilityCondition.Parse(D(Obj(form, value)))))
            .Should().NotThrow();

    [Theory, MemberData(nameof(FormObjects))]
    public void Parse_FormWithAnExtraKey_Throws(string form, string value)
        => ((Action)(() => VisibilityCondition.Parse(D(Obj(form, value, "\"pth\":1")))))
            .Should().Throw<UiValidationException>()
            .WithMessage("visible: unknown key \"pth\" next to \"" + form + "\"*");

    [Theory, MemberData(nameof(FormObjects))]
    public void Parse_FormWithAnExtraKeyBeforeIt_Throws(string form, string value)
        => ((Action)(() => VisibilityCondition.Parse(D(Obj(form, value, "\"pth\":1", extraFirst: true)))))
            .Should().Throw<UiValidationException>()
            .WithMessage("visible: unknown key \"pth\" next to \"" + form + "\"*");

    [Fact]
    public void Parse_OperandPathWithOnlyItsKey_IsAccepted()
        => ((Action)(() => VisibilityCondition.Parse(D("""{"eq":[{"path":"a"},1]}"""))))
            .Should().NotThrow();

    [Fact]
    public void Parse_OperandPathWithAnExtraKey_Throws()
        => ((Action)(() => VisibilityCondition.Parse(D("""{"eq":[{"path":"a","pth":1},1]}"""))))
            .Should().Throw<UiValidationException>()
            .WithMessage("visible.eq[0]: unknown key \"pth\" next to \"path\"*");

    [Fact]
    public void Validate_NodeWhoseConditionHoldsAnExtraKey_IsRefusedAtTheNode()
        => ((Action)(() => Validate("""{"type":"Note","props":{"text":"hi"},"visible":{"path":"a","pth":1}}""")))
            .Should().Throw<UiValidationException>()
            .WithMessage("Note.visible: visible: unknown key \"pth\" next to \"path\"*");

    // ---- an operand error names the position that the caller wrote -------------------------------
    //
    // ParsePath used to prefix every path error with "visible.path:", so a bad operand of gt reported a
    // key that the caller never wrote. The position now names the form and the index of the operand. It
    // does not name an enclosing and, or or not.

    public static TheoryData<string> ComparisonOperators => new() { "eq", "neq", "gt", "gte", "lt", "lte" };

    [Theory, MemberData(nameof(ComparisonOperators))]
    public void Parse_BadPathInTheLeftOperand_NamesTheOperatorAndTheIndex(string op)
        => ((Action)(() => VisibilityCondition.Parse(D("{\"" + op + "\":[{\"path\":\"bad path\"},1]}"))))
            .Should().Throw<UiValidationException>().WithMessage("visible." + op + "[0].path: *");

    [Theory, MemberData(nameof(ComparisonOperators))]
    public void Parse_BadPathInTheRightOperand_NamesTheOperatorAndTheIndex(string op)
        => ((Action)(() => VisibilityCondition.Parse(D("{\"" + op + "\":[1,{\"path\":\"bad path\"}]}"))))
            .Should().Throw<UiValidationException>().WithMessage("visible." + op + "[1].path: *");

    [Fact]
    public void Parse_ItemScopeInAnOperand_NamesTheOperatorAndTheIndex()
        => ((Action)(() => VisibilityCondition.Parse(D("""{"gt":[{"path":"$item.x"},1]}"""))))
            .Should().Throw<UiValidationException>()
            .WithMessage("visible.gt[0].path: $item is not available in a condition*");

    // ---- an operand object holds a string `path` key, or the condition is refused ------------------
    //
    // An operand object without a string `path` was a literal that reduced to null, so {"pth":"a"}
    // was equal to null and the condition decided without reading any data. It is refused now, like a
    // second key next to `path`. An array stays a literal; the control below pins that.

    public static TheoryData<string> OperandObjectsWithoutAPath => new()
    {
        "{}",
        "{\"pth\":\"a\"}",
        "{\"a\":1}",
        "{\"Path\":\"a\"}",
    };

    public static TheoryData<string> OperandObjectsWhosePathIsNotAString => new()
    {
        "{\"path\":5}",
        "{\"path\":null}",
        "{\"path\":true}",
        "{\"path\":[\"a\"]}",
        "{\"path\":{\"path\":\"a\"}}",
    };

    [Theory, MemberData(nameof(OperandObjectsWithoutAPath))]
    public void Parse_LeftOperandObjectWithoutAPathKey_Throws(string operand)
        => ((Action)(() => VisibilityCondition.Parse(D("{\"eq\":[" + operand + ",1]}"))))
            .Should().Throw<UiValidationException>()
            .WithMessage("visible.eq[0]: an operand object needs a \"path\" key");

    [Theory, MemberData(nameof(OperandObjectsWithoutAPath))]
    public void Parse_RightOperandObjectWithoutAPathKey_Throws(string operand)
        => ((Action)(() => VisibilityCondition.Parse(D("{\"eq\":[1," + operand + "]}"))))
            .Should().Throw<UiValidationException>()
            .WithMessage("visible.eq[1]: an operand object needs a \"path\" key");

    [Theory, MemberData(nameof(ComparisonOperators))]
    public void Parse_OperandObjectWithoutAPathKey_NamesTheOperatorAndTheIndex(string op)
        => ((Action)(() => VisibilityCondition.Parse(D("{\"" + op + "\":[1,{\"pth\":\"a\"}]}"))))
            .Should().Throw<UiValidationException>()
            .WithMessage("visible." + op + "[1]: an operand object needs a \"path\" key");

    [Theory, MemberData(nameof(OperandObjectsWhosePathIsNotAString))]
    public void Parse_LeftOperandObjectWhosePathIsNotAString_Throws(string operand)
        => ((Action)(() => VisibilityCondition.Parse(D("{\"eq\":[" + operand + ",1]}"))))
            .Should().Throw<UiValidationException>()
            .WithMessage("visible.eq[0].path must be a string");

    [Theory, MemberData(nameof(OperandObjectsWhosePathIsNotAString))]
    public void Parse_RightOperandObjectWhosePathIsNotAString_Throws(string operand)
        => ((Action)(() => VisibilityCondition.Parse(D("{\"eq\":[1," + operand + "]}"))))
            .Should().Throw<UiValidationException>()
            .WithMessage("visible.eq[1].path must be a string");

    [Fact]
    public void Parse_OperandArrayAndScalars_AreStillLiterals_ControlForTheObjectRefusal()
        => ((Action)(() => VisibilityCondition.Parse(
                D("""{"and":[{"eq":[[1,2],{"path":"a"}]},{"neq":["x",null]},{"gt":[1,2.5]},{"eq":[true,false]}]}"""))))
            .Should().NotThrow();

    [Fact]
    public void Validate_NodeWhoseOperandObjectHasNoPath_IsRefusedAtTheNode()
        => ((Action)(() => Validate(
                """{"type":"Note","props":{"text":"hi"},"visible":{"eq":[{"pth":"a"},1]}}""")))
            .Should().Throw<UiValidationException>()
            .WithMessage("Note.visible: visible.eq[0]: an operand object needs a \"path\" key*");

    [Fact]
    public void Parse_BadPathInThePathForm_StillNamesVisiblePath()
        => ((Action)(() => VisibilityCondition.Parse(D("""{"path":"bad path"}"""))))
            .Should().Throw<UiValidationException>().WithMessage("visible.path: *");

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

    // ---- eq and neq compare by value ---------------------------------------------------

    private static VisibilityOperand Lit(string json) => new VisibilityOperand.Literal(D(json));

    [Fact]
    public void IsVisible_Eq_EqualStrings_IsTrue()
        => V(new VisibilityCondition.Eq(Lit("\"on\""), Lit("\"on\"")), "{}").Should().BeTrue();

    [Fact]
    public void IsVisible_Eq_EqualBooleans_IsTrue()
        => V(new VisibilityCondition.Eq(Lit("true"), Lit("true")), "{}").Should().BeTrue();

    [Fact]
    public void IsVisible_Eq_PathAgainstLiteral_ComparesTheResolvedValue()
    {
        var eq = new VisibilityCondition.Eq(new VisibilityOperand.PathRef("mode"), Lit("\"on\""));
        V(eq, """{"mode":"on"}""").Should().BeTrue();
        V(eq, """{"mode":"off"}""").Should().BeFalse();
    }

    [Fact]
    public void IsVisible_Eq_NumberAgainstItsStringForm_IsFalse()
        => V(new VisibilityCondition.Eq(Lit("1"), Lit("\"1\"")), "{}").Should().BeFalse();

    [Fact]
    public void IsVisible_Neq_EqualNumbers_IsFalse()
        => V(new VisibilityCondition.Neq(Lit("1"), Lit("1")), "{}").Should().BeFalse();

    [Fact]
    public void IsVisible_Neq_EqualStrings_IsFalse()
        => V(new VisibilityCondition.Neq(Lit("\"on\""), Lit("\"on\"")), "{}").Should().BeFalse();

    [Fact]
    public void IsVisible_Neq_NumberAgainstItsStringForm_IsTrue()
        => V(new VisibilityCondition.Neq(Lit("1"), Lit("\"1\"")), "{}").Should().BeTrue();

    // ---- ordering operators are false when an operand is not a number ------------------

    [Fact]
    public void IsVisible_Gte_StringOperand_IsFalse()
        => V(new VisibilityCondition.Gte(Lit("\"hello\""), Lit("0")), "{}").Should().BeFalse();

    [Fact]
    public void IsVisible_Lte_StringOperand_IsFalse()
        => V(new VisibilityCondition.Lte(Lit("\"hello\""), Lit("0")), "{}").Should().BeFalse();

    [Fact]
    public void IsVisible_Gte_MissingPath_IsFalse()
        => V(new VisibilityCondition.Gte(new VisibilityOperand.PathRef("missing"), Lit("0")), "{}")
            .Should().BeFalse();

    [Fact]
    public void IsVisible_Lte_MissingPath_IsFalse()
        => V(new VisibilityCondition.Lte(new VisibilityOperand.PathRef("missing"), Lit("0")), "{}")
            .Should().BeFalse();

    [Fact]
    public void IsVisible_Lt_StringOperand_IsFalse()
        => V(new VisibilityCondition.Lt(Lit("\"hello\""), Lit("0")), "{}").Should().BeFalse();

    [Fact]
    public void IsVisible_Gte_SmallerLeft_IsFalse()
        => V(new VisibilityCondition.Gte(Lit("1"), Lit("2")), "{}").Should().BeFalse();

    [Fact]
    public void IsVisible_Lte_GreaterLeft_IsFalse()
        => V(new VisibilityCondition.Lte(Lit("3"), Lit("2")), "{}").Should().BeFalse();

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

    // ---- behaviours the documents state and no earlier test pinned ---------------------------------

    private static VisibilityCondition P(string json) => VisibilityCondition.Parse(D(json));

    [Theory]
    [InlineData("""{"and":[]}""", true)]
    [InlineData("""{"or":[]}""", false)]
    [InlineData("""{"and":[false]}""", false)]
    [InlineData("""{"or":[true]}""", true)]
    public void IsVisible_EmptyAndIsTrue_EmptyOrIsFalse(string conditionJson, bool expected)
        => V(P(conditionJson), "{}").Should().Be(expected);

    [Theory]
    [InlineData("""{"and":[],"path":"a"}""", "unknown key \"and\" next to \"path\"")]
    [InlineData("""{"not":true,"or":[]}""", "unknown key \"not\" next to \"or\"")]
    [InlineData("""{"eq":[1,1],"not":true}""", "unknown key \"eq\" next to \"not\"")]
    [InlineData("""{"gt":[2,1],"eq":[1,1]}""", "unknown key \"gt\" next to \"eq\"")]
    [InlineData("""{"lte":[1,1],"gt":[1,1]}""", "unknown key \"lte\" next to \"gt\"")]
    public void Parse_TwoFormKeys_AreRefused_WhateverTheirOrder(string conditionJson, string expectedMessage)
        // A condition object holds exactly one form key. Each case lists the later form first. The form
        // that comes first in the order path, and, or, not, eq, neq, gt, gte, lt, lte is the chosen one,
        // and the other form key is the unknown key of the message. Before, the chosen form won and the
        // other key was dropped.
        => ((Action)(() => P(conditionJson)))
            .Should().Throw<UiValidationException>().WithMessage("visible: " + expectedMessage);

    [Theory]
    [InlineData("""{"x":1}""")]
    [InlineData("""{"x":{"y":1}}""")]
    public void IsVisible_AnItemPathIsUnresolvedAtTheRoot(string data)
    {
        // Built directly: the validator refuses a `$item` path in a condition, so only the evaluator
        // can be asked what it does with one.
        V(new VisibilityCondition.WhenPath("$item.x"), data).Should().BeFalse();
        V(P("""{"path":"x"}"""), data).Should().BeTrue("the same name without $item resolves");
    }

    [Theory]
    [InlineData("""{"a":[]}""", true)]
    [InlineData("""{"a":{}}""", true)]
    [InlineData("""{"a":"0"}""", true)]
    [InlineData("""{"a":"x"}""", true)]
    [InlineData("""{"a":1}""", true)]
    [InlineData("""{"a":0}""", false)]
    [InlineData("""{"a":""}""", false)]
    [InlineData("""{"a":false}""", false)]
    [InlineData("""{"a":null}""", false)]
    public void IsVisible_TruthinessFollowsJavaScript(string data, bool expected)
        => V(new VisibilityCondition.WhenPath("a"), data).Should().Be(expected);

    [Theory]
    [InlineData("""{"eq":[{"path":"nope1"},{"path":"nope2"}]}""", true)]
    [InlineData("""{"neq":[{"path":"nope1"},{"path":"nope2"}]}""", false)]
    [InlineData("""{"eq":[{"path":"nope"},null]}""", true)]
    [InlineData("""{"eq":[{"path":"nope"},1]}""", false)]
    [InlineData("""{"eq":[{"path":"nope"},""]}""", false)]
    public void IsVisible_TwoUnresolvedOperandsAreEqual_AndAnUnresolvedOperandEqualsNull(
        string conditionJson, bool expected)
        => V(P(conditionJson), "{}").Should().Be(expected);

    [Theory]
    [InlineData("""{"eq":[[1,2],null]}""", true)]
    [InlineData("""{"eq":[[1,2],[3]]}""", true)]
    [InlineData("""{"eq":[[1,2],1]}""", false)]
    public void IsVisible_AnArrayLiteralReducesToNull(string conditionJson, bool expected)
        => V(P(conditionJson), "{}").Should().Be(expected);

    [Theory]
    [InlineData("""{"auth":"signedIn"}""")]
    [InlineData("""{"auth":"signedOut"}""")]
    [InlineData("""{"auth":true}""")]
    public void Validator_AnAuthCondition_IsRefused_NotTreatedAsHidden(string conditionJson)
        => ((Action)(() => Validate(
                """{"type":"Note","props":{"text":"hi"},"visible":""" + conditionJson + "}")))
            .Should().Throw<UiValidationException>()
            .WithMessage("*Note.visible*unrecognised condition shape*");

    [Fact]
    public void Validator_APathCondition_IsAccepted_ControlForTheAuthRefusal()
        => Validate("""{"type":"Note","props":{"text":"hi"},"visible":{"path":"signedIn"}}""")
            .Visible.Should().BeOfType<VisibilityCondition.WhenPath>();

    // ---- the path of a condition follows the path rule of the prop types --------------------------
    // The path of a `visible` condition once skipped the charset rule that every prop path obeys.
    // Both parse sites (the `path` form and a `path` operand) now share one check. The refusals
    // below each have a positive control: a rule that refuses every path fails the control.

    private static string PathObject(string path) => "{\"path\":" + JsonSerializer.Serialize(path) + "}";

    public static TheoryData<string> IllegalPaths => new()
    {
        "a b", "a'b", "a\"b", "a/b", "a;b", "a-b", "", "$other", "$", "a$b",
        // .NET `$` also matches before a final line feed, so a path that ends in one is a case of its own.
        "a\n", "a.b\n",
    };

    [Theory]
    [MemberData(nameof(IllegalPaths))]
    public void Parse_APathWithIllegalCharacters_IsRefused(string path)
        => ((Action)(() => VisibilityCondition.Parse(D(PathObject(path)))))
            .Should().Throw<UiValidationException>().WithMessage("*illegal characters in path*");

    [Theory]
    [MemberData(nameof(IllegalPaths))]
    public void Parse_AnOperandPathWithIllegalCharacters_IsRefused_OnEitherSide(string path)
    {
        ((Action)(() => VisibilityCondition.Parse(D("{\"eq\":[" + PathObject(path) + ",1]}"))))
            .Should().Throw<UiValidationException>().WithMessage("*illegal characters in path*");
        ((Action)(() => VisibilityCondition.Parse(D("{\"gt\":[1," + PathObject(path) + "]}"))))
            .Should().Throw<UiValidationException>().WithMessage("*illegal characters in path*");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a.b")]
    [InlineData("a.b[0]")]
    [InlineData("_x1.Y2")]
    public void Parse_ALegalPath_IsAccepted_AtBothSites_ControlForTheRefusals(string path)
    {
        P(PathObject(path)).Should().Be(new VisibilityCondition.WhenPath(path));
        P("{\"eq\":[" + PathObject(path) + ",1]}").Should().BeOfType<VisibilityCondition.Eq>()
            .Which.Left.Should().Be(new VisibilityOperand.PathRef(path));
    }

    [Theory]
    [InlineData("a.__proto__.b")]
    [InlineData("constructor")]
    [InlineData("prototype")]
    public void Parse_APrototypePath_IsRefused_AtBothSites(string path)
    {
        ((Action)(() => VisibilityCondition.Parse(D(PathObject(path)))))
            .Should().Throw<UiValidationException>().WithMessage("*prototype access refused*");
        ((Action)(() => VisibilityCondition.Parse(D("{\"neq\":[" + PathObject(path) + ",1]}"))))
            .Should().Throw<UiValidationException>().WithMessage("*prototype access refused*");
    }

    [Theory]
    [InlineData("$item")]
    [InlineData("$item.x")]
    public void Parse_AnItemPath_IsRefused_AtBothSites(string path)
    {
        // A condition reads the root data. `$item` has no scope there, so the validator says so
        // instead of accepting a path that can never resolve.
        ((Action)(() => VisibilityCondition.Parse(D(PathObject(path)))))
            .Should().Throw<UiValidationException>().WithMessage("*$item*");
        ((Action)(() => VisibilityCondition.Parse(D("{\"lt\":[" + PathObject(path) + ",1]}"))))
            .Should().Throw<UiValidationException>().WithMessage("*$item*");
    }

    [Fact]
    public void Parse_AnIllegalPath_InsideALogicForm_IsRefused()
    {
        ((Action)(() => VisibilityCondition.Parse(D("{\"not\":" + PathObject("a b") + "}"))))
            .Should().Throw<UiValidationException>().WithMessage("*illegal characters in path*");
        ((Action)(() => VisibilityCondition.Parse(D("{\"and\":[true," + PathObject("a'b") + "]}"))))
            .Should().Throw<UiValidationException>().WithMessage("*illegal characters in path*");
    }

    [Fact]
    public void Validator_AVisiblePathWithIllegalCharacters_IsRefused_NamingTheComponent()
        => ((Action)(() => Validate(
                "{\"type\":\"Note\",\"props\":{\"text\":\"hi\"},\"visible\":" + PathObject("a b") + "}")))
            .Should().Throw<UiValidationException>().WithMessage("*Note.visible*illegal characters in path*");

    // ---- an unknown condition subtype ---------------------------------------------------------------
    // VisibilityCondition has a private constructor, so C# source outside the type cannot add a
    // subtype, and Parse builds only the twelve nested records. A validated tree never reaches the
    // default arm. The arm decides what a subtype does that reaches it anyway, and it must hide the
    // node. The stub below is a real subtype built at run time: the record has a protected copy
    // constructor, and a derived type may call it.

    private static VisibilityCondition StubSubtype()
    {
        var module = AssemblyBuilder
            .DefineDynamicAssembly(new AssemblyName("UiMcp.Tests.StubCondition"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("StubCondition");
        var type = module.DefineType("StubCondition", TypeAttributes.Public | TypeAttributes.Sealed,
            typeof(VisibilityCondition));

        var copyCtor = typeof(VisibilityCondition).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(VisibilityCondition) }, null)!;
        var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard,
            new[] { typeof(VisibilityCondition) });
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Call, copyCtor);
        ctorIl.Emit(OpCodes.Ret);

        // An abstract record declares an abstract clone method, and the stub must override it.
        var clone = typeof(VisibilityCondition).GetMethod("<Clone>$");
        if (clone is { IsAbstract: true })
        {
            var cloneIl = type.DefineMethod("<Clone>$",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(VisibilityCondition), Type.EmptyTypes).GetILGenerator();
            cloneIl.Emit(OpCodes.Ldarg_0);
            cloneIl.Emit(OpCodes.Ret);
        }

        return (VisibilityCondition)Activator.CreateInstance(type.CreateType(), new VisibilityCondition.Always())!;
    }

    [Fact]
    public void IsVisible_AnUnknownConditionSubtype_IsHidden()
    {
        var stub = StubSubtype();

        stub.GetType().BaseType.Should().Be(typeof(VisibilityCondition), "the stub must be a subtype the switch does not name");
        V(stub, "{}").Should().BeFalse();
    }

    [Fact]
    public void IsVisible_AnUnknownConditionSubtype_InsideAnd_HidesTheNode_ControlTheKnownFormsStillShow()
    {
        var stub = StubSubtype();

        V(new VisibilityCondition.And(new[] { stub }), "{}").Should().BeFalse();
        V(new VisibilityCondition.Or(new[] { stub }), "{}").Should().BeFalse();
        V(new VisibilityCondition.And(new VisibilityCondition[] { new VisibilityCondition.Always() }), "{}")
            .Should().BeTrue("a known form still shows the node");
    }

    [Fact]
    public void IsVisible_ANotOfAnUnknownConditionSubtype_ShowsTheNode_AsTheDocumentsSay()
    {
        // Not negates the hidden result of the unknown subtype. The architecture documents state this
        // on purpose, and no test read it.
        V(new VisibilityCondition.Not(StubSubtype()), "{}").Should().BeTrue();
    }

    // ---- a new condition subtype must come with a row and an evaluator arm --------------------------
    //
    // The default arm hides a subtype that it does not know, and Not shows the node when its inner
    // condition is hidden, so a new subtype without an arm fails open under Not. The union is closed
    // today, and Parse builds the twelve records only. This table turns the future risk into a red
    // build. A new nested record fails the first test until it has a row. A row whose condition shows
    // the node fails the second test until the evaluator has an arm for the subtype. Never is the one
    // subtype that the default arm decides correctly, because its answer is false.

    private static readonly (string Subtype, string Json, string DataJson, bool Expected)[] SubtypeRows =
    {
        ("Always",   "true",               "{}",          true),
        ("Never",    "false",              "{}",          false),
        ("WhenPath", """{"path":"a"}""",   """{"a":1}""", true),
        ("And",      """{"and":[true]}""", "{}",          true),
        ("Or",       """{"or":[true]}""",  "{}",          true),
        ("Not",      """{"not":false}""",  "{}",          true),
        ("Eq",       """{"eq":[1,1]}""",   "{}",          true),
        ("Neq",      """{"neq":[1,2]}""",  "{}",          true),
        ("Gt",       """{"gt":[2,1]}""",   "{}",          true),
        ("Gte",      """{"gte":[1,1]}""",  "{}",          true),
        ("Lt",       """{"lt":[1,2]}""",   "{}",          true),
        ("Lte",      """{"lte":[1,1]}""",  "{}",          true),
    };

    [Fact]
    public void EveryNestedSubtypeOfTheUnion_HasARowInTheSubtypeTable()
        => SubtypeRows.Select(r => r.Subtype).OrderBy(n => n, StringComparer.Ordinal)
            .Should().Equal(
                typeof(VisibilityCondition).GetNestedTypes().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal),
                "a new subtype needs a row here and an arm in VisibilityEvaluator.IsVisible");

    [Fact]
    public void EveryRow_ParsesToItsSubtype_AndIsDecidedByAnArm_NotByTheDefault()
    {
        foreach (var (subtype, json, dataJson, expected) in SubtypeRows)
        {
            var condition = P(json);
            condition.GetType().Name.Should().Be(subtype, "the row {0} must build that subtype", json);
            V(condition, dataJson).Should().Be(expected, "{0} must be decided by its own arm", subtype);
            V(new VisibilityCondition.Not(condition), dataJson).Should().Be(!expected, "Not of {0}", subtype);
        }
    }
}
