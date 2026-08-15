# ui-mcp — todo

Format: `- [ ] (YYYY-MM-DD) task`. 🟢 READY means unblocked and next.

## Now

- [x] (2026-08-14) Specification written — `docs/SPEC.md`. Architecture, tool contract, catalog,
      safety invariants, testing strategy, v0.1 definition of done, risks.

## Done 2026-08-15

- [x] (2026-08-15) **Desktop assumption — PARTLY retired, and the split matters.** Taken OUT OF
      ORDER, ahead of the scaffold: its own title said EARLY, and a failure would have changed the
      hosting model and therefore the scaffold. Probe: `tools/probe-desktop.ps1`.
      **RETIRED for the production path** (host started by `ResumeStarship`, `LogonType:
      Interactive`): a real WPF window created inside `WindowsMcp`'s process tree returned
      `HWND 1770336` with the OS reporting `IsWindowVisible=True`; control from a `claude.exe` child
      gave a different handle (`3868598`), so neither is a cached result; all 59 live MCP processes
      are in session 1; and a human confirmed seeing the window. Three independent methods.
      **NOT retired for S4U** — the case the risk originally named. `ResumeStarship` is
      `InteractiveToken`, which requires a logged-on session by definition, so a desktop is expected
      there. No task that starts `claude.exe` uses S4U today, so it does not arise; if the host ever
      moves to an S4U trigger, re-run the probe first. See SPEC 10.1.
      **Residual:** the probe proves a *descendant* of an MCP server can draw. ui-mcp will draw
      *in-process*. Children inherit session/window-station/desktop so the gap is small, but it does
      not fully close until `ui_open` puts a real window up.

- [x] (2026-08-15) **Scaffold the solution.** `src/UiMcp` (net9.0-windows10.0.19041.0, `UseWPF`,
      Exe), `src/UiMcp.Abstractions`, `tests/UiMcp.Tests` (xunit + FluentAssertions + Moq),
      `global.json` pinned to 9.0.314 `rollForward: latestFeature` (resolves 9.0.317 here — verified,
      not assumed), `Directory.Build.props` as the single version source, `UiMcp.sln`.
      **Abstractions targets plain `net9.0`, NOT `net9.0-windows`** — the spec says it must not
      reference WPF, and a non-Windows TFM makes that a compiler guarantee instead of a convention.
      `dotnet build`: **0 warnings, 0 errors**. Proven to SERVE, not merely compile: the built exe
      answered a real MCP `initialize` with `{name: ui-mcp, version: 0.1.0, protocol: 2024-11-05}`,
      and that version arrived via Directory.Build.props -> assembly -> `ServerVersion`, so the
      no-hardcoded-version wiring is verified too.

## 🟢 READY — next session, in order
- [x] (2026-08-15) **Catalog + validator, TDD.** All nine components ported from
      `AdminLTE/JSON-UI/catalog.js`. **44 tests, 44 pass, 0 warnings.** Every rejection path is
      paired with a positive control, and boundaries are tested on both sides (500/501 chars,
      64/65 children, depth 11/13, 8/9 table columns).
      **Proven to have teeth by mutation, not by passing:** with `ForbiddenPathTokens` emptied the
      suite failed exactly 5 tests — the 4 prototype-path cases plus the nested table-column case —
      then the file was restored byte-identical (SHA256 verified). A green suite that has never been
      shown to fail is not evidence.
      Two deliberate hardenings over the JS: the prototype check runs **before** the charset check
      (`__proto__` is charset-legal, so a future regex edit could otherwise let it through), and
      `ValidatedNode` is a distinct type from raw JSON so "was this validated?" is a compiler
      question, not a call-site reading exercise.
