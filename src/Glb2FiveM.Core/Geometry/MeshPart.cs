using System.Numerics;

namespace Glb2FiveM.Geometry;

/// <summary>Un maillage triangulé qui utilise un seul matériau.</summary>
public sealed class MeshPart
{
    public int MaterialIndex;
    public List<Vector3> Positions = new();
    public List<Vector3> Normals = new();
    public List<Vector2> Uvs = new();
    public List<Vector4> Colors = new();
    /// <summary>Tangentes (xyz + signe du binormal en w). Vide = à calculer.</summary>
    public List<Vector4> Tangents = new();
    public List<int> Indices = new();

    public int VertexCount => Positions.Count;
    public int TriangleCount => Indices.Count / 3;

    public MeshPart CloneEmpty() => new() { MaterialIndex = MaterialIndex };

    public MeshPart Clone() => new()
    {
        MaterialIndex = MaterialIndex,
        Positions = new(Positions),
        Normals = new(Normals),
        Uvs = new(Uvs),
        Colors = new(Colors),
        Tangents = new(Tangents),
        Indices = new(Indices),
    };
}

/// <summary>Le modèle complet : une liste de parties (une par matériau).</summary>
public sealed class SceneMesh
{
    public List<MeshPart> Parts = new();

    public int TriangleCount => Parts.Sum(p => p.TriangleCount);
    public int VertexCount => Parts.Sum(p => p.VertexCount);

    public (Vector3 Min, Vector3 Max) GetBounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in Parts)
            foreach (var v in p.Positions)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        if (min.X > max.X) return (Vector3.Zero, Vector3.Zero);
        return (min, max);
    }

    public SceneMesh Clone() => new() { Parts = Parts.Select(p => p.Clone()).ToList() };
}
