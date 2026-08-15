# ui-mcp — Test Coverage

**132 tests, 0 failures, 0 skipped** (`dotnet test`, 267 ms). 5 test files, 984 lines —
41% of the repository's source lines are tests.

No line-coverage instrumentation is configured, so this file reports coverage **by component
and by behaviour**, which is the honest thing to report. It does not report a percentage,
because none was measured.

## By component

| Component | LOC | Test file | Tests | Assessment |
|---|---|---|---|---|
| `CatalogValidator` + `PropTypes` | 259 | `CatalogValidatorTests.cs` | 24 Fact + 7 Theory | Thorough |
| `PathResolver` | 110 | `PathResolverTests.cs` | 26 Fact + 1 Theory | Thorough |
| `RenderRules` | 71 | `RenderRulesTests.cs` | 21 Fact | Thorough |
| `UiThreadHost` | 130 | `UiThreadHostTests.cs` | 13 Fact | Thorough |
| `UiTools` | 140 | `UiToolsTests.cs` | 15 Fact | Thorough |
| **`TreeRenderer`** | **248** | **none** | **0** | ⚠ **gap — see below** |
| **`UiSurface`** | **124** | **none** | **0** | ⚠ **gap — see below** |
| `Program` | 46 | none | 0 | Composition root; exercised by every e2e run |

## What is tested well

**Every rejection path is paired with a positive control**, and boundaries are tested on both
sides — 500/501 characters, 64/65 children, depth 11/13, 8/9 table columns. A rejection test
that never proves the accepting case still works is only half a test.

**The missing-vs-zero invariant is tested as a pair**, because each half alone is worthless: a
real `0` must display as `"0"`, *and* a missing value must never display as `"0"`.

**The most important test needs no desktop.** `UiToolsTests` uses a `SpySurface` implementing
`IUiSurface` to assert the negative — that `Render` was *never called* when a tree is invalid.
No screenshot can prove that. Covered: unknown component, unknown prop, malformed tree JSON,
malformed data JSON, and a partially-invalid tree that must render **nothing**.

**The suites have been mutation-proven, not merely observed passing.** A green suite that has
never been shown to fail is not evidence. Three deliberate mutations, each reverted
byte-identical (SHA-256 verified):

| Mutation | Result |
|---|---|
| `ForbiddenPathTokens` emptied | exactly 5 failures — 4 prototype-path cases plus the nested table-column case |
| Blind spot made to render `"0"` | exactly 2 failures |
| `e.Handled = false` in the supervisor | **the test host process crashed mid-run** |

The second is worth keeping for the reasoning, not the number: 3 failures were predicted and 2
occurred. The third case (`JsonNull_DisplaysAsUnknown`) resolves to a real `JsonElement` of kind
`Null` and takes a different switch arm from the `v is null` branch that was mutated — so 2 was
correct, and the count *corroborated* the reading of the code rather than contradicting it.

**The supervisor test nearly passed for the wrong reason.** The first two fault tests passed on
a framework guarantee — `Dispatcher.InvokeAsync` captures exceptions into the returned `Task`,
so awaited work was never going to kill anything. `Post()` was added to model the case the spec
actually means: a window event handler throwing with nobody awaiting it.

## The gaps that matter

### 1. `TreeRenderer` has no unit tests — 248 lines, the largest source file

The design intent was that all *judgement* lives in `RenderRules` (thoroughly tested with no
WPF), leaving `TreeRenderer` "thin enough to check by reading". At 248 lines across nine
component builders, it is no longer thin.

What is consequently unverified by any automated test:

- The `switch` dispatches each of the nine component names to the right builder.
- `Repeat` truncates at 64 and `Table` at 200 — `RenderRules.MaxRepeatItems`/`MaxTableRows`
  are tested as *constants*, but nothing asserts the renderer applies them.
- `Repeat` passes the correct `$item` scope to each child.
- `Table` resolves each column path against the row rather than the root.
- The `tone` → brush mapping, and that unresolved values get the distinct `UNKNOWN` brush.
- `Row` distributes children across `cols` columns.

**What does cover it:** manual end-to-end renders, most substantially
`examples/starship-view.json` driven with real `briefing.ps1 -Json` output — 19 nodes, window
observed by an independent process. That is real evidence, but it is a *manual* run, it is not
in CI, and it asserts nothing automatically.

This gap is also where the two defects found by running the system lived — neither was visible
to any unit test:

1. **`$item` paths were refused by the validator while the resolver implemented them.** Each
   component was correct in isolation; nobody tested the seam.
2. **`ui_render` and `ui_status` reported different `treeHash` values** for one render — the
   tool hashed raw JSON, the surface hashed structure.

Both are now fixed and pinned, but the lesson stands: **the seams between correct components
are where the untested defects were**, and `TreeRenderer` is the largest remaining untested
seam.

### 2. `UiSurface` has no unit tests — 124 lines

Needs a real window and an STA pump, so it is genuinely harder. Unverified automatically:
`Open` idempotency (focus rather than a second window), the `Window.Closed` handler clearing
the references, `Render`'s auto-open, the `ScrollViewer` wrapping, and `Status` reading live
window state rather than a cached flag.

`UiThreadHost` — the hard concurrency part underneath it — **is** thoroughly tested, including
50 concurrent marshals without deadlock. So the gap is the WPF-object handling on top, not the
threading model.

### 3. No CI runs the tests

`.github/workflows/ci.yml` builds on `windows-latest` and gates the bundle's freshness plus an
MCP `initialize` handshake against the shipped artifact. Adding `dotnet test` to it would make
the 132 tests a gate rather than a local habit.

## Recommended next tests, in priority order

1. **`TreeRendererTests`** for the truncation caps and `$item` scoping — the two behaviours
   with real data-dependent limits and the ones a reader cannot verify by inspection.
2. **A seam test** that walks every catalog component through validate → render, which is the
   class of defect that has actually occurred here twice.
3. **`dotnet test` in CI.**
4. `UiSurface` tests behind an STA-gated fixture, lowest priority — `UiThreadHost` already
   covers the part most likely to break.

## Verification

Generated 2026-08-15 by `repo_map.py map`.
Regenerate: `python repo_map.py map <repo> --out <dir>` · Check: `python repo_map.py check <repo> --docs docs/architecture`

| Claim | Value | Source |
|---|---|---|
| totalSourceFiles | 17 | dependency-graph.json |
| totalLinesOfCode | 2376 | dependency-graph.json |
| testOnlyFiles | 1 | dependency-graph.json |

**Claims the gate cannot hold:** the **132 tests / 0 failures / 267 ms** figure is `dotnet test`
output. Per-file `[Fact]`/`[Theory]` counts were obtained by counting those attributes in each
test file — note 99 `Fact` + 8 `Theory` attributes expand to 132 executed cases, because a
`Theory` runs once per `InlineData`. The **absence** of `TreeRenderer` and `UiSurface` tests was
verified by two methods: no `TreeRendererTests.cs`/`UiSurfaceTests.cs` exists, and a grep for
`TreeRenderer|UiSurface` across `tests/` returns exactly one hit — `SpySurface : IUiSurface`,
which is a stand-in for the surface, not a test of it. The mutation-testing results are recorded
in the repository `todo.md` from the sessions that performed them.
