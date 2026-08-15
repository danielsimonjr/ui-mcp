# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Installed as a plugin, and the deployed artifact is verified serving.** Registered in the
  local marketplace as `./ui-mcp` — a symlink to this repo, matching the `episodic-memory`
  precedent, because this repo has no git remote and a `url` source would have nothing to fetch.
  - **The last two steps did not need the user-only slash commands.** `todo.md` recorded them as
    `/plugin marketplace update` + `/reload-plugins`, both user-invoked. The equivalent CLI exists
    and is not gated: `claude plugin marketplace update local-marketplace` followed by
    `claude plugin install ui-mcp@local-marketplace`. The update step also *validates* the
    manifest, which is the "re-run the consumer that parses it" check that note asks for.
  - **Verified against the DEPLOYED exe, not the dev build.** The binary in
    `~/.claude/plugins/cache/local-marketplace/ui-mcp/0.1.0/bundle/UiMcp.exe` (29,799,584 bytes)
    answered a real MCP `initialize` with `{"name":"ui-mcp","version":"0.1.0"}` and advertised
    `ui_open`, `ui_render`, `ui_status`, `ui_close`.
  - **Still not serving in a live session, and that distinction is the point.** The tools are
    absent from the current session: the plugin was registered at 13:43 while the last reload ran
    at ~11:25. Binding needs `/reload-plugins`, which genuinely is user-only. Installed is not
    serving.
  - README gained Build and Install sections; the Build heading no longer says "once implemented".

- **Plugin packaging** — `bundle/UiMcp.exe` (28.42 MB, single file), `.claude-plugin/plugin.json`,
  `.mcp.json` with `${CLAUDE_PLUGIN_ROOT}`.
  - **Deployment model decided by measurement, not preference** (SPEC 10.2). Both were built:
    self-contained **153.7 MB** vs framework-dependent **28.42 MB**. Chose framework-dependent
    because `WindowsDesktop.App 9.0.19` is installed on **both** the ZBOOK and the EVO (verified via
    `dotnet --list-runtimes` on each), and the bundle is committed and cache-cloned per version, so
    125 MB of avoidable payload would be paid on every version on every machine forever. The cost is
    stated in the spec rather than buried: a machine without the runtime cannot run this server.
  - **Tested the artifact that SHIPS.** `bundle/UiMcp.exe` answered a real MCP `initialize` with
    `ui-mcp v0.1.0`. A dev build passing proves nothing about the published one — different runtime,
    different failure modes.
  - **Caught a trap that would have shipped a silently useless plugin.** `~/.gitignore_global`
    line 3 ignores `.mcp.json` **everywhere**. A normal commit would have omitted it, the plugin
    would have installed and served **nothing**, and the symptom would have looked like a broken
    server rather than a missing file. Fixed structurally with a `!.mcp.json` negation in the repo
    `.gitignore` (repo rules take precedence over `core.excludesFile`) rather than with
    `git add -f`, which works once and rots the moment anyone commits normally.

- **The renderer** — `RenderRules` (Abstractions) + `TreeRenderer` (WPF), all nine components.
  **129 tests, 0 warnings.** Rendered live: `examples/starship-view.json` bound to real
  `briefing.ps1 -Json` output, 19 nodes, window confirmed by an independent process.
  - **Judgement and drawing are separated on purpose.** Unit suppression on a missing value, delta
    only when numeric, gauge clamping, empty text, the 64/200 caps — all in `RenderRules`, testable
    with no window. `TreeRenderer` is assembly only, thin enough to verify by reading.
  - `MetricText` omits the unit when the value is missing: *"UNKNOWN live"* implies a measured
    quantity in some unit when there is no quantity at all.
  - `DeltaText` returns **null** for a missing or non-numeric delta rather than `0` — "unchanged"
    and "not measured" are different claims.
  - A missing gauge reads 0% because a bar must have some length, so the **label** carries UNKNOWN.
    A zero-length bar with no UNKNOWN beside it is the green-zero failure again.
  - Security posture carried over intact: element types come from the renderer only, text reaches
    the UI via `TextBlock.Text` (inert — WPF parses no markup from it, the structural equivalent of
    "textContent, never innerHTML"), colours come from the closed tone enum, lookups go through the
    guarded resolver.

### Fixed

