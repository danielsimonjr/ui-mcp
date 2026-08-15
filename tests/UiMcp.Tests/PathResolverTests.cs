using System.Text.Json;
using FluentAssertions;
using UiMcp.Abstractions;

namespace UiMcp.Tests;

/// <summary>
/// Ported from AdminLTE/JSON-UI/render.js. Where the two disagree, the JS is right.
///
/// THE INVARIANT THIS FILE EXISTS TO PROTECT: an unresolvable path returns MISSING, and missing
/// renders as UNKNOWN - never as 0, never as "". SPEC section 6 calls this the most expensive
/// recurring failure on this machine, because a blind spot displayed as a green zero reads as
/// health. So the decisive tests here are not the happy paths; they are the pairs that prove
/// "no value" and "the value zero" stay distinguishable all the way to the screen.
/// </summary>
public class PathResolverTests
{
    private const string Data = """
    {
      "machine": "ZBOOK",
      "uptimeH": 0,
      "healthy": false,
      "sections": {
        "roster": { "live": 3, "expected": 3 },
        "disk": { "drives": [ { "letter": "C", "freeGb": 258.5 }, { "letter": "I", "freeGb": 377.6 } ] }
      },
      "grid": [ [ 10, 11 ], [ 20, 21 ] ],
      "empty": null,
      "tags": [ "a", "b", "c" ]
    }
    """;

    private static JsonElement Root => JsonDocument.Parse(Data).RootElement;

    private static JsonElement? R(string path, JsonElement? scope = null)
        => PathResolver.Resolve(Root, path, scope);

    private static string D(string path, JsonElement? scope = null)
        => PathResolver.Display(R(path, scope));

    // ---- resolution ---------------------------------------------------------------------------

    [Fact]
    public void ResolvesTopLevelKey()
        => R("machine")!.Value.GetString().Should().Be("ZBOOK");

    [Fact]
    public void ResolvesNestedKey()
        => R("sections.roster.live")!.Value.GetInt32().Should().Be(3);

    [Fact]
    public void ResolvesArrayIndex()
        => R("tags[1]")!.Value.GetString().Should().Be("b");

    [Fact]
    public void ResolvesIndexThenKey()
        => R("sections.disk.drives[1].letter")!.Value.GetString().Should().Be("I");

    [Fact]
    public void ResolvesConsecutiveIndices()
        => R("grid[1][0]")!.Value.GetInt32().Should().Be(20);

    // ---- the missing-vs-zero pair. This is the whole point. ------------------------------------

    [Fact]
    public void MissingKey_ResolvesToMissing_NotZero()
        => R("nope").Should().BeNull();

    [Fact]
    public void MissingNestedKey_ResolvesToMissing_WithoutThrowing()
        => R("sections.nope.deeper.stillNope").Should().BeNull();

    [Fact]
    public void IndexOutOfRange_ResolvesToMissing()
        => R("tags[99]").Should().BeNull();

    [Fact]
    public void IndexingANonArray_ResolvesToMissing()
        => R("machine[0]").Should().BeNull();

    [Fact]
    public void KeyOnAScalar_ResolvesToMissing()
        => R("machine.nope").Should().BeNull();

    [Fact]
    public void JsonNull_DisplaysAsUnknown()
        => D("empty").Should().Be("UNKNOWN");

    [Fact]
    public void MissingPath_DisplaysAsUnknown()
        => D("nope").Should().Be("UNKNOWN");

    /// <summary>The pair that matters: a real zero must NOT be swallowed into UNKNOWN.</summary>
    [Fact]
    public void RealZero_DisplaysAsZero_NotUnknown()
        => D("uptimeH").Should().Be("0");

    /// <summary>And the converse: a blind spot must NOT be dressed up as a zero.</summary>
    [Fact]
    public void MissingValue_NeverDisplaysAsZero()
        => D("definitely.not.here").Should().NotBe("0");

    [Fact]
    public void RealFalse_DisplaysAsNo_NotUnknown()
        => D("healthy").Should().Be("NO");

    // ---- $item scope --------------------------------------------------------------------------

    private static JsonElement Item => JsonDocument.Parse("""{"letter":"C","freeGb":258.5}""").RootElement;

    [Fact]
    public void ItemScope_ResolvesAgainstTheItem()
        => R("$item.letter", Item)!.Value.GetString().Should().Be("C");

    [Fact]
    public void BareItem_ResolvesToTheItemItself()
        => R("$item", Item)!.Value.ValueKind.Should().Be(JsonValueKind.Object);

    [Fact]
    public void ItemScope_WithoutAScope_ResolvesToMissing()
        => R("$item.letter").Should().BeNull();

    [Fact]
    public void ItemScope_DoesNotLeakIntoTheRootDocument()
        => R("$item.machine", Item).Should().BeNull();

    [Fact]
    public void NonItemPath_StillResolvesAgainstRoot_EvenWhenScopePresent_PositiveControl()
        => R("machine", Item)!.Value.GetString().Should().Be("ZBOOK");

    // ---- prototype refusal --------------------------------------------------------------------

    [Theory]
    [InlineData("__proto__")]
    [InlineData("sections.__proto__.x")]
    [InlineData("constructor")]
    [InlineData("a.prototype.b")]
    public void PrototypePaths_ResolveToMissing(string path)
        => R(path).Should().BeNull();

    [Fact]
    public void OrdinaryPath_Resolves_PositiveControlForThePrototypeGuard()
        => R("sections.roster.expected")!.Value.GetInt32().Should().Be(3);

    // ---- display formatting -------------------------------------------------------------------

    [Fact]
    public void Display_String_IsItself()
        => D("machine").Should().Be("ZBOOK");

    [Fact]
    public void Display_TrueIsYes()
        => PathResolver.Display(JsonDocument.Parse("true").RootElement).Should().Be("YES");

    [Fact]
    public void Display_Array_IsItsLength()
        => D("tags").Should().Be("3");

    [Fact]
    public void Display_Object_IsOBJ()
        => D("sections.roster").Should().Be("OBJ");

    [Fact]
    public void Display_Number_KeepsPrecision()
        => D("sections.disk.drives[0].freeGb").Should().Be("258.5");
}
