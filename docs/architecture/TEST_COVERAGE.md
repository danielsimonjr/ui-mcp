# ui-mcp — Test Coverage

**217 tests, 0 failures, 0 skipped** (`dotnet test`, ~6 s). 9 test files, 1,840 lines — 56% of
the repository's source lines are tests.

No line-coverage instrumentation is configured, so this file reports coverage **by component and
by behaviour**, which is the honest thing to report. This document gives no percentage, because
nobody measured one.

## By component

| Component | LOC | Test file | Tests | Assessment |
|---|---|---|---|---|
| `CatalogValidator` + `PropTypes` | 266 | `CatalogValidatorTests.cs` | 24 Fact + 7 Theory | Thorough |
| `PathResolver` | 110 | `PathResolverTests.cs` | 26 Fact + 1 Theory | Thorough |
| `RenderRules` | 90 | `RenderRulesTests.cs` | 25 Fact | Thorough |
| `UiThreadHost` | 130 | `UiThreadHostTests.cs` | 13 Fact | Thorough |
| `UiTools` | 140 | `UiToolsTests.cs` | 15 Fact | Thorough |
| `TreeRenderer` | 268 | `TreeRendererTests.cs` | 26 Fact + 3 Theory | Thorough |
| `UiSurface` | 124 | `UiSurfaceTests.cs` | 12 Fact | Good; window paths need a desktop |
| *validator ↔ renderer seam* | — | `CatalogRendererSeamTests.cs` | 3 Fact + 3 Theory | Exhaustive over the catalog |
| `Program` | 46 | none | 0 | Composition root; exercised by every end-to-end run |

`StaFixture.cs` (26 lines) is shared infrastructure, not a test: one STA thread, built from the
same `UiThreadHost` production uses.

## What is tested well

**Every rejection path is paired with a positive control**, and boundaries are tested on both
sides — 500/501 characters, 64/65 children, depth 11/13, 8/9 table columns. A rejection test that
never proves the accepting case still works is only half a test.

**The tests treat the missing-against-zero rule as a pair**, at every layer where it appears.
A real `0` must display as `"0"`. A missing value must never display as `"0"`. A surface that
has never rendered must report `null` for its node count, and never `0`.

**The most important tool test needs no desktop.** `UiToolsTests` uses a `SpySurface` to assert
the negative — that `Render` was *never called* when a tree is invalid. No screenshot can prove
that.

**The renderer's truncation caps are asserted as APPLIED, not merely declared.** `RenderRules`
tests the constants; `TreeRendererTests` supplies 70 `Repeat` items and 250 `Table` rows and
counts what actually came out. The two are different claims, and only the second can fail
without a warning.

**The validator↔renderer seam is covered exhaustively over the catalog**, not sampled.
`EveryCatalogComponentIsCovered` fails if a tenth component is ever added without a seam case —
without it, a "comprehensive" suite quietly stops being one.

### Everything here has been mutation-proven

A green suite that has never been shown to fail is not evidence. Every mutation below was
reverted and the file verified **byte-identical by SHA-256**.

| Mutation | Result |
|---|---|
| `ForbiddenPathTokens` emptied | exactly 5 failures — 4 prototype-path cases + the nested table-column case |
| Blind spot made to render `"0"` | exactly 2 failures |
| `e.Handled = false` in the supervisor | **the test host process crashed mid-run** |
| Gauge `maxWasRequested` reverted to `false` | exactly 1 — `AGaugeWhoseRequestedMaxDidNotResolveShowsAnEmptyBar` |
| Unknown tone reverted to Amber | exactly 1 — `AToneOutsideTheClosedSetIsMutedRatherThanAlarming` |
| `Repeat`'s `.Take(MaxRepeatItems)` removed | exactly 1 — `RepeatRendersAtMost64Items` |
| `Repeat` stops passing the item as scope | 2 — the targeted renderer test **and** the seam test, from different angles |
| `NodeCount` defaulted to `0` instead of `null` | exactly 1 — `ASurfaceThatHasNeverRenderedReportsNulls_NotZeros` |
| `Describe` returns a constant | exactly 1 — `ADifferentTreeStructureHashesDifferently` |