- **`$item` scope paths were refused by the validator while the resolver implemented them.**
  `PropTypes.Path`'s charset excluded `$`; `PathResolver` documented and implemented a `$item`
  prefix. Each component was correct in isolation and no unit test covered the seam between them —
  it surfaced only on the first real end-to-end render. Fixed by accepting `$item` as a **literal
  prefix**, not by adding `$` to the charset, which would have legalised every other use of it in
  one character of diff. `$other`, `$`, `a$b` and `$items.x` stay refused, and `$item.__proto__`
  still hits the prototype guard because that check runs against the whole string first.
  **The JS original carries the identical bug:** `AdminLTE/JSON-UI/catalog.js` refuses `$` while
  `render.js` implements `$item`, and `view.json:332` uses `"valuePath": "$item"` — so the HTML
  console refuses its own view tree. Tracked separately; not fixed here.

- **`ui_render` and `ui_status` reported different `treeHash` values for the same render**
  (`7d4ef2048c4b` vs `febd32fc836e`): the tool hashed the raw JSON text, the surface hashed the
  structure. One field name, two functions, two answers — the second-source-of-truth defect.
  `ui_render` now reads the hash **back from the surface**, so they cannot disagree. Deleting the
  duplicate rather than syncing it, because syncing re-arms the drift. Verified equal live.

### Added

- **The four MCP tools** — `ui_open`, `ui_render`, `ui_status`, `ui_close` — plus `IUiSurface` and
  the WPF `UiSurface`. **98 tests, all passing, 0 warnings on a full rebuild.**
  - **`ui_render` validates the WHOLE tree before anything touches the window.** A partially
    rendered invalid tree is worse than none: it looks like a working display while silently
    omitting what failed. Tested with a partially-invalid tree that must render **nothing at all**.
  - **`IUiSurface` exists so the most important test is a negative that needs no desktop.** A spy
    can assert "Render was never called"; a screenshot cannot. Covers unknown component, unknown
    prop, malformed tree JSON, and malformed data JSON — all refusals, never thrown faults.
  - **`ui_status` reports UNKNOWN for anything unmeasured**, never a fabricated zero, and surfaces
    an absorbed UI fault rather than hiding it. It reads the LIVE window state instead of a cached
    flag, because the user can close the window at any moment and a status reporting our last
    *intention* would be confidently wrong.
  - **Verified over the wire, not inferred from the attribute compiling:** `tools/list` returns all
    four with exactly the spec names and `ui_render`'s schema exposes `tree` + `data`. An end-to-end
    drive over stdio produced a real window an **independent process** observed
    (`MainWindowTitle: "ui-mcp e2e"`), with `ui_status` reporting `windowAlive: true, nodeCount: 3,
    treeHash: 105e2c486c52, lastFault: none`.
  - `UiSurface` starts the UI thread **lazily on first open**, so a host with no desktop fails where
    a window was actually requested and can say so, rather than at launch.
  - **Known and labelled:** `UiSurface.Render` currently draws a placeholder that says
    "renderer pending" in the window itself. The catalog visual tree is the next task; the
    placeholder announces what it is rather than implying a finished renderer.

- **Fixed 7 `xUnit1031` warnings** (blocking task operations in tests) by making the host tests
  async throughout. Not cosmetic: that suite exists to prove a marshalling boundary does not
  deadlock, so a test that blocks a thread waiting on that boundary is the one place the shortcut
  could manufacture the failure it claims to rule out. **These had been reported as "0 warnings"
  earlier — incorrectly.** Incremental builds were not recompiling the test project;
  `--no-incremental` surfaced them.

