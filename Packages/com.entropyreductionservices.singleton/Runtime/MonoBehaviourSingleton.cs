// =============================================================================================
//  MonoBehaviourSingleton
//
//  The full contract — guarantees, teardown semantics, forbidden usages, known gaps — lives in
//  Documentation~/contract.md. It is kept there rather than here because a header this far from
//  the code it describes drifts out of date without anyone noticing.
//
//  Two rules the compiler will not enforce for you:
//    - CRTP: class Foo : MonoBehaviourSingletonPersistent<Foo>
//    - An Awake or OnDestroy override MUST call its base implementation. The base claims and
//      releases the singleton slot; skipping it leaves the type silently non-functional.
//
//  ERS0001 and ERS0005 flag the second one. Nothing flags the first, because it does not compile.
// =============================================================================================

//#define SINGLETON_DEBUG
//#define SINGLETON_DEBUG_GET

using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Conditional = System.Diagnostics.ConditionalAttribute;
using Object = UnityEngine.Object;

// ReSharper disable StaticMemberInGenericType
// ReSharper disable ClassWithVirtualMembersNeverInherited.Global
// ReSharper disable UnusedMember.Global

namespace EntropyReductionServices.Singletons
{
    /// <summary>
    /// Declares the Resources path a singleton should be instantiated from when one is not
    /// already present in the scene. Without this attribute the path defaults to the type name.
    /// </summary>
    /// <example><code>[SingletonResource("Audio/FX/GlobalSounds")] public class GlobalSounds : ...</code></example>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SingletonResourceAttribute : Attribute
    {
        public string Path { get; }
        public SingletonResourceAttribute(string path) => Path = path;
    }

    /// <summary>
    /// What an auto-creating singleton does when Instance is requested outside play mode and
    /// nothing exists yet.
    /// </summary>
    public enum SingletonEditModePolicy
    {
        /// <summary>
        /// Resolve or create on demand exactly as in play mode, but mark anything created with
        /// HideFlags.DontSave so it can never be written into the open scene or a prefab stage.
        /// Default: preserves the never-null contract of Instance.
        /// </summary>
        CreateTransient,

        /// <summary>
        /// Resolve from the loaded scenes only; never create. Outside play mode, reading Instance
        /// when no instance is in the scene throws MissingSingletonException rather than returning
        /// null.
        ///
        /// Guard with ExistsOrFindInScene(), which is the only accessor that searches. IsAvailable,
        /// Exists and TryGetInstance read the cache alone, so under this policy they all report
        /// false while an instance is sitting in the loaded scene, until something resolves it.
        /// </summary>
        FindOnly,

        /// <summary>
        /// Do not resolve at all outside play mode. Reading Instance there throws
        /// MissingSingletonException immediately, without looking for an instance, and
        /// ExistsOrFindInScene does not search either — so every accessor agrees that nothing is
        /// available, whatever is in the scene. Guard the call site with Application.isPlaying.
        /// </summary>
        Disabled
    }

    /// <summary>
    /// Opts a singleton out of edit-mode auto-creation. Use on types whose construction has side
    /// effects the editor should not trigger — XR session ownership, sockets, threads, audio
    /// devices — where a custom inspector drawing itself should not spin up hardware.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class SingletonEditModeAttribute : Attribute
    {
        public SingletonEditModePolicy Policy { get; }
        public SingletonEditModeAttribute(SingletonEditModePolicy policy) => Policy = policy;
    }

    /// <summary>
    /// Thrown when Instance is required but cannot be produced for a reason that is a programming
    /// or configuration error rather than a lifecycle fact — currently, requesting an instance from
    /// a type that opted out of edit-mode resolution via [SingletonEditMode].
    /// Deliberately not thrown during application shutdown: that case returns null, because no
    /// amount of correct calling code can avoid it.
    /// </summary>
    public sealed class MissingSingletonException : InvalidOperationException
    {
        public MissingSingletonException(string message) : base(message) { }
    }

    /// <summary>
    /// Non-generic session tracker shared by every singleton.
    ///
    /// Why this exists: Unity does NOT invoke [RuntimeInitializeOnLoadMethod] on generic types,
    /// so a generic base class cannot reset its own statics. With "Enter Play Mode Options ->
    /// Reload Domain" disabled (which you want for iteration speed), statics survive between play
    /// sessions, which used to leave `applicationIsQuitting == true` forever and permanently break
    /// every singleton on the second play session.
    ///
    /// SessionId is bumped at SubsystemRegistration (earliest runtime hook, before any Awake).
    /// Generic singletons compare the session their cached instance was captured in against this
    /// value and discard anything stale, which is equivalent to a static reset without reflection.
    /// </summary>
    public static class SingletonRuntime
    {
        /// <summary>Monotonic id for the current play session. Changes on every entry to play mode.</summary>
        public static int SessionId { get; private set; }

