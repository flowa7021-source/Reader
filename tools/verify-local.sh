#!/usr/bin/env bash
# Local "написал -> собрал -> прогнал" loop for the cross-platform layer (net10.0).
#
# Builds under the strict gate (-warnaserror) and runs the fast unit tests for every
# non-WPF project: Domain, Application, Infrastructure, ViewModels, Engines, DjVu plugin.
# WPF UI (Foliant.UI/App) and native/Windows smoke tests are NOT covered here — they run
# on Windows via .github/workflows/verify.yml. Run this before every commit.
set -euo pipefail

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
DOTNET="dotnet"
command -v dotnet >/dev/null 2>&1 || DOTNET="$HOME/.dotnet/dotnet"
if ! [ -x "$DOTNET" ] && ! command -v "$DOTNET" >/dev/null 2>&1; then
  echo "No .NET SDK found. Run tools/install-dotnet.sh first." >&2
  exit 1
fi

FILTER="Foliant.CrossPlatform.slnf"
cd "$(dirname "$0")/.."

echo "== build $FILTER (net10.0, -warnaserror) =="
# -maxcpucount:1: passing -f to a *solution* build sets TargetFramework globally, which
# breaks project-reference dedup and races on shared outputs (Domain.deps.json). Serialize.
"$DOTNET" build "$FILTER" -c Release -f net10.0 -warnaserror -maxcpucount:1

echo "== test (unit only: skip Slow/Integration/E2E) =="
"$DOTNET" test "$FILTER" -c Release -f net10.0 --no-build \
  --filter "Category!=Slow&Category!=Integration&Category!=E2E"

echo "== OK: cross-platform layer builds clean and unit tests pass =="
