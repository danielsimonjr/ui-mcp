# ui-mcp — specification

An MCP server that hosts a native Windows window and renders catalog-constrained JSON UI trees.
Any agent connected to the host can draw to it; nothing else can.

Status: **specification. No code yet.** Written in ASD-STE100 Simplified Technical English.

---

## 1. Purpose

Agents on this machine can measure the system and report in text. They cannot show anything. The
existing consoles prove the data layer works but each has a defect:

| Surface | Defect |
|---|---|
| HTML page (`danielsimonjr/JSON-UI/`) | Needs a browser. A tab gets closed and buried. |
| WPF script (`starship-console.ps1`) | One blocking `ShowDialog()`. **No agent can drive it.** |

ui-mcp fixes the second. The window becomes a **shared surface** addressable by tools, so the
Starship, Github and EVO agents can all render to one display.

---

## 2. Why C# and not PowerShell

**Threading is the deciding argument, not preference.**

WPF requires an STA thread with a message pump. MCP requires async stdio that never blocks. In
PowerShell those two cannot coexist cleanly: `ShowDialog()` blocks the only thread, so the script
can run timers but cannot serve requests. In C# the UI runs on a dedicated STA thread and every
update is marshalled through `Dispatcher.InvokeAsync`. The two never touch.

Supporting reasons, in order of weight:

1. **Lifetimes already match.** An MCP server is a long-lived process. So is a window.
2. **A proven template exists here.** `Windows-mcp` is C#, ships `bundle/WindowsMcp.exe`, and
   deploys through `.claude-plugin/plugin.json` + `.mcp.json` with `${CLAUDE_PLUGIN_ROOT}`.
3. **The catalog becomes a typed contract** — C# records plus `System.Text.Json` validation. This
   is the guarantee `json-render` gets from Zod, expressed in the host's own language.

---

## 3. Architecture

```
  agent (any)
      | MCP stdio (JSON-RPC)
      v
  +-------------------+        marshal        +----------------------+
  |  McpServer        | --------------------> |  STA UI thread       |
  |  tool handlers    |  Dispatcher.Invoke    |  WPF Window          |
  |  CatalogValidator |                       |  TreeRenderer        |
  +-------------------+                       +----------------------+
      ^
      | reads (never executes)
  data.json  <- starship-dashboard.ps1 <- briefing.ps1 -Json  (both machines)
```

**Two threads, one direction.** Tool handlers never touch WPF objects directly. The UI thread never
blocks on MCP. Every cross-boundary call is an explicit marshal.

**The renderer is a pure function of (tree, data).** Given the same inputs it produces the same
visual output. That is what makes it testable without a window (§7).

---

## 4. Tool contract

| Tool | Input | Returns | Notes |
|---|---|---|---|
| `ui_open` | `title?`, `topmost?`, `width?`, `height?` | window id | Idempotent. A second call focuses the existing window. |
| `ui_render` | `tree` (catalog JSON), `data?` | `{ok, rejected?}` | Full replace. Validates BEFORE touching the UI. |
| `ui_update` | `path`, `value` | `{ok}` | Patch one bound value without a full re-render. |
| `ui_status` | — | window state, last render, tree hash | Must report **UNKNOWN** for anything it cannot measure. |
| `ui_close` | — | `{ok}` | Window closes; server stays up. |

**`ui_render` validates the whole tree first and renders only if all of it passed.** A partially
rendered invalid tree is worse than none: it looks like a working display while silently omitting
what failed.

---

## 5. Catalog

Nine components, ported from the proven JS catalog. Adding one is a deliberate act; if it is not in
the catalog it cannot render.

`StatusBanner` · `Panel` · `Row` · `Metric` · `Field` · `Gauge` · `Repeat` · `Table` · `Note`

Prop types: `text` (max 500 chars) · `num` (finite) · `bool` · `path` (restricted charset) ·
`tone` (closed enum: nominal, attention, critical, degraded, muted, info) · `enum`.

---

## 6. Safety invariants

Carried unchanged from the JS renderer. Each exists because of a real incident.

