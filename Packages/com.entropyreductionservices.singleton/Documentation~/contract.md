# The contract

What this package guarantees, what it asks of you in return, and where it will not help. The
companion documents are [singletons.md](singletons.md), which teaches the four flavours and why
they exist, and [analyzers.md](analyzers.md), which documents the rules that enforce the parts of
this contract a compiler can check.

## Goals

- One well-known access point per component type, correct across scene loads, editor domain
  reload, and application shutdown.
- Four flavours expressing genuinely different contracts, not four spellings of one.
- Failures surface at the mistake, not three call sites downstream.
- No `UnityEditor` dependency, so the runtime drops into any assembly.

## Guarantees

**Lifetime**

- A live `MonoBehaviourSingleton<T>` exists for the whole time the application is running.
- `Instance` never returns null once the singleton has existed. During teardown it returns the
  component that held the slot — a live C# object whose native peer is gone — so a bare
  dereference reaches something. See [Teardown](#teardown) for the limits.
- `MonoBehaviourSingletonPassive<T>.Instance` is null until some component's `Awake` claims the
  slot, and never auto-creates.
- Nothing is created during teardown: neither after `Application.quitting` has fired, nor in the
  frame in which a scene unload destroyed the singleton.
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

Teardown is two windows: the application is quitting, or a scene unload has destroyed the
singleton, for the remainder of that frame. `SingletonRuntime.IsTearingDown` is true in both.

In either window `Instance` hands back the destroyed component instead of creating a replacement,
because creating one then drops a `GameObject` into a scene that is going away and runs its `Awake`
against subsystems that may already be shutting down.

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

- Must be CRTP: `class Foo : MonoBehaviourSingletonPersistent<Foo>`.
- An `Awake` override must call `base.Awake()` **first**; an `OnDestroy` override must call
  `base.OnDestroy()` **last**. The base Awake claims the slot and destroys duplicates, so work
  ahead of it runs on instances that are about to disappear; the base OnDestroy releases the slot,
  so work behind it sees no live instance. **Not compiler-enforced** — the one place this design
  relies on discipline. ERS0001, ERS0005 and ERS0007 exist to catch it.
- A non-default `Resources` path requires `[SingletonResource("path")]`.
- Persistent flavours accept being reparented to the scene root.
- `Current` is read-only to subclasses; claim the slot via `AssignInAwake` or the lazy path.
- Unity 6.3 LTS (6000.3) or newer, per `package.json`.

## Forbidden usages

- **Field initialisers and `MonoBehaviour` constructors.** Unity throws on `Find` and
  `new GameObject` there. ERS0004.
- **`OnValidate` and `ISerializationCallbackReceiver`.** Object creation during deserialisation is
  unsupported and can throw.
- **Any thread but the main thread.**
- **Caching `Instance` in your own static or serialised field.** Bypasses both the session guard
  and the fake-null collapse — the exact bug class this package exists to prevent. ERS0002.
- **Reading a Passive `Instance` from another object's `Awake`.** `Awake` order is undefined, so
  that is a race. Use `Start`, `OnEnable`, or Script Execution Order.
- **Setting `hideFlags` on a singleton yourself.** The find path depends on them.
- **Name-matching a singleton** (`GameObject.Find`) in any build where `SINGLETON_DEBUG` may be on.
- **Sharing a `GameObject`** is no longer forbidden outright, but stays discouraged on any flavour
  that deduplicates. A duplicate now destroys only itself when its host carries anything else, so a
  sibling singleton is no longer collateral; the host does still travel as one object, so a
  persistent flavour reparents its siblings to the scene root and makes them persistent too. See
  `DestroyWholeGameObject`, and *Tools > Entropy Reduction Services > Validate Singleton Hosts*,
  which reports both cases in the loaded scenes and on every scene save.

  A persistent singleton **with no serialized fields** resolves this itself: it is rebuilt on a
  new `GameObject` named for the type, and the shared host stays in the scene. A `Component` cannot
  be moved between `GameObject`s, so this destroys the authored component and constructs a
  replacement — safe only because there was no serialized state to carry across. Two costs remain
  that no check can see: **serialized references to the component break** (an inspector field or a
  `UnityEvent` wired to it), and the subclass's own `Awake` body runs on both the original and the
  replacement. Give the type a serialized field, or put it on its own object, to opt out.

  With serialized fields there is nothing safe to do, so the host is persisted as before and the
  validator reports it.
- **Unguarded `OnDestroy` / `OnApplicationQuit` access to a `UnityEngine` member** of the
  singleton. Your own members are fine there. ERS0003. See [Teardown](#teardown).

## Side effects of touching Instance

- First access on the auto flavour may, in order: search every active object of the type,
  synchronously read from `Resources`, instantiate a prefab, create a `GameObject`, reparent it to
  root, and mark it `DontDestroyOnLoad`. Pay that at load, not mid-session on device.
- If the lazy path creates the instance, that instance's `Awake` runs re-entrantly inside your
  `Instance` call.
- Logs an error on duplicates. Throws `MissingSingletonException` on policy violation. Warns once
  per session when `Instance` is read during teardown.
- In edit mode, adds a hierarchy object that vanishes on the next recompile.

## Known gaps and accepted risks

- Plain `MonoBehaviourSingleton<T>` declares no `Awake`, so it never claims the slot or destroys
  duplicates — it picks one and logs the rest. Two scene-authored instances both survive. Use a
  Persistent or Passive flavour if you want enforcement.
- `FindObjectsInactive.Exclude` misses singletons on inactive objects; a duplicate can be created
  and will self-destruct only when the inactive one is enabled.
- Edit-mode transients lose all state on every recompile.
- `s_resourceProbed` means a `Resources` asset appearing after the first probe is not picked up for
  the rest of the session.
- `DestroyImmediate` in edit mode can invalidate an enumeration you are inside.
- The duplicate blast radius is decided inside the duplicate's `Awake`, from the components present
  at that moment. A scene-authored or prefab host has them all by then. A singleton added with
  `AddComponent` to a live object *before* its siblings is evaluated while alone on it, and will
  still take the host with it — add the singleton last, or build the host inactive.
- Additive scene loads still produce a duplicate. It self-destructs, but its `Awake` has already
  run by then.
- The scene-unload window only opens if a singleton is itself destroyed by the unload. If none was,
  the slot is still valid and there was nothing to guard against.
- Not a substitute for dependency injection or explicit init order. If construction order between
  singletons matters, this package will not give it to you.
