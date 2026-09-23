#!/usr/bin/env python3
"""Generate the Unity ruleset files from the severities declared in .editorconfig.

Two files have to state the same analyzer policy. IDEs read .editorconfig; Unity ignores it when
it runs analyzers through the Editor and reads a ruleset instead, so configuring only one makes
the Console and the IDE disagree. Rather than ask anyone to remember that, .editorconfig is the
source of truth and the rulesets are generated from it.

    python3 scripts/sync-analyzer-severities.py            # write the rulesets
    python3 scripts/sync-analyzer-severities.py --check     # exit 1 if they have drifted
    python3 scripts/sync-analyzer-severities.py --list-ids  # rule ids that emit a build diagnostic

--check is what the pre-commit hook and CI run. --list-ids lets the staleness-probe CI step
derive the rule list from .editorconfig rather than restating it in a shell loop, so a rule
added here cannot be silently left unchecked.

--list-ids prints only the rules set to error or warning. The probe greps a build log for
"(warning|error) ERSxxxx", and suggestion/silent/none map to Info/Hidden/None, which never appear
there — listing those would fail the probe for a rule that is behaving exactly as configured.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EDITORCONFIG = ROOT / ".editorconfig"
PACKAGE = ROOT / "Packages" / "com.entropyreductionservices.singleton"
RULESETS = [
    PACKAGE / "Tests" / "EditMode" / "Default.ruleset",
    PACKAGE / "Tests" / "PlayMode" / "Default.ruleset",
]

ANALYZER_ID = "ERS.Singleton.Analyzers"
RULE_NAMESPACE = "EntropyReductionServices.Analyzers"

# .editorconfig severity -> ruleset Action. Ruleset has no vocabulary for "suggestion"/"silent"
# beyond Info/Hidden, and these are the documented equivalents.
# Severities that put a matchable "(warning|error) ERSxxxx" line in build output. Info, Hidden
# and None do not, which is why --list-ids filters on this rather than listing every rule.
BUILD_VISIBLE = {"error", "warning"}

ACTIONS = {
    "error": "Error",
    "warning": "Warning",
    "suggestion": "Info",
    "silent": "Hidden",
    "none": "None",
}

SEVERITY_LINE = re.compile(
    r"^\s*dotnet_diagnostic\.(?P<rule>ERS\d{4})\.severity\s*=\s*(?P<severity>[a-z]+)"
)


def read_severities():
    """Pull every dotnet_diagnostic.ERSxxxx.severity out of .editorconfig, in file order.

    Deliberately a line scan rather than a full .editorconfig parse: the rules live in one [*.cs]
    section and a real parser would add a dependency to read a handful of lines. Raises if a
    severity is
    not one this script knows how to translate, so a typo fails loudly instead of silently
    dropping a rule from the generated file.
    """
    severities = {}
    for number, line in enumerate(EDITORCONFIG.read_text(encoding="utf-8").splitlines(), 1):
        match = SEVERITY_LINE.match(line)
        if not match:
            continue
        rule, severity = match.group("rule"), match.group("severity")
        if severity not in ACTIONS:
            raise SystemExit(
                f"{EDITORCONFIG.name}:{number}: unknown severity '{severity}' for {rule}. "
                f"Expected one of: {', '.join(ACTIONS)}"
            )
        severities[rule] = severity
    if not severities:
        raise SystemExit(f"No dotnet_diagnostic.ERSxxxx.severity lines found in {EDITORCONFIG}")
    return severities


def render(severities):
    """Build the full text of one ruleset file, including the header that says not to edit it.

    The header names this script and .editorconfig so that someone who opens the file to change a
    severity is redirected before they waste the edit — the previous hand-maintained versions
    carried a "DUPLICATED, edit both" warning that was easy to miss.
    """
    rules = "\n".join(
        f'    <Rule Id="{rule}" Action="{ACTIONS[severity]}" />'
        for rule, severity in sorted(severities.items())
    )
    return f"""<?xml version="1.0" encoding="utf-8"?>
<!--
  GENERATED FILE — do not edit.

  Source of truth is the dotnet_diagnostic.ERSxxxx.severity block in /.editorconfig. Change it
  there and run:

      python3 scripts/sync-analyzer-severities.py

  Both this file and .editorconfig are needed: IDEs read .editorconfig, and Unity ignores it when
  running analyzers through the Editor and reads this ruleset instead. Unity resolves rulesets per
  asmdef folder, and the one shareable Default.ruleset must sit in an Assets root that a UPM
  package does not have, which is why {len(RULESETS)} identical copies exist rather than one.
-->
<RuleSet Name="Unity Singletons development rules" ToolsVersion="16.0">
  <Rules AnalyzerId="{ANALYZER_ID}" RuleNamespace="{RULE_NAMESPACE}">
{rules}
  </Rules>
</RuleSet>
"""


def main():
    args = sys.argv[1:]
    check_only = "--check" in args
    severities = read_severities()

    # Consumed by the staleness-probe CI step, which asserts every rule fires against the
    # committed DLL. Driving that loop from here means a new rule is checked the moment it is
    # declared in .editorconfig, instead of when someone remembers to edit the workflow.
    if "--list-ids" in args:
        visible = sorted(rule for rule, severity in severities.items() if severity in BUILD_VISIBLE)
        print("\n".join(visible))
        return 0

    wanted = render(severities)

    stale = []
    for path in RULESETS:
        current = path.read_text(encoding="utf-8") if path.exists() else None
        if current == wanted:
            continue
        if check_only:
            stale.append(path)
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(wanted, encoding="utf-8")
            print(f"wrote {path.relative_to(ROOT)}")

    if check_only and stale:
        print("Analyzer severities are out of sync with .editorconfig:", file=sys.stderr)
        for path in stale:
            print(f"  {path.relative_to(ROOT)}", file=sys.stderr)
        print("\nRun: python3 scripts/sync-analyzer-severities.py", file=sys.stderr)
        return 1

    if check_only:
        print(f"Analyzer severities in sync ({len(severities)} rules, {len(RULESETS)} rulesets).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
