# CLAUDE.md

Working notes for this repository. Conventions here override general defaults.

## What this repo is

A Unity project whose only real content is the embedded package at
`Packages/com.entropyreductionservices.singleton/`. The surrounding project exists so the package
can be opened, compiled and tested by a real editor — it is a test harness, not an application.

The package ships two things: the singleton base classes in `Runtime/`, and seven Roslyn analyzers
that enforce their contract in *consuming* assemblies.

## Branching and releases

**Trunk plus annotated tags.** Work on a short-lived branch off `main`, or directly on `main` for
small fixes; merge fast-forward. No `develop`, no release branches.

Release branches are deliberately not used: they exist so a team can stabilise a release while
feature work continues elsewhere, and there is no parallel work here to stabilise against. If a
patch is ever needed for an older minor after `main` has moved past it, cut `release/x.y` from that
tag at that point — lazily, not as routine.

A release is a version bump, a commit, and an annotated tag on `main`.

### Release checklist

`scripts/release.sh` runs every step below in order and refuses to tag if any of them fails:

```sh
scripts/release.sh minor                 # plan + full verification, writes nothing
scripts/release.sh minor --execute       # bump, rebuild, verify, commit, tag (local)
scripts/release.sh 2.3.1 --execute --push
```

`--skip-unity` drops the Unity suites and the editor-installed check, for a machine without Unity.
Use it only when the runtime code matches a commit whose suites you have already run; the tag
message records that they were skipped.

It is a local script rather than a workflow because the Unity suites are the release gate and CI
cannot run them. It refuses to start unless the tree is clean, `main` matches `origin/main`, a
matching Unity editor is installed (unless `--skip-unity`), and `CHANGELOG.md` has an `## [Unreleased]` heading to
promote. Rules still sitting in `AnalyzerReleases.Unshipped.md` are not a blocker — the script
moves them into `AnalyzerReleases.Shipped.md` under the new version as part of the release commit. Pushing is a separate flag: a
local tag is trivially deletable, a pushed one triggers publication.

The manual steps it automates, for reference — steps 2 and 3 are the ones that silently produce
a broken package if skipped:

1. **Update `CHANGELOG.md`** — rename the `[Unreleased]` heading to the new version, or add one.
   Keep a Changelog format; `Changed`/`Fixed`/`Added`, breaking items marked **Breaking:**.
2. **Bump `"version"` in `package.json`, then run `python3 scripts/sync-version.py`** to regenerate
   `Analyzers~/Version.props`. Move any rules in `AnalyzerReleases.Unshipped.md` across under a new
   `## Release x.y.z` heading. `--check` then verifies all four agree (see below).
3. **Rebuild and recommit the analyzer DLL.** The csproj stamps the version into the assembly, so
   a version bump without a rebuild ships a binary claiming the old version. `CommittedAnalyzerVersionTests`
   in `Analyzers~/Tests` fails when you forget.
4. **Verify locally** — all four suites, below. CI cannot do this for you.
5. **Commit**, then `git tag -a vX.Y.Z -m "..."`, then `git push origin main --follow-tags`.
6. **Publishing is automatic.** Pushing the tag triggers `.github/workflows/release.yml`, which
   signs the package once with `upm pack` and publishes that tarball as a GitHub Release asset, to
   Verdaccio, and (when `OPENUPM_ENABLED` is set) to OpenUPM. Check the workflow result rather than
   assuming — a tag whose version does not match `package.json` is rejected before anything is
   signed. `gh workflow run Release` is a dry run: it signs and checks the tarball and the
   Verdaccio token, and publishes nothing.

   `VERDACCIO_TOKEN` is a JWT from Verdaccio's GitHub OAuth login and expires a year after it is
   issued. The Verdaccio job prints its expiry date, warns within 30 days of it, and fails once it
   has passed. To renew: log in to Verdaccio again, then
   `gh secret set VERDACCIO_TOKEN --env release`.

### The version locations

`package.json` is the source of truth. The others are generated from it or checked against it by
`scripts/sync-version.py`, so they can no longer drift silently:

| File | Field | How it is kept true |
|---|---|---|
| `…/package.json` | `"version"` | **source of truth** — edit this one |
| `…/Analyzers~/Version.props` | `<Version>` | generated; imported by the analyzer and test csproj |
| `…/CHANGELOG.md` | the top `## [x.y.z]` heading | checked |
| `…/Analyzers~/AnalyzerReleases.Shipped.md` | the top `## Release x.y.z` heading | checked |
| `…/Runtime/Analyzers/ERS.Singleton.Analyzers.dll` | stamped assembly version | checked by `CommittedAnalyzerVersionTests` |

```sh
python3 scripts/sync-version.py            # regenerate Version.props
python3 scripts/sync-version.py --check     # exit 1 if anything disagrees
```

`--check` runs in the CI analyzer job and in `.githooks/pre-commit`, beside the severity check.

