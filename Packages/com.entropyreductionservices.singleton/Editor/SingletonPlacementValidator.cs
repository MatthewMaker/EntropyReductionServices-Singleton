using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EntropyReductionServices.Singletons.Editor
{
    /// <summary>
    /// Reports singletons that share a GameObject, at the point they are authored.
    ///
    /// This is an editor concern and cannot be anything else. The analyzers read source, and a
    /// shared GameObject is scene data — invisible to the compiler. The runtime can detect it, but by
    /// then the only honest responses are to drag the siblings along or to destroy something, and
    /// neither is a fix: a Component cannot be moved to another GameObject at runtime, so nothing
    /// can relocate an authored singleton without discarding its serialized state.
    ///
    /// A stateless persistent singleton rebuilds itself on a dedicated object at runtime, so this
    /// reports only what that cannot fix: a persistent singleton with serialized fields, and any
    /// GameObject carrying more than one singleton.
    ///
    /// Lives in its own Editor-only assembly so the runtime keeps the "no UnityEditor dependency"
    /// goal in Documentation~/contract.md.
    /// </summary>
    public static class SingletonPlacementValidator
    {
        private const string MenuPath = "Tools/Entropy Reduction Services/Validate Singleton Placement";

        [MenuItem(MenuPath)]
        private static void ValidateFromMenu()
        {
            var problems = ValidateLoadedScenes();
            if (problems == 0)
                Debug.Log("[SINGLETON] No shared singleton GameObjects found in the loaded scenes.");
        }

        /// <summary>
        /// Runs on save so the warning arrives with the edit that caused it rather than waiting
        /// for someone to remember the menu item. Reports only; it never blocks the save.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void HookSceneSave() => EditorSceneManager.sceneSaved += _ => ValidateLoadedScenes();

        /// <summary>
        /// Walks every root object of every loaded scene and reports each shared GameObject once.
        /// Returns the number of problems found.
        /// </summary>
        public static int ValidateLoadedScenes()
        {
            var problems = 0;

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                foreach (var owner in root.GetComponentsInChildren<Transform>(true))
                    problems += ReportGameObject(owner.gameObject);
            }

            return problems;
        }

        /// <summary>
        /// Emits a warning per problem on one GameObject, with the object as the log context so
        /// clicking the entry selects it in the hierarchy.
        /// </summary>
        private static int ReportGameObject(GameObject owner)
        {
            var singletons = owner.GetComponents<MonoBehaviour>()
                .Where(component => component != null && IsSingleton(component.GetType()))
                .ToList();

            if (singletons.Count == 0) return 0;

            var problems = 0;

            if (singletons.Count > 1)
            {
                problems++;
                Debug.LogWarning(
                    $"[SINGLETON] '{Path(owner)}' has {singletons.Count} singletons on it: " +
                    $"{string.Join(", ", singletons.Select(s => s.GetType().Name))}. Each resolves " +
                    "duplicates independently, and a persistent one moves the whole GameObject to " +
                    "DontDestroyOnLoad, taking the others with it. Give each its own object.",
                    owner);
            }

            // Only the ones that cannot rebuild themselves. A stateless persistent singleton is
            // moved onto its own object at runtime, so warning about it here would be noise about
            // a problem that resolves itself.
            var persistent = singletons.FirstOrDefault(
                s => IsPersistent(s.GetType()) && SingletonRuntime.DeclaresSerializedFields(s.GetType()));
            if (persistent == null) return problems;

            // Transform plus the singletons themselves are expected; anything else is dragged along.
            var bystanders = owner.GetComponents<Component>()
                .Where(component => component != null
                                    && !(component is Transform)
                                    && !singletons.Contains(component as MonoBehaviour))
                .ToList();

            if (bystanders.Count > 0)
            {
                problems++;
                Debug.LogWarning(
                    $"[SINGLETON] '{Path(owner)}' carries the persistent singleton " +
                    $"'{persistent.GetType().Name}' alongside " +
                    $"{string.Join(", ", bystanders.Select(b => b.GetType().Name))}. It has serialized " +
                    "fields, so it cannot be rebuilt on an object of its own without losing them. On " +
                    "play the whole GameObject is reparented to the scene root and marked " +
                    $"DontDestroyOnLoad, so those come too. Move '{persistent.GetType().Name}' onto its " +
                    "own object.",
                    owner);
            }

            return problems;
        }

        /// <summary>Full hierarchy path, so the warning is actionable in a large scene.</summary>
        private static string Path(GameObject go)
        {
            var path = go.name;
            for (var parent = go.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        /// <summary>
        /// True when the type descends from MonoBehaviourSingletonBase&lt;T&gt;. Compared by generic
        /// type definition: the base is always a constructed generic, never the open definition.
        /// </summary>
        private static bool IsSingleton(Type type) => DerivesFrom(type, typeof(MonoBehaviourSingletonBase<>));

        /// <summary>True for the flavours that call DontDestroyOnLoad on their owner.</summary>
        private static bool IsPersistent(Type type) =>
            DerivesFrom(type, typeof(MonoBehaviourSingletonPersistent<>)) ||
            DerivesFrom(type, typeof(MonoBehaviourSingletonPassivePersistent<>));

        private static bool DerivesFrom(Type type, Type openBaseDefinition)
        {
            for (var current = type?.BaseType; current != null; current = current.BaseType)
                if (current.IsGenericType && current.GetGenericTypeDefinition() == openBaseDefinition)
                    return true;

            return false;
        }
    }
}
