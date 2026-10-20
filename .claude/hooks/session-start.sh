#!/bin/bash
# Restores and builds the solution so tests run straight away in Claude Code
# cloud sessions. The image already ships the .NET 10 SDK that global.json asks for.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "$CLAUDE_PROJECT_DIR"
dotnet restore
dotnet build --no-restore
