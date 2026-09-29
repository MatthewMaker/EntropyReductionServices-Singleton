# The contract

What this package guarantees, what it asks of you in return, and where it will not help. The
companion documents are [singletons.md](singletons.md), which teaches the lifetime and creation policies and why
they exist, and [analyzers.md](analyzers.md), which documents the rules that enforce the parts of
this contract a compiler can check.

## Goals

- One well-known access point per component type, correct across scene loads, editor domain
  reload, and application shutdown.
- One class; each lifetime and creation policy expresses a genuinely different contract, not
  another spelling of one.
- Failures surface at the mistake, not three call sites downstream.
- No `UnityEditor` dependency, so the runtime drops into any assembly.

## Guarantees

**Lifetime**

- Outside teardown, `Instance` returns a live singleton or throws `MissingSingletonException`;
  it never returns null. It throws only when creation is forbidden — `[SingletonCreation(FindOnly)]`,
  or a `[SingletonEditMode]` policy outside play mode — and no instance is in the loaded scenes.
  Under the default policies it always returns a live one.
- During teardown `Instance` returns the component that last held the slot — a live C# object
  whose native peer is gone — so a bare dereference reaches something. It is null only if the
  singleton never existed. See [Teardown](#teardown) for the limits.
- A singleton with the default Application lifetime lives until the application quits. One with a
  Scene lifetime is destroyed with its scene, and the next read resolves afresh.
- Under `[SingletonCreation(FindOnly)]` nothing is ever created: `Instance` resolves from the loaded
  scenes or throws `MissingSingletonException`, in play mode and edit mode alike.
- A singleton with the default Application lifetime is made persistent even when another object
  resolved it before its own `Awake` ran. One with a Scene lifetime is never made persistent, and
  is destroyed with its scene.
- Nothing is created after `Application.quitting` has fired, nor into an active scene whose unload
  has destroyed a singleton. An unload that destroys none opens no window — see
  [Known gaps](#known-gaps-and-accepted-risks).
- Nothing created in edit mode can be written to a scene or prefab (`HideFlags.DontSave`).

**Correctness**

- The cache is never stale: not across play sessions (session id), and not across native
  destruction, where Unity's "fake null" is collapsed to a real null on read.
- Duplicate resolution is deterministic — scene build index, then hierarchy path — so the same
  scene yields the same winner every run, and every loser is logged with its full path.
- `MaybeFindInScene` never adopts an instance whose scene is being unloaded.
- `class Foo : MonoBehaviourSingleton<Bar>` does not compile (CRTP constraint).
- `FramePromoted` is never serialized.
- Debug logging and debug name mutation compile out unless `SINGLETON_DEBUG` or
  `SINGLETON_DEBUG_GET` are defined.
- Exactly one `Application.quitting` subscription regardless of domain-reload settings.

## Teardown

Teardown is two windows: the application is quitting, or the active scene — the one a replacement
would be created in — is being unloaded. `SingletonRuntime.IsTearingDown` is true in both.

In either window `Instance` hands back the destroyed component instead of creating a replacement,
because creating one then drops a `GameObject` into a scene that is going away and runs its `Awake`
against subsystems that may already be shutting down.

A single-mode scene load opens the second window while it destroys the old scene, which is still
active then, and closes it within the same frame once the new scene is active, so the new scene's
`Awake` can create singletons. Unloading a scene that is not active does not open it: a read from
`OnDestroy` there builds a replacement in the active scene — for a Scene-lifetime singleton, one
that then lives with the active scene.

What that buys you and what it does not:

| | |
|---|---|
| Plain C# members on the returned object | Work. Lists, dictionaries, events, plain fields. |
| Members declared by `UnityEngine` | Throw `MissingReferenceException`: `transform`, `gameObject`, `enabled`, `StartCoroutine`. ERS0003 reports these, and only these. |
| `Instance != null` | Unchanged. Unity's `==` overload still reports the tombstone as null. |
| `Instance?.Foo()` | **Not a guard.** `?.` tests the reference, not the `==` overload, and the destroyed component is a live C# object — so the call proceeds. ERS0003 reports it. |
| `IsAvailable`, `Exists`, `TryGetInstance` | Answer whether a *live* singleton exists, so they go false while `Instance` still returns a reference. |

So an `OnDisable` that unregisters itself from a manager needs no guard. Teardown code that touches
more than managed state still does — via `IsAvailable` or `TryGetInstance`, never `?.`.

## Constraints on the subclass

- Must be CRTP: `class Foo : MonoBehaviourSingleton<Foo>`.
- An `Awake` override must call `base.Awake()` **first**; an `OnDestroy` override must call
  `base.OnDestroy()` **last**. The base Awake claims the slot and destroys duplicates, so work
  ahead of it runs on instances that are about to disappear; the base OnDestroy releases the slot,
  so work behind it sees no live instance. **Not compiler-enforced** — the one place this design
  relies on discipline. ERS0001, ERS0005 and ERS0007 exist to catch it.
- A non-default `Resources` path requires `[SingletonResource("path")]`.
- Singletons with the default Application lifetime accept being reparented to the scene root.
- `Current` is read-only to subclasses; claim the slot via `AssignInAwake` or the lazy path.
- Unity 6.3 LTS (6000.3) or newer, per `package.json`.

## Forbidden usages

- **Field initialisers and `MonoBehaviour` constructors.** Unity throws on `Find` and
  `new GameObject` there. ERS0004.
- **`OnValidate` and `ISerializationCallbackReceiver`.** Object creation during deserialisation is
  unsupported and can throw. ERS0008.
- **Any thread but the main thread.**
- **Caching `Instance` in a field that can outlive the singleton.** Bypasses both the session
  guard and the fake-null collapse — the exact bug class this package exists to prevent. That is a
  static or serialised field, a field on anything but a `Component`, or any field holding a
  singleton with a Scene lifetime. A private, non-serialised field on a `Component` holding a
  singleton with the default lifetime cannot outlive it. ERS0002.
- **Setting `hideFlags` on a singleton yourself.** The find path depends on them. ERS0009.
- **Name-matching a singleton** (`GameObject.Find`) in any build where `SINGLETON_DEBUG` may be on.
- **Sharing a `GameObject`** is allowed but discouraged. A duplicate destroys only itself when its
  GameObject carries anything else, so a sibling singleton is not collateral; but the GameObject
  still travels as one object, so a singleton with the default lifetime reparents its siblings to
  the scene root and makes them persistent too. See
  `DestroyWholeGameObject`, and *Tools > Entropy Reduction Services > Validate Singleton Placement*,
  which reports both cases in the loaded scenes and on every scene save.

  A singleton with the default lifetime and **no serialized fields** resolves this itself: it is rebuilt on a
  new `GameObject` named for the type, and the shared GameObject stays in the scene. A `Component` cannot
  be moved between `GameObject`s, so this destroys the authored component and constructs a
  replacement — safe only because there was no serialized state to carry across. Two costs remain
  that no check can see: **serialized references to the component break** (an inspector field or a
  `UnityEvent` wired to it), and the subclass's own `Awake` body runs on both the original and the
  replacement. Give the type a serialized field, or put it on its own object, to opt out.

  With serialized fields there is nothing safe to do, so the GameObject is persisted as before and the
  validator reports it.
- **Unguarded `OnDestroy` / `OnApplicationQuit` access to a `UnityEngine` member** of the
  singleton. Your own members are fine there. ERS0003. See [Teardown](#teardown).

## Side effects of touching Instance

- First access may, in order: search every active object of the type,
  synchronously read from `Resources`, instantiate a prefab, create a `GameObject`, reparent it to
  root, and mark it `DontDestroyOnLoad` unless its lifetime is Scene. Pay that at load, not
  mid-session on device.
- If the lazy path creates the instance, that instance's `Awake` runs re-entrantly inside your
  `Instance` call.
- Logs an error on duplicates. Throws `MissingSingletonException` on policy violation. Warns once
  per session when `Instance` is read during teardown.
- In edit mode, adds a hierarchy object that vanishes on the next recompile.

## Known gaps and accepted risks

- `FindObjectsInactive.Exclude` misses singletons on inactive objects; a duplicate can be created
  and will self-destruct only when the inactive one is enabled.
- Edit-mode transients lose all state on every recompile.
- `s_resourceProbed` means a `Resources` asset appearing after the first probe is not picked up for
  the rest of the session.
- `DestroyImmediate` in edit mode can invalidate an enumeration you are inside.
- The duplicate blast radius is decided inside the duplicate's `Awake`, from the components present
  at that moment. A scene-authored or prefab GameObject has them all by then. A singleton added with
  `AddComponent` to a live object *before* its siblings is evaluated while alone on it, and will
  still take the GameObject with it — add the singleton last, or build the GameObject inactive.
- Additive scene loads still produce a duplicate. It self-destructs, but its `Awake` has already
  run by then.
- The scene-unload window only opens if a singleton is itself destroyed by the unload of the
  active scene. If none was, a singleton first read from that scene's teardown is created into it.
- Not a substitute for dependency injection or explicit init order. Lazy resolution builds a
  dependency before its first user, but it cannot order side effects that no reference expresses,
  break a cycle between two `Awake`s, or warm singletons up at a moment you choose. For those,
  touch the `Instance`s you need from an explicit bootstrapper.
