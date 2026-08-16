# ui-mcp — Unused Analysis

> **Derived from `unused-analysis.json`.** ui-mcp has no Markdown-emitting analyser of its own,
> so this file is authored from that artifact rather than generated. Refresh it by re-running
> `repo_map.py map` and updating both the tables and the Verification block.

## Result

| | |
|---|---|
| Files with no in-repo importer | **0** |
| Exports flagged | **0** |
| — of which deletion candidates (`unreferencedAnywhere`) | **0** |
| — referenced only within their own file | **0** |
| — unclassifiable | **0** |

Nothing is flagged.

## What changed, and why it is not merely a quieter report

The previous run flagged **2** exports: `ComponentSpec` and `PropRule`, both in
`CatalogValidator.cs`, both classified `referencedInModule` — used, but only inside the file that
declares them.

That classification was correct, and it was a **real finding**: both were `public` while every
one of their 15 occurrences was in one file. A consumer handed a `ComponentSpec` could do nothing
with it, because `Validate()` is the only entry point and it takes JSON. They described how the
catalog is *built*, which is not a fact `UiMcp.Abstractions` needs to publish.

**Both are now `internal`.** The count went 2 → 0 because the surface actually narrowed, not
because the analysis was relaxed. `dotnet build` reports 0 warnings and 0 errors, and all 217
tests pass — nothing outside the file was relying on them, exactly as the analysis said.

That change is the intended use of this artifact. A `referencedInModule` entry is not a bug
report. The entry asks a question: *does this type need to be public?* Here the answer was no.

## How usage is determined for C#, and what that limits

This matters enough to state prominently, because the method here is **weaker** than for the
other languages `repo_map` supports.

A C# `using` names a **namespace**, never a symbol. `using UiMcp.Abstractions;` says nothing
about *which* types the file uses. The import edges therefore carry no symbol names. The
dependency graph cannot answer the question "who imports this symbol?".

A **whole-identifier text search across every scanned file** therefore determines the usage.
That search is the same class of text-level heuristic that splits `referencedInModule` from
`unreferencedAnywhere`. The consequences follow. This document states them now, so that no
reader has to discover them later.

- **The search can over-count.** A name in another file's comment or string literal counts as
  a use. The analysis therefore **reports less dead code than the truth, and never more**. That
  is the safe direction for a list whose entries read as deletion candidates.
- **Reflection is invisible.** Neither method sees a type that only `Activator.CreateInstance`,
  DI-by-convention, or attribute discovery reaches. `UiTools` is such a type. A source
  generator finds it from `[McpServerToolType]`, so it appears `test-only` in the graph while
  it is fully live. See [DEPENDENCY_GRAPH.md](DEPENDENCY_GRAPH.md).
- **Names in non-scanned files do not count.** A type referenced only from a `.csproj`, XAML, or
  a JSON manifest would look unused.

This method exists for a reason. Before it, the analysis assumed that an import names its
symbols. That assumption holds for TypeScript and Python. It fails for C#. Under that
assumption the analysis flagged **15 of 20 exports in this repository, and called 9 of them
genuine deletion candidates**. The 9 included `IUiSurface`, `PathResolver` and `TreeRenderer`,
which are three of the most-used types here. Every count was internally consistent and every
gate passed, and that is what made the report dangerous.

## Files with no in-repo importer

None. The entry root (`Program.cs`) is excluded from this measure by definition — a root is what
nothing imports.

## Before deleting anything

Read the caveats above first. Then confirm with a second method. Search for the identifier
across the whole working tree, and include the `.csproj`, XAML and JSON files. Then check
whether an attribute or reflection reaches the type. A `public` type that only *looks* unused
is common in this repository, because attribute discovery finds every MCP tool.

## Verification

Generated 2026-08-15 by `repo_map.py map`.
Regenerate: `python repo_map.py map <repo> --out <dir>` · Check: `python repo_map.py check <repo> --docs docs/architecture`

| Claim | Value | Source |
|---|---|---|
| unusedExportCount | 0 | unused-analysis.json |
| unreferencedAnywhereCount | 0 | unused-analysis.json |
| referencedInModuleCount | 0 | unused-analysis.json |
| unclassifiedExportCount | 0 | unused-analysis.json |
| noImporterFileCount | 0 | unused-analysis.json |
| unusedExportsCount | 0 | dependency-graph.json |

**Claims that the gate cannot hold.** A search across the repository confirmed two facts:
`ComponentSpec` and `PropRule` were the two flagged exports, and all 15 of their occurrences
sit in `CatalogValidator.cs`. That search is a second method, and not a restatement of the
artifact. The "15 of 20" and "9 deletion candidates" figures come from an earlier run. That
run used the previous version of the analysis, during the work that fixed it.
