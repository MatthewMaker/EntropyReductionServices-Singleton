# Singleton analyzers

Nine rules enforcing [the contract](contract.md). They ship
as `Runtime/Analyzers/ERS.Singleton.Analyzers.dll` and apply to this package's assembly **and to
every assembly that references it** — that scoping is Unity's documented behaviour for an analyzer
sitting in or under a folder containing an `.asmdef`, and it is why the DLL lives beside the
runtime asmdef rather than at the package root. Consumers need no setup: no asset labels, no
manifest entries, nothing copied into their `Assets` folder.

| ID | Default | Rule |
|----|---------|------|
| ERS0001 | Warning | Singleton message override must call its base implementation |
| ERS0002 | Warning | Do not cache a singleton `Instance` in a field that can outlive it |
| ERS0003 | Warning | Guard teardown access to a singleton's Unity members |
| ERS0004 | Warning | Do not access a singleton during MonoBehaviour construction |
| ERS0005 | Warning | Singleton message must be declared with `override` |
| ERS0006 | Warning | Null-conditional access on a lazy singleton's `Instance` is misleading |
| ERS0007 | Warning | Singleton base call is in the wrong position |
| ERS0008 | Warning | Do not read a lazy singleton's `Instance` from a serialization callback |
| ERS0009 | Warning | Do not set `hideFlags` on a singleton |

Every rule is a warning by default. ERS0001 and ERS0004 describe outright breakage and would
justify errors, but these rules arrive with your first reference to the package rather than by
opt-in, and a false positive should not break a build you never asked to have linted. Escalate
them locally — see *Retuning severities* below.

---

## ers0001 — override must call base

The base `Awake` claims the singleton slot and destroys duplicates; the base `OnDestroy` releases
it. An override that does not chain leaves a singleton that is never registered, or a static
reference to a destroyed object.

```csharp
protected override void Awake()
{
    base.Awake();          // required
    _mixer = GetComponent<AudioMixer>();
}
```

The check is flow-insensitive: a `base.Awake()` anywhere in the declaration satisfies it, including
inside a branch. Proving the call always executes would trade real false negatives for marginal
precision.

## ers0002 — do not cache Instance in a field

`Instance` discards references captured in a previous play session and collapses Unity's
destroyed-object wrapper into a real null. A field copy does neither, so once the singleton is
replaced the field points at an object that no longer exists. The rule reports a field that can
outlive the singleton it holds:

- **a static field** — with domain reload disabled it carries into the next play session;
- **a field on anything that is not a `Component`** — a plain class or a `ScriptableObject` is not
  tied to a scene, so nothing ends its lifetime alongside the singleton's;
- **a serialized field** (`[SerializeField]`, or public without `[NonSerialized]`) — in edit mode it
  can write a reference to a transient, never-saved singleton into the scene;
- **a field holding a singleton that is not persistent** — `MonoBehaviourSingleton<T>` dies with
  its scene, and the field keeps the dead object.

What remains is not reported: a private, non-serialized field on a `Component`, holding a
`MonoBehaviourSingletonPersistent<T>`. The holder
dies with its scene or at quit, and the singleton outlives both. Locals are not reported either:

```csharp
private AudioBus _bus;             // AudioBus is persistent: fine

private void Start()  { _bus = AudioBus.Instance; }

private void Update()
{
    var bus = AudioBus.Instance;   // fine
    bus.Play(clip);
}
```

## ers0003 — guard teardown access to Unity members

During teardown — application quit, or the frame in which a scene unload destroyed it — `Instance`
returns the component that held the slot, still a live C# object after its native peer is gone.

**Calling your own members on it is safe and is not reported.** This is the common teardown shape
and it needs no guard:

```csharp
private void OnDestroy()
{
    AudioBus.Instance.Unregister(this);   // clean: Unregister is yours, and touches managed state
}
```

Members declared by `UnityEngine` are the exception. `transform`, `gameObject`, `enabled`,
`StartCoroutine`, `name` and the rest reach the native object and raise
`MissingReferenceException`, so those are reported:

```csharp
private void OnDestroy()
{
    AudioBus.Instance.StartCoroutine(Fade());   // ERS0003
}
```

Guard those with either of:

```csharp
if (AudioBus.IsAvailable) AudioBus.Instance.StartCoroutine(Fade());
if (AudioBus.TryGetInstance(out var bus)) bus.StartCoroutine(Fade());
```

