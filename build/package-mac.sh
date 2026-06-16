#!/usr/bin/env bash
#
# package-mac.sh: build a notarized, double-click-ready macOS app (.dmg + .zip).
#
# This produces a self-contained arm64 app (players install nothing), wraps it in a
# .app bundle, signs it with your Developer ID + hardened runtime, notarizes it with
# Apple, staples the ticket, and writes both a .dmg and a .zip into dist/.
#
# You provide your signing identity and notarization credentials via environment
# variables (this script never stores or echoes secrets):
#
#   DEV_ID_APP    Your "Developer ID Application: NAME (TEAMID)" identity.
#                 List yours with:  security find-identity -v -p codesigning
#
#   Notarization, pick ONE of:
#     a) NOTARY_PROFILE   name of a stored notarytool keychain profile, created once with:
#          xcrun notarytool store-credentials NOTARY_PROFILE \
#            --apple-id "you@example.com" --team-id "TEAMID" --password "app-specific-pw"
#     b) APPLE_ID + TEAM_ID + APP_PW   Apple ID, team id, and an app-specific password
#          (create the app-specific password at https://account.apple.com > Sign-In and Security).
#
# Optional:
#   VERSION    version string (default 1.0.0)
#   BUNDLE_ID  bundle identifier (default com.ysrdevs.cp2077savekit)
#   An icon at build/AppIcon.icns is used if present.
#
# Example:
#   DEV_ID_APP="Developer ID Application: Yuvraj Singh (ABCDE12345)" \
#   NOTARY_PROFILE="cp2077notary" VERSION=1.0.0 build/package-mac.sh

set -euo pipefail
cd "$(dirname "$0")/.."   # repo root

APP_NAME="CP2077 Save Kit"
BUNDLE_ID="${BUNDLE_ID:-com.ysrdevs.cp2077savekit}"
VERSION="${VERSION:-1.0.0}"
PROJECT="src/CP2077SaveKit.App/CP2077SaveKit.App.csproj"
EXE="CP2077SaveKit.App"
RID="osx-arm64"
DIST="dist"
APP_DIR="$DIST/$APP_NAME.app"

export PATH="$HOME/.dotnet:$PATH"

need() { command -v "$1" >/dev/null 2>&1 || { echo "ERROR: missing tool: $1"; exit 1; }; }
need dotnet; need codesign; need xcrun; need ditto; need hdiutil; need file

if [ -z "${DEV_ID_APP:-}" ]; then
  echo "ERROR: set DEV_ID_APP to your 'Developer ID Application: ...' identity."
  echo "       security find-identity -v -p codesigning"
  exit 1
fi

echo "==> Publishing self-contained $RID (no trimming; WolvenKit needs reflection)…"
PUB="$DIST/publish"
rm -rf "$PUB" "$APP_DIR"
dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false \
  -o "$PUB"

echo "==> Dropping unused non-arm64 native libs (we use managed LZ4 + gzip, not Oodle)…"
rm -f "$PUB"/libkraken.so "$PUB"/kraken.dll "$PUB"/libkraken.dylib 2>/dev/null || true
find "$PUB" -name "*.pdb" -delete 2>/dev/null || true

echo "==> Assembling $APP_DIR …"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp -R "$PUB"/. "$APP_DIR/Contents/MacOS/"
[ -f build/AppIcon.icns ] && cp build/AppIcon.icns "$APP_DIR/Contents/Resources/AppIcon.icns"

cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>$APP_NAME</string>
  <key>CFBundleDisplayName</key><string>$APP_NAME</string>
  <key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>$EXE</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
</dict></plist>
PLIST

chmod +x "$APP_DIR/Contents/MacOS/$EXE"

echo "==> Writing entitlements (.NET runtime needs JIT)…"
ENT="$DIST/entitlements.plist"
cat > "$ENT" <<ENTL
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict></plist>
ENTL

echo "==> Signing the app (deep, hardened runtime, entitlements)…"
# --deep recursively signs all nested Mach-O (the .NET runtime dylibs, Avalonia/Skia
# natives, helper executables) and seals the managed assemblies. This is the reliable
# path for self-contained .NET app bundles; a non-deep sign trips on managed .dll files.
codesign --force --deep --options runtime --timestamp \
  --entitlements "$ENT" --sign "$DEV_ID_APP" "$APP_DIR"
codesign --verify --deep --strict --verbose=2 "$APP_DIR"

echo "==> Submitting to Apple for notarization (this can take a few minutes)…"
NZIP="$DIST/notarize.zip"
ditto -c -k --keepParent "$APP_DIR" "$NZIP"
if [ -n "${NOTARY_PROFILE:-}" ]; then
  xcrun notarytool submit "$NZIP" --keychain-profile "$NOTARY_PROFILE" --wait
else
  : "${APPLE_ID:?set NOTARY_PROFILE, or APPLE_ID + TEAM_ID + APP_PW}"
  : "${TEAM_ID:?set TEAM_ID}"; : "${APP_PW:?set APP_PW}"
  xcrun notarytool submit "$NZIP" --apple-id "$APPLE_ID" --team-id "$TEAM_ID" --password "$APP_PW" --wait
fi
rm -f "$NZIP"

echo "==> Stapling the notarization ticket…"
xcrun stapler staple "$APP_DIR"
spctl -a -vvv --type exec "$APP_DIR" || true   # Gatekeeper sanity check

echo "==> Building distribution .zip …"
DIST_ZIP="$DIST/CP2077SaveKit-$VERSION-arm64.zip"
rm -f "$DIST_ZIP"
ditto -c -k --keepParent "$APP_DIR" "$DIST_ZIP"

echo "==> Building .dmg …"
DMG="$DIST/CP2077SaveKit-$VERSION-arm64.dmg"
STAGE="$DIST/dmg-stage"; rm -rf "$STAGE"; mkdir -p "$STAGE"
cp -R "$APP_DIR" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
rm -f "$DMG"
hdiutil create -volname "$APP_NAME" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
xcrun stapler staple "$DMG" || true
rm -rf "$STAGE"

echo
echo "Done. Distribute these:"
echo "  $DMG"
echo "  $DIST_ZIP"
