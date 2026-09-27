; Shipped analyzer releases. Required by RS2008 and parsed by the release-tracking analyzer, so
; the table format is strict — no blank line after the section heading. See
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 2.6.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
ERS0008 | Singleton | Warning | Do not read a lazy singleton's Instance from a serialization callback. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0008)
ERS0009 | Singleton | Warning | Do not set hideFlags on a singleton. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0009)
ERS0010 | Singleton | Warning | Do not read a passive singleton's Instance from Awake or OnEnable. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0010)

## Release 2.2.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
ERS0007 | Singleton | Warning | Singleton base call is in the wrong position. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0007)

## Release 2.1.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
ERS0006 | Singleton | Warning | Null-conditional access on a lazy singleton's Instance is misleading. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0006)

## Release 1.0.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
ERS0001 | Singleton | Warning | Singleton message override must call its base implementation. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0001)
ERS0002 | Singleton | Warning | Do not cache a singleton Instance in a field. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0002)
ERS0003 | Singleton | Warning | Guard singleton access in teardown callbacks. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0003)
ERS0004 | Singleton | Warning | Do not access a singleton during MonoBehaviour construction. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0004)
ERS0005 | Singleton | Warning | Singleton message must be declared with 'override'. [Documentation](https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#ers0005)
