using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityGen.EditorTools
{
    // Step 2 entry point. Regenerates the blockout (clearing CityGen first, same seed = same city) and then
    // replaces it with detailed buildings, street furniture, the fountain and the log.
    public static class CityDetail
    {
        [MenuItem("CityGen/2 Generate Details")]
        public static void GenerateDetails()
        {
            var settings = CityGenSettings.Load();
            var result = CityBlockout.BuildBlockout();
            BuildDetails(result, settings);
            CityBlockout.Finish(result, "CITYGEN_DETAILS_DONE");
        }

        // Step 3 = the whole chain from a clean CityGen (blockout → details → materials, lighting, post-processing),
        // so re-running always gives the same city. Captures the step 3 screenshots at the end.
        [MenuItem("CityGen/3 Generate Final (materials + lighting)")]
        public static void GenerateFinal()
        {
            var settings = CityGenSettings.Load();
            var result = CityBlockout.BuildBlockout();
            BuildDetails(result, settings);
            CityLook.Apply(result, settings);
            CityCelebrationSetup.Build(result);
            CityBlockout.Finish(result, "CITYGEN_FINAL_DONE");
            CityScreenshots.Capture("step3");
        }

        internal static void BuildDetails(CityBlockout.Result result, CityGenSettings settings)
        {
            var buildings = result.root.transform.Find("Buildings");
            try
            {
                // No StartAssetEditing here: generated textures are imported and read back immediately.
                var prefabs = CityProps.BuildPrefabs();
                var infos = buildings.GetComponentsInChildren<CityBuildingInfo>();
                for (int i = 0; i < infos.Length; i++)
                {
                    EditorUtility.DisplayProgressBar("CityGen", "Building " + infos[i].name, (float)i / infos.Length);
                    CityBuildings.Build(infos[i], settings);
                }
                CityProps.PlaceBillboards(buildings, settings);
                CityProps.Place(result.root.transform, result.ctx, prefabs, settings);
                result.hidden.AddRange(CityProps.RestyledTreeRenderers(result.ctx));
                CityFountainLog.BuildFountain(result.root.transform, result.ctx, settings);
                CityFountainLog.BuildLog(result, settings);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // Edit a building's CityBuildingInfo (style, floors, bays, seed) in the Inspector, select it and run this.
        [MenuItem("CityGen/Rebuild Selected Buildings")]
        public static void RebuildSelected()
        {
            var settings = CityGenSettings.Load();
            var infos = Selection.gameObjects.Select(g => g.GetComponentInParent<CityBuildingInfo>()).Where(i => i).Distinct().ToArray();
            if (infos.Length == 0) throw new InvalidOperationException("Select one or more CityGen buildings first.");
            foreach (var info in infos)
            {
                foreach (Transform child in info.transform.Cast<Transform>().Where(c => c.name == "Flag").ToArray())
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                CityBuildings.Build(info, settings);
                EditorUtility.SetDirty(info.gameObject);
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("CITYGEN_REBUILT " + string.Join(", ", infos.Select(i => i.name)) + " | " + CityBlockout.RunOverlapCheck());
        }

        [MenuItem("CityGen/Rebuild Selected Buildings", true)]
        static bool CanRebuild() => Selection.gameObjects.Any(g => g.GetComponentInParent<CityBuildingInfo>());

        [MenuItem("CityGen/Select Settings Asset")]
        static void SelectSettings() => Selection.activeObject = CityGenSettings.Load();
    }
}
