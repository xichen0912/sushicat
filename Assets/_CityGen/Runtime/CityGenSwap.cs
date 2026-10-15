using System;
using UnityEngine;

namespace CityGen
{
    // Lives on the "CityGen" root. While CityGen is active, the listed originals are changed reversibly:
    // renderers hidden (ochre ground, old basin, trees that gave way), materials swapped (the road's asphalt),
    // components disabled (the original key light, replaced by the city sun). Turning CityGen off or deleting it
    // restores all of them. Original objects are never moved, deleted or given new components.
    [ExecuteAlways]
    public sealed class CityGenSwap : MonoBehaviour
    {
        [Serializable]
        public sealed class MaterialSwap
        {
            public Renderer renderer;
            public Material[] original = new Material[0];
            public Material[] replacement = new Material[0];
        }

        public Renderer[] hideWhileActive = new Renderer[0];
        // Detail that must live under an original object (the log's bark and end grain, so it is
        // carried with the log when the trunk picks it up). Shown only while CityGen is active.
        public GameObject[] activeWhileEnabled = new GameObject[0];
        public MaterialSwap[] materialSwaps = new MaterialSwap[0];
        public Behaviour[] disableWhileActive = new Behaviour[0];

        void OnEnable() => Apply(true);
        void OnDisable() => Apply(false);

        void Apply(bool city)
        {
            foreach (var renderer in hideWhileActive)
                if (renderer) renderer.enabled = !city;
            foreach (var detail in activeWhileEnabled)
                if (detail) detail.SetActive(city);
            foreach (var swap in materialSwaps)
                if (swap != null && swap.renderer) swap.renderer.sharedMaterials = city ? swap.replacement : swap.original;
            foreach (var behaviour in disableWhileActive)
                if (behaviour) behaviour.enabled = !city;
        }
    }
}
