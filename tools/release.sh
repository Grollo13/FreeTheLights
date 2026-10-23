#!/bin/sh
# Publishes a GitHub release of the version in src/FreeTheLights/Properties/AssemblyInfo.cs: builds and tests main,
# tags it vX.Y.Z and creates the release with FreeTheLights.dll, the installer FreeTheLights-Setup-X.Y.Z.exe, their
# SHA-256 and notes generated from the pull requests merged since the previous release (.github/release.yml). Needs the
# GitHub CLI (gh), logged in as an admin of the repository, and Inno Setup 6 (tools/installer.sh).
#
#   sh tools/release.sh        publish the release
#   sh tools/release.sh -d     create it as a draft, to review the notes before publishing it on GitHub
set -e
REPO="Grollo13/FreeTheLights"
T="$(cd "$(dirname "$0")" && pwd)"
R="$(dirname "$T")"
cd "$R"

DRAFT=
case "$1" in
  -d) DRAFT=--draft ;;
  "") ;;
  *) echo "usage: sh tools/release.sh [-d]" >&2; exit 2 ;;
esac

fail() { echo "release: $*" >&2; exit 1; }

# --- Preconditions ---
command -v gh >/dev/null || fail "the GitHub CLI (gh) is not installed"
gh auth status >/dev/null 2>&1 || fail "gh is not logged in (gh auth login)"
[ "$(gh api "repos/$REPO" --jq .permissions.admin)" = "true" ] || fail "only admins of $REPO can publish releases"
[ "$(git rev-parse --abbrev-ref HEAD)" = "main" ] || fail "check out main first"
[ -z "$(git status --porcelain)" ] || fail "the working tree is not clean"
git fetch -q origin main --tags
[ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ] || fail "main is not the same as origin/main"

# --- Version ---
INFO="src/FreeTheLights/Properties/AssemblyInfo.cs"
VERSION="$(sed -n 's/.*AssemblyVersion("\([0-9]*\.[0-9]*\.[0-9]*\)\.0").*/\1/p' "$INFO")"
[ -n "$VERSION" ] || fail "no AssemblyVersion(\"X.Y.Z.0\") in $INFO"
grep -q "AssemblyFileVersion(\"$VERSION.0\")" "$INFO" || fail "AssemblyFileVersion differs from AssemblyVersion in $INFO"
TAG="v$VERSION"
git rev-parse -q --verify "refs/tags/$TAG" >/dev/null && fail "tag $TAG already exists: bump the version first"
[ -z "$(git ls-remote --tags origin "refs/tags/$TAG")" ] || fail "tag $TAG already exists on origin: bump the version first"
echo "release: FreeTheLights $VERSION from $(git rev-parse --short HEAD)"

# --- Build and test ---
. "$T/msbuild.sh"

# -warnaserror: a release builds without warnings, like main.
"$MSBUILD" FreeTheLights.sln -restore -nologo -v:m -warnaserror -p:Configuration=Release -p:DeployToSimHub=false
"$XUNIT" tests/FreeTheLights.Tests/bin/Release/FreeTheLights.Tests.dll -nologo -noshadow

# --- Assets ---
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT
cp src/FreeTheLights/bin/Release/FreeTheLights.dll "$OUT/"
sh "$T/installer.sh" "$OUT/FreeTheLights.dll" "$OUT" >/dev/null || fail "building the installer failed"
SETUP="FreeTheLights-Setup-$VERSION.exe"
[ -f "$OUT/$SETUP" ] || fail "the installer is not $SETUP: does the DLL have version $VERSION?"
(cd "$OUT" && sha256sum FreeTheLights.dll > FreeTheLights.dll.sha256 && sha256sum "$SETUP" > "$SETUP.sha256")
cat "$OUT/FreeTheLights.dll.sha256" "$OUT/$SETUP.sha256"

# --- Tag and release ---
git tag -a "$TAG" -m "FreeTheLights $VERSION"
git push origin "$TAG"
gh release create "$TAG" "$OUT/FreeTheLights.dll" "$OUT/FreeTheLights.dll.sha256" "$OUT/$SETUP" "$OUT/$SETUP.sha256" \
  --repo "$REPO" --title "FreeTheLights $VERSION" --generate-notes --verify-tag $DRAFT \
  || fail "creating the release failed; the tag $TAG is pushed: run gh release create again, or delete the tag (git push origin :refs/tags/$TAG; git tag -d $TAG)"
echo "release: done${DRAFT:+ (draft: review and publish it on GitHub)}"