- [x] (2026-08-15) **Path resolver, TDD.** Ported from `render.js`. Nested keys, array indices,
      consecutive indices (`grid[1][0]`), `$item` scope, prototype refusal. **74 tests total, all
      passing.** Never throws - one bad binding degrades to UNKNOWN in place rather than taking the
      render down.
      **The missing-vs-zero invariant is tested as a PAIR**, because each half alone is worthless:
      a real `0` must display as `"0"`, and a missing value must never display as `"0"`.
      **Mutation-proven:** making a blind spot render as `"0"` failed exactly 2 tests, then restored
      byte-identical (SHA256). I predicted 3 and got 2 — the third (`JsonNull_DisplaysAsUnknown`)
      resolves to a real `JsonElement` of kind `Null` and takes a different switch arm than the
      `v is null` branch I mutated, so 2 is correct. The count corroborated the reading of the code.
      Extra over the JS: the prototype guard is repeated in the resolver even though the catalog
      already refuses those paths - the resolver is reachable from the renderer's own bindings, and
      a guard present at only one of two entry points is a door left open.
- [ ] (2026-08-15) **Re-check the desktop assumption IF the host ever moves to an S4U trigger.**
      Not currently reachable - every task that starts `claude.exe` is `InteractiveToken` today.
      `tools/probe-desktop.ps1` answers it in one run. See SPEC 10.1.
- [x] (2026-08-15) **WPF host on an STA thread** (`UiMcp.Hosting.UiThreadHost`) with `Dispatcher`
      marshalling and a real supervisor. **87 tests, all passing.** Covers: STA apartment, single UI
      thread for all work, 50 concurrent marshals without deadlock (SPEC 7 names this), fail-fast
      after shutdown rather than queueing onto a dead pump, idempotent shutdown, refused double-start.
      **The supervisor test nearly passed for the wrong reason, and finding that was the point.**
      The first two fault tests passed on a FRAMEWORK guarantee - `Dispatcher.InvokeAsync` captures
      exceptions into the returned Task, so awaited work was never going to kill anything. The case
      the spec actually means is a window event handler throwing with nobody awaiting it: that
      reaches `Dispatcher.UnhandledException` and terminates the PROCESS. Added `Post()` to model
      that path, plus a `UnhandledException` handler that marks it handled and records `LastFault`.
      **Mutation-proven, and unusually starkly:** with `e.Handled = false` the TEST HOST PROCESS
      CRASHED mid-run (`Unhandled exception ... window handler blew up`). Restored byte-identical.
      The fault is recorded rather than swallowed so `ui_status` can report a degraded display -
      a supervisor that hides what it absorbed is a silent failure with extra steps.
- [x] (2026-08-15) **Tools:** `ui_open`, `ui_render`, `ui_status`, `ui_close`. **98 tests, all
      passing, 0 warnings on a FULL rebuild.**
      **Verified over the wire, not assumed from the attribute compiling:** `tools/list` returns all
      four with exactly the spec names, and `ui_render`'s schema exposes `tree` + `data`.
      **End-to-end proven:** `ui_open` -> `ui_render` -> `ui_status` driven over stdio produced a
      real window that an INDEPENDENT process observed (`MainWindowTitle: "ui-mcp e2e"`), with
      `ui_status` reporting `windowAlive: true, nodeCount: 3, treeHash: 105e2c486c52, lastFault: none`.
      The most important test is a NEGATIVE and needs no desktop: an invalid tree must be refused
      BEFORE anything touches the window. `IUiSurface` exists so a spy can assert "Render was never
      called", which no screenshot can. Covered: unknown component, unknown prop, malformed tree
      JSON, malformed data JSON, and a partially-invalid tree that must render NOTHING.
      Caveat carried forward: `UiSurface.Render` currently draws a labelled PLACEHOLDER, not the
      catalog visual tree. That is the next item and the placeholder says so in the window itself.
