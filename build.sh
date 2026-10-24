#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT=$(pwd)
OUT_DIR=""

DASH_CSPROJ="$PROJECT_ROOT/Nolvus.Dashboard/Nolvus.Dashboard.csproj"

VERSION=$(grep -oPm1 "(?<=<Version>)[^<]+" "$DASH_CSPROJ" || true)

if [ -z "${VERSION:-}" ]; then
    echo "ERROR: Could not find <Version> in $DASH_CSPROJ"
    exit 1
fi

DESKTOP_RELEASE="$HOME/Desktop/Binaries-$VERSION.tar.gz"

mkdir -p "$HOME/Desktop"
if [ -e "$DESKTOP_RELEASE" ]; then
    echo "ERROR: Refusing to overwrite existing release archive: $DESKTOP_RELEASE" >&2
    exit 1
fi

CACHE_ROOT="${XDG_CACHE_HOME:-$HOME/.cache}"
mkdir -p "$CACHE_ROOT"
OUT_DIR=$(mktemp -d "$CACHE_ROOT/nknova-build.XXXXXX")
cleanup() { rm -rf "$OUT_DIR"; }
trap cleanup EXIT

echo "Building Nolvus Dashboard v$VERSION"

mkdir -p "$OUT_DIR/Dashboard" "$OUT_DIR/Nolvus"

echo "Publishing Nolvus.Dashboard..."
dotnet publish "$DASH_CSPROJ" \
    -c Release \
    -r linux-x64 \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    -o "$OUT_DIR/Dashboard"

cp -a "$OUT_DIR/Dashboard/." "$OUT_DIR/Nolvus/"

# DebugType only governs what our own projects emit. Dependencies such as CefGlue ship prebuilt
# .pdb files in their packages, so sweep those out of the payload as well.
echo "Removing debug symbols..."
find "$OUT_DIR/Nolvus" -name "*.pdb" -type f -delete

# The CefGlue package ships its browser subprocess as a self-contained .NET 8 application, so the
# payload carries a second copy of the runtime (~70MB) that the machine already has - the dashboard
# itself is framework dependent and resolves Microsoft.NETCore.App from the system. Point the
# subprocess at the shared runtime too and drop the bundled copy.
#
# Three things have to change together. The runtimeconfig has to ask for a framework instead of
# declaring an included one. The deps manifest describes a self-contained layout and has to go, or
# the host keeps looking for a runtime beside the assembly. And the prebuilt apphost is itself a
# self-contained native host that only ever looks for coreclr next to itself, so it is replaced by a
# launcher that runs the assembly through the installed dotnet. CEF executes whatever sits at that
# path and appends its own arguments, so keeping the file name is all that is needed to stay wired
# up - no dashboard code has to know about any of this.
echo "Removing the bundled .NET runtime from the CEF subprocess..."

SUBPROC_DIR="$OUT_DIR/Nolvus/CefGlueBrowserProcess"
SUBPROC_HOST="$SUBPROC_DIR/Xilium.CefGlue.BrowserProcess"

if [ -f "$SUBPROC_HOST" ]; then
    cat > "$SUBPROC_DIR/Xilium.CefGlue.BrowserProcess.runtimeconfig.json" <<'JSON'
{
  "runtimeOptions": {
    "tfm": "net8.0",
    "rollForward": "Major",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "8.0.0"
    },
    "configProperties": {
      "System.Reflection.Metadata.MetadataUpdater.IsSupported": false,
      "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false
    }
  }
}
JSON

    rm -f "$SUBPROC_DIR/Xilium.CefGlue.BrowserProcess.deps.json"

    (
        cd "$SUBPROC_DIR"
        rm -f System.dll System.*.dll Microsoft.*.dll netstandard.dll mscorlib.dll WindowsBase.dll \
              libcoreclr.so libcoreclrtraceptprovider.so libclrjit.so libclrgc.so \
              libhostfxr.so libhostpolicy.so libmscordaccore.so libmscordbi.so \
              libSystem.*.so createdump
    )

    cat > "$SUBPROC_HOST" <<'LAUNCHER'
#!/usr/bin/env bash
# Runs the CEF browser subprocess on the system .NET runtime instead of a bundled copy. CEF execs
# this path directly and appends its own arguments, so they are forwarded verbatim.
set -eu

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DOTNET="$(command -v dotnet || true)"

if [ -z "$DOTNET" ]; then
    for CANDIDATE in "${DOTNET_ROOT:-}/dotnet" /usr/share/dotnet/dotnet /usr/lib/dotnet/dotnet /opt/dotnet/dotnet; do
        if [ -x "$CANDIDATE" ]; then
            DOTNET="$CANDIDATE"
            break
        fi
    done
fi

if [ -z "$DOTNET" ]; then
    echo "Nolvus Dashboard : the .NET runtime is required but 'dotnet' could not be found." >&2
    exit 1
fi

exec "$DOTNET" "$HERE/Xilium.CefGlue.BrowserProcess.dll" "$@"
LAUNCHER

    chmod +x "$SUBPROC_HOST"
else
    echo "WARNING: CEF subprocess not found at $SUBPROC_HOST"
fi

echo "Copying icon and license documents..."
cp "$PROJECT_ROOT/Nolvus.Dashboard/Assets/nolvus-ico.jpg" "$OUT_DIR/Nolvus/" || true
# Include the source license, attribution, and user-facing documentation with the binary package.
cp "$PROJECT_ROOT/LICENSE" "$OUT_DIR/Nolvus/LICENSE"
for document in README.md CHANGELOG.md THIRD-PARTY-NOTICES.md; do
    cp "$PROJECT_ROOT/$document" "$OUT_DIR/Nolvus/"
done
mkdir -p "$OUT_DIR/Nolvus/docs"
for document in docs/INSTALLATION.md docs/TROUBLESHOOTING.md docs/CREDENTIALS.md docs/RELEASING.md; do
    cp "$PROJECT_ROOT/$document" "$OUT_DIR/Nolvus/docs/"
done

DASH_APPHOST="$OUT_DIR/Nolvus/NolvusDashboard"

if [ -f "$DASH_APPHOST" ]; then
    chmod +x "$DASH_APPHOST"
else
    echo "WARNING: Dashboard apphost not found at $DASH_APPHOST"
fi

find "$OUT_DIR/Nolvus" -type f \( -name "*.sh" -o -perm -u+x \) -exec chmod +x {} \; 2>/dev/null || true

echo "Compressing to: $DESKTOP_RELEASE"
cd "$OUT_DIR"
tar --sort=name --mtime='@315532800' --owner=0 --group=0 --numeric-owner --format=gnu -cf - Nolvus | gzip -n -9 > "$DESKTOP_RELEASE"

cd "$PROJECT_ROOT"

echo "Build complete!"
echo "Created: $DESKTOP_RELEASE"
echo "The upstream updater is intentionally omitted. Update NKNOVA by installing a verified NKNOVA release manually."
