#!/usr/bin/env node

const { spawn } = require("node:child_process");
const path = require("node:path");

if (process.platform !== "win32" || process.arch !== "x64") {
  console.error("ui-mcp is only supported on Windows x64.");
  process.exit(1);
}

const exePath = path.resolve(__dirname, "..", "bundle", "UiMcp.exe");
const child = spawn(exePath, process.argv.slice(2), {
  stdio: "inherit"
});

child.on("error", (error) => {
  console.error(`Failed to start ${exePath}: ${error.message}`);
  process.exit(1);
});

child.on("exit", (code, signal) => {
  if (signal) {
    console.error(`ui-mcp exited due to signal ${signal}.`);
    process.exit(1);
  }

  process.exit(code ?? 1);
});
