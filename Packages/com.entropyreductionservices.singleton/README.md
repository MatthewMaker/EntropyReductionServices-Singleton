# Unity Singletons

MonoBehaviour singleton base class that survives domain reload, scene loads and application
shutdown, with Roslyn analyzers that enforce its contract in consuming assemblies.

The usual Unity singleton is a static field and an `Awake`. That holds until you disable Domain
Reload and your statics outlive the play session, or the app quits and a lazy accessor resurrects
the singleton into a scene that is already unloading, or you mark a nested object
`DontDestroyOnLoad` and Unity quietly ignores you. This package handles all three and states
exactly what it guarantees.

## Quick start

```csharp
using System;
using System.Collections;
using EntropyReductionServices.Singletons;
using UnityEngine;

public class Timers : MonoBehaviourSingleton<Timers>
{
    public Coroutine After(float seconds, Action callback) => StartCoroutine(Run(seconds, callback));

    public void Cancel(Coroutine timer)
    {
        if (timer != null) StopCoroutine(timer);
    }

    private static IEnumerator Run(float seconds, Action callback)
    {
        yield return new WaitForSecondsRealtime(seconds);
        callback();
    }
}
```

```csharp
private Coroutine _respawn;

private void Start() => _respawn = Timers.Instance.After(2f, Respawn);   // created on first use

private void OnDestroy()
{
    // During teardown Instance hands back the destroyed component rather than null. StopCoroutine
    // reaches its native object, so guard the call; your own managed members need no guard.
    if (Timers.TryGetInstance(out var timers)) timers.Cancel(_respawn);
}
```

To extend `Awake` or `OnDestroy`, override them and call `base.Awake()` first and
`base.OnDestroy()` last: the base methods claim and release the slot. ERS0001, ERS0005 and
ERS0007 check this.

## Lifetime and creation

`MonoBehaviourSingleton<T>` resolves from the scene, then `Resources`, then a new `GameObject`,
claims the slot in `Awake`, destroys later duplicates, and lives until the application quits. Two
attributes adjust that:

- `[SingletonLifetime(SingletonLifetimePolicy.Scene)]` — die with the scene instead, for per-scene
  objects such as a level director.
- `[SingletonCreation(SingletonCreationPolicy.FindOnly)]` — never create; resolve from the scene
  only and throw if nothing was authored, for objects carrying inspector state or scene
  references.

## Analyzers

Ten rules ship as a precompiled analyzer and apply automatically to any assembly referencing
this package — no setup, no asset labels, nothing copied into `Assets`. They catch the mistakes
the compiler cannot: a missing `base.Awake()`, caching `Instance` in a field, unguarded teardown
access, construction-time access, `Awake` declared without `override`, a pointless `?.` on
`Instance`, a base call that runs in the wrong order, access from a serialization callback,
`hideFlags` set on a singleton, and a type argument that is not the type itself.

All ten ship as **warnings**, never errors, because they arrive with your first reference to the
package rather than by opt-in. Retune or switch any of them off with a `Default.ruleset` in your
`Assets` root — `<Rule Id="ERS0003" Action="None" />` disables one outright. See
[analyzers.md](Documentation~/analyzers.md#turning-them-off).

## Documentation

- [contract.md](Documentation~/contract.md) — the normative contract: guarantees, teardown
  semantics, constraints on your subclass, forbidden usages, known gaps.
- [singletons.md](Documentation~/singletons.md) — why the implementation is shaped this way, and
  how to choose a lifetime and creation policy.
- [analyzers.md](Documentation~/analyzers.md) — the ten rules, and how to change their severity.
- [CHANGELOG.md](CHANGELOG.md)

## Compatibility

Unity 6.3 LTS (6000.3) and newer. MIT licensed.
