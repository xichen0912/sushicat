using UnityEditor;
using UnityEngine;

namespace CityGen.EditorTools
{
    // Tunable parameters for every CityGen step, kept in Assets/_CityGen/CityGenSettings.asset.
    // Select the asset to edit it, then run CityGen → 2 Generate Details again (same seed = same city).
    // Per-building overrides (style, floors, bays, seed) live on each building's CityBuildingInfo;
    // edit them and run CityGen → Rebuild Selected Buildings.
    public sealed class CityGenSettings : ScriptableObject
    {
        const string AssetPath = "Assets/_CityGen/CityGenSettings.asset";

        [Header("Layout (step 1)")]
        public int seed = 1930;
        [Tooltip("Storey heights × this. 0.3 keeps the city small so the elephant looks big.")]
        [Range(.15f, 1f)] public float heightScale = .3f;
        [Tooltip("Lot widths and depths × this.")]
        [Range(.25f, 1f)] public float footprintScale = .5f;

        [Header("Facades (step 2)")]
        [Range(0, 1)] public float litWindowRatio = .3f;
        [Tooltip("Window bay width in real metres before footprintScale.")]
        [Min(1)] public float bayWidth = 3f;
        [Range(0, 1)] public float fireEscapeChance = .6f;
        [Range(0, 1)] public float waterTowerChance = .45f;
        [Range(0, 1)] public float flagChance = .3f;
        [Min(0)] public int billboardCount = 12;
        [Tooltip("Towers at or above this many storeys get an Art Deco spire.")]
        [Min(10)] public int spireFromFloors = 20;

        [Header("Streets (step 2)")]
        [Tooltip("Street furniture size × this (lamps, taxis, hydrants…).")]
        [Range(.2f, 1f)] public float propScale = .35f;
        [Min(3)] public float lampSpacing = 7f;
        [Min(3)] public float hydrantSpacing = 16f;
        [Min(3)] public float binSpacing = 12f;
        [Min(3)] public float streetTreeSpacing = 11f;
        [Min(0)] public int taxiCount = 16;
        [Min(0)] public int steamingManholes = 8;

        public static CityGenSettings Load()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CityGenSettings>(AssetPath);
            if (settings) return settings;
            settings = CreateInstance<CityGenSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }
}
