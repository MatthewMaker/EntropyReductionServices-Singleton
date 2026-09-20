# Unity Singletons

MonoBehaviour singleton base classes that survive domain reload, scene loads and application
shutdown.

The usual Unity singleton is a static field and an `Awake`. That holds until you disable Domain
Reload and your statics outlive the play session, or the app quits and a lazy accessor resurrects
the singleton into a scene that is already unloading, or you mark a nested object
`DontDestroyOnLoad` and Unity quietly ignores you. This package handles all three, states exactly
what it guarantees, and ships analyzers that check callers hold up their end.

MIT licensed. Unity 6.3 LTS and newer.

```csharp
using EntropyReductionServices.Singletons;

public class AudioBus : MonoBehaviourSingletonPersistent<AudioBus>
{
    protected override void Awake()
    {
        base.Awake();                       // claims the slot, destroys duplicates
        _mixer = GetComponent<AudioMixer>();
    }
}
```

```csharp
AudioBus.Instance.Play(clip);               // non-null the whole time the app is running

private void OnDestroy()
{
    if (AudioBus.TryGetInstance(out var bus)) bus.Unregister(this);   // null once shutdown begins
}
```

## The contract

> `Instance` is non-null for the entire time the application is running. It is null only once
> shutdown has begun.

The exception is not negotiable — recreating a singleton during teardown leaks objects into an
unloading scene and, on device, can touch XR or audio subsystems that have already shut down. So
teardown code asks first, via `IsAvailable`, `TryGetInstance`, or `?.`. Everywhere else,
dereference directly.

Anything other than shutdown that would produce a null throws `MissingSingletonException` naming
the type and the reason, so a configuration mistake fails where you made it rather than as a
`NullReferenceException` in unrelated code forty frames later.

## Four flavors

| | Auto-creates | Survives scene load |
|---|---|---|
| `MonoBehaviourSingleton<T>` | yes | no |
| `MonoBehaviourSingletonPersistent<T>` | yes | yes |
| `MonoBehaviourSingletonPassive<T>` | no | no |
| `MonoBehaviourSingletonPassivePersistent<T>` | no | yes |

Auto-creating flavors resolve on demand — scene search, then `Resources`, then a bare GameObject —
and suit stateless services whose existence is an implementation detail. Passive flavors never
create anything and suit objects that must be authored, carrying inspector state or scene
references.

**[Read the full documentation →](Packages/com.entropyreductionservices.singleton/Documentation~/singletons.md)**
for choosing between them, the lifecycle walkthrough, edit-mode behaviour, configuration
attributes, and the things this deliberately will not do.

**[contract.md](Packages/com.entropyreductionservices.singleton/Documentation~/contract.md)** is
the normative contract: guarantees, teardown semantics, constraints on your subclass, forbidden
usages, side effects and known gaps. Read it before relying on any of them.

## Install

### OpenUPM

```zsh
openupm add com.entropyreductionservices.singleton
```

Or add the scoped registry to `Packages/manifest.json` directly:

```json
{
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.entropyreductionservices"]
    }
  ],
  "dependencies": {
    "com.entropyreductionservices.singleton": "1.0.0"
  }
}
```

### Tarball

Every release attaches a `.tgz`. Drop it in or beside your project and reference it by relative
path:

```json
"com.entropyreductionservices.singleton": "file:../Packages/com.entropyreductionservices.singleton-1.0.0.tgz"
```

Both routes give a versioned, resolvable dependency that upgrades and rolls back cleanly.

## Analyzers

Five Roslyn rules ship with the package and apply to your assembly automatically once it
references `EntropyReductionServices.Singletons` — no asset labels, no manifest entries, nothing
copied into your `Assets` folder. They catch the contract violations that are statically
checkable: a missing `base.Awake()`, caching `Instance` in a field, unguarded teardown access,
access during `MonoBehaviour` construction, and `Awake` declared without `override`.

All five ship as warnings, because they arrive with your first reference to the package rather
than by opt-in and should not break a build you did not ask to have linted. Escalating them in
your own project is a three-line `Default.ruleset`. See
[analyzers.md](Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md).

## Non-goals

Deliberately out of scope. Issues requesting these will be closed with a pointer back here:

- **Dependency injection or a service locator.** Use [VContainer](https://github.com/hadashiA/VContainer)
  or [Reflex](https://github.com/gustavopsantos/Reflex). A half-built registry alongside a real
  container is worse than either alone.
- **Initialization order between singletons.** Lazy resolution cannot give you deterministic
  construction order. If order matters, write an explicit bootstrapper — that is the right answer,
  not a limitation to work around.
- **ScriptableObject config singletons.** A different correctness problem, chiefly that in the
  editor `Instance` *is* the asset and runtime mutation writes to disk. Planned as a separate
  assembly with its own contract rather than bolted onto this one.
- **A general-purpose `Singleton<T>`.** The type name is the one most likely to already exist in
  your codebase or another package. Every type here is prefixed deliberately.

## Compatibility

Unity 6.3 LTS (6000.3) and newer; CI runs the test suites on 6000.3.24f1. 6.3 is the floor because
it is the oldest Unity still under support — 6.0 LTS ended in October 2026 and 2022 LTS in May
2025. Older editors are likely to work, since nothing here uses an API newer than 2021.3, but
they are not tested and so are not claimed.

The analyzer targets `netstandard2.0` against Microsoft.CodeAnalysis.CSharp 3.8. Unity 6
documentation specifies Roslyn 4.3, but an analyzer built against older Roslyn loads on newer
hosts, and building against 3.8 keeps it loadable on editors older than the supported floor.

## Developing

The repository is a Unity project with the package embedded at
`Packages/com.entropyreductionservices.singleton/`, so you can open the root folder in Unity and
iterate directly. Analyzer sources live in `Analyzers~/` — the trailing tilde keeps Unity from
importing and trying to compile files that reference Roslyn.

```zsh
brew install --cask dotnet-sdk
cd Packages/com.entropyreductionservices.singleton/Analyzers~
dotnet build -c Release
cp bin/Release/ERS.Singleton.Analyzers.dll ../Runtime/Analyzers/
```

Run the rule tests with `dotnet test` from `Analyzers~/Tests`. CI builds the analyzer, runs
those tests, and separately compiles a deliberately violating file against the *committed* DLL
so a binary that was never rebuilt fails the build.