- **`UiMcp.Hosting.UiThreadHost`** — the single STA thread WPF requires, and the only route from a
  tool handler to the UI. **87 tests, all passing, 0 warnings.** SPEC section 3's "two threads, one
  direction": handlers marshal and get a Task; the UI thread never waits on MCP.
  - Verified: STA apartment, all work on one UI thread, **50 concurrent marshals with no deadlock**
    (SPEC section 7 names this case), fail-fast after shutdown instead of queueing onto a dead pump,
    idempotent shutdown, refused double-start.
  - **The supervisor almost passed for the wrong reason.** The first fault tests were satisfied by a
    framework guarantee — `Dispatcher.InvokeAsync` captures exceptions into the returned Task, so
    awaited work was never at risk. The case the spec means is a **window event handler throwing
    with nobody awaiting it**, which reaches `Dispatcher.UnhandledException` and terminates the
    process. Added `Post()` to model that path and a handler that marks it handled.
  - **Mutation-proven, starkly:** with `e.Handled = false` the **test host process crashed** mid-run
    (`Unhandled exception ... window handler blew up`) and the run aborted. Restored byte-identical.
    That is precisely the failure the supervisor exists to prevent: a dead window is a degraded
    display, a dead process is an outage, and they must not be the same event.
  - `LastFault` records what was absorbed so `ui_status` can report a degraded display honestly.
    A supervisor that hides the fault it caught is a silent failure with extra steps.
  - The UI thread is a **background** thread: a foreground one would keep the process alive after
    MCP shutdown, turning a clean exit into a hang that looks like an ignored SIGTERM.

- **Path resolver and display formatter** (`UiMcp.Abstractions.PathResolver`), ported from
  `AdminLTE/JSON-UI/render.js`. Nested keys, array indices, consecutive indices (`grid[1][0]`),
  `$item` scope, prototype refusal. **74 tests total, all passing, 0 warnings.**
  - **An unresolvable path returns missing and formats as `UNKNOWN` — never `0`, never `""`.**
    SPEC section 6 names this the most expensive recurring failure on this machine: a section that
    could not be measured, shown as a green zero, reads as health. The tests assert it as a **pair**
    (a real `0` displays as `"0"`; a missing value never displays as `"0"`) because either half
    alone is satisfiable by a broken implementation.
  - **Mutation-proven.** Making a blind spot render as `"0"` failed exactly 2 tests; the file was
    then restored byte-identical (SHA256). The prediction was 3 — the third case resolves to a real
    `JsonElement` of kind `Null` and takes a different branch than the mutated one, so 2 is the
    correct number and the count corroborated the reading of the code rather than contradicting it.
  - **Never throws.** A missing value is a display problem, not a crash; one bad binding in a
    sixty-node tree degrades to `UNKNOWN` in place instead of taking the console down.
  - JSON `null` also formats as `UNKNOWN`: a key present but null carries no more information than
    an absent key, and treating them differently puts a confident-looking blank on a status board.
  - Numbers format with `"R"` under `InvariantCulture` — a comma decimal separator on a
    differently-configured machine would silently change every number on the display.
  - The prototype guard is repeated here even though the catalog already refuses such paths: the
    resolver is reachable from the renderer's own bindings, and a guard present at only one of two
    entry points is a door left open.

- **Catalog and validator** (`UiMcp.Abstractions`): all nine components ported from
  `AdminLTE/JSON-UI/catalog.js` — `StatusBanner` · `Panel` · `Row` · `Metric` · `Field` · `Gauge` ·
  `Repeat` · `Table` · `Note`. Enforces the SPEC section 6 invariants: unknown component and unknown
  prop are **refused, not ignored**; tone is a closed set; paths are charset-restricted and refuse
  `__proto__` / `constructor` / `prototype`; depth capped at 12, children at 64, table columns at 8.
  **44 tests, 44 passing, 0 warnings.**
  - **Every rejection test is paired with a positive control**, and boundaries are tested on both
    sides (500 vs 501 chars, 64 vs 65 children, depth 11 vs 13, 8 vs 9 columns). A validator that
    refuses everything passes a naive rejection suite perfectly.
  - **The suite was proven able to fail, not merely observed passing.** Emptying
    `ForbiddenPathTokens` broke exactly 5 tests — the 4 prototype-path cases plus the nested
    table-column case — and the file was then restored byte-identical (SHA256 verified). A green
    suite that has never been shown to go red is not evidence that it checks anything.
  - Two deliberate hardenings over the JS original: the **prototype check runs before the charset
    check** (`__proto__` is charset-legal, so ordering the charset rule first would let a future
    regex edit quietly reopen the hole), and **`ValidatedNode` is a distinct type from raw JSON**, so
    "has this been validated?" is answered by the compiler rather than by reading call sites.
  - Column paths are validated through the same `Path` rule as top-level paths, so the prototype
    guard reaches *inside* the array — nested paths are exactly where a boundary check gets skipped.
  - `UiValidationException` is its own type so the MCP layer can report a deliberate refusal
    verbatim instead of letting the SDK flatten it to "An error occurred invoking '&lt;tool&gt;'",
    which would hide the guard at the moment it did its job.

