; Rules added since the last entry in AnalyzerReleases.Shipped.md. Move them across under a new
; "## Release <version>" heading when the version in package.json is bumped and tagged.

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
ERS0008 | Singleton | Warning | Do not read a lazy singleton's Instance from a serialization callback. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0008)
ERS0009 | Singleton | Warning | Do not set hideFlags on a singleton. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0009)
ERS0010 | Singleton | Warning | Do not read a passive singleton's Instance from Awake or OnEnable. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0010)
