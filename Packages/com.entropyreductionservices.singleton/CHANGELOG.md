# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this package follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- **ERS0010's help link lands on its rule again.** The 2.x analyzer links to `analyzers.md#ers0010`
  on `main`, whose section 3.0.0 dropped, so the link opened the top of the page. The section is
  back, saying the rule was removed and what replaced it.

## [3.0.0]

### Changed

- **Breaking: unloading a scene that is not the active one no longer opens the teardown window.** A read of
  `Instance` from its `OnDestroy` builds a replacement in the active scene, as it would outside
  teardown, instead of handing back the destroyed component. `SingletonRuntime.IsUnloadingScene`
  and `IsTearingDown` are true only while the active scene is being unloaded.
- **ERS0006 and ERS0008 titles and descriptions** say "a singleton's `Instance`" rather than "a
  lazy singleton's `Instance`", and ERS0001's description names `MonoBehaviourSingleton<T>` rather
  than "the singleton base classes", now that it is the only base class. The rules themselves are
  unchanged.

### Fixed

- **A scene's `Awake` can create singletons during a single-mode scene load.** The load destroys
  the old scene and wakes the new one in the same frame, and destroying a singleton with its scene
  blocked creation of every singleton type for the rest of that frame. The new scene's `Awake`
  then got the destroyed instance back from `Instance`, or null for a type that had never existed.
  Creation is now blocked only while the active scene, where a new instance would be created, is
  the one being unloaded.
- **Analyzer help links land on their rule.** Each rule links to `analyzers.md#ers000N`, an
  anchor that the page's headings did not produce, so the link opened the top of the page.

## [3.0.0-pre.1]

### Added

- **`[SingletonLifetime(SingletonLifetimePolicy.Scene)]`** keeps a singleton in its scene and
  destroys it with that scene, for per-scene objects such as a level director.

### Changed

- **Breaking: `MonoBehaviourSingleton<T>` declares a virtual `Awake`** that claims the slot and
  destroys later duplicates, and marks the instance `DontDestroyOnLoad` unless the type has
  `[SingletonLifetime(SingletonLifetimePolicy.Scene)]`. A subclass declaring `Awake` must now
  override it and call `base.Awake()` first (ERS0001, ERS0005, ERS0007). Previously this class had
  no `Awake`, never claimed the slot on its own, logged duplicates instead of destroying them, and
  died with its scene.
- **ERS0002** reads `[SingletonLifetime]` to decide whether a singleton can die before a field
  holding it.

### Removed

- **Breaking: `MonoBehaviourSingletonPersistent<T>`.** `MonoBehaviourSingleton<T>` now does what it
  did, and lives until the application quits by default.
- **Breaking: `MonoBehaviourSingletonBase<T>`**, folded into `MonoBehaviourSingleton<T>` now that it
  has no other subclass. `Exists`, `ExistsOrFindInScene` and `TryGetInstance` are unchanged, and
  are now declared on `MonoBehaviourSingleton<T>`.
- **Breaking: `MonoBehaviourSingletonPassive<T>` and `MonoBehaviourSingletonPassivePersistent<T>`**,
  deprecated in 2.6.0. Use `MonoBehaviourSingleton<T>` with
  `[SingletonCreation(SingletonCreationPolicy.FindOnly)]`, adding
  `[SingletonLifetime(SingletonLifetimePolicy.Scene)]` in place of the non-persistent one.
  `Instance` then throws instead of returning null when nothing is authored.
- **ERS0010**, which only applied to the passive flavours.

## [2.6.0]

### Added

- **`[SingletonCreation(SingletonCreationPolicy.FindOnly)]`** forbids a lazy singleton from
  creating an instance, in play mode and edit mode. `Instance` resolves from the loaded scenes or
  throws `MissingSingletonException` naming the policy; `IsAvailable` no longer claims availability
  for a type that cannot create.

- **ERS0008** reports a lazy singleton's `Instance` read from `OnValidate`, `OnBeforeSerialize` or
  `OnAfterDeserialize`, where the search and creation it may perform are unsupported.
- **ERS0009** reports an assignment to `hideFlags` on a singleton or its `GameObject`, which hides
  it from the scene lookup or lets an edit-mode transient be saved.
