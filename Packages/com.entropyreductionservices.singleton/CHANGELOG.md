# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this package follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1]

### Changed

- `Instance` no longer returns null once shutdown has begun. It hands back the component that
  held the slot — still a live C# object after its native peer is destroyed — so teardown code
  that dereferences it does not raise a `NullReferenceException`. Members reaching the native
  peer (`transform`, `gameObject`, `enabled`, `StartCoroutine`) still raise
  `MissingReferenceException`, and Unity's `==` overload still reports the result as null, so
  existing `!= null` checks and `?.` call sites keep the meaning they had.
- `ERS0003` no longer reports `OnDisable`. It runs during teardown but also throughout ordinary
  play, and the analyzer cannot tell the two apart, so it warned on correct code. `OnDestroy` and
  `OnApplicationQuit` are still reported.

`IsAvailable`, `Exists` and `TryGetInstance` are unchanged: they answer whether a *live*
singleton exists, and now go false while `Instance` is still returning a reference.

## [1.0.0]

### Added

- `MonoBehaviourSingleton<T>`, `MonoBehaviourSingletonPersistent<T>`,
  `MonoBehaviourSingletonPassive<T>` and `MonoBehaviourSingletonPassivePersistent<T>`.
- Five Roslyn analyzers (`ERS0001`–`ERS0005`) enforcing the singleton contract in consuming
  assemblies.