**Not `AudioBus.Instance?.StartCoroutine(...)`.** `?.` tests the reference rather than Unity's `==`
overload, and the destroyed component is a live C# object, so the call proceeds. It reads as a
guard and is not one, which is why it is reported rather than exempt.

The whole method is exempted when it mentions `IsAvailable` or `TryGetInstance` anywhere —
deliberately crude, so the exemption is predictable rather than dependent on the analyzer's flow
analysis agreeing with yours.

**The limit.** Only the member named directly on `Instance` is read. A method of your own that
itself touches `transform` is invisible to the rule, and will still throw. Narrowing this way was
a deliberate trade: the previous version reported every teardown dereference, including the safe
majority, and a rule that fires mostly on correct code gets switched off wholesale.

`OnDisable` is not reported at all. It runs during teardown, but also throughout ordinary play —
`SetActive(false)`, a disabled component, a pooled object returning to its pool — and the analyzer
cannot tell the two apart.

## ers0004 — no access during construction

Field initializers and constructors on a `MonoBehaviour` run on Unity's deserialization path, where
`FindObjectsByType` and `new GameObject` throw. Move the access to `Awake`, `OnEnable` or `Start`.
Static field initializers are reported as ERS0002 instead, since their hazard is staleness rather
than timing.

## ers0005 — declare the message with `override`

Unity invokes the most-derived declaration of a magic method by name. `private void Awake()` in a
singleton subclass compiles with only a hiding warning, and the base registration silently never
runs — the same end state as ERS0001, reached by a different mistake.

---

## ers0006 — no null-conditional on a lazy Instance

`MonoBehaviourSingleton<T>` and `MonoBehaviourSingletonPersistent<T>` resolve, create, or throw.
Their `Instance` does not return null while the application is running, and during teardown it
returns the destroyed component — which `?.` does not stop, because the operator tests the
reference rather than consulting Unity's `==` overload.

So on these flavours `?.` is dead outside teardown and useless inside it, while telling every
reader that the value may be null. Dereference directly:

```csharp
AudioBus.Instance.Play(clip);        // not AudioBus.Instance?.Play(clip)
```

Inside `OnDestroy` or `OnApplicationQuit` the same expression is reported as ERS0003 instead —
one diagnostic, the more serious reading.

## ers0007 — put the base call in the right place

ERS0001 asks only whether the base call happens. This asks where. Both of the following satisfy
ERS0001 and are still wrong:

```csharp
protected override void Awake()
{
    _mixer = GetComponent<AudioMixer>();   // runs even on a duplicate about to destroy itself
    base.Awake();                          // ERS0007 — claims the slot too late
}

protected override void OnDestroy()
{
    base.OnDestroy();                      // ERS0007 — releases the slot too early
    _sources.Clear();                      // IsCurrentInstance is already false here
}
```

`base.Awake()` claims the slot and destroys duplicates, so it belongs **first**. `base.OnDestroy()`
releases the slot, so it belongs **last**:

```csharp
protected override void Awake()     { base.Awake(); _mixer = GetComponent<AudioMixer>(); }
protected override void OnDestroy() { _sources.Clear(); base.OnDestroy(); }
```

Only a base call that is a whole statement directly in the method body is considered. One nested
in an `if`, a loop or a local function is left alone — ERS0001 deliberately accepts those, and
their position is not a simple ordering question. An expression-bodied override is first and last
at once, so it is never reported.

## ers0008 — no lazy `Instance` in serialization callbacks

`OnValidate`, `OnBeforeSerialize` and `OnAfterDeserialize` run on Unity's serialization path, where
object lookup and creation are unsupported and can throw. The first read of a lazy singleton's
`Instance` may do both:

```csharp
private void OnValidate()
{
    AudioBus.Instance.SetVolume(_volume);   // ERS0008
}
```

Defer the work out of the callback, or move it to `Awake`, `OnEnable` or `Start`:

```csharp
private void OnValidate()
{
    UnityEditor.EditorApplication.delayCall += () => AudioBus.Instance.SetVolume(_volume);
}
```

`OnValidate` is reported on any `UnityEngine.Object`, the other two on any type implementing
`ISerializationCallbackReceiver`. A read inside a lambda, anonymous method or local function is
not reported, since that is how the deferral is written and the analyzer cannot tell a deferred
delegate from one invoked on the spot.

## ers0009 — do not set `hideFlags` on a singleton

