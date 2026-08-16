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

A duplicate name is rarely a compile error in C#. Two types with the same name can live in
**different namespaces**, and the compiler accepts them. The cost falls on the readers and on
the `using` statements. An ambiguous reference makes each call site use an alias or a full
name. A reviewer then starts to read the source of the wrong type.

The near-miss worth naming is that this repository deliberately keeps **two** vocabularies that
could easily have collided:

- `UiMcp.Abstractions` holds the validated, WPF-free model (`ValidatedNode`, `ValidatedColumn`).
- `UiMcp.Rendering` turns those into WPF objects.

This report would show a `Node` type or a `Column` type if the renderer declared one. The
report shows neither. `TreeRenderer` takes `ValidatedNode` directly and returns `UIElement`.
No intermediate model exists, so no second name for one idea exists.

## The related defect this does not catch

Duplicate *symbols* are clean. A duplicate **function** did occur here, under one name.
`ui_render` and `ui_status` each computed a `treeHash`. One hashed the raw JSON. The other
hashed the structure. The two tools therefore reported different values for one render.

This analysis cannot see that defect. Both methods were private, so neither was an exported
symbol. The two methods also had different names in different classes. A writer found the
defect by running both tools in one session and comparing the output.

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

**Claims that the gate cannot hold.** The repository `todo.md` records the `treeHash`
incident, and so does the source comment in `UiTools.Render`. The incident is history, and not
a metric. The signature in `TreeRenderer.cs` gives the second claim: the method takes
`ValidatedNode` and returns `UIElement`, so `UiMcp.Rendering` declares no parallel model.