- **C# solution scaffold**, mirroring `Windows-mcp`: `src/UiMcp` (net9.0-windows10.0.19041.0,
  `UseWPF`, Exe), `src/UiMcp.Abstractions`, `tests/UiMcp.Tests` (xunit + FluentAssertions + Moq),
  `global.json`, `Directory.Build.props`, `UiMcp.sln`. Builds **0 warnings, 0 errors**.
  - `UiMcp.Abstractions` targets plain **`net9.0`**, not `net9.0-windows`. The spec requires it not
    to reference WPF; a non-Windows TFM makes that a **compiler guarantee** rather than a convention
    someone must remember. A stray `using System.Windows;` now fails the build.
  - **Version has exactly one source.** `Directory.Build.props` holds it; `Program.ServerVersion`
    reads it back off the assembly. Verified live, not assumed - the server reported `0.1.0` over
    the wire. Windows-mcp shipped a hardcoded `"0.4.1"` through three releases, and a server that
    misreports its version makes a stale-bundle deploy invisible.
  - Logging is pinned to **stderr**. Stdout is the MCP transport; a stray log line there corrupts
    the protocol and surfaces as an opaque client-side parse error.
  - **Proven to SERVE, not merely to compile.** The built exe answered a real MCP `initialize`:
    `{name: ui-mcp, version: 0.1.0, protocol: 2024-11-05, capabilities: present}`. Tool set is
    deliberately empty at this stage.

- **`tools/probe-desktop.ps1`** - retires the SPEC section 10 desktop risk by measurement.
  Creates a real WPF window, resolves its native `HWND`, and asks the OS `IsWindowVisible`.
  Ships with a mandatory control run, because a probe that cannot produce a positive proves
  nothing when it reports a negative. It earned that design on its first execution: a raw
  `System.Threading.Thread` running a PowerShell ScriptBlock throws *"no Runspace available to run
  scripts in this thread"*, which is **indistinguishable from "no desktop"**. The control caught it;
  the fix is an STA `Runspace`, which carries both the apartment and an execution context.

### Changed

- **SPEC section 10 risk 1 is now PARTLY RETIRED, with the split recorded rather than summarised**
  (new section 10.1). Retired for the production path: host started by `ResumeStarship`
  (`LogonType: Interactive`); window created inside `WindowsMcp`'s process tree, `HWND 1770336`,
  OS-confirmed visible, control from a `claude.exe` child gave a different handle (`3868598`), all
  59 live MCP processes in session 1, and a human confirmed seeing it. **Still open for S4U** - the
  case the risk actually named. `InteractiveToken` requires a logged-on session by definition, so a
  desktop is expected there; S4U runs with nobody logged on and is the case that plausibly has none.
  No task starting `claude.exe` uses S4U today, so it does not currently arise.
  **Residual:** the probe proves a *descendant* of an MCP server can draw; ui-mcp will draw
  in-process. Children inherit session, window station and desktop, so the gap is small - but it
  does not close entirely until `ui_open` shows a real window.

- **Specification** (`docs/SPEC.md`) for an MCP server that hosts a native WPF window and renders
  catalog-constrained JSON UI trees. Covers architecture, the five-tool contract, the nine-component
  catalog, seven safety invariants, testing strategy, a v0.1 definition of done, and four named
  risks with mitigations.
- **README** describing purpose, the threading rationale for C# over PowerShell, the safety model,
  and the relationship to `Windows-mcp`, `json-render`, and the existing HTML/PowerShell consoles.
- **`todo.md`** with the ordered build sequence.

### Notes

- No implementation yet. This release is specification only.
- The design is not greenfield: a working JS renderer (`AdminLTE/JSON-UI/`) and a working WPF
  renderer (`~/.claude/scripts/starship-console.ps1`) already exist and are proven against live
  data from two machines. The C# port is transcription, not design. Where the spec and that code
  disagree, **the code is right and the spec is stale**.
- The desktop-availability risk (can a stdio MCP server create a visible window?) is scheduled to
  be retired **before** the UI is built. It is the cheapest risk to test and the most expensive to
  discover late.
