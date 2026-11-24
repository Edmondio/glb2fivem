using System.Numerics;

namespace Glb2FiveM.Geometry;

/// <summary>Construit le maillage de collision : positions seules, soudées, puis décimées.</summary>
public static class CollisionBuilder
{
    /// <summary>Plafond absolu : au-delà, la collision coûte cher au moteur physique.</summary>
    public const int HardLimit = 30000;

    public static MeshPart Build(SceneMesh visual, int maxTriangles, ConversionLog? log = null)
    {
        // 1. Fusion de toutes les parties en soudant les sommets proches (0,5 mm) :
        //    sans ça, chaque couture d'UV deviendrait un trou dans la collision.
        var part = new MeshPart();
        var map = new Dictionary<(int, int, int), int>();
        const float q = 2000f;
        foreach (var p in visual.Parts)
        {
            var remap = new int[p.VertexCount];
            for (int i = 0; i < p.VertexCount; i++)
            {
                var v = p.Positions[i];
                var key = ((int)MathF.Round(v.X * q), (int)MathF.Round(v.Y * q), (int)MathF.Round(v.Z * q));
                if (!map.TryGetValue(key, out var ni))
                {
                    ni = part.Positions.Count;
                    map[key] = ni;
                    part.Positions.Add(v);
                    part.Normals.Add(Vector3.Zero);
                    part.Uvs.Add(Vector2.Zero);
                    part.Colors.Add(Vector4.One);
                }
                remap[i] = ni;
            }
            for (int t = 0; t < p.Indices.Count; t += 3)
            {
                int a = remap[p.Indices[t]], b = remap[p.Indices[t + 1]], c = remap[p.Indices[t + 2]];
                if (a == b || b == c || a == c) continue;
                part.Indices.Add(a); part.Indices.Add(b); part.Indices.Add(c);
            }
        }

        // 2. Décimation qui respecte la topologie : les ouvertures (porte, barreaux) restent ouvertes.
        //    Si la forme ne peut pas descendre jusqu'à la cible, on garde plus de triangles plutôt que de boucher.
        var scene = new SceneMesh { Parts = { part } };
        var reduced = Simplifier.Decimate(scene, maxTriangles, allowSloppy: false);
        if (reduced.TriangleCount > HardLimit)
        {
            log?.Warn(L.T($"Collision trop détaillée ({reduced.TriangleCount:N0} triangles) : réduction forcée à {HardLimit:N0}, des ouvertures peuvent se fermer.", $"Collision too detailed ({reduced.TriangleCount:N0} triangles): forced down to {HardLimit:N0}, some openings may close."));
            reduced = Simplifier.Decimate(reduced, HardLimit, allowSloppy: true);
        }
        else if (reduced.TriangleCount > maxTriangles * 1.2)
        {
            log?.Info(L.T($"Collision : {reduced.TriangleCount:N0} triangles au lieu de {maxTriangles:N0}, pour garder les ouvertures du modèle.", $"Collision: {reduced.TriangleCount:N0} triangles instead of {maxTriangles:N0}, to keep the model's openings."));
        }
        var result = reduced.Parts.FirstOrDefault() ?? new MeshPart();

        // 3. Retire les triangles dégénérés (aire quasi nulle) laissés par la décimation.
        var clean = result.CloneEmpty();
        var used = new Dictionary<int, int>();
        int Map(int old)
        {
            if (used.TryGetValue(old, out var ni)) return ni;
            ni = clean.Positions.Count;
            used[old] = ni;
            clean.Positions.Add(result.Positions[old]);
            clean.Normals.Add(Vector3.Zero);
            clean.Uvs.Add(Vector2.Zero);
            clean.Colors.Add(Vector4.One);
            return ni;
        }
        for (int t = 0; t < result.Indices.Count; t += 3)
        {
            int a = result.Indices[t], b = result.Indices[t + 1], c = result.Indices[t + 2];
            var area = Vector3.Cross(result.Positions[b] - result.Positions[a], result.Positions[c] - result.Positions[a]).Length();
            if (area < 1e-8f) continue;
            clean.Indices.Add(Map(a)); clean.Indices.Add(Map(b)); clean.Indices.Add(Map(c));
        }
        return clean;
    }
}
