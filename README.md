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
    private AudioSource _source;

    protected override void Awake()
    {
        base.Awake();                       // claims the slot, destroys duplicates
        _source = GetComponent<AudioSource>();
    }
}
```

```csharp
AudioBus.Instance.Play(clip);               // a live singleton exists while the app is running

private void OnDestroy()
{
    // During teardown Instance returns the destroyed component rather than null, so managed
    // calls are safe. Guard anything that touches the GameObject.
    if (AudioBus.TryGetInstance(out var bus)) bus.Unregister(this);
}
```

## The contract

> A live singleton exists for the entire time the application is running. `Instance` never returns
> null once the singleton has existed: during teardown it hands back the destroyed component rather
> than creating a replacement.

Not recreating is not negotiable — recreating a singleton during teardown leaks objects into an
unloading scene and, on device, can touch XR or audio subsystems that have already shut down. The
destroyed component is a live C# object, so managed calls on it are safe, but anything touching
its GameObject throws. So teardown code asks first, via `IsAvailable` or `TryGetInstance` — not
`?.`, which tests the reference rather than Unity's `==` overload and so is not a guard here.
Everywhere else, dereference directly.

On the auto-creating flavors, anything other than teardown that leaves `Instance` unresolvable
throws `MissingSingletonException` naming the type and the reason, so a configuration mistake fails
where you made it rather than as a `NullReferenceException` in unrelated code forty frames later.
Passive flavors never create, so their `Instance` is null until an authored instance claims the
slot.

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

The OpenUPM listing is pending; the first version published there will be 2.6.0. Once it is live:

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
    "com.entropyreductionservices.singleton": "2.6.0"
  }
}
```

### Tarball

Every release from 2.6.0 attaches a signed `.tgz` to its GitHub Release. Drop it in or beside
your project and reference it by relative path:

```json
"com.entropyreductionservices.singleton": "file:../Packages/com.entropyreductionservices.singleton-2.6.0.tgz"
```

Both routes give a versioned, resolvable dependency that upgrades and rolls back cleanly.

## Analyzers

Seven Roslyn rules ship with the package and apply to your assembly automatically once it
references `EntropyReductionServices.Singletons` — no asset labels, no manifest entries, nothing
copied into your `Assets` folder. They catch the contract violations that are statically
checkable: a missing or misplaced `base.Awake()`, caching `Instance` in a field, unguarded teardown
access, `?.` on a lazy `Instance`, access during `MonoBehaviour` construction, and `Awake` declared
without `override`.

All seven ship as warnings, because they arrive with your first reference to the package rather
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

Unity 6.3 LTS (6000.3) and newer. 6.3 is the floor because it is the oldest Unity still under
support once 6.0 LTS ends in October 2026; 2022 LTS ended in May 2025. Older editors are likely to
work, since nothing here uses an API newer than 2021.3, but they are not tested and so are not
claimed.

CI runs the EditMode and PlayMode suites on the floor, 6000.3.24f1, on every push to `main` and
every tag. Each release is also gated by a local run on the project's own editor (6000.5).

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
