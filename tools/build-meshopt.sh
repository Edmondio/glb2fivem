#!/usr/bin/env bash
# Compile meshoptimizer (third_party/meshoptimizer) en bibliothèque native :
#   native/win-x64/meshoptimizer.dll   (cross-compilation MinGW, aucune dépendance)
#   native/linux-x64/libmeshoptimizer.so
# Prérequis Linux : g++ et g++-mingw-w64-x86-64.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
src="$root/third_party/meshoptimizer/src"
out="$root/native"
mkdir -p "$out/win-x64" "$out/linux-x64"

if command -v x86_64-w64-mingw32-g++ >/dev/null; then
  echo "==> win-x64"
  x86_64-w64-mingw32-g++ -O2 -shared -std=c++11 -fno-exceptions -fno-rtti \
    -DMESHOPTIMIZER_API='__declspec(dllexport)' \
    -static -static-libgcc -static-libstdc++ \
    -o "$out/win-x64/meshoptimizer.dll" "$src"/*.cpp
  x86_64-w64-mingw32-strip "$out/win-x64/meshoptimizer.dll"
fi

echo "==> linux-x64"
g++ -O2 -shared -fPIC -std=c++11 -fno-exceptions -fno-rtti \
  -o "$out/linux-x64/libmeshoptimizer.so" "$src"/*.cpp
strip "$out/linux-x64/libmeshoptimizer.so"
ls -la "$out"/*/