- [x] (2026-08-15) **Renderer** for the nine components. **129 tests, 0 warnings.**
      Split deliberately: every JUDGEMENT lives in `RenderRules` (Abstractions, no WPF, unit-tested
      without a window) - unit suppression on a missing value, delta only when numeric, gauge
      clamping, empty text, the 64/200 caps. `TreeRenderer` is assembly only, thin enough to check
      by reading. Security posture carried over intact: element types come from the renderer only,
      text reaches the UI through `TextBlock.Text` (inert - WPF parses no markup from it), colours
      come from the closed tone enum, lookups go through the guarded resolver.
      **PROVEN AGAINST LIVE DATA:** `examples/starship-view.json` rendered with real
      `briefing.ps1 -Json` output over MCP - 19 nodes, window observed by an independent process.
      **TWO REAL DEFECTS FOUND BY RUNNING IT, neither visible to any unit test:**
      (1) `$item` scope paths were REFUSED by the validator while the resolver implemented them.
          Each component was correct alone; nobody tested the seam. Fixed by accepting `$item` as a
          literal prefix - NOT by adding `$` to the charset, which would have legalised every other
          use of it. **The JS original still has this bug** (see the C:\ tracker entry).
      (2) `ui_render` and `ui_status` reported DIFFERENT `treeHash` values for one render
          (`7d4ef2048c4b` vs `febd32fc836e`) - the tool hashed raw JSON, the surface hashed
          structure. Same name, two functions. Fixed by deleting the duplicate and reading the
          value back from the surface, so they cannot disagree. Now verified equal live.
- [x] (2026-08-15) **Publish + wire — repo side DONE.** `bundle/UiMcp.exe` (28.42 MB, one file),
      `.claude-plugin/plugin.json`, `.mcp.json` with `${CLAUDE_PLUGIN_ROOT}`.
      **Deployment model decided by measurement** (SPEC 10.2): framework-dependent 28.42 MB over
      self-contained 153.7 MB, because `WindowsDesktop.App 9.0.19` is present on BOTH machines and
      the bundle is committed and cache-cloned per version.
      **Tested the artifact that SHIPS, not the dev build:** `bundle/UiMcp.exe` answered a real MCP
      `initialize` with `ui-mcp v0.1.0`.
      **Trap caught before it bit:** `~/.gitignore_global` line 3 ignores `.mcp.json` everywhere, so
      a normal commit would have shipped a plugin that installs and serves NOTHING - and the failure
      would have looked like a broken server, not a missing file. Fixed structurally with a `!` rule
      in the repo `.gitignore` (which takes precedence over `core.excludesFile`) rather than with
      `git add -f`, because a force-add rots the moment anyone commits normally.

- [ ] 🟢 **READY — marketplace registration + live serving check.** The last mile, and it needs
      Daniel for two user-only slash commands.
      1. Add a `plugins[]` entry to `~/Github/skills/.claude-plugin/marketplace.json`
      2. Enable `"ui-mcp@local-marketplace": true` in `~/.claude/settings.json`
      3. `/plugin marketplace update local-marketplace` then `/reload-plugins` (USER-INVOKED ONLY)
      4. **Confirm it SERVES TOOLS in a live session** — installed is not serving. Look for
         `mcp__plugin_ui-mcp_ui-mcp__ui_open` and friends, and call one.
      NOTE: after editing marketplace.json, re-run the consumer that parses it. Valid JSON is not a
      valid manifest - a name sweep once deleted the schema-required `owner` field and broke every
      marketplace refresh for a day, silently.

## Deferred

- [ ] `ui_update` — patch one bound value without a full re-render (v0.2).
- [ ] Multi-agent arbitration. v0.1 is last-write-wins with `ui_status` naming the last renderer.
- [ ] Decide whether ui-mcp replaces the HTML console or sits beside it. Two surfaces rendering one
      tree is fine; two surfaces with different data is the two-sources-of-truth failure.

## Blocked

- [ ] **Publish to GitHub — needs the owner's approval.** The repo is local-only
      (`git init`, no remote). Creating a remote repository is an outward-facing act.
