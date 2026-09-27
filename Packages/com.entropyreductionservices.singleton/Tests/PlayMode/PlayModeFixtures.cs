using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EntropyReductionServices.Singletons.PlayModeTests
{
    /// <summary>
    /// Probe types and helpers for the play-mode half of the contract.
    ///
    /// Unlike the EditMode fixtures these are NOT [ExecuteAlways]: in play mode Unity calls Awake
    /// on its own, which is the arrangement the package actually ships for. One type per test,
    /// for the same reason as in EditMode — the cache is a static on the closed generic type and
    /// the whole run shares one domain.
    /// </summary>
    internal static class Probes
    {
        /// <summary>Hoisted out of the sweep below, which runs over every live MonoBehaviour.</summary>
        private static readonly System.Reflection.Assembly OwnAssembly = typeof(Probes).Assembly;

        /// <summary>
        /// Destroys every probe left behind by a test. DestroyImmediate rather than Destroy so
        /// OnDestroy — and therefore the slot release — happens before the next test starts,
        /// instead of at the end of the frame.
        ///
        /// Sweeps by assembly rather than by an explicit type list, so a probe added without a
        /// matching teardown line cannot silently leak static state into the next test.
        /// </summary>
        public static void PurgeAll()
        {
            foreach (var probe in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (probe == null) continue;
                if (probe.GetType().Assembly != OwnAssembly) continue;
                if (!probe.gameObject.scene.IsValid()) continue;
                Object.DestroyImmediate(probe.gameObject);
            }
        }

        /// <summary>
        /// Creates a probe inside a scene of our own, so a later unload of that scene is a real
        /// test of survival rather than a test of the test runner's scene.
        /// </summary>
        public static T AuthorIn<T>(Scene scene, string name) where T : MonoBehaviour
        {
            var owner = new GameObject(name);
            SceneManager.MoveGameObjectToScene(owner, scene);
            return owner.AddComponent<T>();   // Awake runs here
        }

        /// <summary>True when the object sits in Unity's DontDestroyOnLoad scene.</summary>
        public static bool IsPersistent(GameObject go) =>
            go != null && go.scene.buildIndex == -1 && go.scene.name == "DontDestroyOnLoad";

        /// <summary>
        /// Unloads a scene and gives deferred destruction a frame to land — the trailing yield is
        /// load-bearing for the end-of-frame Destroy assertions, not padding.
        /// </summary>
        public static IEnumerator UnloadAndWait(Scene scene)
        {
            yield return SceneManager.UnloadSceneAsync(scene);
            yield return null;
        }
    }

    internal class PersistentSurvives : MonoBehaviourSingletonPersistent<PersistentSurvives> { }
    internal class PersistentGoesToDdol : MonoBehaviourSingletonPersistent<PersistentGoesToDdol> { }
    internal class PersistentDetaches : MonoBehaviourSingletonPersistent<PersistentDetaches> { }
    internal class PersistentDeferredDedupe : MonoBehaviourSingletonPersistent<PersistentDeferredDedupe> { }
    internal class PersistentFrame : MonoBehaviourSingletonPersistent<PersistentFrame> { }

    [SingletonCreation(SingletonCreationPolicy.FindOnly)]
    internal class FindOnlyAbsentInPlay : MonoBehaviourSingleton<FindOnlyAbsentInPlay> { }

    [SingletonCreation(SingletonCreationPolicy.FindOnly)]
    internal class FindOnlyAvailableInPlay : MonoBehaviourSingleton<FindOnlyAvailableInPlay> { }

    [SingletonCreation(SingletonCreationPolicy.FindOnly)]
    internal class PersistentFindOnly : MonoBehaviourSingletonPersistent<PersistentFindOnly> { }

    /// <summary>Records whether its own Awake has run, so a reader can prove it got there first.</summary>
    internal class PersistentReadBeforeAwake : MonoBehaviourSingletonPersistent<PersistentReadBeforeAwake>
    {
        public static bool AwakeRan;

        protected override void Awake()
        {
            base.Awake();
            AwakeRan = true;
        }
    }

    /// <summary>Reads the singleton from its own Awake and records whether that beat the singleton's.</summary>
    internal class ReadsPersistentInAwake : MonoBehaviour
    {
        public bool ReadBeforeSingletonAwake { get; private set; }
        public PersistentReadBeforeAwake Seen { get; private set; }

        private void Awake()
        {
            ReadBeforeSingletonAwake = !PersistentReadBeforeAwake.AwakeRan;
            Seen = PersistentReadBeforeAwake.Instance;
        }
    }

    internal class PersistentDuplicate : MonoBehaviourSingletonPersistent<PersistentDuplicate> { }

    // --- Scene-unload teardown ------------------------------------------------------------------

    /// <summary>
    /// Witnesses the teardown window from inside its own OnDestroy, which is the only vantage
    /// point that sees it: the window is a frame stamp, and a test resuming after
    /// UnloadSceneAsync is already on a later frame. Reading Instance here — immediately after
    /// base.OnDestroy has released the slot — reproduces exactly the hazard the guard exists for.
    ///
    /// Generic so each test gets its own closed type, and therefore its own statics, without the
    /// body being copied per test. Both the singleton cache and the counters below are statics on
    /// the closed generic, so two tests sharing one concrete type would see each other's state.
    /// </summary>
    internal abstract class TeardownWitness<T> : MonoBehaviourSingleton<T> where T : TeardownWitness<T>
    {
        public static bool SawWindow;
        public static bool WasAvailable = true;
        public static bool GotTombstone;
        public static bool GotFreshObject;
        public static int Destroys;

        /// <summary>True when the read inside OnDestroy is wanted; off for ordinary-destroy tests,
        /// where resurrecting is correct behaviour and would leak a probe into the next test.</summary>
        protected virtual bool ReadsInstance => true;

        // ERS0007 wants base.OnDestroy() last, and is right for ordinary code. This probe is the
        // exception it cannot know about: everything below deliberately observes the state AFTER
        // the slot has been released, because that is the moment the hazard occurs. Moving the
        // base call to the end would measure the wrong instant and silently pass every test.
#pragma warning disable ERS0007
        protected override void OnDestroy()
        {
            base.OnDestroy();                       // releases the slot, reports the unload

            Destroys++;
            SawWindow = SingletonRuntime.IsUnloadingScene;
            WasAvailable = IsAvailable;

            if (!ReadsInstance) return;

            var got = Instance;                     // the read that used to resurrect
            GotTombstone = ReferenceEquals(got, this);
            GotFreshObject = !ReferenceEquals(got, this) && !ReferenceEquals(got, null);
        }
#pragma warning restore ERS0007
    }

    internal class UnloadTombstoneWitness : TeardownWitness<UnloadTombstoneWitness> { }
    internal class UnloadAvailabilityWitness : TeardownWitness<UnloadAvailabilityWitness> { }

    internal class UnloadIndividualWitness : TeardownWitness<UnloadIndividualWitness>
    {
        protected override bool ReadsInstance => false;
    }

    internal class UnloadWindowCloses : MonoBehaviourSingleton<UnloadWindowCloses> { }
    internal class UnloadDdolSurvivesFilter : MonoBehaviourSingletonPersistent<UnloadDdolSurvivesFilter> { }

    // The duplicate and the lone singleton need a flavour that destroys duplicates, which only the
    // persistent one does. The bystander must not persist, or it would rebuild itself on a new
    // object and the test could not tell that from being destroyed, so it is the plain flavour.
    internal class SharedObjectDuplicate : MonoBehaviourSingletonPersistent<SharedObjectDuplicate> { }
    internal class SharedObjectBystander : MonoBehaviourSingleton<SharedObjectBystander> { }
    internal class SharedObjectAlone : MonoBehaviourSingletonPersistent<SharedObjectAlone> { }

    // Extraction probes. Stateless: nothing Unity would serialize, so rebuilding on a dedicated
    // GameObject loses nothing and the runtime is allowed to do it.
    internal class ExtractStateless : MonoBehaviourSingletonPersistent<ExtractStateless> { }

    // Has a serialized field, so extraction must decline and persist the shared GameObject instead.
    internal class ExtractStateful : MonoBehaviourSingletonPersistent<ExtractStateful>
    {
        [SerializeField] private int _configured;
        public int Configured => _configured;
    }

    internal class AutoPlayMode : MonoBehaviourSingleton<AutoPlayMode> { }
    internal class AutoDiesWithScene : MonoBehaviourSingleton<AutoDiesWithScene> { }
    internal class AutoAvailable : MonoBehaviourSingleton<AutoAvailable> { }
}
