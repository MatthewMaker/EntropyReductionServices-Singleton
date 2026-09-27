# Singletons

The singleton base class in `Runtime/MonoBehaviourSingleton.cs`, its lifetime and creation
policies, what each guarantees, and why the implementation is shaped the way it is.

[contract.md](contract.md) is the normative contract — terse, complete, and the thing to check when
you need a precise answer. This document is the explanation.

## The problem

A Unity singleton is usually fifteen lines:

```csharp
public class AudioBus : MonoBehaviour
{
    public static AudioBus Instance;
    private void Awake() => Instance = this;
}
```

That works until one of three things happens, and all three eventually happen.

**Domain reload is disabled.** Under *Enter Play Mode Options* with *Reload Domain* off — which you
want, because it turns a twelve-second iteration loop into a one-second one — statics survive
between play sessions. Any `applicationIsQuitting`-style latch stays latched, and the singleton is
permanently unavailable from the second play session onward. You cannot fix this inside a generic
base class, because Unity does not invoke `[RuntimeInitializeOnLoadMethod]` on generic types.

**The application quits.** Teardown order is not yours to control. A singleton accessed from some
other component's `OnDestroy` gets lazily recreated into a scene that is already unloading; you get
"leaked GameObject" warnings on desktop and, on device, a native crash when the new object touches
an XR or audio subsystem that has already shut down.

**The singleton is not at the scene root.** `DontDestroyOnLoad` only applies to root objects. Give
it a nested one and it warns and does nothing, so the object you carefully marked persistent
silently dies on the next scene load.

This package's answer to each: a non-generic session tracker that generic types consult; shutdown
detected from `Application.quitting` rather than inferred; and reparenting to root before marking
persistent.

## Quick start

```csharp
using EntropyReductionServices.Singletons;

public class AudioBus : MonoBehaviourSingleton<AudioBus>
{
    private AudioMixer _mixer;

    protected override void Awake()
    {
        base.Awake();                       // claims the slot, destroys duplicates
        _mixer = GetComponent<AudioMixer>();
    }

    public void Play(AudioClip clip) { /* … */ }
}
```

```csharp
AudioBus.Instance.Play(clip);               // anywhere, any time the app is running
```

Note the CRTP shape: `AudioBus : MonoBehaviourSingleton<AudioBus>` names itself as the
type parameter. `AudioBus : MonoBehaviourSingleton<SomethingElse>` does not compile, which
is deliberate — the older form of this code accepted it and threw at runtime instead.

## Choosing a lifetime and a creation policy

There is one class, `MonoBehaviourSingleton<T>`. Its `Awake` claims the slot and destroys later
duplicates, and two attributes decide the rest.

`Instance` resolves on demand: it searches the loaded scenes, then tries `Resources`, then creates
a bare GameObject. `Instance` is non-null the entire time the application is running. That suits
stateless services whose existence is an implementation detail — an audio router, a coroutine
host, a logging sink.

**Lifetime.** By default a singleton is marked `DontDestroyOnLoad` and lives until the application
quits. A per-scene object — a level director, a scene's UI root — should instead die with its
scene, so that the next scene's own instance takes the slot:

```csharp
[SingletonLifetime(SingletonLifetimePolicy.Scene)]
public class LevelDirector : MonoBehaviourSingleton<LevelDirector> { }
```

On a single-mode scene change Unity destroys the old scene's objects before the new scene's `Awake`
runs, so the old instance has released the slot by the time the new one claims it.

**Authored singletons.** When the object must be authored — it carries inspector-configured state,
scene references, or anything that would be wrong if silently conjured from nothing — forbid
creation:

```csharp
[SingletonLifetime(SingletonLifetimePolicy.Scene)]
[SingletonCreation(SingletonCreationPolicy.FindOnly)]
public class LevelDirector : MonoBehaviourSingleton<LevelDirector>
{
    [SerializeField] private LevelData _level;
}
```

`Instance` then resolves from the loaded scenes only, in play mode and edit mode alike. If nothing
was authored it throws `MissingSingletonException` naming the policy, rather than returning null or
building a stand-in with default fields. Because the lookup is a scene search, an instance whose own
`Awake` has not run yet is still found, so reading it from another object's `Awake` is not a race.
Where the singleton is genuinely optional, ask `ExistsOrFindInScene()` first: it is the only
accessor that searches, while `IsAvailable`, `Exists` and `TryGetInstance` read the cache alone.


## The contract

The summary below is what you need to write correct calling code. The complete, normative version
— every guarantee, constraint, forbidden usage and known gap — is [contract.md](contract.md).