Semver against *consumers*. Raising `"unity"` (the minimum editor) drops support for a class of
consumers and is a major bump — that is why 2.0.0 followed 1.0.2.

## Verifying before a release

The Unity job requires a `UNITY_LICENSE` secret to activate an editor; where that is unavailable
the job cannot run and the analyzer job is the only CI signal. **Treat a local run as the gate**
before tagging, rather than assuming CI has covered it.

Note for zsh: paths containing `Analyzers~` must be quoted or the tilde is expanded and the `cd`
fails.

```sh
# Run from the repo root. Each step is a subshell, so the cd does not carry into the next.
PKG="Packages/com.entropyreductionservices.singleton"
UNITY="/Applications/Unity/Hub/Editor/6000.5.5f1/Unity.app/Contents/MacOS/Unity"

# 1. Analyzer builds clean. TreatWarningsAsErrors catches RS-prefixed authoring mistakes
#    (unregistered rules, missing release tracking) that would otherwise ship silently.
( cd "$PKG/Analyzers~" && dotnet build -c Release -p:TreatWarningsAsErrors=true )

# 2. Rules actually fire. The analyzer resolves its base type by metadata name and registers no
#    actions when that lookup misses, so "it built" says nothing about whether any rule works.
( cd "$PKG/Analyzers~/Tests" && dotnet test -c Release )

# 3. The committed DLL matches its source, checked behaviourally rather than by bytes.
#    --no-incremental is required: an up-to-date build recompiles nothing, so the analyzer emits
#    no warnings and the check reports zero rules firing, which looks exactly like total failure.
#    Count unique ids, not lines — each warning is printed twice.
cp "$PKG/Analyzers~/bin/Release/ERS.Singleton.Analyzers.dll" "$PKG/Runtime/Analyzers/"
( cd "$PKG/Analyzers~/StalenessProbe" && dotnet build -c Release --no-incremental 2>&1 \
    | grep -oE "(warning|error) ERS000[0-9]" | sort -u )   # expect every id --list-ids prints

# CI drives the same loop from .editorconfig rather than a hardcoded list:
#   python3 scripts/sync-analyzer-severities.py --list-ids

# 4. Runtime behaviour, both platforms. Unity needs Assets/ and ProjectSettings/ present.
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode \
  -testResults /tmp/edit.xml -logFile /tmp/edit.log
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode \
  -testResults /tmp/play.xml -logFile /tmp/play.log
```

Batchmode exits non-zero on failure; read the `<test-run>` attributes in the XML for counts.

## Analyzer severities

`.editorconfig` at the repo root is the source of truth. The two `Default.ruleset` files under
`Packages/.../Tests/` are **generated** from it:

```sh
python3 scripts/sync-analyzer-severities.py            # regenerate after editing .editorconfig
python3 scripts/sync-analyzer-severities.py --check     # exit 1 if drifted
python3 scripts/sync-analyzer-severities.py --list-ids  # every rule id, one per line
```

`--list-ids` is what the staleness-probe CI step loops over, so a rule added to `.editorconfig` is
checked against the committed DLL without anyone editing the workflow.

Both formats are required and neither is redundant: IDEs read `.editorconfig`, while Unity ignores
it when running analyzers through the Editor and reads the ruleset instead. Configure one and the
Console and the IDE quietly disagree about severities.

`--check` runs in the CI analyzer job and in `.githooks/pre-commit`. Enable the hook once per
clone — git does not version `.git/hooks`:

```sh
git config core.hooksPath .githooks
```

## Going public

The repo is private on a Free plan, where GitHub refuses rulesets and environments (`403 Upgrade to
GitHub Pro or make this repository public`). These steps wait until it is public, in this order:

1. **Make the repository public.**
2. **Apply the tag rulesets** committed under `.github/rulesets/`:
   ```sh
   for f in .github/rulesets/*.json; do
     gh api -X POST repos/MatthewMaker/EntropyReductionServices-Singleton/rulesets --input "$f"
   done
   ```
   - `release-tags-creation.json` — only repository admins may create a `v*` tag, since pushing
     one triggers publication.
   - `release-tags-immutable.json` — no one, admins included, may move or delete a `v*` tag. To do
     so deliberately, set that ruleset to `disabled`, act, and re-enable it.

   Both match `refs/tags/v*`, the same pattern `release.yml` triggers on. Change them
   together.
3. **Restrict the `release` environment** to tags matching `v*`. The environment and its secrets
   (`UPM_SERVICE_ACCOUNT_KEY_ID`, `UPM_SERVICE_ACCOUNT_KEY_SECRET`, `VERDACCIO_TOKEN`) already
   exist and work while private; only the deployment policy is unavailable, so until then any ref
   can deploy to it. The rulesets control who can create a release tag; the environment controls
   which refs receive the secrets.
4. **Enable immutable releases** in the repository settings, so a published release's tag and
   assets cannot be changed.
