#!/usr/bin/env bash
#
# Runs the release checklist from CLAUDE.md in order, and refuses to tag if any step fails.
#
# Dry-run by default: it prints the plan and runs the full verification suite without writing
# anything. --execute performs the version bump, the DLL rebuild, the commit and the tag, all
# locally. Pushing stays a separate, explicit --push, because a local tag is trivially deletable
# and a pushed one triggers publication.
#
#     scripts/release.sh minor                # plan + verify, write nothing
#     scripts/release.sh minor --execute      # bump, rebuild, verify, commit, tag
#     scripts/release.sh 2.3.1 --execute --push
#
# The verification step runs the Unity EditMode and PlayMode suites, which CI cannot: the Unity
# job has no licence and exhausts the runner's disk on the editor image. That is the whole reason
# this is a local script rather than a workflow_dispatch job.
#
# There is no separate backup step. The script refuses to run on a dirty tree, so every file it
# touches is committed and `git checkout -- .` is the rollback.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PKG="Packages/com.entropyreductionservices.singleton"
PACKAGE_JSON="$PKG/package.json"
CHANGELOG="$PKG/CHANGELOG.md"
UNSHIPPED="$PKG/Analyzers~/AnalyzerReleases.Unshipped.md"
SHIPPED="$PKG/Analyzers~/AnalyzerReleases.Shipped.md"
DLL="$PKG/Runtime/Analyzers/ERS.Singleton.Analyzers.dll"

EXECUTE=0
PUSH=0
LEVEL=""

die()  { printf '\nerror: %s\n' "$*" >&2; exit 1; }
step() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
note() { printf '    %s\n' "$*"; }

usage() {
    sed -n '3,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    exit "${1:-0}"
}

# ---------------------------------------------------------------------------------------------
# Arguments
# ---------------------------------------------------------------------------------------------
for arg in "$@"; do
    case "$arg" in
        --execute) EXECUTE=1 ;;
        --push)    PUSH=1 ;;
        -h|--help) usage 0 ;;
        major|minor|patch) LEVEL="$arg" ;;
        [0-9]*.[0-9]*.[0-9]*) LEVEL="$arg" ;;
        *) die "unrecognised argument '$arg'. Expected major|minor|patch|x.y.z, --execute, --push." ;;
    esac
done

[ -n "$LEVEL" ] || usage 1
[ "$PUSH" -eq 1 ] && [ "$EXECUTE" -eq 0 ] && die "--push requires --execute."

# ---------------------------------------------------------------------------------------------
# Preflight. Everything that can refuse the release cheaply, before any long build.
# ---------------------------------------------------------------------------------------------
step "Preflight"

for tool in git dotnet python3 npm; do
    command -v "$tool" >/dev/null || die "$tool is not on PATH."
done

BRANCH="$(git rev-parse --abbrev-ref HEAD)"
[ "$BRANCH" = "main" ] || die "on branch '$BRANCH'. Releases are cut from main."

[ -z "$(git status --porcelain)" ] || die "working tree is dirty. Commit or stash first."

git fetch --quiet origin main 2>/dev/null || note "could not reach origin; skipping the up-to-date check."
if git rev-parse --verify --quiet origin/main >/dev/null; then
    [ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ] \
        || die "main differs from origin/main. Pull or push first."
fi

# Unity is resolved from the project's own version, never hardcoded.
UNITY_VERSION="$(awk -F': ' '/^m_EditorVersion:/ {print $2}' ProjectSettings/ProjectVersion.txt | tr -d '\r')"
UNITY="/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity"
[ -x "$UNITY" ] || die "no Unity $UNITY_VERSION at $UNITY. The runtime suites are the release gate."
note "Unity $UNITY_VERSION"

# Rules still in Unshipped are moved into Shipped as part of the release commit, not before it.
# Doing it in an earlier commit would leave Shipped claiming a version package.json had not
# reached yet, which the pre-commit hook rejects — there was no ordering that satisfied both.
PENDING_RULES="$(grep -cE '^ERS[0-9]{4} \|' "$UNSHIPPED" || true)"

CURRENT="$(python3 -c "import json;print(json.load(open('$PACKAGE_JSON'))['version'])")"
NEXT="$(python3 - "$CURRENT" "$LEVEL" <<'PY'
import re, sys
current, level = sys.argv[1], sys.argv[2]
if re.fullmatch(r"\d+\.\d+\.\d+", level):
    print(level); raise SystemExit