The package owns these flags. Unity's find APIs skip objects flagged `DontSave`, so setting it on a
scene-authored singleton hides it from `Instance`, which then creates a second one. Clearing it on
an edit-mode transient lets that transient be saved into the open scene or prefab.

```csharp
hideFlags = HideFlags.DontSave;                          // ERS0009, inside the singleton
AudioBus.Instance.gameObject.hideFlags = HideFlags.None; // ERS0009, from anywhere
```

Plain and compound assignments (`|=`, `&=`, `^=`) are reported, on the component and on its
`gameObject`, when spelled directly. A `GameObject` reached through a local variable or a field is
not followed. Reading `hideFlags` is fine.

## ers0010 — removed

Removed in 3.0.0 along with `MonoBehaviourSingletonPassive<T>` and
`MonoBehaviourSingletonPassivePersistent<T>`, the only flavours it applied to. It reported a passive
`Instance` read from another object's `Awake` or `OnEnable`, which raced the singleton's own
`Awake`. `[SingletonCreation(SingletonCreationPolicy.FindOnly)]`, their replacement, resolves by
scene search and has no such race. See [migrating from the passive
flavors](singletons.md#migrating-from-the-passive-flavors).

## Retuning severities

Place a `Default.ruleset` in your project's `Assets` root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<RuleSet Name="Project rules" ToolsVersion="16.0">
  <Rules AnalyzerId="ERS.Singleton.Analyzers" RuleNamespace="EntropyReductionServices.Analyzers">
    <Rule Id="ERS0001" Action="Error" />
    <Rule Id="ERS0004" Action="Error" />
    <Rule Id="ERS0002" Action="None" />
  </Rules>
</RuleSet>
```

Per-assembly overrides use `[AssemblyName].ruleset` alongside the `.asmdef`. Single sites use
`#pragma warning disable ERS0003` as usual.

## Turning them off

`Action="None"` disables a rule outright. There is no hard feeling about this — the rules ship
enabled because they arrive with your first reference to the package, not because you are expected
to keep all of them.

```xml
<?xml version="1.0" encoding="utf-8"?>
<RuleSet Name="Project rules" ToolsVersion="16.0">
  <Rules AnalyzerId="ERS.Singleton.Analyzers" RuleNamespace="EntropyReductionServices.Analyzers">
    <Rule Id="ERS0001" Action="None" />
    <Rule Id="ERS0002" Action="None" />
    <Rule Id="ERS0003" Action="None" />
    <Rule Id="ERS0004" Action="None" />
    <Rule Id="ERS0005" Action="None" />
    <Rule Id="ERS0006" Action="None" />
    <Rule Id="ERS0007" Action="None" />
  </Rules>
</RuleSet>
```

**Use a ruleset, not `.editorconfig`.** `dotnet_diagnostic.ERS0001.severity` is the modern idiom
and your IDE will honour it, but Unity ignores `.editorconfig` when it runs analyzers through the
Editor ([issue 14549](https://discussions.unity.com/t/editorconfig-files-are-ignored-when-a-roslyn-analyzer-is-running-through-the-editor-14549/1727662)).
Configure severities there and they will appear to work in Rider or Visual Studio while the Unity
Console keeps reporting the originals. This repository maintains both files for exactly that
reason, and keeps them in sync mechanically — see `scripts/sync-analyzer-severities.py`.

## Rebuilding

Source lives in `Analyzers~/` — the trailing tilde keeps Unity from importing and trying to compile
files that reference Roslyn.

```zsh
brew install --cask dotnet-sdk        # if needed
cd Analyzers~
dotnet build -c Release
cp bin/Release/ERS.Singleton.Analyzers.dll ../Runtime/Analyzers/
```

CI rebuilds the analyzer and fails if the committed DLL does not match the sources, which is the
most likely way this package would ship rules that disagree with its own documentation.

The project targets `netstandard2.0` and pins `Microsoft.CodeAnalysis.CSharp` to 3.8.0, which
Unity's 2021.3 and 2022.3 documentation names as the required version. Unity 6 documentation
specifies 4.3; an analyzer built against the older Roslyn loads on the newer host, so the 3.8 build
covers both. To build against 4.3 instead:

```zsh
dotnet build -c Release -p:RoslynVersion=4.3.0
```

`PrivateAssets="all"` on the package reference is load-bearing: shipping `Microsoft.CodeAnalysis.*`
next to the analyzer breaks the host's assembly resolution.
