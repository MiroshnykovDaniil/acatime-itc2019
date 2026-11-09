#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/../data"

if command -v sha256sum >/dev/null 2>&1; then
  sha256sum -c CHECKSUMS.sha256
else
  shasum -a 256 -c CHECKSUMS.sha256
fi