major, minor, patch = (int(p) for p in current.split("."))
print({"major": f"{major+1}.0.0",
       "minor": f"{major}.{minor+1}.0",
       "patch": f"{major}.{minor}.{patch+1}"}[level])
PY
)"

[ "$NEXT" != "$CURRENT" ] || die "$NEXT is already the current version."
git rev-parse --verify --quiet "refs/tags/v$NEXT" >/dev/null && die "tag v$NEXT already exists."

note "current $CURRENT  ->  next $NEXT"
[ "$PENDING_RULES" -gt 0 ] && note "$PENDING_RULES unshipped rule(s) will move into AnalyzerReleases.Shipped.md"


grep -q '^## \[Unreleased\]' "$CHANGELOG" \
    || die "$CHANGELOG has no '## [Unreleased]' heading to promote to $NEXT."

# ---------------------------------------------------------------------------------------------
# Verification. All four suites, in the order CLAUDE.md documents.
# ---------------------------------------------------------------------------------------------
verify() {
    step "1/4  Generated files match their sources"
    python3 scripts/sync-analyzer-severities.py --check
    python3 scripts/sync-version.py --check

    step "2/4  Analyzer builds clean"
    # TreatWarningsAsErrors catches RS-prefixed authoring mistakes that would otherwise ship.
    ( cd "$PKG/Analyzers~" && dotnet build -c Release -p:TreatWarningsAsErrors=true )

    step "3/4  Analyzer rules fire"
    ( cd "$PKG/Analyzers~/Tests" && dotnet test -c Release )
    # --no-incremental: an up-to-date build recompiles nothing and emits no diagnostics, which
    # looks identical to every rule being broken.
    local fired
    fired="$( cd "$PKG/Analyzers~/StalenessProbe" \
        && dotnet build -c Release --no-incremental 2>&1 \
        | grep -oE "(warning|error) ERS[0-9]{4}" | grep -oE "ERS[0-9]{4}" | sort -u )"
    local expected
    expected="$(python3 scripts/sync-analyzer-severities.py --list-ids)"
    [ "$fired" = "$expected" ] || die "committed DLL did not fire every rule.
  expected: $(echo "$expected" | tr '\n' ' ')
  fired:    $(echo "$fired" | tr '\n' ' ')"
    note "committed DLL fires: $(echo "$fired" | tr '\n' ' ')"

    step "4/4  Unity runtime suites"
    run_unity EditMode
    run_unity PlayMode
}

# Runs one Unity test platform in batch mode and reports the counts from the result XML.
# Batchmode exits non-zero on failure, but the XML is what says how much actually ran.
run_unity() {
    local platform="$1"
    local results; results="$(mktemp -t "ers-$platform")".xml
    local log;     log="$(mktemp -t "ers-$platform")".log

    if ! "$UNITY" -batchmode -nographics -projectPath "$ROOT" -runTests \
            -testPlatform "$platform" -testResults "$results" -logFile "$log"; then
        note "see $log"
        die "$platform tests failed."
    fi

    python3 - "$results" "$platform" <<'PY'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
total, passed = root.get("total"), root.get("passed")
failed = root.get("failed")
print(f"    {sys.argv[2]}: {passed}/{total} passed, {failed} failed")
if failed != "0":
    sys.exit(1)
PY
}

# ---------------------------------------------------------------------------------------------
# Dry run stops here, having told you whether the release would pass.
# ---------------------------------------------------------------------------------------------
if [ "$EXECUTE" -eq 0 ]; then
    step "DRY RUN — verifying the current tree; nothing will be written"
    verify
    step "Plan for --execute"
    note "set version $CURRENT -> $NEXT in package.json"
    note "regenerate Analyzers~/Version.props"
    note "promote '## [Unreleased]' to '## [$NEXT]' in CHANGELOG.md"
    [ "$PENDING_RULES" -gt 0 ] \
        && note "move $PENDING_RULES unshipped rule(s) into AnalyzerReleases.Shipped.md under '## Release $NEXT'"

    note "rebuild the analyzer and recommit $DLL"
    note "re-run all four suites, then commit and tag v$NEXT"
    [ "$PUSH" -eq 1 ] && note "push main --follow-tags"
    printf '\nNothing was written. Re-run with --execute to perform the release.\n'
    exit 0
