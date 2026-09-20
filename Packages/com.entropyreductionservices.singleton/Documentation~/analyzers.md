# Singleton analyzers

Five rules enforcing the contract in the header of `Runtime/MonoBehaviourSingleton.cs`. They ship
as `Runtime/Analyzers/ERS.Singleton.Analyzers.dll` and apply to this package's assembly **and to
every assembly that references it** — that scoping is Unity's documented behaviour for an analyzer
sitting in or under a folder containing an `.asmdef`, and it is why the DLL lives beside the
runtime asmdef rather than at the package root. Consumers need no setup: no asset labels, no
manifest entries, nothing copied into their `Assets` folder.

| ID | Default | Rule |
|----|---------|------|
| ERS0001 | Warning | Singleton message override must call its base implementation |
| ERS0002 | Warning | Do not cache a singleton `Instance` in a field |
| ERS0003 | Warning | Guard singleton access in teardown callbacks |
| ERS0004 | Warning | Do not access a singleton during MonoBehaviour construction |
| ERS0005 | Warning | Singleton message must be declared with `override` |

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
destroyed-object wrapper into a real null. A field copy does neither, so it survives domain reload
and scene changes as a reference to an object that no longer exists.

Locals are not flagged — a value used within one method call is the intended usage:

```csharp
private void Update()
{
    var bus = AudioBus.Instance;   // fine
    bus.Play(clip);
}
```

## ers0003 — guard teardown access

A live singleton exists for the whole time the application is running, and none exists during
teardown — application quit, or the frame in which a scene unload destroyed it. `Instance` still
returns a reference then — the destroyed component, so plain C# calls on it work — but anything
touching the native peer (`transform`, `gameObject`, `StartCoroutine`) raises
`MissingReferenceException`. Inside `OnDestroy` and `OnApplicationQuit`, where that is a live
possibility, use one of:

```csharp
if (AudioBus.IsAvailable) AudioBus.Instance.Stop();
if (AudioBus.TryGetInstance(out var bus)) bus.Stop();
AudioBus.Instance?.Stop();
```

Only direct dereferences are reported. The whole method is exempted when it mentions `IsAvailable`
or `TryGetInstance` anywhere — deliberately crude, so the exemption is predictable rather than
dependent on the analyzer's flow analysis agreeing with yours.

`OnDisable` is not reported. It runs during teardown, but it also runs throughout ordinary play —
`SetActive(false)`, a disabled component, a pooled object returning to its pool — and the analyzer
cannot tell the two apart. It is also the case the shutdown allowance above is aimed at: an
`OnDisable` that unregisters itself from a singleton touches only managed state, and that now
works whether or not shutdown has begun.

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
