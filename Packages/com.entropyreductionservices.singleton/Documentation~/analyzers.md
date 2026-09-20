# Singleton analyzers

Five rules enforcing [the contract](contract.md). They ship
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
| ERS0006 | Warning | Null-conditional access on a lazy singleton's `Instance` is misleading |

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
```

**Not `AudioBus.Instance?.Stop()`.** `?.` tests the reference rather than Unity's `==` overload,
and during teardown `Instance` hands back the destroyed component — a live C# object — so the call
proceeds. It reads as a guard and is not one, which is why it is reported rather than exempt.

Both a plain dereference and a `?.` dereference are reported; a bare read that is passed along or
compared is not. The whole method is exempted when it mentions `IsAvailable`
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

**Passive singletons are not reported.** `MonoBehaviourSingletonPassive<T>.Instance` is null until
some component's `Awake` claims the slot, so `?.` there is a correct guard and the rule stays
silent:

```csharp
ScoreBoard.Instance?.Refresh();      // fine: passive, may genuinely be null
```

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
