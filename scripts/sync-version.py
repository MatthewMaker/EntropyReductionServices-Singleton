#!/usr/bin/env python3
"""Make package.json the single source of the package version, and check the rest agree.

The version used to live in three hand-edited places that were never compared, and CLAUDE.md said
so outright. Two of them fail silently: the analyzer csproj stamps its <Version> into the assembly,
so a bump without a rebuild ships a binary claiming the old version, and nothing about that looks
wrong until someone reads the DLL's properties.

package.json is the source of truth. Analyzers~/Version.props is generated from it and imported by
the analyzer csproj, which collapses that location into a derived one. The CHANGELOG heading is checked rather
than generated, because it carries prose only a human can write.

AnalyzerReleases.Shipped.md is checked only for being *not newer* than package.json, never for
equality: it gains a heading solely when a release adds a rule, so its history legitimately skips
versions (1.0.0, 2.1.0, 2.2.0 — nothing for 1.0.1, 1.0.2 or 2.0.0). Requiring equality would fail
permanently on the first release that adds no rule.

    python3 scripts/sync-version.py            # regenerate Version.props
    python3 scripts/sync-version.py --check     # exit 1 if anything disagrees

--check is what the pre-commit hook and CI run. The committed DLL is checked separately, by
CommittedAnalyzerVersionTests in Analyzers~/Tests — reading a version out of an assembly needs a
runtime that can load it, which this script has no business doing.
"""

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PACKAGE = ROOT / "Packages" / "com.entropyreductionservices.singleton"
PACKAGE_JSON = PACKAGE / "package.json"
VERSION_PROPS = PACKAGE / "Analyzers~" / "Version.props"
CHANGELOG = PACKAGE / "CHANGELOG.md"
SHIPPED = PACKAGE / "Analyzers~" / "AnalyzerReleases.Shipped.md"

SEMVER = re.compile(r"^\d+\.\d+\.\d+$")


def read_source_version():
    """The version in package.json, which every other location is measured against."""
    version = json.loads(PACKAGE_JSON.read_text(encoding="utf-8")).get("version")
    if not version or not SEMVER.match(version):
        raise SystemExit(f"{PACKAGE_JSON}: expected a x.y.z \"version\", found {version!r}")
    return version


def render_props(version):
    """Build Version.props, carrying the same do-not-edit header the rulesets use."""
    return f"""<Project>
  <!--
    GENERATED FILE — do not edit.

    Source of truth is "version" in ../package.json. Change it there and run:

        python3 scripts/sync-version.py

    Imported by ERS.Singleton.Analyzers.csproj so the version stamped into the analyzer assembly
    cannot drift from the version the package advertises.
  -->
  <PropertyGroup>
    <Version>{version}</Version>
  </PropertyGroup>
</Project>
"""


def as_tuple(version):
    """x.y.z as a comparable tuple of ints."""
    return tuple(int(part) for part in version.split("."))


def first_match(path, pattern, what):
    """The first capture of `pattern` in `path`, or a fatal error naming what was expected."""
    match = re.search(pattern, path.read_text(encoding="utf-8"), re.MULTILINE)
    if not match:
        raise SystemExit(f"{path.relative_to(ROOT)}: no {what} found")
    return match.group(1)


def main():
    check_only = "--check" in sys.argv[1:]
    version = read_source_version()
    wanted = render_props(version)

    problems = []
    current = VERSION_PROPS.read_text(encoding="utf-8") if VERSION_PROPS.exists() else None
    if current != wanted:
        if check_only:
            problems.append(f"{VERSION_PROPS.relative_to(ROOT)} is stale")
        else:
            VERSION_PROPS.write_text(wanted, encoding="utf-8")
            print(f"wrote {VERSION_PROPS.relative_to(ROOT)}")

    changelog = first_match(CHANGELOG, r"^## \[(\d+\.\d+\.\d+)\]", "## [x.y.z] heading")
    if changelog != version:
        problems.append(f"CHANGELOG.md's newest heading is {changelog}, package.json says {version}")

    shipped = first_match(SHIPPED, r"^## Release (\d+\.\d+\.\d+)", "## Release x.y.z heading")
    if as_tuple(shipped) > as_tuple(version):
        problems.append(
            f"AnalyzerReleases.Shipped.md claims release {shipped}, which is newer than "
            f"package.json's {version}"
        )

    if problems:
        print("Package version is inconsistent:", file=sys.stderr)
        for problem in problems:
            print(f"  {problem}", file=sys.stderr)
        print("\nSource of truth is package.json. Run: python3 scripts/sync-version.py",
              file=sys.stderr)
        return 1

    if check_only:
        print(f"Package version consistent at {version} "
              f"(package.json, Version.props, CHANGELOG.md; "
              f"AnalyzerReleases.Shipped.md at {shipped}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
