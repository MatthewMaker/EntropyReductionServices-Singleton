# Singletons

The four base classes in `Runtime/MonoBehaviourSingleton.cs`, what each guarantees, and why the
implementation is shaped the way it is.

The header comment in that file is the normative contract — terse, complete, and the thing to check
when you need a precise answer. This document is the explanation.

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

public class AudioBus : MonoBehaviourSingletonPersistent<AudioBus>
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

Note the CRTP shape: `AudioBus : MonoBehaviourSingletonPersistent<AudioBus>` names itself as the
type parameter. `AudioBus : MonoBehaviourSingletonPersistent<SomethingElse>` does not compile, which
is deliberate — the older form of this code accepted it and threw at runtime instead.

## Choosing a flavor

| | Auto-creates | Survives scene load |
|---|---|---|
| `MonoBehaviourSingleton<T>` | yes | no |
| `MonoBehaviourSingletonPersistent<T>` | yes | yes |
| `MonoBehaviourSingletonPassive<T>` | no | no |
| `MonoBehaviourSingletonPassivePersistent<T>` | no | yes |

**Auto-creating** flavors resolve `Instance` on demand: they search the loaded scenes, then try
`Resources`, then create a bare GameObject. `Instance` is non-null the entire time the application
is running. Use these for stateless services whose existence is an implementation detail — an audio
router, a coroutine host, a logging sink.

**Passive** flavors never create anything. `Instance` is null until some component's `Awake` claims
the slot. Use these when the object must be authored — when it carries inspector-configured state,
scene references, or anything that would be wrong if silently conjured from nothing. If a
teardown-time null would genuinely break your callers, the answer is usually that the type should
be passive and scene-authored rather than lazily created.

**Persistent** variants call `DontDestroyOnLoad` and destroy duplicates that appear on later scene
loads. Plain `MonoBehaviourSingleton<T>` declares no `Awake` at all, so it does not deduplicate — it
picks one instance and logs the others. If you want enforcement, use a persistent or passive flavor.

## The contract

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
`MissingReferenceException`. Unity's `==` overload also still reports it as null, which is what
keeps every `!= null` check and `?.` call site meaning what it always meant. So teardown code that
touches more than managed state — `OnDestroy`, `OnApplicationQuit`, coroutine cleanup,
pooled-object return paths — still checks first:

```csharp
private void OnDestroy()
{
    if (AudioBus.TryGetInstance(out var bus)) bus.Unregister(this);
}
```

Three ways to ask whether a *live* singleton exists, all equivalent in effect:

```csharp
if (AudioBus.IsAvailable) AudioBus.Instance.Stop();
if (AudioBus.TryGetInstance(out var bus)) bus.Stop();
AudioBus.Instance?.Stop();
```

Everywhere else, dereference directly. Defensive null checks in `Update` are noise.

Anything *other* than teardown that would produce a null throws `MissingSingletonException`
instead, naming the type and the reason. A configuration mistake should fail where you made it, not
as a `NullReferenceException` in unrelated code forty frames later.

### Why not cache it

```csharp
private AudioBus _bus;                       // don't
private void Start() => _bus = AudioBus.Instance;
```

Reading `Instance` does two things a field copy cannot. It discards an instance captured in a
previous play session, which matters whenever domain reload is off. And while the application is
running it collapses Unity's "fake null" — the managed wrapper that outlives its destroyed native
object — into a real null, so `?.` and `ReferenceEquals` behave. A cached field skips both and
eventually holds a reference to something that no longer exists, with no way to tell which session
it came from. A local inside one method is fine; a field is not. ERS0002 flags
this.

## Lifecycle

**First access** on an auto-creating flavor runs, in order: search every active instance of the
type in the loaded scenes → load and instantiate from `Resources` → create a bare GameObject →
reparent to root → mark `DontDestroyOnLoad`. Each step is skipped once the previous one succeeds.
That is a meaningful frame cost, and it lands wherever the first access happens to be — so touch
your singletons during loading, not mid-session on device.

If the lazy path creates the instance, that instance's own `Awake` runs re-entrantly inside your
`Instance` call. Code in `Awake` that reads `Instance` again will see it already assigned.

**Duplicates** are resolved deterministically: sorted by scene build index, then by hierarchy path,
so the same scene produces the same winner on every run. The losers are logged with their full
paths, because "there is more than one of these" is useless without knowing where.

**Scene load** destroys non-persistent singletons with their scene; the static slot is cleared in
`OnDestroy`. Persistent ones survive, and a duplicate arriving in the newly loaded scene destroys
itself in its own `Awake`, after that `Awake` has already run.

**Shutdown** is detected from `Application.quitting`. From that point nothing is created, and
`Instance` returns null with a single explanatory warning rather than one per call site.

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
state you expect to survive a reload, make it passive and author it in a scene.

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

## Configuration

**Resources path.** Auto-creating flavors try `Resources.Load<T>` before creating a bare object,
defaulting to the type name. Override it:

```csharp
[SingletonResource("Services/InputRouter")]
public class InputRouter : MonoBehaviourSingletonPersistent<InputRouter> { }
```

The load is probed once per session — `Resources.Load` is synchronous, and probing it on every
access of a null `Instance` is a per-frame hitch risk. The corollary is that an asset appearing
after the first probe is not picked up until the next session.

**Duplicate blast radius.** Destroying a duplicate takes the whole GameObject by default, including
any unrelated components sharing it. If your singleton shares its host:

```csharp
protected override bool DestroyWholeGameObject => false;   // destroy just this component
```

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

Five Roslyn analyzers ship with the package and apply automatically to any assembly referencing it,
covering the rules above that are checkable: the required `base.Awake()`, field caching, unguarded
teardown access, construction-time access, and `Awake` declared without `override`. See
[analyzers.md](analyzers.md).