> A live singleton exists for the entire time the application is running, and none exists during
> teardown. `Instance` never returns null once the singleton has existed: during teardown it
> hands back the destroyed component instead.

**Teardown** means two windows. The application is quitting, or a scene unload has destroyed the
singleton — for the rest of that frame. In both, `Instance` refuses to build a replacement,
because recreating a singleton then drops a GameObject into a scene that is going away and runs
its `Awake` against subsystems that may already be shutting down. That half is not negotiable.

The other half is what keeps teardown code from having to be defensive about the common case.
`Instance` returns the component that held the slot, still a live C# object even though its native
peer is gone. Calling a plain C# member on it works:

```csharp
private void OnDisable()
{
    AudioBus.Instance.Unregister(this);   // safe: Unregister touches only managed state
}
```

The limit is worth knowing. That reference is a destroyed `UnityEngine.Object`, so anything
reaching the native peer — `transform`, `gameObject`, `enabled`, `StartCoroutine` — raises
`MissingReferenceException`. Unity's `==` overload still reports it as null, so `!= null` checks
keep the meaning they always had — but **`?.` does not**. `?.` tests the reference rather than
consulting the overload, and the destroyed component is a live C# object, so the call goes through.
It reads as a guard and is not one; ERS0003 reports it. So teardown code that
touches more than managed state — `OnDestroy`, `OnApplicationQuit`, coroutine cleanup,
pooled-object return paths — still checks first:

```csharp
private void OnDestroy()
{
    if (AudioBus.TryGetInstance(out var bus)) bus.Unregister(this);
}
```

Two ways to ask whether a *live* singleton exists, equivalent in effect:

```csharp
if (AudioBus.IsAvailable) AudioBus.Instance.Stop();
if (AudioBus.TryGetInstance(out var bus)) bus.Stop();
```

Both consult Unity's `==` overload. `AudioBus.Instance?.Stop()` does not, and is not a third
option — see above.

Everywhere else, dereference directly. Defensive null checks in `Update` are noise.

Anything *other* than teardown that would produce a null throws `MissingSingletonException`
instead, naming the type and the reason. A configuration mistake should fail where you made it, not
as a `NullReferenceException` in unrelated code forty frames later.

### When not to cache it

```csharp
private static AudioBus s_bus;               // don't
private void Start() => s_bus = AudioBus.Instance;
```

Reading `Instance` does two things a field copy cannot. It discards an instance captured in a
previous play session, which matters whenever domain reload is off. And while the application is
running it collapses Unity's "fake null" — the managed wrapper that outlives its destroyed native
object — into a real null, so `?.` and `ReferenceEquals` behave. A field that outlives the
singleton skips both and ends up holding a reference to something that no longer exists, with no
way to tell which session it came from.

That takes a field that can outlive it: a static one, a serialized one, one on anything that is not
a `Component`, or one holding a singleton with a Scene lifetime, which dies with its scene. A
private field on a `Component` holding a singleton with the default lifetime cannot — the holder
goes first — so
`private AudioBus _bus;` assigned in `Start` is fine. So is a local. ERS0002 flags the rest.

## Lifecycle

**First access** runs, in order: search every active instance of the type in the loaded scenes →
load and instantiate from `Resources` → create a bare GameObject → reparent to root → mark
`DontDestroyOnLoad` (unless the lifetime is Scene). Each step is skipped once the previous one succeeds.
That is a meaningful frame cost, and it lands wherever the first access happens to be — so touch
your singletons during loading, not mid-session on device.

If the lazy path creates the instance, that instance's own `Awake` runs re-entrantly inside your
`Instance` call. Code in `Awake` that reads `Instance` again will see it already assigned.

**Duplicates** are resolved deterministically: sorted by scene build index, then by hierarchy path,
so the same scene produces the same winner on every run. The losers are logged with their full
paths, because "there is more than one of these" is useless without knowing where.

**Scene load** destroys Scene-lifetime singletons with their scene; the static slot is cleared in
`OnDestroy`. The rest survive, and a duplicate arriving in the newly loaded scene destroys itself in
its own `Awake`, after that `Awake` has already run.

