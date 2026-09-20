; Rules added since the last entry in AnalyzerReleases.Shipped.md. Move them across under a new
; "## Release <version>" heading when the version in package.json is bumped and tagged.

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
ERS0006 | Singleton | Warning | Null-conditional access on a lazy singleton's Instance is misleading. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0006)
