# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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