1. **The tree is data to validate, never instructions to obey.** Control-plane / data-plane
   separation; OWASP LLM01. A wake-on-boot design violated this by telling an agent to read a
   writable file and "do exactly what it says". A guardrail refused it five times before the flaw
   was seen.
2. **Unknown component or prop is REFUSED, not ignored.** Silent ignores hide typos, and a typo in
   a security boundary is how things get through.
3. **No value from JSON becomes a type name, member name, or executable text.** Element types come
   from the renderer only.
4. **Tone maps to a colour in the renderer.** The tree cannot supply a style, class, or colour.
5. **A value that cannot be resolved renders as UNKNOWN, never as 0.** A blind spot shown as a
   green zero reads as health. This is the most expensive recurring failure on this machine.
6. **Path resolution refuses `__proto__`, `constructor`, `prototype`.**
7. **Depth capped at 12, children at 64, table rows at 200.**

---

## 7. Testing strategy

**Testable without a window** — this is most of the value, and it is deliberate:

- `CatalogValidator`: every component, every prop type, every rejection path.
- Path resolver: nested objects, array indices, `$item` scope, prototype-access refusal, missing
  path returns null (NOT zero, NOT empty string).
- Tree normalisation: depth cap, child cap, unknown prop, missing required prop.

**Needs a window** — kept to a thin, explicitly-listed set:

- `ui_open` creates a window and `ui_status` reports it.
- Dispatcher marshalling does not deadlock under concurrent `ui_render` calls.

**A positive control is required for every rejection test.** A validator that rejects everything
passes a naive "it rejected the bad input" suite. Each rejection test is paired with a valid case
that must pass, so the test can distinguish "correctly refused" from "refuses everything".

---

## 8. Project layout — mirrors Windows-mcp

```
ui-mcp/
  src/UiMcp/                 net9.0-windows, <UseWPF>true</UseWPF>, OutputType Exe
  src/UiMcp.Abstractions/    catalog records, validation result types (no WPF reference)
  tests/UiMcp.Tests/         xunit + FluentAssertions
  bundle/UiMcp.exe           self-contained publish output, committed
  .claude-plugin/plugin.json
  .mcp.json                  ${CLAUDE_PLUGIN_ROOT}/bundle/UiMcp.exe
  global.json                SDK pin, rollForward latestFeature
  docs/SPEC.md               this file
```

**`UiMcp.Abstractions` must not reference WPF.** That is what keeps the validator and the path
resolver testable on any runner, and it is the seam a future headless or terminal renderer would
use.

Packages: `ModelContextProtocol` 2.2.0 (MCP `2026-07-28`) · `Microsoft.Extensions.Hosting` 9.* ·
`Microsoft.Extensions.Logging.Console` 9.*.

---

## 9. v0.1 definition of done

Not shippable until every line is true:

- [ ] `ui_open`, `ui_render`, `ui_status`, `ui_close` implemented (`ui_update` may slip to v0.2)
- [ ] All nine catalog components render
- [ ] Validator rejects: unknown component, unknown prop, missing required prop, bad prop type,
      over-depth, over-width, prototype path
- [ ] Every rejection test paired with a passing positive control
- [ ] Renders the real `data.json` from both machines, and the EVO block shows UNKNOWN when the
      peer is unreachable
- [ ] `dotnet test` green, `dotnet build` warning-clean
- [ ] `bundle/UiMcp.exe` published and launched from a clean shell
- [ ] Registered in the marketplace and **confirmed serving tools in a live session** — installed
      is not the same as serving

---

## 10. Risks and open questions

| Risk | Mitigation |
|---|---|
| ~~A stdio MCP server may have no desktop to draw on~~ **PARTLY RETIRED 2026-08-15 — see 10.1** | Measured, not assumed. Retired for the production path; the S4U case named below is still open. |
| Two agents render to one window and fight | v0.1: last write wins, and `ui_status` reports which agent rendered last. Arbitration is a v0.2 question. |
| WPF crash takes the MCP server down | Host the UI thread with a supervisor; a dead window must not kill tool serving. `ui_status` reports `windowAlive=false`. |
| ~~Bundled exe grows large (self-contained + WPF)~~ **MEASURED AND DECIDED 2026-08-15 — see 10.2** | Framework-dependent, 28.42 MB. Self-contained measured 153.7 MB and was rejected. |

