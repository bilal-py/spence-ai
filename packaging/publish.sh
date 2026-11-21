#!/usr/bin/env bash
# Builds the downloadable app for one platform: a self-contained, single-file API with the web app in wwwroot/.
#   packaging/publish.sh <rid> [version]      rid: win-x64 | osx-arm64 | osx-x64 | linux-x64
# Output: artifacts/Spence-<rid>.zip (Windows) or .tar.gz (macOS/Linux; keeps the executable bit), holding a "Spence" folder.
# File names carry no version, so https://github.com/<repo>/releases/latest/download/Spence-<rid>.zip always works.
# macOS builds must run on macOS: Apple Silicon refuses unsigned binaries, and only the SDK on a Mac ad-hoc signs them.
set -euo pipefail

rid="${1:?usage: publish.sh <rid> [version]}"
version="${2:-0.0.0-dev}"
root="$(cd "$(dirname "$0")/.." && pwd)"
name="Spence-${rid}"
stage="$root/artifacts/$rid"
out="$stage/Spence"

rm -rf "$stage"
mkdir -p "$stage"

# SKIP_WEB_BUILD=1 reuses an existing spence-ai-web/dist (CI builds it once for all platforms).
if [ "${SKIP_WEB_BUILD:-}" != "1" ]; then
  (cd "$root/spence-ai-web" && npm ci && npm run build)
fi

dotnet publish "$root/SpenceAI.WebApi/SpenceAI.WebApi.csproj" \
  -c Release -r "$rid" --self-contained true \
  -p:SpenceDesktop=true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none \
  -p:Version="${version%%-*}" \
  -p:InformationalVersion="$version" \
  -o "$out"

# A single-file app can be renamed freely: the bundle carries its own assembly.
if [[ "$rid" == win-* ]]; then mv "$out/SpenceAI.WebApi.exe" "$out/Spence.exe"; else mv "$out/SpenceAI.WebApi" "$out/Spence"; fi

# Dev-only files must not ship: Development settings, local overrides, any stray DB, IIS and static-asset manifests.
rm -f "$out"/appsettings.Development.json "$out"/appsettings.Local.json "$out"/*.db "$out"/*.pdb "$out"/web.config "$out"/*.staticwebassets.*.json "$out"/*.ico

cp "$root/packaging/appsettings.Desktop.json" "$out/"
cp "$root/packaging/README.txt" "$out/"
cp "$root/LICENSE" "$out/LICENSE.txt"
rm -rf "$out/wwwroot"
cp -r "$root/spence-ai-web/dist" "$out/wwwroot"

# Archive from inside the stage folder with a relative destination: Git Bash paths like /d/a/... mean nothing to
# PowerShell (the fallback on Windows runners, which have no zip), but ../file works for both.
cd "$stage"
if [[ "$rid" == win-* ]]; then
  file="$name.zip"
  rm -f "../$file"
  if [ "${FORCE_POWERSHELL_ZIP:-}" != "1" ] && command -v zip >/dev/null; then
    zip -qr "../$file" Spence
  else
    powershell -NoProfile -NonInteractive -Command "Compress-Archive -Path 'Spence' -DestinationPath '../$file'; if (-not \$?) { exit 1 }"
  fi
else
  file="$name.tar.gz"
  tar -czf "../$file" Spence
fi
[ -f "$root/artifacts/$file" ] || { echo "Archive was not created: artifacts/$file" >&2; exit 1; }
echo "Built artifacts/$file"
