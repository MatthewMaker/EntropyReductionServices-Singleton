using System;
using UnityEditor;
using UnityEngine;

namespace EntropyReductionServices.Harness
{
    /// <summary>
    /// Builds the Resources prefab the play-mode Resources test loads. The prefab has to be a real
    /// imported asset for Resources.Load to see it, and it carries a probe type that lives in the
    /// package's play-mode test assembly — which this harness assembly cannot reference, hence the
    /// reflection lookup.
    ///
    /// Run it after deleting or regenerating the asset:
    ///   Unity -batchmode -quit -projectPath . -executeMethod \
    ///     EntropyReductionServices.Harness.SingletonTestFixtureBuilder.Build
    /// </summary>
    public static class SingletonTestFixtureBuilder
    {
        private const string ProbeType =
            "EntropyReductionServices.Singletons.PlayModeTests.ResourceSharedRoot, " +
            "EntropyReductionServices.Singletons.PlayModeTests";

        private const string AssetPath = "Assets/Resources/ResourceSharedRoot.prefab";

        [MenuItem("Tools/Singletons/Rebuild Test Fixtures")]
        public static void Build()
        {
            var probe = Type.GetType(ProbeType);
            if (probe == null) throw new InvalidOperationException($"{ProbeType} not found.");

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

            AssetDatabase.SaveAssets();
            Debug.Log($"[FIXTURES] wrote {AssetPath}");
        }
    }
}
