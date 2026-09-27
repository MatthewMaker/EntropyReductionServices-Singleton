; Rules added since the last entry in AnalyzerReleases.Shipped.md. Move them across under a new
; "## Release <version>" heading when the version in package.json is bumped and tagged.

### Removed Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
ERS0010 | Singleton | Warning | Do not read a passive singleton's Instance from Awake or OnEnable. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0010)
