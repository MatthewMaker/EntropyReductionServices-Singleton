namespace EntropyReductionServices.Singletons.PlayModeTests
{
    /// <summary>
    /// Instantiated from Assets/Resources by the harness project rather than authored by a test:
    /// the fixture prefab's root carries a second component, so persisting this has to rebuild it
    /// on a GameObject of its own while Instantiate is still on the stack.
    ///
    /// Alone in this file, unlike the other probes: Unity only creates the MonoScript a prefab
    /// serializes a reference to when the file is named after the type.
    /// </summary>
    internal class ResourceSharedRoot : MonoBehaviourSingletonPersistent<ResourceSharedRoot> { }
}