One mutation **survived**, and that result is worth a record. The mutation dropped the type
name from `Describe`, and changed `n.Type + "("` to `"" + "("`. It broke nothing, because the
two trees under comparison still differ by their child count. The mutation is weak, and the
test is not: the mutation never broke the property under assertion. A constant `Describe`
killed the test immediately. To tell those two cases apart is the whole discipline. A mutation
that survives is not automatically a finding.

## Two defects were found by writing these tests

Both were confirmed against `AdminLTE/JSON-UI/render.js`, which every ported file names as the
source of truth: *"where the two disagree, the JS is right."*

1. **A `Gauge` whose `maxPath` did not resolve drew a bar against a default maximum of 100.** A
   value of 50 against an unreadable maximum showed as **half full**. That bar is a confident
   measurement against a scale that nobody supplied. The JS returns 0 for this case. The root
   cause is a lost difference. "No max requested" and "a max was requested and could not be
   resolved" both arrived at `GaugePercent` as a `null`. Only the caller could tell them apart.

2. **A tone outside the closed set rendered as Amber, the *attention* colour.** That result
   contradicted the method's own summary, which says that an unknown tone is muted. It also
   contradicted the JS (`TONE_CLASS[t] || 'stx-muted'`). The colour manufactures urgency from a
   value that the renderer simply failed to understand. A validated tree cannot reach the case,
   but `Render` is public. An unreachable branch that behaves wrongly is a trap for whoever
   makes it reachable.

Both are fixed, and each fix is pinned by a mutation-proven test. Note that the *absent* tone
case is pinned separately, so the muted fix cannot later be over-applied and repaint every
untoned panel grey.

## The gaps that remain

### 1. `dotnet test` does not run in CI

`.github/workflows/ci.yml` builds on `windows-latest` and gates bundle freshness plus an MCP
`initialize` handshake against the shipped artifact, but never runs the 217 tests. They are a
local habit, not a gate. Recorded in `todo.md`.

### 2. The window-showing tests need an interactive desktop

`UiSurfaceWindowTests` briefly opens and closes real windows, and accounts for ~5.6 s of the ~6 s
run. On a host with no desktop they fail at `UiSurface.Open` — which is the **correct and
informative** failure, not something to paper over with a skip. See SPEC 10.1: the desktop
assumption is retired for the interactive path and explicitly *not* for S4U.

The split between `UiSurfaceTests` and `UiSurfaceWindowTests` is deliberate. `UiSurfaceTests`
needs no window and runs anywhere. The two groups fail for different reasons: one for logic,
the other for the environment. A suite that cannot tell those two apart teaches you to ignore
it.

### 3. `Program.cs` has no direct test

46 lines of composition root. Covered transitively by every end-to-end run of the shipped
binary, which is the only place its wiring is observable at all.

## Verification

Generated 2026-08-15 by `repo_map.py map`.
Regenerate: `python repo_map.py map <repo> --out <dir>` · Check: `python repo_map.py check <repo> --docs docs/architecture`

| Claim | Value | Source |
|---|---|---|
| totalSourceFiles | 21 | dependency-graph.json |
| totalLinesOfCode | 3279 | dependency-graph.json |
| testOnlyFiles | 1 | dependency-graph.json |

**Claims the gate cannot hold:** the **217 tests / 0 failures / ~6 s** figure is `dotnet test`
output. A count of the `Fact` and `Theory` attributes in each test file gives the per-file
numbers. Note that the attribute count and the executed-test count differ, because a `Theory`
runs one time for each `InlineData` row or `MemberData` row. The 1,840 test lines and the per-component LOC come from
`file-inventory.json`'s per-file `loc`. Every mutation result in the table above was produced by
applying that mutation, running the suite, and restoring the file to a verified identical hash.
