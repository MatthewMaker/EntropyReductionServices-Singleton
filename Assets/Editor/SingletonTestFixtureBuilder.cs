using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EntropyReductionServices.Harness
{
    /// <summary>
    /// Builds the assets the play-mode tests load: the Resources prefab, and the scenes the
    /// single-mode scene-change tests load by path. Each has to be a real imported asset, and each
    /// carries a probe type that lives in the package's play-mode test assembly — which this
    /// harness assembly cannot reference, hence the reflection lookup.
    ///
    /// Run it after deleting or regenerating the asset:
    ///   Unity -batchmode -quit -projectPath . -executeMethod \
    ///     EntropyReductionServices.Harness.SingletonTestFixtureBuilder.Build
    /// </summary>
    public static class SingletonTestFixtureBuilder
    {
        private const string ProbeNamespace = "EntropyReductionServices.Singletons.PlayModeTests";
        private const string ProbeAssembly = "EntropyReductionServices.Singletons.PlayModeTests";

        private const string AssetPath = "Assets/Resources/ResourceSharedRoot.prefab";
        private const string SceneFolder = "Assets/Scenes/SingletonTests";

        /// <summary>Builds every fixture asset.</summary>
        [MenuItem("Tools/Singletons/Rebuild Test Fixtures")]
        public static void Build()
        {
            BuildResourcePrefab();
            BuildScene("SceneChangeHandoff");
            BuildScene("SceneChangeReader");
            AssetDatabase.SaveAssets();
        }

        /// <summary>Resolves a probe type from the play-mode test assembly by its short name.</summary>
        private static Type Probe(string name)
        {
            var qualified = $"{ProbeNamespace}.{name}, {ProbeAssembly}";
            return Type.GetType(qualified) ?? throw new InvalidOperationException($"{qualified} not found.");
        }

        /// <summary>Writes the shared-root prefab the Resources test instantiates.</summary>
        private static void BuildResourcePrefab()
        {
            var probe = Probe("ResourceSharedRoot");

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            // A second component is the whole point: it makes the root shared, so persisting the
            // singleton has to rebuild it on an object of its own during Instantiate's Awake.
            var root = new GameObject("ResourceSharedRoot");
            try
            {
                root.AddComponent<BoxCollider>();
                root.AddComponent(probe);
                PrefabUtility.SaveAsPrefabAsset(root, AssetPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            Debug.Log($"[FIXTURES] wrote {AssetPath}");
        }

        /// <summary>
        /// Writes an otherwise empty scene holding one GameObject with the probe of the same name,
        /// so the scene's own name says what it authors.
        /// </summary>
        private static void BuildScene(string probeName)
        {
            var probe = Probe(probeName);

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            if (!AssetDatabase.IsValidFolder(SceneFolder))
                AssetDatabase.CreateFolder("Assets/Scenes", "SingletonTests");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject(probeName).AddComponent(probe);

            var path = $"{SceneFolder}/{probeName}.unity";
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException($"Could not save {path}.");

            Debug.Log($"[FIXTURES] wrote {path}");
        }
    }
}
