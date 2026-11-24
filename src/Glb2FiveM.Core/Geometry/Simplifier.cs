using System.Numerics;
using System.Runtime.InteropServices;

namespace Glb2FiveM.Geometry;

/// <summary>Réduction du nombre de triangles avec meshoptimizer (bibliothèque native, voir native/).
/// Passe 1 : simplification qui respecte la topologie, les coutures d'UV et les normales.
/// Passe 2 (si la cible n'est pas atteinte, ex. maillage très découpé) : simplification "sloppy".</summary>
public static class Simplifier
{
    const string Lib = "meshoptimizer";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern unsafe nuint meshopt_simplifyWithAttributes(uint* destination, uint* indices, nuint indexCount,
        float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride,
        float* vertexAttributes, nuint vertexAttributesStride, float* attributeWeights, nuint attributeCount,
        byte* vertexLock, nuint targetIndexCount, float targetError, uint options, float* resultError);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern unsafe nuint meshopt_simplifySloppy(uint* destination, uint* indices, nuint indexCount,
        float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride, byte* vertexLock,
        nuint targetIndexCount, float targetError, float* resultError);

    /// <summary>Décime l'ensemble du modèle ; le budget est réparti entre les parties au prorata.</summary>
    /// <param name="allowSloppy">Autorise la passe "sloppy" si la cible n'est pas atteinte. Elle peut fusionner
    /// des parties proches (barreaux, ouvertures) : à éviter pour la collision.</param>
    public static SceneMesh Decimate(SceneMesh src, int targetTriangles, ConversionLog? log = null, bool allowSloppy = true)
    {
        int total = src.TriangleCount;
        if (total <= targetTriangles) return src.Clone();

        var result = new SceneMesh();
        bool usedSloppy = false;
        foreach (var part in src.Parts)
        {
            int target = Math.Max(4, (int)Math.Round((double)part.TriangleCount * targetTriangles / total));
            var (p, sloppy) = DecimatePart(part, target, allowSloppy);
            usedSloppy |= sloppy;
            if (p.TriangleCount > 0) result.Parts.Add(p);
        }
        if (usedSloppy)
            log?.Info(L.T("Maillage très découpé : décimation agressive appliquée pour atteindre la cible.", "Heavily split mesh: aggressive decimation applied to reach the target."));
        return result;
    }

    static unsafe (MeshPart part, bool sloppy) DecimatePart(MeshPart p, int targetTriangles, bool allowSloppy)
    {
        if (p.TriangleCount <= targetTriangles) return (p.Clone(), false);

        int vc = p.VertexCount;
        var positions = new float[vc * 3];
        var attrs = new float[vc * 5];
        for (int i = 0; i < vc; i++)
        {
            var v = p.Positions[i];
            positions[i * 3] = v.X; positions[i * 3 + 1] = v.Y; positions[i * 3 + 2] = v.Z;
            var n = p.Normals[i];
            var uv = p.Uvs[i];
            attrs[i * 5] = n.X; attrs[i * 5 + 1] = n.Y; attrs[i * 5 + 2] = n.Z;
            attrs[i * 5 + 3] = uv.X; attrs[i * 5 + 4] = uv.Y;
        }
        var weights = new float[] { 0.5f, 0.5f, 0.5f, 1f, 1f };
        var indices = p.Indices.Select(i => (uint)i).ToArray();
        var dest = new uint[indices.Length];
        nuint targetIndices = (nuint)(targetTriangles * 3);
        nuint count;
        float err;
        bool sloppy = false;

        fixed (uint* pi = indices, pd = dest)
        fixed (float* pp = positions, pa = attrs, pw = weights)
        {
            count = meshopt_simplifyWithAttributes(pd, pi, (nuint)indices.Length, pp, (nuint)vc, 12,
                pa, 20, pw, 5, null, targetIndices, 1f, 0, &err);

            // Cible pas atteinte (coutures/bords bloquants) : seconde passe sans contrainte de topologie.
            if (allowSloppy && count > targetIndices + targetIndices / 5)
            {
                var pass1 = dest.AsSpan(0, (int)count).ToArray();
                fixed (uint* p1 = pass1)
                    count = meshopt_simplifySloppy(pd, p1, (nuint)pass1.Length, pp, (nuint)vc, 12, null, targetIndices, 1f, &err);
                sloppy = true;
            }
        }

        // Compacte : ne garde que les vertices utilisés.
        var outPart = p.CloneEmpty();
        bool hasTan = p.Tangents.Count == vc;
        var remap = new Dictionary<uint, int>();
        for (int i = 0; i < (int)count; i++)
        {
            uint oi = dest[i];
            if (!remap.TryGetValue(oi, out var ni))
            {
                ni = outPart.Positions.Count;
                remap[oi] = ni;
                outPart.Positions.Add(p.Positions[(int)oi]);
                outPart.Normals.Add(p.Normals[(int)oi]);
                outPart.Uvs.Add(p.Uvs[(int)oi]);
                outPart.Colors.Add(p.Colors[(int)oi]);
                if (hasTan) outPart.Tangents.Add(p.Tangents[(int)oi]);
            }
            outPart.Indices.Add(ni);
        }
        return (outPart, sloppy);
    }
}
