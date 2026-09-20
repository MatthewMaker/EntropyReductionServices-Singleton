# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this package follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.1]

### Fixed

- `ERS0003` now reports `Instance?.Foo()` inside a teardown callback instead of exempting it, and
  no longer suggests `?.` as a remedy. **`?.` is not a guard against the teardown value.** It
  tests the reference rather than consulting Unity's `==` overload, and since 1.0.1 `Instance`
  returns the destroyed component — a live C# object — so the call proceeds where it would
  previously have short-circuited against a real null. A call that touches the native peer
  therefore raises `MissingReferenceException` where nothing happened before 1.0.1.

  The 1.0.1 entry below states that `?.` call sites keep their prior meaning. That was wrong, and
  the documentation repeated it. `IsAvailable` and `TryGetInstance` consult the overload and were
  always correct; `!= null` is also unaffected.

  No runtime behaviour changed in this release — the analyzer and the documentation were wrong
  about it, not the runtime.

## [2.0.0]

### Changed

- **Breaking:** the minimum supported editor is now Unity 6.3 LTS (`"unity": "6000.3"`), raised
  from 2022.3. 6.3 is the oldest Unity still under support: 6.0 LTS ended in October 2026 and
  2022 LTS in May 2025. Consumers on an older editor should stay on 1.0.2.
- `MaybeFindInScene` calls `FindObjectsByType` unconditionally. The `UNITY_2023_1_OR_NEWER` guard
  around it was wrong in both directions: `FindObjectsByType` has existed since 2021.3, so the
  `FindObjectsOfType` fallback was unreachable above the old 2022.3 floor, while on 2022.3 itself
  the guard selected the fallback — the API deprecated in later editors — instead of the current
  one. No behaviour change on any supported editor.
- CI runs one Unity leg, 6000.3.24f1, replacing 2022.3.62f1 and 6000.0.58f1. Both were at or past
  end of support, and neither covered the version this package now targets.

## [1.0.2]

### Fixed

- A singleton is no longer resurrected during a scene unload. Reading `Instance` after the
  singleton had been destroyed but before the unload finished walked the full resolution chain
  and built a replacement `GameObject` inside the scene being unloaded, running its `Awake`
  against subsystems that may already be shutting down. Teardown now covers both windows —
  application quit, and the frame in which a scene unload destroyed the singleton — and behaves
  identically in each, handing back the destroyed component rather than creating.
- `IsAvailable` no longer reports `true` during a scene unload. Its shortcut for play mode was
  only correct because `Instance` creates on demand, which it does not do during teardown.
- `MaybeFindInScene` no longer adopts an instance whose scene is being unloaded, so an additive
  unload cannot hand back an object that is about to be destroyed while a loaded one exists.

### Added

- `SingletonRuntime.IsUnloadingScene` and `SingletonRuntime.IsTearingDown`, the latter being the
  predicate the resolution paths now guard on.

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
