# Unity Singletons

MonoBehaviour singleton base classes that survive domain reload, scene loads and application
shutdown, with Roslyn analyzers that enforce the contract in consuming assemblies.

The usual Unity singleton is a static field and an `Awake`. That holds until you disable Domain
Reload and your statics outlive the play session, or the app quits and a lazy accessor resurrects
the singleton into a scene that is already unloading, or you mark a nested object
`DontDestroyOnLoad` and Unity quietly ignores you. This package handles all three and states
exactly what it guarantees.

## Quick start

```csharp
using EntropyReductionServices.Singletons;
using UnityEngine;

public class AudioBus : MonoBehaviourSingletonPersistent<AudioBus>
{
    private AudioSource _source;

    protected override void Awake()
    {
        base.Awake();                       // claims the slot, destroys duplicates
        _source = GetComponent<AudioSource>();
    }

    public void Play(AudioClip clip) => _source.PlayOneShot(clip);
    public void Stop() => _source.Stop();
}
```

```csharp
AudioBus.Instance.Play(clip);               // a live singleton exists while the app is running

private void OnDestroy()
{
    // During teardown Instance returns the destroyed component rather than null, so managed
    // calls are safe. Guard anything that touches the GameObject.
    if (AudioBus.TryGetInstance(out var bus)) bus.Stop();
}
```

## Four flavours

| Type | Creates itself | Survives scene load |
|---|---|---|
| `MonoBehaviourSingleton<T>` | yes | no |
| `MonoBehaviourSingletonPersistent<T>` | yes | yes |
| `MonoBehaviourSingletonPassive<T>` | no | no |
| `MonoBehaviourSingletonPassivePersistent<T>` | no | yes |

Auto flavours resolve from the scene, then `Resources`, then a new `GameObject`, and suit
stateless services whose existence is an implementation detail. Passive flavours never create
anything and suit objects that must be authored, carrying inspector state or scene references.

## Analyzers

Seven rules ship as a precompiled analyzer and apply automatically to any assembly referencing
this package — no setup, no asset labels, nothing copied into `Assets`. They catch the mistakes
the compiler cannot: a missing `base.Awake()`, caching `Instance` in a field, unguarded teardown
access, construction-time access, `Awake` declared without `override`, a pointless `?.` on a lazy
`Instance`, and a base call that runs in the wrong order.

All seven ship as **warnings**, never errors, because they arrive with your first reference to the
package rather than by opt-in. Retune or switch any of them off with a `Default.ruleset` in your
`Assets` root — `<Rule Id="ERS0003" Action="None" />` disables one outright. See
[analyzers.md](Documentation~/analyzers.md#turning-them-off).

## Documentation

- [contract.md](Documentation~/contract.md) — the normative contract: guarantees, teardown
  semantics, constraints on your subclass, forbidden usages, known gaps.
- [singletons.md](Documentation~/singletons.md) — why the implementation is shaped this way, and
  how to choose a flavour.
- [analyzers.md](Documentation~/analyzers.md) — the seven rules, and how to change their severity.
- [CHANGELOG.md](CHANGELOG.md)

## Compatibility

Unity 6.3 LTS (6000.3) and newer. MIT licensed.
