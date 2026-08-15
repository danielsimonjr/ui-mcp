# ui-mcp — Duplicate Symbols

> **Derived from `duplicate-symbols.json`.** ui-mcp has no Markdown-emitting analyser of its
> own, so this file is authored from that artifact rather than generated. Refresh it by
> re-running `repo_map.py map` and updating both the result and the Verification block.

## Result

| | |
|---|---|
| Distinct exported symbols analysed | 15 |
| Symbols defined more than once | **0** |

No exported type name is declared in more than one file.

## Why this is worth checking here

A duplicate name is rarely a compile error in C# — two types with the same name in **different
namespaces** coexist happily, and the compiler is satisfied. The cost lands on readers and on
`using` statements: an ambiguous reference forces an alias or a fully-qualified name at every
call site, and reviewers start reading the wrong type's source.

The near-miss worth naming is that this repository deliberately keeps **two** vocabularies that
could easily have collided:

- `UiMcp.Abstractions` holds the validated, WPF-free model (`ValidatedNode`, `ValidatedColumn`).
- `UiMcp.Rendering` turns those into WPF objects.

Had the renderer introduced its own `Node` or `Column` type, this report would show it. It does
not, because `TreeRenderer` consumes `ValidatedNode` directly and produces `UIElement` — there
is no intermediate model, and therefore no second name for the same idea.

## The related defect this does not catch

Duplicate *symbols* are clean. The equivalent defect that **did** occur here was a duplicate
**function** under one name: `ui_render` and `ui_status` each computed a `treeHash`, one over
raw JSON and one over structure, so the two tools reported different values for the same render.

That is invisible to this analysis — both were private methods, not exported symbols, and they
had different names in different classes. It was caught by running both tools in one session
and comparing the output.

The fix was to **delete** one and read the value back from the surface, rather than to sync
them. Two sources of truth for one fact is the recurring defect across this workspace, and
syncing them re-arms it.

## Scope

13 distinct symbols across 21 files. The exported-type count is 23; the difference is that this
analysis counts each **distinct name once**, while `totalExports` counts every export in every
file.

The distinct-symbol count went **15 → 13** when `ComponentSpec` and `PropRule` were narrowed to
`internal` — they stopped being exported symbols. Fewer symbols here is a narrowed public surface,
not lost code.

## Verification

Generated 2026-08-15 by `repo_map.py map`.
Regenerate: `python repo_map.py map <repo> --out <dir>` · Check: `python repo_map.py check <repo> --docs docs/architecture`

| Claim | Value | Source |
|---|---|---|
| duplicateCount | 0 | duplicate-symbols.json |
| totalSymbols | 13 | duplicate-symbols.json |
| totalExports | 23 | dependency-graph.json |

**Claims the gate cannot hold:** the `treeHash` incident is recorded in the repository
`todo.md` and in `UiTools.Render`'s own source comment; it is history, not a metric. The
observation about `UiMcp.Rendering` introducing no parallel model is read from
`TreeRenderer.cs`'s signature — it takes `ValidatedNode` and returns `UIElement`.
