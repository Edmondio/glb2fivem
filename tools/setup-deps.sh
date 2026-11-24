#!/usr/bin/env bash
# Récupère les sources des dépendances (versions figées) dans third_party/.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
tp="$root/third_party"
mkdir -p "$tp"

deps=(
  "CodeWalker https://github.com/dexyfex/CodeWalker 485d56bec00262ed7fa472261cce7bbc6202b96e"
  "SharpDX https://github.com/sharpdx/SharpDX ab36f12303e24aa60fe804866617716b6ded95db"
  "SharpGLTF https://github.com/vpenades/SharpGLTF 0c01536ff919209d56426897f5605023c2445dbf"
  "StbImageSharp https://github.com/StbSharp/StbImageSharp 274a325dafc7dd1ddb715c36b32a23fd18b2d9ab"
  "meshoptimizer https://github.com/zeux/meshoptimizer 8547fc321b782a2235dfc14c65a346a640544cd8"
  "miniPaint https://github.com/viliusle/miniPaint c9192c15918eb61f7dd89e362cf8065a276b89e9"
)

for d in "${deps[@]}"; do
  read -r name url commit <<<"$d"
  dir="$tp/$name"
  if [ ! -d "$dir/.git" ]; then
    echo "==> clone $name"
    git init -q "$dir"
    git -C "$dir" remote add origin "$url"
  else
    echo "==> maj $name"
  fi
  git -C "$dir" fetch -q --depth 1 origin "$commit"
  git -C "$dir" checkout -q --force FETCH_HEAD
done
echo "Dépendances prêtes dans $tp"
