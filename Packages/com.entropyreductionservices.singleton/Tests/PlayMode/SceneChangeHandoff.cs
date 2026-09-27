namespace EntropyReductionServices.Singletons.PlayModeTests
{
    /// <summary>
    /// Authored in the harness's SceneChangeHandoff scene, and created at runtime in the scene a
    /// test loads it over. The next scene's instance must take the slot the old one releases —
    /// not find it still held and destroy itself as a duplicate.
    ///
    /// Alone in this file, unlike the other probes: Unity only creates the MonoScript a scene
    /// serializes a reference to when the file is named after the type.
    /// </summary>
    [SingletonLifetime(SingletonLifetimePolicy.Scene)]
    internal class SceneChangeHandoff : MonoBehaviourSingleton<SceneChangeHandoff> { }
}
