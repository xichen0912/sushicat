using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityGen.EditorTools
{
    // Accumulates flat-shaded primitives into one mesh with one sub-mesh per material slot, so a whole
    // building is a single renderer. UVs are in metres (tiling brick/stone textures in step 3 line up).
    internal sealed class MeshKit
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int>[] triangles;

        public MeshKit(int slots)
        {
            triangles = new List<int>[slots];
            for (int i = 0; i < slots; i++) triangles[i] = new List<int>();
        }

        public bool IsEmpty(int slot) => triangles[slot].Count == 0;

        // A rectangle facing `normal`, centred at `center`; `up` is its vertical direction.
        public void Quad(int slot, Vector3 center, Vector3 normal, Vector3 up, float width, float height)
        {
            Vector3 right = Vector3.Cross(normal, up).normalized * (width / 2);
            Vector3 u = up.normalized * (height / 2);
            AddQuad(slot, center - right - u, center + right - u, center + right + u, center - right + u, normal);
        }

        // Corners: bottom-left, bottom-right, top-right, top-left as seen from the front.
        public void AddQuad(int slot, Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector3 normal)
        {
            int i = vertices.Count;
            vertices.Add(bl); vertices.Add(br); vertices.Add(tr); vertices.Add(tl);
            for (int k = 0; k < 4; k++) normals.Add(normal.normalized);
            Vector3 right = (br - bl).normalized, up = (tl - bl).normalized;
            foreach (var p in new[] { bl, br, tr, tl }) uvs.Add(new Vector2(Vector3.Dot(p, right), Vector3.Dot(p, up)));
            triangles[slot].AddRange(new[] { i, i + 3, i + 2, i, i + 2, i + 1 });
        }

        // Single triangle; the vertex order is fixed so its face points along `normal`.
        public void Triangle(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0) { var t = b; b = c; c = t; }
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            for (int k = 0; k < 3; k++) normals.Add(normal.normalized);
            Vector3 right = (b - a).normalized, up = Vector3.Cross(normal, right).normalized;
            foreach (var p in new[] { a, b, c }) uvs.Add(new Vector2(Vector3.Dot(p, right), Vector3.Dot(p, up)));
            triangles[slot].AddRange(new[] { i, i + 1, i + 2 });
        }

        public void Box(int slot, Vector3 center, Vector3 size) => Box(slot, center, size, Quaternion.identity);

        public void Box(int slot, Vector3 center, Vector3 size, Quaternion rotation, bool bottom = false)
        {
            Vector3 h = size / 2;
            Vector3 X = rotation * Vector3.right, Y = rotation * Vector3.up, Z = rotation * Vector3.forward;
            Quad(slot, center + X * h.x, X, Y, size.z, size.y);
            Quad(slot, center - X * h.x, -X, Y, size.z, size.y);
            Quad(slot, center + Z * h.z, Z, Y, size.x, size.y);
            Quad(slot, center - Z * h.z, -Z, Y, size.x, size.y);
            Quad(slot, center + Y * h.y, Y, Z, size.x, size.z);
            if (bottom) Quad(slot, center - Y * h.y, -Y, Z, size.x, size.z);
        }

        // Vertical n-sided prism (tanks, pillars). Optional top cap.
        public void Prism(int slot, Vector3 baseCenter, float radius, float height, int sides, bool cap = true)
        {
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2 / sides, a1 = (s + 1) * Mathf.PI * 2 / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius, p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                Vector3 n = new Vector3(Mathf.Cos((a0 + a1) / 2), 0, Mathf.Sin((a0 + a1) / 2));
                AddQuad(slot, baseCenter + p0, baseCenter + p1, baseCenter + p1 + Vector3.up * height, baseCenter + p0 + Vector3.up * height, n);
            }
            if (cap) Disc(slot, baseCenter + Vector3.up * height, radius, sides, true);
        }

        public void Disc(int slot, Vector3 center, float radius, int sides, bool facingUp)
        {
            int c = vertices.Count;
            vertices.Add(center); normals.Add(facingUp ? Vector3.up : Vector3.down); uvs.Add(new Vector2(center.x, center.z));
            for (int s = 0; s <= sides; s++)
            {
                float a = s * Mathf.PI * 2 / sides;
                var p = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                vertices.Add(p); normals.Add(facingUp ? Vector3.up : Vector3.down); uvs.Add(new Vector2(p.x, p.z));
            }
            for (int s = 0; s < sides; s++)
                if (facingUp) triangles[slot].AddRange(new[] { c, c + s + 2, c + s + 1 });
                else triangles[slot].AddRange(new[] { c, c + s + 1, c + s + 2 });
        }

        // Surface of revolution around +Y through `center`. `profile` points are (radius, height),
        // listed so that the outside of the surface is on the left when walking the list.
        public void Lathe(int slot, Vector3 center, IList<Vector2> profile, int sides)
        {
            for (int k = 0; k < profile.Count - 1; k++)
            {
                Vector2 a = profile[k], b = profile[k + 1];
                Vector2 edge = b - a;
                Vector2 outward = new Vector2(edge.y, -edge.x).normalized; // (radial, vertical)
                for (int s = 0; s < sides; s++)
                {
                    float t0 = s * Mathf.PI * 2 / sides, t1 = (s + 1) * Mathf.PI * 2 / sides;
                    Vector3 d0 = new Vector3(Mathf.Cos(t0), 0, Mathf.Sin(t0)), d1 = new Vector3(Mathf.Cos(t1), 0, Mathf.Sin(t1));
                    Vector3 dm = (d0 + d1).normalized;
                    Vector3 n = dm * outward.x + Vector3.up * outward.y;
                    Vector3 a0 = center + d0 * a.x + Vector3.up * a.y, a1 = center + d1 * a.x + Vector3.up * a.y;
                    Vector3 b0 = center + d0 * b.x + Vector3.up * b.y, b1 = center + d1 * b.x + Vector3.up * b.y;
                    AddQuad(slot, a0, a1, b1, b0, n);
                }
            }
        }

        // Cylinder along any axis (wheels, rails), with end caps.
        public void CylinderAlong(int slot, Vector3 center, Vector3 axis, float radius, float length, int sides)
        {
            axis = axis.normalized;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            Vector3 a = center - axis * length / 2, b = center + axis * length / 2;
            for (int s = 0; s < sides; s++)
            {
                float t0 = s * Mathf.PI * 2 / sides, t1 = (s + 1) * Mathf.PI * 2 / sides;
                Vector3 d0 = u * Mathf.Cos(t0) + v * Mathf.Sin(t0), d1 = u * Mathf.Cos(t1) + v * Mathf.Sin(t1);
                Vector3 n = (d0 + d1).normalized;
                Vector3 p0 = a + d0 * radius, p1 = a + d1 * radius, q0 = b + d0 * radius, q1 = b + d1 * radius;
                // Choose the order whose geometric normal points outward.
                if (Vector3.Dot(Vector3.Cross(q0 - p0, p1 - p0), n) > 0) AddQuad(slot, p0, p1, q1, q0, n);
                else AddQuad(slot, p1, p0, q0, q1, n);
            }
            foreach (var (end, normal) in new[] { (a, -axis), (b, axis) })
            {
                int c = vertices.Count;
                vertices.Add(end); normals.Add(normal); uvs.Add(Vector2.zero);
                for (int s = 0; s <= sides; s++)
                {
                    float t = s * Mathf.PI * 2 / sides;
                    vertices.Add(end + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * radius); normals.Add(normal); uvs.Add(Vector2.zero);
                }
                for (int s = 0; s < sides; s++)
                {
                    Vector3 p = vertices[c + s + 1], q = vertices[c + s + 2];
                    if (Vector3.Dot(Vector3.Cross(q - end, p - end), normal) > 0) triangles[slot].AddRange(new[] { c, c + s + 2, c + s + 1 });
                    else triangles[slot].AddRange(new[] { c, c + s + 1, c + s + 2 });
                }
            }
        }

        // Pyramid / cone from a square or n-gon base to an apex.
        public void Cone(int slot, Vector3 baseCenter, float radius, float height, int sides, float twistDegrees = 0)
        {
            Vector3 apex = baseCenter + Vector3.up * height;
            for (int s = 0; s < sides; s++)
            {
                float a0 = (s * 360f / sides + twistDegrees) * Mathf.Deg2Rad, a1 = ((s + 1) * 360f / sides + twistDegrees) * Mathf.Deg2Rad;
                Vector3 p0 = baseCenter + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                Vector3 p1 = baseCenter + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                Vector3 n = Vector3.Cross(p0 - p1, apex - p1).normalized;
                int i = vertices.Count;
                vertices.Add(p1); vertices.Add(p0); vertices.Add(apex);
                normals.Add(n); normals.Add(n); normals.Add(n);
                uvs.Add(Vector2.zero); uvs.Add(Vector2.right); uvs.Add(new Vector2(.5f, 1));
                triangles[slot].AddRange(new[] { i + 1, i + 2, i });
            }
        }

        // Builds the mesh, dropping empty slots. `usedSlots` maps sub-mesh index → original slot.
        public Mesh ToMesh(string name, out int[] usedSlots)
        {
            var used = new List<int>();
            for (int i = 0; i < triangles.Length; i++) if (triangles[i].Count > 0) used.Add(i);
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs);
            mesh.subMeshCount = used.Count;
            for (int i = 0; i < used.Count; i++) mesh.SetTriangles(triangles[used[i]], i);
            mesh.RecalculateBounds();
            usedSlots = used.ToArray();
            return mesh;
        }
    }
}