        /// <summary>
        /// True once the player (or the editor play session) has begun shutting down. Driven by
        /// Application.quitting rather than inferred from hideFlags, so it is actually reliable.
        /// </summary>
        public static bool IsQuitting { get; private set; }

        /// <summary>
        /// True when the singleton type declares anything Unity would serialize, ignoring members
        /// of the singleton base classes themselves.
        ///
        /// Decides whether a persistent singleton on a shared GameObject may be rebuilt on an object of
        /// its own: with no serialized state there is nothing for the rebuild to lose. Non-generic
        /// so the editor-side validator can ask exactly the same question the runtime asks, rather
        /// than keeping a second copy of the rule that drifts.
        ///
        /// Deliberately conservative — any public or [SerializeField] instance field counts,
        /// without asking whether its type is actually serializable. Answering "has state" when it
        /// does not merely declines a rebuild; the opposite would discard authored data.
        /// </summary>
        public static bool DeclaresSerializedFields(Type type)
        {
            const BindingFlags Declared =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (var current = type; current != null; current = current.BaseType)
            {
                if (current.IsGenericType &&
                    current.GetGenericTypeDefinition() == typeof(MonoBehaviourSingletonBase<>))
                    break;

                foreach (var field in current.GetFields(Declared))
                {
                    if (field.IsNotSerialized) continue;               // [NonSerialized]
                    if (field.IsInitOnly || field.IsLiteral) continue; // readonly and const are not serialized

                    if (field.IsPublic || field.IsDefined(typeof(SerializeField), true)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The frame in which a singleton was last seen being destroyed by a scene unload.
        /// A frame stamp rather than a flag: it expires on its own at the frame boundary, so no
        /// event can be missed in a way that leaves resurrection permanently disabled. That
        /// failure would be silent and would only surface on the second scene load.
        /// </summary>
        private static int s_unloadFrame = -1;

        /// <summary>
        /// True for the remainder of the frame in which a scene unload destroyed a singleton.
        ///
        /// Unity offers no "scene is about to unload" event — sceneUnloaded fires after the fact —
        /// but a component can tell the difference from inside its own OnDestroy: a scene being
        /// unloaded reports isLoaded == false, while an individually destroyed object's scene
        /// still reports true. The singleton reports what it sees there, and this is where it
        /// lands.
        /// </summary>
        public static bool IsUnloadingScene => s_unloadFrame >= 0 && s_unloadFrame == Time.frameCount;

        /// <summary>
        /// True while the singleton must not resurrect itself: the application is quitting, or a
        /// scene unload is in progress this frame. Creating a singleton in either window drops a
        /// GameObject into a scene that is going away and runs Awake against subsystems that may
        /// already be shutting down.
        /// </summary>
        public static bool IsTearingDown => IsQuitting || IsUnloadingScene;

        /// <summary>
        /// Records that a singleton was destroyed by a scene unload. Called by the singleton base
        /// from OnDestroy; there is no public event that fires early enough to do this for us.
        /// </summary>
        internal static void NotifySceneUnloading() => s_unloadFrame = Time.frameCount;

        /// <summary>
        /// Opens a new session and (re)arms the quit hook. The unsubscribe-then-subscribe pair
        /// keeps the delegate list at one entry when the domain is not reloaded.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void BeginSession()
        {
            SessionId++;
            IsQuitting = false;
            s_unloadFrame = -1;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting() => IsQuitting = true;
    }

    /// <summary>
    /// Shared plumbing for the singleton flavours below.
    ///
    /// The type parameter is constrained CRTP-style (T must be the concrete subclass) so that
    /// `_instance` is statically known to be a MonoBehaviourSingletonBase<T>.
	/// </summary>
    /// <remarks>Usage: <c>public class Foo : MonoBehaviourSingletonPersistent<Foo> { }</c></remarks>
    [DisallowMultipleComponent]
    public abstract class MonoBehaviourSingletonBase<T> : MonoBehaviour where T : MonoBehaviourSingletonBase<T>
    {
        private static T s_instance;
        private static T s_lastKnown;
        private static int s_instanceSession = -1;
        private static bool s_resourceProbed;
        private static string s_resourcePath;

        /// <summary>
        /// The frame in which this object became the singleton. Debug aid only.
        /// Deliberately a property: the old public field was serialized into scenes and prefabs
        /// and shipped stale frame numbers in the build.
        /// </summary>
        public int FramePromoted { get; private set; }

        /// <summary>True when this component is the live singleton. Reference equality, so it is
        /// safe to call from OnDestroy where Unity's == overload is misleading.</summary>
        protected bool IsCurrentInstance => ReferenceEquals(s_instance, this);

        /// <summary>
        /// Session-guarded accessor for the cached instance. Reading it discards an instance
        /// captured in a previous play session, and collapses Unity's "fake null" wrapper (a
        /// managed object whose native peer is gone) into a real null so callers can use
        /// ReferenceEquals and ?. safely.
        /// </summary>
        protected static T Current
        {
            get
            {
                EnsureSession();

                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (s_instance == null) s_instance = null; // Unity operator== -> drop the dead wrapper
                return s_instance;
            }
            private set
            {
                s_instance = value;
                s_instanceSession = SingletonRuntime.SessionId;
                if (value != null)
                {
                    s_lastKnown = value;
                    value.FramePromoted = Time.frameCount;
                }
            }
        }

        /// <summary>
        /// Discards everything captured in a previous play session. Split out of Current so that
        /// LastKnown can apply the same guard without going through the fake-null collapse.
        /// </summary>
        private static void EnsureSession()
        {
            if (s_instanceSession == SingletonRuntime.SessionId) return;

            s_instance = null;
            s_lastKnown = null;
            s_instanceSession = SingletonRuntime.SessionId;
            s_resourceProbed = false;
            s_warnedUnavailable = false;
        }

        /// <summary>
        /// The most recent component to hold the slot, still reachable after its native peer is
        /// gone. Deliberately NOT put through the fake-null collapse that Current applies.
        ///
        /// This is what Instance hands back during shutdown. Returning the destroyed component
        /// rather than null lets teardown code — the recurring case being an OnDisable that
        /// unregisters itself from a manager — call plain C# members without a
        /// NullReferenceException. Unity's == overload still reports the result as null, so
        /// existing `!= null` checks and `?.` keep exactly the meaning they had.
        ///
        /// The limit is real and not fixable here: members that touch the native peer
        /// (transform, gameObject, enabled, StartCoroutine) raise MissingReferenceException
        /// instead. Null only when the singleton never existed this session.
        /// </summary>
        protected static T LastKnown
        {
            get
            {
                EnsureSession();
                return s_lastKnown;
            }
        }

        /// <summary>Pure existence check. Does not search, create, or rename anything.</summary>
        public static bool Exists => Current != null;

        /// <summary>Searches the scene if nothing is cached yet, then reports existence.</summary>
        public static bool ExistsOrFindInScene()
        {
            MaybeFindInScene();
            return Current != null;
        }

        /// <summary>Non-throwing accessor for callers that legitimately run during teardown.</summary>
        public static bool TryGetInstance(out T instance)
        {
            instance = Current;
            return instance != null;
        }

        [Conditional("SINGLETON_DEBUG_GET")]
        protected static void DebugSingletonGet()
        {
            Debug.LogWarning($"[{Time.frameCount}] [SINGLEGET] {(object)s_instance ?? "<null>"} GET", s_instance);
        }

        [Conditional("SINGLETON_DEBUG")]
        protected static void Log(string s, Object context = null)
        {
            Debug.LogWarning($"[{Time.frameCount}] [SINGLETON] {typeof(T).Name} {s}", context);
        }

        /// <summary>
        /// Appends a debug suffix to an object's name. Compiled out unless SINGLETON_DEBUG is
        /// defined: in the old code these suffixes shipped in release builds, allocated a string
        /// per event, and grew without bound on persistent singletons.
        /// </summary>
        [Conditional("SINGLETON_DEBUG")]
        protected static void Annotate(Component target, string suffix)
        {
            if (target == null) return;
            if (target.name.Length > 128) return; // stop runaway concatenation across scene loads
            target.name += suffix;
        }

        private static SingletonEditModePolicy? s_editModePolicy;

        /// <summary>
        /// Resolves (once) how this type behaves outside play mode. Defaults to CreateTransient,
        /// so Instance keeps the same never-null contract in the editor that it has at runtime.
        /// </summary>
        protected static SingletonEditModePolicy EditModePolicy
        {
            get
            {
                if (s_editModePolicy == null)
                {
                    var attr = Attribute.GetCustomAttribute(
                        typeof(T), typeof(SingletonEditModeAttribute), true) as SingletonEditModeAttribute;
                    s_editModePolicy = attr?.Policy ?? SingletonEditModePolicy.CreateTransient;
                }

                return s_editModePolicy.Value;
            }
        }

        /// <summary>True when this type is allowed to create an instance in the current mode.</summary>
        private static bool MayCreate =>
            Application.isPlaying || EditModePolicy == SingletonEditModePolicy.CreateTransient;

        /// <summary>
        /// True when this type is allowed to resolve an *existing* instance in the current mode.
        /// Disabled means "do not resolve at all outside play mode", which has to bind the search
        /// as well as creation: a search that caches an instance the policy forbids leaves
        /// ExistsOrFindInScene reporting true and TryGetInstance handing out an object while
        /// Instance still throws.
        ///
        /// isPlaying is tested first so the attribute reflection behind EditModePolicy is
        /// short-circuited away in a player build, exactly as in Instance.
        /// </summary>
        protected static bool MayResolve =>
            Application.isPlaying || EditModePolicy != SingletonEditModePolicy.Disabled;

        /// <summary>
        /// Flags an edit-mode creation as non-serializable. Without this, an object created because
        /// an inspector or [ExecuteAlways] component touched Instance is a real member of the open
        /// scene (or of whatever prefab stage is open) and is committed on save, with no undo entry.
        /// </summary>
        private static void MarkTransientIfEditMode(GameObject go)
        {
            if (Application.isPlaying) return;
            go.hideFlags = HideFlags.DontSave;
        }

        /// <summary>
        /// Object.Destroy is deferred to end of frame and logs an error outside play mode;
        /// DestroyImmediate is required there but is unsafe during runtime Awake/physics callbacks.
        /// </summary>
        protected static void DestroySafe(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private static bool s_warnedUnavailable;

        /// <summary>
        /// True when Instance can currently hand back a live object.
        ///
        /// This is the single predicate behind the contract: a live singleton exists for the
        /// entire time the application is running, and none exists during teardown — application
        /// quit, or the frame in which a scene unload destroyed it. Code that runs then —
        /// OnDestroy, OnDisable, OnApplicationQuit, coroutine cleanup, pooled object return paths
        /// — should test this or use TryGetInstance. Code that runs during normal operation does
        /// not need to check anything.
        ///
        /// Note that this is stricter than Instance, which during shutdown hands back the
        /// destroyed component so that a bare dereference does not throw. IsAvailable answers
        /// "is there a live one", and goes false while Instance is still returning a reference.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                // IsTearingDown, not IsQuitting: the unconditional "true" below is only honest
                // because Instance would create one on demand, and during an unload frame it
                // will not. Reporting availability there would be a lie the caller acts on.
                if (SingletonRuntime.IsTearingDown) return Current != null;
                if (Application.isPlaying) return true;
                return EditModePolicy == SingletonEditModePolicy.CreateTransient || Current != null;
            }
        }

        /// <summary>
        /// Notes, once per session, that Instance was read during shutdown. One line at app exit,
        /// not a per-call-site log.
        /// </summary>
        protected static void WarnUnavailable()
        {
            if (s_warnedUnavailable) return;
            s_warnedUnavailable = true;
            Debug.LogWarning(
                $"[SINGLETON] {typeof(T).Name}.Instance was read after shutdown began: the singleton " +
                "will not be recreated, so the destroyed component is handed back. Plain C# members " +
                "still work; anything touching transform, gameObject or coroutines will throw. Use " +
                "TryGetInstance in teardown code.");
        }

        /// <summary>Message for the configuration-error case, which throws rather than returning null.</summary>
        protected static string PolicyViolationMessage() =>
            $"{typeof(T).Name}.Instance was requested outside play mode, but the type is marked " +
            $"[SingletonEditMode({EditModePolicy})] and no instance exists in the loaded scenes. " +
            "Either place one in the scene, relax the policy to CreateTransient, or guard the call " +
            "site with Application.isPlaying / TryGetInstance.";

        /// <summary>Resolves (once) the Resources path for this singleton type.</summary>
        private static string ResourcePath =>
            s_resourcePath ??=
                (Attribute.GetCustomAttribute(typeof(T), typeof(SingletonResourceAttribute), false)
                    as SingletonResourceAttribute)?.Path ?? typeof(T).Name;

        /// <summary>
        /// Populates the cache from objects already in the loaded scenes. Only looks at active
        /// GameObjects; a singleton parked on an inactive object will be missed, and will destroy
        /// itself via DestroyDuplicate when it is eventually enabled.
        /// </summary>
        protected static void MaybeFindInScene()
        {
            if (SingletonRuntime.IsQuitting)
            {
                Log("MaybeFindInScene skipped: application is quitting.");
                return;
            }

            if (!MayResolve)
            {
                Log("MaybeFindInScene skipped: edit-mode resolution is disabled for this type.");
                return;
            }

            if (Current != null) return;

            var objs = FindObjectsByType<T>(FindObjectsInactive.Exclude);

#if UNITY_EDITOR
            // The find APIs skip HideFlags.DontSave, and the editor reloads the domain on every
            // recompile while leaving the scene loaded. Without this fallback an edit-mode
            // transient becomes orphaned and unreachable, and we would leak a fresh one per
            // recompile. FindObjectsOfTypeAll does see them.
            //
            // The #if is a stripping choice, not a compile requirement: nothing below references
            // the UnityEditor assembly, and the !Application.isPlaying gate is already false in
            // every player build.
            if (objs.Length == 0 && !Application.isPlaying)
            {
                var all = Resources.FindObjectsOfTypeAll<T>();
                var found = new System.Collections.Generic.List<T>(1);
                foreach (var candidate in all)
                    if (IsInLoadedScene(candidate.gameObject)) found.Add(candidate);

                objs = found.ToArray();
            }
#endif

            objs = RejectUnloadingScenes(objs);
            if (objs.Length == 0) return;

            var chosen = objs.Length == 1 ? objs[0] : ResolveDuplicates(objs);
            Current = chosen;
            Annotate(chosen, $" {typeof(T).Name}(s{SceneManager.GetActiveScene().buildIndex})");
            Log("found in the scene.", chosen);
        }

        /// <summary>
        /// Drops candidates whose scene is being unloaded, so an additive unload cannot hand back
        /// an instance that is about to be destroyed while a perfectly good one stays loaded.
        ///
        /// Deliberately a rejection test rather than reusing IsInLoadedScene as an acceptance
        /// test. IsInLoadedScene walks SceneManager's enumeration, which never includes the
        /// DontDestroyOnLoad scene — accepting only what it matches would refuse to re-adopt a
        /// persistent singleton after a domain reload, which is the one case that most needs to
        /// work. Rejecting on IsValid() && !isLoaded touches neither DontDestroyOnLoad nor
        /// anything whose scene is invalid.
        ///
        /// Returns the input array untouched in the common case, so the normal path allocates
        /// nothing extra.
        /// </summary>
        private static T[] RejectUnloadingScenes(T[] objs)
        {
            var doomed = 0;
            foreach (var candidate in objs)
                if (IsInUnloadingScene(candidate.gameObject)) doomed++;

            if (doomed == 0) return objs;

            var kept = new T[objs.Length - doomed];
            var next = 0;
            foreach (var candidate in objs)
                if (!IsInUnloadingScene(candidate.gameObject)) kept[next++] = candidate;

            return kept;
        }

        /// <summary>
        /// True when this object's scene is mid-unload. A scene under unload reports
        /// isLoaded == false while its objects are still reachable by the find APIs, which is the
        /// window this closes.
        /// </summary>
        private static bool IsInUnloadingScene(GameObject go)
        {
            var scene = go.scene;
            return scene.IsValid() && !scene.isLoaded;
        }

        /// <summary>
        /// Picks a winner deterministically when several instances exist, and reports every
        /// candidate with its hierarchy path so the duplicate is actually findable.
        /// </summary>
        private static T ResolveDuplicates(T[] objs)
        {
            Array.Sort(objs, (a, b) =>
            {
                var byScene = a.gameObject.scene.buildIndex.CompareTo(b.gameObject.scene.buildIndex);
                return byScene != 0
                    ? byScene
                    : string.CompareOrdinal(HierarchyPath(a.transform), HierarchyPath(b.transform));
            });

            var sb = new StringBuilder();
            sb.Append($"[{Time.frameCount}] [SINGLETON] {objs.Length} instances of {typeof(T).Name} found. ");
            sb.Append($"Keeping '{HierarchyPath(objs[0].transform)}'. Others:");
            for (var i = 1; i < objs.Length; i++)
                sb.Append($"\n  {objs[i].gameObject.scene.name} :: {HierarchyPath(objs[i].transform)}");
            Debug.LogError(sb.ToString(), objs[0]);

            return objs[0];
        }

        /// <summary>
        /// True when the object lives in a scene the SceneManager currently has loaded.
        /// Excludes prefab assets, whose scene is invalid, and prefab-stage previews, whose scene
        /// is valid but is not enumerated — neither should be adopted as the live singleton.
        /// Uses only UnityEngine API, so this file carries no UnityEditor dependency.
        /// </summary>
        private static bool IsInLoadedScene(GameObject go)
        {
            var scene = go.scene;
            if (!scene.IsValid()) return false;

            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i) == scene)
                    return true;

            return false;
        }

        /// <summary>Builds a slash-delimited path from the scene root to this transform.</summary>
        private static string HierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        /// <summary>
        /// Creates a bare GameObject hosting the singleton. Runs in edit mode too, unless the type
        /// opts out via [SingletonEditMode], but edit-mode creations are marked DontSave so they
        /// cannot be serialized into the scene or prefab the user happens to have open.
        /// </summary>
        protected static void MaybeCreateNew()
        {
            if (SingletonRuntime.IsTearingDown || !MayCreate) return;
            if (Current != null) return;

            var obj = new GameObject($"{typeof(T).Name} (c{SceneManager.GetActiveScene().buildIndex})");
            MarkTransientIfEditMode(obj);
            Current = obj.AddComponent<T>();
            Log("created new.", Current);
        }

        /// <summary>
        /// Instantiates the singleton from Resources, at most once per session. Resources.Load is a
        /// synchronous read, so probing it on every access of a null Instance is a per-frame hitch
        /// risk on Quest.
        /// </summary>
        protected static void MaybeCreateFromResource()
        {
            if (SingletonRuntime.IsTearingDown || !MayCreate) return;
            if (Current != null || s_resourceProbed) return;

            s_resourceProbed = true;
            var resource = Resources.Load<T>(ResourcePath);
            if (resource == null) return;

            Current = Instantiate(resource);
            MarkTransientIfEditMode(Current.gameObject);
            Annotate(Current, $" {typeof(T).Name}(cr{SceneManager.GetActiveScene().buildIndex})");
            Log($"created from resource '{ResourcePath}'.", Current);
        }

        /// <summary>Claims the singleton slot for this component during Awake, if it is still free.</summary>
        protected virtual void AssignInAwake()
        {
            if (Current == null)
            {
                Current = (T)this;
                Annotate(this, $" {typeof(T).Name}(^{SceneManager.GetActiveScene().buildIndex})");
                Log("assigned in Awake.", this);
            }
            else
            {
                Log($"not assigned in Awake, already set to {Current}.", Current);
            }
        }

        /// <summary>
        /// Moves this object to the scene root so DontDestroyOnLoad actually takes effect.
        /// </summary>
        /// <param name="promoteChildren">
        /// When true, reparents children to the current parent first, so only this object persists.
        /// When false, the subtree travels with it.
        /// </param>
        protected virtual void DetachIfNotRoot(bool promoteChildren = false)
        {
            if (transform.parent == null) return;

            if (promoteChildren)
                while (transform.childCount > 0)
                    transform.GetChild(0).SetParent(transform.parent, true);

            transform.SetParent(null, true);
            Annotate(this, "<");
            Log("detached to scene root.", this);
        }

        /// <summary>
        /// Marks the singleton persistent. Unity's DontDestroyOnLoad only applies to root objects
        /// and warns-then-ignores otherwise.
        /// </summary>
        protected virtual void DontDestroy()
        {
            if (Current == null) return;
            if (!IsCurrentInstance) return; // never persist a duplicate that is about to be destroyed

            // DontDestroyOnLoad is a play-mode-only API and logs an error otherwise. An edit-mode
            // transient is already effectively persistent: DontSave survives scene loads.
            if (!Application.isPlaying) return;

            // DontDestroyOnLoad is GameObject-scoped, so persisting a shared GameObject reparents every
            // sibling to the scene root and makes them persistent too. A Component cannot be moved
            // between GameObjects, so the only way out is to rebuild this one on an object of its
            // own — which is safe exactly when the type has no serialized state to lose.
            if (!AloneOnItsGameObject && TryRebuildOnOwnGameObject()) return;

            DetachIfNotRoot();
            DontDestroyOnLoad(gameObject);
            Annotate(this, "(!)");
            Log("marked DontDestroyOnLoad.", this);
        }

        /// <summary>
        /// True when the live instance lives in Unity's DontDestroyOnLoad scene. The old
        /// implementation tested HideFlags.DontSave, which DontDestroyOnLoad does not set, so it
        /// always returned false.
        /// </summary>
        protected static bool IsDontDestroyOnLoad()
        {
            var inst = Current;
            if (inst == null) return false;
            var scene = inst.gameObject.scene;
            return scene.buildIndex == -1 && scene.name == "DontDestroyOnLoad";
        }

        /// <summary>
        /// Controls the blast radius when a duplicate is discovered: the whole GameObject GameObject,
        /// or only this component.
        ///
        /// Decided per GameObject rather than per type, because that is the axis the question lives on.
        /// A type cannot know whether it is alone on its object — bundled onto a shared "Managers"
        /// object in one scene, on its own in another — and the old constant default of true made
        /// the victim's safety depend on the *duplicate's* type overriding it. A sole, correctly
        /// registered singleton could be destroyed as collateral because its GameObject also carried a
        /// duplicate of an unrelated singleton type, and it had no way to defend itself.
        ///
        /// Transform plus this component means nothing else is lost with the GameObject, so the shell
        /// goes too. Anything else present, and only this component is destroyed — leaving a
        /// stripped GameObject behind is cheaper than silently breaking another component's
        /// contract. Override to force either answer.
        /// </summary>
        protected virtual bool DestroyWholeGameObject => AloneOnItsGameObject;

        /// <summary>
        /// True when the Transform and this component are all that is on the GameObject, so nothing else
        /// is affected by what happens to the GameObject.
        /// </summary>
        protected bool AloneOnItsGameObject => gameObject.GetComponents<Component>().Length <= 2;

        private static bool s_warnedSharedGameObject;

        /// <summary>Resolved once per type; the rule itself lives on SingletonRuntime.</summary>
        private static bool HasSerializedFields =>
            s_hasSerializedFields ??= SingletonRuntime.DeclaresSerializedFields(typeof(T));

        private static bool? s_hasSerializedFields;

        /// <summary>Removes this component (or its GameObject) because the slot is already filled.</summary>
        protected virtual void DestroyDuplicate()
        {
            if (IsCurrentInstance)
            {
                Log("DestroyDuplicate called on the live instance; ignoring.", this);
                return;
            }

            Log($"destroying duplicate; live instance is {Current}.", this);
            Annotate(Current, "(+)");
            DestroySafe(DestroyWholeGameObject ? (Object)gameObject : this);
        }

        /// <summary>
        /// Rebuilds this singleton on a GameObject of its own, named for the type, so persisting it
        /// does not drag the rest of a shared GameObject into DontDestroyOnLoad. Returns false when it
        /// declines, leaving the caller to persist the shared GameObject as before.
        ///
        /// Only for types with no serialized fields, because this destroys the authored component
        /// and constructs a replacement. What that costs even so, and what no check here can see:
        /// serialized references *to* this component — an inspector field or a UnityEvent wired to
        /// it — break, and the subclass's own Awake body runs on this instance and again on the
        /// replacement. Give the type serialized fields, or put it on its own object, to opt out.
        /// </summary>
        private bool TryRebuildOnOwnGameObject()
        {
            if (HasSerializedFields)
            {
                if (!s_warnedSharedGameObject)
                {
                    s_warnedSharedGameObject = true;
                    Debug.LogWarning(
                        $"[SINGLETON] {typeof(T).Name} is persistent but shares '{gameObject.name}' with " +
                        "other components, and has serialized fields, so it cannot be rebuilt on its own " +
                        "object without losing them. The whole GameObject will be reparented to the scene " +
                        "root and marked DontDestroyOnLoad. Move the singleton onto its own object.",
                        this);
                }

                return false;
            }

            // Release the slot first: the replacement claims it in its own Awake, and would
            // otherwise see a live instance and destroy itself as a duplicate.
            var doomed = this;
            s_instance = null;

            var owner = new GameObject(typeof(T).Name);
            var replacement = owner.AddComponent<T>();   // Awake runs here, claims the slot, persists

            if (!ReferenceEquals(Current, replacement))
            {
                Log("extraction did not take; restoring the original.", doomed);
                Current = (T)doomed;
                DestroySafe(owner);
                return false;
            }

            Log($"rebuilt on '{owner.name}' to keep '{doomed.gameObject.name}' out of DontDestroyOnLoad.",
                replacement);
            DestroySafe(doomed);   // the component only; the shared GameObject and its siblings stay
            return true;
        }

        /// <summary>
        /// Releases the cached reference if this component owned it, and reports a scene unload
        /// so nothing resurrects the singleton into a scene that is going away.
        ///
        /// gameObject.scene.isLoaded is the only signal available this early: it reads false when
        /// the scene is being unloaded and true when this object alone was destroyed. Read before
        /// the slot is released, because the check needs a live gameObject.
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (!IsCurrentInstance) return;

            if (!gameObject.scene.isLoaded) SingletonRuntime.NotifySceneUnloading();

            // Written through the field, not Current: the setter stamps s_lastKnown, which would
            // overwrite the tombstone that Instance hands back during teardown.
            s_instance = null;
            Log("cleared instance on destroy.", this);
        }
    }