### 10.1 Desktop availability — measured 2026-08-15

Retired for the production path, still open for one case. The distinction matters, so both are
recorded rather than one summary verdict.

| Case | Status |
|---|---|
| Host started by `ResumeStarship` (**`LogonType: Interactive`**) | **RETIRED.** |
| Host started under **S4U** — the case this risk originally named | **NOT TESTED.** |

**Evidence for the retired case.** A probe (`tools/probe-desktop.ps1`) created a real WPF window,
resolved its native `HWND`, and asked the OS `IsWindowVisible`:

- **Control**, from a child of `claude.exe`: `HWND 3868598`, visible `True`.
- **Test**, run inside `WindowsMcp`'s process tree (a real stdio MCP server, running unelevated):
  `HWND 1770336`, visible `True`. Different handle, so not a cached result.
- All 59 live MCP server processes are in **session 1**, not session 0.
- The window was also seen by a human. Three independent methods agree.

**Why S4U is still open.** `ResumeStarship` is `InteractiveToken`, which requires a logged-on
session by definition, so a desktop is expected there. S4U runs whether or not anyone is logged on
and is the case that plausibly has none. Measured ancestry:
`svchost (session 0) -> pwsh (session 1) -> claude (session 1)`. No task that starts `claude.exe`
uses S4U today, so the case does not currently arise. **If the host is ever moved to an S4U
trigger, re-run the probe before assuming the window still appears.**

**Residual, stated rather than glossed:** the probe proves a *descendant* of an MCP server can
create a window. ui-mcp will create it *in its own process*. Session, window station and desktop
are inherited by children, so the gap is small - but it is a gap, and it closes for good the first
time `ui_open` puts a real window on screen.

### 10.2 Deployment model — decided 2026-08-15

**Decision: framework-dependent single file.** Both options were built and measured rather than
argued about.

| Option | Bundle | Requires |
|---|---|---|
| Self-contained | **153.7 MB** | nothing |
| **Framework-dependent (chosen)** | **28.42 MB** | .NET Desktop Runtime 9 |

**Why.** 5.4x smaller, and the dependency is already satisfied: `WindowsDesktop.App 9.0.19` is
installed on **both** the ZBOOK and the EVO (verified with `dotnet --list-runtimes` on each). The
bundle is committed to git and the plugin cache clones it per version, so 125 MB of avoidable
payload would be paid on every version, on every machine, forever.

**The cost, stated rather than buried.** A machine without the .NET 9 Desktop Runtime cannot run
this server. That is acceptable here because ui-mcp is WPF: it already requires Windows with a
desktop, so the set of machines that could run a self-contained build but not this one is small.
The failure is also legible - the .NET host prints an explicit "you must install the .NET Desktop
Runtime" message rather than failing silently.

**Revisit if** ui-mcp is ever deployed to a machine outside this pair, or if a runtime-version
mismatch appears. Switching back is a one-flag change to the publish command.

---

**Open, needs a decision before v0.2:** whether ui-mcp should replace the HTML console or sit
beside it. Two surfaces rendering one tree is fine; two surfaces with different data is the
two-sources-of-truth failure.

---

## 11. What already exists as the executable specification

This is not a greenfield design. The following are working and proven against live data from two
machines, and port to C# as transcription rather than design:

- `danielsimonjr/JSON-UI/catalog.js` — the nine components and their prop types
- `danielsimonjr/JSON-UI/render.js` — tree walk, path resolver, UNKNOWN handling
- `danielsimonjr/JSON-UI/view.json` — a real tree, 20 bound paths, all resolving
- `~/.claude/scripts/starship-console.ps1` — the same renderer against WPF primitives
- `~/.claude/scripts/starship-dashboard.ps1` — the data producer, both machines

The PowerShell console is the reference implementation. Where this spec and that code disagree,
the code is right and this document is stale.
