using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EntropyReductionServices.Singletons.PlayModeTests
{
    /// <summary>
    /// The Resources leg of the lazy resolution chain, which only a real prefab asset can reach:
    /// Instantiate runs Awake on the stack, so anything the singleton's own Awake does to the slot
    /// happens before the creating code has finished with it.
    ///
    /// The fixture prefab lives in the harness project rather than the package, so a consumer
    /// running these tests does not need it — and does not ship it. Absent, the test says so
    /// instead of failing.
    /// </summary>
    public class ResourceCreationTests
    {
        [TearDown]
        public void TearDown()
        {
            Probes.PurgeAll();
        }

        [UnityTest]
        public IEnumerator Instance_FromASharedResourcePrefab_KeepsWhatAwakeClaimed()
        {
            if (Resources.Load<ResourceSharedRoot>(nameof(ResourceSharedRoot)) == null)
                Assert.Ignore($"Assets/Resources/{nameof(ResourceSharedRoot)}.prefab is not present.");

            var created = ResourceSharedRoot.Instance;
            Assert.IsTrue(created != null, "the Resources leg should have produced an instance");

            // The rebuilt instance lives on an object of its own, in DontDestroyOnLoad. The
            // component Instantiate returned was destroyed on the way there, and assigning it back
            // over the slot used to cost the singleton its own instance at end of frame.
            Assert.IsTrue(Probes.IsPersistent(created.gameObject),
                "the instance the slot holds should be the persisted one");

            yield return null;   // deferred Destroy of the discarded component lands here

            Assert.IsTrue(ResourceSharedRoot.Exists, "the slot should have survived that destroy");
            Assert.AreSame(created, ResourceSharedRoot.Instance, "and still hold the same instance");

            // Scene objects only: FindObjectsOfTypeAll also returns the prefab asset itself.
            var live = System.Array.FindAll(
                Resources.FindObjectsOfTypeAll<ResourceSharedRoot>(),
                probe => probe.gameObject.scene.IsValid());
            Assert.AreEqual(1, live.Length, "a lost slot resolves again and leaves a second instance");

            // The rebuild takes the component, not the object it was sharing.
            var leftover = GameObject.Find($"{nameof(ResourceSharedRoot)}(Clone)");
            Assert.IsTrue(leftover != null, "the instantiated prefab root should still be there");
            Assert.IsTrue(leftover.GetComponent<BoxCollider>() != null, "along with its siblings");
            Object.DestroyImmediate(leftover);   // nothing on it is a probe, so PurgeAll cannot
        }
    }
}