    /// <summary>
    /// Lazy singleton: resolves from the scene, then Resources, then an empty GameObject.
    /// It will not resurrect itself during teardown — application quit or scene unload — which is
    /// what produced "leaked GameObject" warnings. Instance hands back the destroyed component in
    /// that window. Outside play mode it either resolves or throws MissingSingletonException,
    /// depending on [SingletonEditMode] — it does not return null.
    /// </summary>
    public abstract class MonoBehaviourSingleton<T> : MonoBehaviourSingletonBase<T>
        where T : MonoBehaviourSingleton<T>
    {
        /// <summary>
        /// The singleton, resolved from the loaded scenes, then Resources, then a new GameObject.
        ///
        /// CONTRACT: a live singleton exists for the entire time the application is running.
        /// During teardown — application quit, or the frame in which a scene unload destroyed it —
        /// this does not recreate one, because that leaks objects into a scene that is going away
        /// and, on device, can touch XR and audio subsystems that have already shut down. It hands
        /// back the destroyed component instead, so a bare dereference reaches a live C# object;
        /// members touching the native peer still throw. Test IsAvailable or use TryGetInstance in
        /// teardown code that needs more than managed state; everywhere else, dereference directly.
        /// </summary>
        /// <exception cref="MissingSingletonException">
        /// The type opted out of edit-mode resolution and no instance exists. This is a
        /// configuration error, so it fails here rather than as an NRE at an unrelated call site.
        /// </exception>
        public static T Instance
        {
            get
            {
                if (!MayResolve)
                    throw new MissingSingletonException(PolicyViolationMessage());

                // Fast path. All three helpers below open with "if (Current != null) return", so
                // once the slot is filled they are no-ops and the resolved read costs one
                // session-guarded fake-null collapse rather than four.
                var cached = Current;
                if (cached != null)
                {
                    DebugSingletonGet();
                    return cached;
                }

                MaybeFindInScene();
                MaybeCreateFromResource();
                MaybeCreateNew();
                DebugSingletonGet();

                var instance = Current;
                if (instance != null) return instance;

                // Shutdown is a lifecycle fact no caller can avoid. Hand back the destroyed
                // component rather than null so a bare dereference does not throw, and note it once.
                if (SingletonRuntime.IsTearingDown)
                {
                    WarnUnavailable();
                    return LastKnown;
                }

                // Anything else means the contract was configured away, which is a bug to fix.
                throw new MissingSingletonException(PolicyViolationMessage());
            }
        }
    }

