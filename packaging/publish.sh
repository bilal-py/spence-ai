#!/usr/bin/env bash
# Builds the downloadable app for one platform: a self-contained, single-file API with the web app in wwwroot/.
#   packaging/publish.sh <rid> [version]      rid: win-x64 | osx-arm64 | osx-x64 | linux-x64
# Output: artifacts/Spence-<version>-<rid>.zip (Windows) or .tar.gz (macOS/Linux; keeps the executable bit).
# macOS builds must run on macOS: Apple Silicon refuses unsigned binaries, and only the SDK on a Mac ad-hoc signs them.
set -euo pipefail

rid="${1:?usage: publish.sh <rid> [version]}"
version="${2:-0.0.0-dev}"
root="$(cd "$(dirname "$0")/.." && pwd)"
name="Spence-${version}-${rid}"
out="$root/artifacts/$name"

rm -rf "$out"
mkdir -p "$root/artifacts"

# SKIP_WEB_BUILD=1 reuses an existing spence-ai-web/dist (CI builds it once for all platforms).
if [ "${SKIP_WEB_BUILD:-}" != "1" ]; then
  (cd "$root/spence-ai-web" && npm ci && npm run build)
fi

dotnet publish "$root/SpenceAI.WebApi/SpenceAI.WebApi.csproj" \
  -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none \
  -p:Version="${version%%-*}" \
  -o "$out"

# A single-file app can be renamed freely: the bundle carries its own assembly.
if [[ "$rid" == win-* ]]; then mv "$out/SpenceAI.WebApi.exe" "$out/Spence.exe"; else mv "$out/SpenceAI.WebApi" "$out/Spence"; fi

# Dev-only files must not ship: Development settings, the user-secrets marker, any stray local DB.
rm -f "$out"/appsettings.Development.json "$out"/appsettings.Local.json "$out"/*.db "$out"/*.pdb "$out"/web.config "$out"/*.staticwebassets.*.json

cp "$root/packaging/appsettings.Desktop.json" "$out/"
cp "$root/packaging/README.txt" "$out/"
cp "$root/LICENSE" "$out/LICENSE.txt"
rm -rf "$out/wwwroot"
cp -r "$root/spence-ai-web/dist" "$out/wwwroot"

cd "$root/artifacts"
if [[ "$rid" == win-* ]]; then
  rm -f "$name.zip"
  if command -v zip >/dev/null; then zip -qr "$name.zip" "$name"; else powershell -NoProfile -Command "Compress-Archive -Path '$name' -DestinationPath '$name.zip'"; fi
  echo "Built artifacts/$name.zip"
else
  tar -czf "$name.tar.gz" "$name"
  echo "Built artifacts/$name.tar.gz"
fi