- **ERS0010** reports a passive singleton's `Instance` read from another `MonoBehaviour`'s `Awake`
  or `OnEnable`. Neither is ordered after the singleton's own `Awake`, which fills the slot.

### Changed

- **ERS0002 reports only fields that can outlive the singleton they hold**: static fields,
  serialized fields, fields on anything that is not a `Component`, and fields holding a singleton
  that is not persistent. A private, non-serialized field on a `Component` holding a persistent
  singleton is no longer reported. The message now says which of these applies.

### Deprecated

- **`MonoBehaviourSingletonPassive<T>` and `MonoBehaviourSingletonPassivePersistent<T>`** are marked
  `[Obsolete]` and will be removed in 3.0.0. Use `MonoBehaviourSingleton<T>` or
  `MonoBehaviourSingletonPersistent<T>` with `[SingletonCreation(SingletonCreationPolicy.FindOnly)]`.
  `Instance` then throws instead of returning null when nothing is authored, and the plain lazy
  flavour logs duplicates where `MonoBehaviourSingletonPassive<T>` destroyed them.

### Fixed

- **A scene-authored persistent singleton resolved before its own `Awake` is now made
  persistent.** Its `Awake` persisted it only when it claimed an empty slot. When another object's
  `Awake` had already resolved it through the scene search, it found itself in the slot and did
  nothing, so it was destroyed with its scene.

- **The package compiles on Unity 6000.3, its declared minimum.** Scene lookup called the
  `FindObjectsByType<T>(FindObjectsInactive)` overload, which only exists from 6000.4, so the
  runtime assembly failed to compile on 6000.3 with CS1503. It now uses the overload each editor
  version provides.

## [2.5.0]

### Fixed

- **`IsAvailable` on the passive flavours no longer reports `true` while the slot is empty.** It
  was declared once on the shared base, with a shortcut — `true` whenever playing, or in edit mode
  under `CreateTransient` — that only holds for a flavour whose `Instance` creates on demand. A
  passive `Instance` never does, so `IsAvailable` could be `true` while `Instance` was null. Each
  flavour now declares its own: the lazy one is unchanged, the passive one is true exactly when
  something has claimed the slot. `IsAvailable` is consequently no longer a member of
  `MonoBehaviourSingletonBase<T>`; calls through a concrete singleton type are unaffected.

## [2.4.0]

### Fixed

- **A singleton created from `Resources` no longer loses its slot to the component it replaced.**
  `Instantiate` runs `Awake` on the stack, so a persistent singleton sharing its prefab root
  rebuilt itself on a GameObject of its own before `Instantiate` returned — and the creating code
  then assigned the *returned* component, already scheduled for destruction, back over the slot.
  Its `OnDestroy` released the slot at end of frame, stranding the live instance in
  `DontDestroyOnLoad` and leaving the next access to create a second one. The creating code now
  keeps whatever `Awake` claimed.

### Changed

- **A duplicate no longer destroys its GameObject GameObject when anything else is on it.**
  `DestroyWholeGameObject` defaulted to a constant `true`, so resolving a duplicate of one
  singleton type could destroy a sole, correctly registered instance of a *different* type that
  happened to share the GameObject — and the victim had no way to defend itself, because the override
  lived on the duplicate's type. It is now decided per GameObject: `Transform` plus the singleton means
  the shell goes too, anything else means only the component is destroyed.

  Override to force either answer. Existing `=> false` overrides keep working unchanged; an
  explicit `=> true` is now the way to get the old unconditional behaviour.

- **A persistent singleton with no serialized fields is rebuilt on its own GameObject** when it
  shares a `GameObject`, instead of dragging the siblings into `DontDestroyOnLoad`. The new object is named
  for the type. A `Component` cannot be moved between `GameObject`s, so this destroys the authored
  component and constructs a replacement — which is why it is limited to types with no serialized
  state to lose.

  Two costs it cannot detect: serialized references *to* the component break, and the subclass's
  `Awake` body runs on both the original and the replacement. Adding a serialized field opts the
  type out, as does putting it on its own object. Types with serialized fields behave as before,
  and now log a warning naming the GameObject.

### Added

