namespace UiMcp.Abstractions;

/// <summary>
/// A node that has PASSED validation. The renderer accepts only this type, never raw JSON.
///
/// The distinction is the whole safety model in one signature: raw input and checked input are
/// different types, so "did anyone validate this?" is answered by the compiler rather than by
/// reading call sites and hoping. There is deliberately no public constructor path from JSON that
/// skips <see cref="CatalogValidator"/>.
/// </summary>
/// <param name="Type">Catalog component name. Guaranteed to be a key of the catalog.</param>
/// <param name="Props">
/// Coerced, safe prop values - never the raw input. A string here is length-checked, a path here
/// is charset-checked and prototype-guarded, a tone here is a member of the closed set.
/// </param>
/// <param name="Children">Empty for leaf components. Never null, so callers need no null check.</param>
/// <param name="Visible">
/// The validated visibility condition, or <c>null</c> when the <c>visible</c> prop was absent
/// (always-visible, the JSON-UI default).
/// </param>
public sealed record ValidatedNode(
    string Type,
    IReadOnlyDictionary<string, object> Props,
    IReadOnlyList<ValidatedNode> Children,
    VisibilityCondition? Visible = null);

/// <summary>One validated Table column: a header and a path relative to the row item.</summary>
public sealed record ValidatedColumn(string Header, string ValuePath);
