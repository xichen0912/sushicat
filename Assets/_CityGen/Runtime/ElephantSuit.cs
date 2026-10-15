using System;
using UnityEngine;

namespace CityGen
{
    // The elephant's jazz suit for the finish dance, built by CityGen (CityJazzSuit). The jacket and trousers are
    // a skinned mesh driven by the elephant's own bones, so they move with every dance move; the fedora and the
    // bow tie are rigid pieces that ride on the head and neck bones while worn. Nothing is added to the elephant
    // itself until Wear(true), and Wear(false) puts everything back under this object.
    public sealed class ElephantSuit : MonoBehaviour
    {
        [Serializable]
        public sealed class Piece
        {
            public Transform piece;
            public Transform bone;
            public Vector3 localPosition;
            public Quaternion localRotation = Quaternion.identity;
            public Vector3 localScale = Vector3.one;
        }

        public SkinnedMeshRenderer clothes;
        public Piece[] pieces = new Piece[0];

        public bool Worn { get; private set; }

        public void Wear(bool on)
        {
            Worn = on;
            if (clothes) clothes.gameObject.SetActive(on);
            foreach (var p in pieces)
            {
                if (p == null || !p.piece) continue;
                if (on && p.bone)
                {
                    p.piece.SetParent(p.bone, false);
                    p.piece.localPosition = p.localPosition;
                    p.piece.localRotation = p.localRotation;
                    p.piece.localScale = p.localScale;
                }
                else
                {
                    p.piece.SetParent(transform, false);
                }
                p.piece.gameObject.SetActive(on && p.bone);
            }
        }
    }
}