5. **Turn on the security settings** GitHub offers free on public repos, all currently off:
   ```sh
   R=repos/MatthewMaker/EntropyReductionServices-Singleton
   gh api -X PATCH $R -f 'security_and_analysis[secret_scanning][status]=enabled' \
     -f 'security_and_analysis[secret_scanning_push_protection][status]=enabled'
   gh api -X PUT $R/vulnerability-alerts            # Dependabot alerts
   gh api -X PUT $R/private-vulnerability-reporting # SECURITY.md points reporters here
   ```
   Then, under Settings → Actions → General: **require actions to be pinned to a full-length
   commit SHA** (every workflow already is), and **require approval for fork pull request
   workflows from all outside collaborators**. Optionally add yourself as a required reviewer on
   the `release` environment, so every signing run waits for approval.
6. **Register the package with OpenUPM** with `trackingMode: githubRelease`, so it publishes the
   signed Release asset rather than packing the tag itself. Then
   `gh variable set OPENUPM_ENABLED --body true` to turn on the `openupm` job in `release.yml`.

## Non-obvious repo facts

- **The analyzer's stub Unity hierarchy lives in one place.** `Analyzers~/Shared/SingletonStubs.cs`
  is compiled into `StalenessProbe` and embedded as a resource into the analyzer tests, which
  prepend it to every snippet. Both used to carry their own copy with a comment asking the next
  reader to keep them in step; if the probe modelled a smaller hierarchy than the tests, a rule
  that only misbehaved on the passive base passed the committed-DLL check.
- **`Analyzers~` and `Documentation~` end in `~`, so Unity never imports them.** The analyzer
  *source* is therefore invisible to the editor; only the built DLL at
  `Runtime/Analyzers/ERS.Singleton.Analyzers.dll` is. That DLL is committed on purpose, and
  `Analyzers~/StalenessProbe/` is what stops it drifting from its source.
- **The analyzer applies to every assembly referencing the package**, which is Unity's documented
  behaviour for an analyzer under a folder containing an `.asmdef`. That is why the DLL sits beside
  the runtime asmdef and not at the package root. Consumers need no setup.
- **The contract is `Documentation~/contract.md`**, not a source header. It was moved there after
  drifting three times in one day; keep it updated in the same commit as any behaviour change.
- **Test probes are one type per test.** The singleton cache is a static on the closed generic
  type and the whole run shares one domain, so two tests sharing a probe type see each other's
  state. EditMode probes need `[ExecuteAlways]` for `Awake`/`OnDestroy` to run outside play mode.
- **Teardown windows cannot be simulated from a test.** `SingletonRuntime.IsQuitting` is driven by
  `Application.quitting` and has no setter; the scene-unload window is a frame stamp, and a test
  resuming after `UnloadSceneAsync` is already on a later frame. Assert from inside a probe's own
  `OnDestroy` instead — see `Tests/PlayMode/SceneUnloadTests.cs`.
- **Distribution is one signed tarball.** `release.yml` runs `upm pack` in the package folder and
  publishes that file to GitHub Releases, Verdaccio and OpenUPM; nothing downstream repacks it.
  `upm pack` follows `.npmignore` and adds only `.attestation.p7m`, the signature — the workflow
  checks that on every release. So that folder's `.npmignore` decides what consumers receive — currently
  everything except `Analyzers~/`, whose only consumer-relevant output is the committed DLL under
  `Runtime/`. Verify a change to it with `npm pack --dry-run` from the package folder. Unity needs
  a `.meta` beside every shipped asset, which is why the ignore file is a deny list rather than a
  `package.json` `"files"` allow list.
- **Two `Default.ruleset` copies exist on purpose.** Unity resolves rulesets per asmdef folder,
  and the single shareable `Default.ruleset` must sit in an `Assets` root that a UPM package does
  not have. They are generated rather than hand-synced — see Analyzer severities above.
- **Three workflows, one of them disabled.** `analyzer.yml` runs on every push and PR.
  `release.yml` runs on `v*` tags. `ci.yml` (the Unity job) is `disabled_manually`: the Unity
  runner exhausts its disk pulling the ~5 GB editor image and has no `UNITY_LICENSE`. Re-enable
  with `gh workflow enable Unity` — a switch, not a revert; the file is written as it will run.
  OpenUPM is gated by the `OPENUPM_ENABLED` repository variable rather than a disabled workflow,
  because it returns `404 PackageNotFound` until the package is registered, which needs the repo
  to be public.
- **`ci.yml` holds the Unity job despite the name.** GitHub keys a workflow, and its disabled
  state, to the file path. Renaming it to `unity.yml` would register a new workflow that is
  enabled by default and would fail immediately. Rename when the job works.
- **`analyzer.yml`** runs on every push and PR, needs no Unity, and is fast. The `unity` job runs only on
  `main`, tags and manual dispatch, because it pulls a ~5 GB editor image and dominates the
  workflow's runtime — a poor trade on every pull request. It uses the `base` editor image rather
  than `il2cpp`, which exhausted the runner's disk, and targets one version matching the
  `package.json` floor.
