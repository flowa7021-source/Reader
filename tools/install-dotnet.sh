#!/usr/bin/env bash
# Installs the .NET 10 SDK into ~/.dotnet so the cross-platform layer can be built and
# unit-tested inside a Linux container (no Windows / WPF needed for these projects).
#
# Wire this into the cloud environment's setup script, or run it once in a fresh container.
# WPF (Foliant.UI/App) and native smoke tests still require Windows — see verify.yml.
set -euo pipefail

if command -v dotnet >/dev/null 2>&1 || [ -x "$HOME/.dotnet/dotnet" ]; then
  echo ".NET SDK already present:"
  "$(command -v dotnet || echo "$HOME/.dotnet/dotnet")" --version
  exit 0
fi

echo "Installing .NET 10 SDK into $HOME/.dotnet ..."
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"

if ! grep -q '.dotnet' "$HOME/.bashrc" 2>/dev/null; then
  echo 'export PATH="$HOME/.dotnet:$PATH"' >> "$HOME/.bashrc"
fi
echo "Done. Run: export PATH=\"\$HOME/.dotnet:\$PATH\""
"$HOME/.dotnet/dotnet" --version