fi

# ---------------------------------------------------------------------------------------------
# Execute
# ---------------------------------------------------------------------------------------------
step "Bumping $CURRENT -> $NEXT"

python3 - "$PACKAGE_JSON" "$CURRENT" "$NEXT" <<'PY'
import pathlib, sys
path, current, new = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3]
text = path.read_text(encoding="utf-8")
old = f'"version": "{current}"'
if text.count(old) != 1:
    raise SystemExit(f"{path}: expected exactly one {old}, found {text.count(old)}")
path.write_text(text.replace(old, f'"version": "{new}"'), encoding="utf-8")
print(f"    package.json -> {new}")
PY

python3 scripts/sync-version.py

python3 - "$CHANGELOG" "$NEXT" <<'PY'
import pathlib, sys
path, new = pathlib.Path(sys.argv[1]), sys.argv[2]
text = path.read_text(encoding="utf-8")
if text.count("## [Unreleased]") != 1:
    raise SystemExit(f"{path}: expected exactly one '## [Unreleased]' heading")
path.write_text(text.replace("## [Unreleased]", f"## [{new}]", 1), encoding="utf-8")
print(f"    CHANGELOG.md -> ## [{new}]")
PY

if [ "$PENDING_RULES" -gt 0 ]; then
    step "Moving $PENDING_RULES unshipped rule(s) into AnalyzerReleases.Shipped.md"
    python3 - "$UNSHIPPED" "$SHIPPED" "$NEXT" <<'PY'
import pathlib, re, sys

unshipped, shipped, version = (pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), sys.argv[3])

# The leading ';' comment block is the file's own instructions and stays put; everything after it
# is the pending release content, moved across verbatim so section headings survive.
lines = unshipped.read_text(encoding="utf-8").splitlines(keepends=True)
head = [line for line in lines if line.startswith(";")]
body = "".join(lines[len(head):]).strip("\n")
if not body:
    raise SystemExit(f"{unshipped}: no content to move")

text = shipped.read_text(encoding="utf-8")
match = re.search(r"^## Release ", text, re.MULTILINE)
if not match:
    raise SystemExit(f"{shipped}: no existing '## Release' heading to insert above")

# The release-tracking analyzer's format is strict: no blank line after the section heading.
shipped.write_text(
    text[:match.start()] + f"## Release {version}\n\n{body}\n\n" + text[match.start():],
    encoding="utf-8")
unshipped.write_text("".join(head), encoding="utf-8")
print(f"    moved into '## Release {version}'")
PY
fi

step "Rebuilding the analyzer DLL"
# The csproj stamps the version into the assembly, so the bump above makes the committed DLL
# stale by definition. CommittedAnalyzerVersionTests is what catches skipping this.
( cd "$PKG/Analyzers~" && dotnet build -c Release -p:TreatWarningsAsErrors=true )
cp "$PKG/Analyzers~/bin/Release/ERS.Singleton.Analyzers.dll" "$PKG/Runtime/Analyzers/"
note "recommitted $DLL"

verify

step "Committing and tagging v$NEXT"
git add "$PACKAGE_JSON" "$CHANGELOG" "$DLL" "$PKG/Analyzers~/Version.props" "$UNSHIPPED" "$SHIPPED"
if [ -n "$(git status --porcelain --untracked-files=no | grep -v '^M ' || true)" ]; then
    note "note: other tracked files were modified (Unity rewrites project settings in batch mode)"
    note "they are NOT part of this commit; review them with git status afterwards"
fi
git commit -m "Release $NEXT"
git tag -a "v$NEXT" -m "Release $NEXT"
note "committed $(git rev-parse --short HEAD) and tagged v$NEXT"

if [ "$PUSH" -eq 1 ]; then
    step "Pushing"
    git push origin main --follow-tags
    note "pushed. The OpenUPM workflow publishes on the tag — check it rather than assuming."
else
    printf '\nNot pushed. When ready:\n\n    git push origin main --follow-tags\n\n'
    command -v pbcopy >/dev/null && printf 'git push origin main --follow-tags' | pbcopy \
        && note "(copied to clipboard)"
    note "To undo: git tag -d v$NEXT && git reset --hard HEAD~1"
fi
