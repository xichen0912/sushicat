using UnityEngine;

namespace CityGen
{
    // Lot data written by the blockout generator; the detail pass (step 2) reads it.
    public sealed class CityBuildingInfo : MonoBehaviour
    {
        public enum Style { RowHouse, Apartment, Tower }
        public Style style;
        public int floors;
        public int bays;
        public Vector2 footprint;   // world X × Z size in metres
        public Vector3 frontDirection; // facade facing the nearest street
        public int seed;
    }
}