- **An editor-side validator for singleton placement**, at *Tools > Entropy Reduction Services >
  Validate Singleton Placement*. It also runs on every scene save, and reports two cases: more than one
  singleton on a GameObject, and a persistent singleton sharing its GameObject with ordinary components
  that `DontDestroyOnLoad` will drag along with it. Warnings carry the object as their log context,
  so clicking one selects it.

  It is an editor tool because it cannot be anything else. A shared GameObject is scene data, invisible
  to the analyzers, and the runtime cannot repair it — a `Component` cannot be moved to another
  `GameObject`, so relocating an authored singleton would mean recreating it and discarding its
  serialized state. Ships in its own Editor-only assembly, so the runtime keeps its no-`UnityEditor`
  dependency.
## [2.3.0]

### Changed

- `Instance` resolves in fewer Unity API calls once the singleton exists. The three resolution
  steps it ran on every access — scene search, `Resources` probe, create — each already returned
  immediately when the slot was filled, but reaching them cost four session-guarded reads of the
  cached reference plus repeated `Application.isPlaying` and `Time.frameCount` calls. A resolved
  access now takes one such read. The cache guarantees are unchanged: the session guard and the
  fake-null collapse still run, once.

- `SingletonRuntime.IsUnloadingScene` no longer reads `Time.frameCount` when no scene unload has
  destroyed a singleton this session.

No behaviour change in either case, and no change to the analyzer rules.

### Fixed

- `[SingletonEditMode(SingletonEditModePolicy.Disabled)]` now suppresses the scene search as well
  as creation. `MaybeFindInScene` consulted only `Application.quitting`, so `ExistsOrFindInScene()`
  resolved and cached an instance the policy forbade — leaving `Exists`, `IsAvailable` and
  `TryGetInstance` reporting a live singleton while `Instance` threw `MissingSingletonException`
  for the same type. All the accessors now agree with `Instance`.

  Affects `Disabled` types only, and only outside play mode. If you relied on
  `ExistsOrFindInScene()` finding a scene instance for a `Disabled` type, use `FindOnly`, which is
  the policy that means "resolve from the scene, never create".

## [2.2.0]

### Added

- `ERS0007` — the singleton base call must be in the right position, not merely present.
  `base.Awake()` claims the slot and destroys duplicates, so it belongs **first**: work ahead of
  it runs even on an instance that is about to destroy itself. `base.OnDestroy()` releases the
  slot, so it belongs **last**: code behind it sees no live instance and an `IsCurrentInstance`
  that has already turned false. `ERS0001` is satisfied by both wrong orderings.

  Only a base call that is a whole statement directly in the method body is considered. One
  nested in an `if`, a loop or a local function is left alone, and an expression-bodied override
  is first and last at once, so neither is reported.

### Changed

- `ERS0003` now reports only members **declared by `UnityEngine`** — `transform`, `gameObject`,
  `enabled`, `StartCoroutine`, `name` — rather than every dereference in a teardown callback.
  Since 1.0.1 `Instance` returns the component that held the slot, so calling your own members on
  it during teardown runs against live managed state and is safe. The rule was warning about the
  shape it is most often used for, an `OnDestroy` unregistering itself from a manager, and a rule
  that fires mostly on correct code gets switched off wholesale.

  The limit is deliberate: only the member named directly on `Instance` is read, so a method of
  your own that itself touches the native peer is invisible to the rule and will still throw.

  A `?.` in teardown on a managed member now falls through to `ERS0006` instead — still one
  diagnostic, and the call is safe even though the `?.` is misleading.

## [2.1.0]

### Added

- `ERS0006` — reports `Instance?.Foo()` on a lazy singleton (`MonoBehaviourSingleton<T>` and
  `MonoBehaviourSingletonPersistent<T>`), where the accessor resolves, creates, or throws and so
  never returns null while the application is running. The operator is dead there, and during
  teardown it does not guard anything, so it only misinforms the reader.

  **Passive singletons are not reported.** `MonoBehaviourSingletonPassive<T>.Instance` is null
  until a component's `Awake` claims the slot, so `?.` on one is a correct guard.

  A `?.` inside `OnDestroy` or `OnApplicationQuit` continues to raise `ERS0003` alone; the two
  rules are mutually exclusive.

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