**Shutdown** is detected from `Application.quitting`. From that point nothing is created, and
`Instance` hands back the destroyed component (see [The contract](#the-contract)) with a single
explanatory warning rather than one per call site.

**Domain reload**, whether from recompiling or entering play mode, bumps a session counter on the
non-generic `SingletonRuntime`. Every generic singleton records the session its cached instance was
captured in and discards anything stale on read. That is a static reset without reflection, and it
is what makes disabled Domain Reload safe.

## Edit mode

`Instance` works outside play mode, and keeps the same non-null contract there. Objects created in
edit mode are marked `HideFlags.DontSave`, so they cannot be written into the open scene or into
whatever prefab stage happens to be open, and they vanish on the next recompile. `DontDestroyOnLoad`
is skipped, since it is play-mode-only and logs an error otherwise.

That last point has a consequence worth knowing: an edit-mode singleton loses all state on every
script recompile. For stateless service objects this is invisible. If a type accumulates editor-time
state you expect to survive a reload, mark it `FindOnly` and author it in a scene.

Some types should not be conjured by an inspector drawing itself — anything that claims hardware,
opens sockets, or spins up threads. Opt those out:

```csharp
[SingletonEditMode(SingletonEditModePolicy.FindOnly)]     // resolve from scene, never create
public class XRSessionManager : MonoBehaviourSingleton<XRSessionManager> { }

[SingletonEditMode(SingletonEditModePolicy.Disabled)]     // never resolve outside play mode
public class DeviceLink : MonoBehaviourSingleton<DeviceLink> { }
```

Requesting `Instance` from an opted-out type outside play mode throws `MissingSingletonException`,
naming the policy.

Under `FindOnly`, guard with `ExistsOrFindInScene()` — it is the only accessor that searches the
loaded scenes. `IsAvailable`, `Exists` and `TryGetInstance` read the cache alone, so before
anything has resolved they all report false even when an instance is sitting in the scene. Under
`Disabled` there is nothing to search for; guard with `Application.isPlaying`.

## Configuration

**Resources path.** Unless the type is `FindOnly`, `Instance` tries `Resources.Load<T>` before creating a bare object,
defaulting to the type name. Override it:

```csharp
[SingletonResource("Services/InputRouter")]
public class InputRouter : MonoBehaviourSingleton<InputRouter> { }
```

The load is probed once per session — `Resources.Load` is synchronous, and probing it on every
access of a null `Instance` is a per-frame hitch risk. The corollary is that an asset appearing
after the first probe is not picked up until the next session.

**Duplicate blast radius.** Destroying a duplicate takes the whole GameObject only when the
singleton is alone on it — `Transform` plus itself. If anything else is riding along, only the
component is destroyed, so a sibling singleton is never collateral damage. Force either answer:

```csharp
protected override bool DestroyWholeGameObject => false;   // never take the GameObject
protected override bool DestroyWholeGameObject => true;    // always take the GameObject
```

**Singletons on a shared object.** `DontDestroyOnLoad` moves the whole `GameObject`, so a singleton
with the default lifetime sharing its GameObject drags every sibling into the persistent scene. A singleton
with no serialized fields is rebuilt on an object of its own named for the type, and the GameObject stays
put. One with serialized fields cannot be — there is authored state that a rebuild would discard —
so the GameObject is persisted and *Tools > Entropy Reduction Services > Validate Singleton Placement* reports
it.

Note what the rebuild costs even when it is allowed: the component is destroyed and replaced, so
anything holding a serialized reference to it — an inspector field, a `UnityEvent` — is left
pointing at nothing, and the subclass's `Awake` body runs on both instances. Put the singleton on
its own object and neither happens.

**Debug tracing.** Uncomment `SINGLETON_DEBUG` (lifecycle events) or `SINGLETON_DEBUG_GET` (every
`Instance` read) at the top of `MonoBehaviourSingleton.cs`. Both are `[Conditional]`, so they cost
nothing when off. They also enable name-suffix annotations — `(!)` for persistent, `(+)` for a
duplicate kill, `(s0)` for found-in-scene — which is why any code matching singletons by name will
break in a build with these on. Don't match singletons by name.

## What this will not do

- **Initialization order between singletons.** Lazy resolution cannot give you deterministic
  construction order. If order matters, write an explicit bootstrapper; that is the right answer,
  not a workaround.
- **Find singletons on inactive GameObjects.** The scene search excludes them, so a duplicate can
  be created and will self-destruct only when the inactive one is enabled.
- **Thread safety.** Main thread only, like the rest of the Unity API.
- **Survive being constructed too early.** Field initializers and `MonoBehaviour` constructors run
  on Unity's deserialization path, where object lookup and creation throw. Use `Awake`, `OnEnable`
  or `Start`. ERS0004 flags this.

## Enforcement

Nine Roslyn analyzers ship with the package and apply automatically to any assembly referencing it,
covering the rules above that are checkable: the required `base.Awake()` and its position, field
caching, unguarded teardown access, `?.` on a lazy `Instance`, construction-time and
serialization-callback access, `Awake` declared without `override`, and `hideFlags` set on a
singleton. See [analyzers.md](analyzers.md).
