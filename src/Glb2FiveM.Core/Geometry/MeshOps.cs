using System.Numerics;

namespace Glb2FiveM.Geometry;

public static class MeshOps
{
    /// <summary>glTF (Y-up) vers GTA (Z-up) : rotation de +90° autour de X. (x, y, z) -> (x, -z, y).</summary>
    public static readonly Matrix4x4 GltfToGta = new(
        1, 0, 0, 0,
        0, 0, 1, 0,
        0, -1, 0, 0,
        0, 0, 0, 1);

    /// <summary>Applique une transformation affine (rotation + échelle uniforme + translation).</summary>
    public static void Transform(SceneMesh mesh, Matrix4x4 m)
    {
        Matrix4x4.Invert(m, out var inv);
        var nm = Matrix4x4.Transpose(inv);
        foreach (var p in mesh.Parts)
        {
            for (int i = 0; i < p.Positions.Count; i++)
                p.Positions[i] = Vector3.Transform(p.Positions[i], m);
            for (int i = 0; i < p.Normals.Count; i++)
                p.Normals[i] = SafeNormalize(Vector3.TransformNormal(p.Normals[i], nm), Vector3.UnitZ);
            for (int i = 0; i < p.Tangents.Count; i++)
            {
                var t = p.Tangents[i];
                var t3 = SafeNormalize(Vector3.TransformNormal(new Vector3(t.X, t.Y, t.Z), m), Vector3.UnitX);
                p.Tangents[i] = new Vector4(t3, t.W);
            }
        }
    }

    public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        var l = v.Length();
        return l > 1e-12f ? v / l : fallback;
    }

    /// <summary>Normales lissées pondérées par l'aire des triangles.</summary>
    public static void RecomputeNormals(MeshPart p)
    {
        var acc = new Vector3[p.VertexCount];
        for (int t = 0; t < p.Indices.Count; t += 3)
        {
            int a = p.Indices[t], b = p.Indices[t + 1], c = p.Indices[t + 2];
            var n = Vector3.Cross(p.Positions[b] - p.Positions[a], p.Positions[c] - p.Positions[a]);
            acc[a] += n; acc[b] += n; acc[c] += n;
        }
        p.Normals = acc.Select(n => SafeNormalize(n, Vector3.UnitZ)).ToList();
    }

    /// <summary>Tangentes par triangle (méthode de Lengyel), orthonormalisées sur la normale.</summary>
    public static void ComputeTangents(MeshPart p)
    {
        int n = p.VertexCount;
        var tan = new Vector3[n];
        var bit = new Vector3[n];
        for (int t = 0; t < p.Indices.Count; t += 3)
        {
            int a = p.Indices[t], b = p.Indices[t + 1], c = p.Indices[t + 2];
            var e1 = p.Positions[b] - p.Positions[a];
            var e2 = p.Positions[c] - p.Positions[a];
            var d1 = p.Uvs[b] - p.Uvs[a];
            var d2 = p.Uvs[c] - p.Uvs[a];
            float det = d1.X * d2.Y - d2.X * d1.Y;
            if (MathF.Abs(det) < 1e-12f) continue;
            float r = 1f / det;
            var sdir = (e1 * d2.Y - e2 * d1.Y) * r;
            var tdir = (e2 * d1.X - e1 * d2.X) * r;
            tan[a] += sdir; tan[b] += sdir; tan[c] += sdir;
            bit[a] += tdir; bit[b] += tdir; bit[c] += tdir;
        }
        p.Tangents = new List<Vector4>(n);
        for (int i = 0; i < n; i++)
        {
            var nrm = p.Normals[i];
            var t = tan[i] - nrm * Vector3.Dot(nrm, tan[i]);
            if (t.LengthSquared() < 1e-12f)
            {
                // Pas d'UV exploitables : une tangente quelconque perpendiculaire à la normale.
                t = MathF.Abs(nrm.Z) < 0.9f ? Vector3.Cross(nrm, Vector3.UnitZ) : Vector3.Cross(nrm, Vector3.UnitX);
            }
            t = SafeNormalize(t, Vector3.UnitX);
            float w = Vector3.Dot(Vector3.Cross(nrm, t), bit[i]) < 0 ? -1f : 1f;
            p.Tangents.Add(new Vector4(t, w));
        }
    }

    /// <summary>Fusionne les vertices strictement identiques (position, normale, UV, couleur, tangente).
    /// Indispensable avant la décimation : certains exports glTF ne partagent aucun vertex.</summary>
    public static void Weld(MeshPart p)
    {
        bool hasTan = p.Tangents.Count == p.VertexCount;
        var map = new Dictionary<VertexKey, int>(p.VertexCount);
        var remap = new int[p.VertexCount];
        var np = p.CloneEmpty();
        for (int i = 0; i < p.VertexCount; i++)
        {
            var key = new VertexKey(p.Positions[i], p.Normals[i], p.Uvs[i], p.Colors[i], hasTan ? p.Tangents[i] : Vector4.Zero);
            if (!map.TryGetValue(key, out var ni))
            {
                ni = np.Positions.Count;
                map[key] = ni;
                np.Positions.Add(p.Positions[i]);
                np.Normals.Add(p.Normals[i]);
                np.Uvs.Add(p.Uvs[i]);
                np.Colors.Add(p.Colors[i]);
                if (hasTan) np.Tangents.Add(p.Tangents[i]);
            }
            remap[i] = ni;
        }
        p.Positions = np.Positions; p.Normals = np.Normals; p.Uvs = np.Uvs; p.Colors = np.Colors; p.Tangents = np.Tangents;
        for (int i = 0; i < p.Indices.Count; i++) p.Indices[i] = remap[p.Indices[i]];
    }

    readonly record struct VertexKey(Vector3 P, Vector3 N, Vector2 U, Vector4 C, Vector4 T);

    /// <summary>Découpe une partie en morceaux de 65 535 vertices max (indices 16 bits de GTA).</summary>
    public static List<MeshPart> SplitByVertexLimit(MeshPart p, int maxVertices = 65535)
    {
        if (p.VertexCount <= maxVertices) return new List<MeshPart> { p };

        bool hasTan = p.Tangents.Count == p.VertexCount;
        var chunks = new List<MeshPart>();
        MeshPart cur = p.CloneEmpty();
        var map = new Dictionary<int, int>();

        int Map(int old)
        {
            if (map.TryGetValue(old, out var ni)) return ni;
            ni = cur.Positions.Count;
            map[old] = ni;
            cur.Positions.Add(p.Positions[old]);
            cur.Normals.Add(p.Normals[old]);
            cur.Uvs.Add(p.Uvs[old]);
            cur.Colors.Add(p.Colors[old]);
            if (hasTan) cur.Tangents.Add(p.Tangents[old]);
            return ni;
        }

        for (int t = 0; t < p.Indices.Count; t += 3)
        {
            if (cur.Positions.Count + 3 > maxVertices)
            {
                chunks.Add(cur);
                cur = p.CloneEmpty();
                map.Clear();
            }
            cur.Indices.Add(Map(p.Indices[t]));
            cur.Indices.Add(Map(p.Indices[t + 1]));
            cur.Indices.Add(Map(p.Indices[t + 2]));
        }
        if (cur.Indices.Count > 0) chunks.Add(cur);
        return chunks;
    }
}
