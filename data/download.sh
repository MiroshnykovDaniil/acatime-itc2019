#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

base="https://ndownloader.figshare.com/files"
files=(
  "35662994 tg-fal17_reduced.xml"
  "35663216 tg-spr18_reduced.xml"
  "35663195 lums-fal17_reduced.xml"
  "35663075 lums-spr18_reduced.xml"
  "35662964 iku-fal17_reduced.xml"
)

for entry in "${files[@]}"; do
  read -r id name <<<"$entry"
  if [ -f "$name" ]; then
    echo "exists: $name"
  else
    echo "download: $name"
    curl -fL --retry 3 --retry-delay 2 "$base/$id" -o "$name"
  fi
done

if command -v sha256sum >/dev/null 2>&1; then
  sha256sum -c CHECKSUMS.sha256
else
  shasum -a 256 -c CHECKSUMS.sha256
fi