    /// <summary>Lazy singleton that survives scene loads; later duplicates destroy themselves.</summary>
    public abstract class MonoBehaviourSingletonPersistent<T> : MonoBehaviourSingleton<T>
        where T : MonoBehaviourSingletonPersistent<T>
    {
        protected virtual void Awake()
        {
            if (Current == null)
            {
                AssignInAwake();
                DontDestroy();
            }
            else if (!IsCurrentInstance)
            {
                DestroyDuplicate();
            }
        }
    }

    /// <summary>
    /// Passive singleton: Instance is null until some component's Awake claims the slot. Never
    /// auto-creates, so it is the right choice for anything scene-authored or dependency-injected.
    /// </summary>
    public abstract class MonoBehaviourSingletonPassive<T> : MonoBehaviourSingletonBase<T>
        where T : MonoBehaviourSingletonPassive<T>
    {
        public static T Instance
        {
            get
            {
                DebugSingletonGet();

                var instance = Current;
                if (instance != null) return instance;

                // Same shutdown allowance as the lazy flavour: a destroyed component beats null
                // for teardown callers. Outside shutdown a passive singleton is legitimately
                // absent until something places one, so null is the honest answer.
                return SingletonRuntime.IsTearingDown ? LastKnown : null;
            }
        }

        protected virtual void Awake()
        {
            if (Current != null && !IsCurrentInstance) DestroyDuplicate();
            else AssignInAwake();
        }
    }

    /// <summary>
    /// Passive singleton that also survives scene loads. Only the winning instance is marked
    /// persistent. If we were to call DontDestroy() unconditionally, we'd briefly move a doomed
    /// duplicate into the DontDestroyOnLoad scene.
    /// </summary>
    public abstract class MonoBehaviourSingletonPassivePersistent<T> : MonoBehaviourSingletonPassive<T>
        where T : MonoBehaviourSingletonPassivePersistent<T>
    {
        protected override void Awake()
        {
            base.Awake();
            if (IsCurrentInstance) DontDestroy();
        }
    }
}
