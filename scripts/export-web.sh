#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT/CerberusExpo"

npm run typecheck
npx expo export --platform web

rm -rf "$ROOT/CerberusService/WebUI"
cp -R "$ROOT/CerberusExpo/dist" "$ROOT/CerberusService/WebUI"
echo "Synced CerberusExpo/dist -> CerberusService/WebUI"
