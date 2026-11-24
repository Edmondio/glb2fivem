using System.Numerics;
using System.Text;
using Glb2FiveM.Geometry;
using static Glb2FiveM.Gta.XmlFmt;

namespace Glb2FiveM.Gta;

public sealed class GtaGeometry
{
    public required int ShaderIndex { get; init; }
    public required MeshPart Mesh { get; init; }
    public bool WithTangents { get; init; }
}

public sealed class GtaLod
{
    public List<GtaGeometry> Geometries { get; } = new();
    public float Distance { get; set; } = 9998f;
}

public sealed class GtaDrawable
{
    public required string Name { get; init; }
    public List<GtaShader> Shaders { get; } = new();
    /// <summary>High, Medium, Low, VeryLow (null = absent).</summary>
    public GtaLod?[] Lods { get; } = new GtaLod?[4];
    public CollisionMode Collision { get; init; }
    /// <summary>Maillage de collision (mode Mesh) : sommets + indices de triangles.</summary>
    public MeshPart? CollisionMesh { get; set; }
    public List<LightDef> Lights { get; } = new();
    public Vector3 BoundsMin { get; set; }
    public Vector3 BoundsMax { get; set; }
    public Vector3 SphereCenter { get; set; }
    public float SphereRadius { get; set; }
}

/// <summary>Écrit le XML "Drawable" de CodeWalker, compilé ensuite en .ydr.</summary>
public static class YdrXmlWriter
{
    static readonly string[] LodTags = { "DrawableModelsHigh", "DrawableModelsMedium", "DrawableModelsLow", "DrawableModelsVeryLow" };

    /// <param name="embedTextures">Textures à intégrer au YDR (aperçu CodeWalker). Null = YTD séparé.</param>
    public static string Write(GtaDrawable d, IEnumerable<GtaTexture>? embedTextures = null)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        Line(sb, 0, "<Drawable>");
        Line(sb, 1, $"<Name>{Esc(d.Name)}</Name>");
        Line(sb, 1, $"<BoundingSphereCenter {V3(d.SphereCenter)} />");
        Line(sb, 1, $"<BoundingSphereRadius value=\"{F(d.SphereRadius)}\" />");
        Line(sb, 1, $"<BoundingBoxMin {V3(d.BoundsMin)} />");
        Line(sb, 1, $"<BoundingBoxMax {V3(d.BoundsMax)} />");
        string[] distTags = { "LodDistHigh", "LodDistMed", "LodDistLow", "LodDistVlow" };
        for (int i = 0; i < 4; i++) Line(sb, 1, $"<{distTags[i]} value=\"{F(d.Lods[i]?.Distance ?? 9998f)}\" />");
        string[] flagTags = { "FlagsHigh", "FlagsMed", "FlagsLow", "FlagsVlow" };
        for (int i = 0; i < 4; i++) Line(sb, 1, $"<{flagTags[i]} value=\"{(d.Lods[i] != null ? 1 : 0)}\" />");

        WriteShaderGroup(sb, d, embedTextures);

        for (int i = 0; i < 4; i++)
        {
            var lod = d.Lods[i];
            if (lod == null || lod.Geometries.Count == 0) continue;
            Line(sb, 1, $"<{LodTags[i]}>");
            Line(sb, 2, "<Item>");
            Line(sb, 3, "<RenderMask value=\"255\" />");
            Line(sb, 3, "<Flags value=\"0\" />");
            Line(sb, 3, "<HasSkin value=\"0\" />");
            Line(sb, 3, "<BoneIndex value=\"0\" />");
            Line(sb, 3, "<Unknown1 value=\"0\" />");
            Line(sb, 3, "<Geometries>");
            foreach (var g in lod.Geometries) WriteGeometry(sb, g);
            Line(sb, 3, "</Geometries>");
            Line(sb, 2, "</Item>");
            Line(sb, 1, $"</{LodTags[i]}>");
        }

        if (d.Lights.Count > 0) WriteLights(sb, d.Lights);

        if (d.Collision == CollisionMode.Box) WriteBounds(sb, d.BoundsMin, d.BoundsMax, null);
        else if (d.Collision == CollisionMode.Mesh && d.CollisionMesh is { TriangleCount: > 0 } cm) WriteBounds(sb, d.BoundsMin, d.BoundsMax, cm);

        Line(sb, 0, "</Drawable>");
        return sb.ToString();
    }

    static void WriteShaderGroup(StringBuilder sb, GtaDrawable d, IEnumerable<GtaTexture>? embedTextures)
    {
        Line(sb, 1, "<ShaderGroup>");
        Line(sb, 2, "<Unknown30 value=\"8\" />");
        if (embedTextures != null)
        {
            Line(sb, 2, "<TextureDictionary>");
            YtdXmlWriter.WriteItems(sb, embedTextures, 3);
            Line(sb, 2, "</TextureDictionary>");
        }
        Line(sb, 2, "<Shaders>");
        foreach (var s in d.Shaders)
        {
            Line(sb, 3, "<Item>");
            Line(sb, 4, $"<Name>{s.Name}</Name>");
            Line(sb, 4, $"<FileName>{s.FileName}</FileName>");
            Line(sb, 4, $"<RenderBucket value=\"{s.RenderBucket}\" />");
            Line(sb, 4, "<Parameters>");
            foreach (var p in s.Parameters)
            {
                switch (p.Kind)
                {
                    case ShaderParamKind.Texture:
                        if (p.TextureName != null)
                        {
                            Line(sb, 5, $"<Item name=\"{p.Name}\" type=\"Texture\">");
                            Line(sb, 6, $"<Name>{Esc(p.TextureName)}</Name>");
                            Line(sb, 5, "</Item>");
                        }
                        else Line(sb, 5, $"<Item name=\"{p.Name}\" type=\"Texture\" />");
                        break;
                    case ShaderParamKind.Vector:
                        Line(sb, 5, $"<Item name=\"{p.Name}\" type=\"Vector\" {V4(p.Value)} />");
                        break;
                    case ShaderParamKind.Array:
                        Line(sb, 5, $"<Item name=\"{p.Name}\" type=\"Array\">");
                        foreach (var v in p.Values ?? Array.Empty<Vector4>()) Line(sb, 6, $"<Value {V4(v)} />");
                        Line(sb, 5, "</Item>");
                        break;
                }
            }
            Line(sb, 4, "</Parameters>");
            Line(sb, 3, "</Item>");
        }
        Line(sb, 2, "</Shaders>");
        Line(sb, 1, "</ShaderGroup>");
    }

    static void WriteGeometry(StringBuilder sb, GtaGeometry g)
    {
        var m = g.Mesh;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in m.Positions) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }

        Line(sb, 4, "<Item>");
        Line(sb, 5, $"<ShaderIndex value=\"{g.ShaderIndex}\" />");
        Line(sb, 5, $"<BoundingBoxMin {V4(new Vector4(min, 0))} />");
        Line(sb, 5, $"<BoundingBoxMax {V4(new Vector4(max, 0))} />");
        Line(sb, 5, "<VertexBuffer>");
        Line(sb, 6, "<Flags value=\"0\" />");
        Line(sb, 6, "<Layout type=\"GTAV1\">");
        Line(sb, 7, "<Position />");
        Line(sb, 7, "<Normal />");
        Line(sb, 7, "<Colour0 />");
        Line(sb, 7, "<TexCoord0 />");
        if (g.WithTangents) Line(sb, 7, "<Tangent />");
        Line(sb, 6, "</Layout>");
        Line(sb, 6, "<Data>");
        var row = new StringBuilder(160);
        for (int i = 0; i < m.VertexCount; i++)
        {
            var p = m.Positions[i];
            var n = m.Normals[i];
            var c = m.Colors[i];
            var uv = m.Uvs[i];
            row.Clear();
            row.Append(F(p.X)).Append(' ').Append(F(p.Y)).Append(' ').Append(F(p.Z)).Append("   ");
            row.Append(F(n.X)).Append(' ').Append(F(n.Y)).Append(' ').Append(F(n.Z)).Append("   ");
            row.Append(B(c.X)).Append(' ').Append(B(c.Y)).Append(' ').Append(B(c.Z)).Append(' ').Append(B(c.W)).Append("   ");
            row.Append(F(uv.X)).Append(' ').Append(F(uv.Y));
            if (g.WithTangents)
            {
                var t = m.Tangents[i];
                row.Append("   ").Append(F(t.X)).Append(' ').Append(F(t.Y)).Append(' ').Append(F(t.Z)).Append(' ').Append(F(t.W));
            }
            Line(sb, 7, row.ToString());
        }
        Line(sb, 6, "</Data>");
        Line(sb, 5, "</VertexBuffer>");
        Line(sb, 5, "<IndexBuffer>");
        Line(sb, 6, "<Data>");
        for (int i = 0; i < m.Indices.Count; i += 24)
        {
            row.Clear();
            for (int k = i; k < Math.Min(i + 24, m.Indices.Count); k++)
            {
                if (k > i) row.Append(' ');
                row.Append(m.Indices[k]);
            }
            Line(sb, 7, row.ToString());
        }
        Line(sb, 6, "</Data>");
        Line(sb, 5, "</IndexBuffer>");
        Line(sb, 4, "</Item>");
    }

    static string B(float v) => Math.Clamp((int)MathF.Round(v * 255f), 0, 255).ToString();

    /// <summary>Lumières intégrées (même format que les lampes de R*, attachées à la racine du drawable).</summary>
    static void WriteLights(StringBuilder sb, List<LightDef> lights)
    {
        Line(sb, 1, "<Lights>");
        foreach (var l in lights)
        {
            var dir = Vector3.Normalize(l.Type != GtaLightType.Point && l.Direction.LengthSquared() > 1e-6f ? l.Direction : -Vector3.UnitZ);
            // Capsule (tube) : seule la composante X de l'Extent est utilisée, c'est la longueur du tube.
            float ext = l.Type == GtaLightType.Capsule ? Math.Clamp(l.Length, 0.01f, 50f) : 1f;
            // Tangente : n'importe quel vecteur perpendiculaire à la direction.
            var tangent = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
            float outer = l.Type == GtaLightType.Spot ? Math.Clamp(l.ConeAngle, 1f, 90f) : 0f;
            float inner = l.Type == GtaLightType.Spot ? outer * 0.6f : 0f;
            // Ombres : statiques + dynamiques (valeur des spots de R*).
            uint flags = l.CastShadows ? 384u : 0u;

            Line(sb, 2, "<Item>");
            Line(sb, 3, $"<Position {V3(l.Position)} />");
            Line(sb, 3, $"<Colour r=\"{l.R}\" g=\"{l.G}\" b=\"{l.B}\" />");
            Line(sb, 3, $"<Flashiness value=\"{l.Flashiness}\" />");
            Line(sb, 3, $"<Intensity value=\"{F(l.Intensity)}\" />");
            Line(sb, 3, $"<Flags value=\"{flags}\" />");
            Line(sb, 3, "<BoneId value=\"0\" />");
            Line(sb, 3, $"<Type>{l.Type}</Type>");
            Line(sb, 3, "<GroupId value=\"0\" />");
            Line(sb, 3, $"<TimeFlags value=\"{l.TimeFlags & 0xFFFFFF}\" />");
            Line(sb, 3, $"<Falloff value=\"{F(MathF.Max(0.1f, l.Falloff))}\" />");
            Line(sb, 3, $"<FalloffExponent value=\"{F(l.FalloffExponent)}\" />");
            Line(sb, 3, "<CullingPlaneNormal x=\"0\" y=\"0\" z=\"0\" />");
            Line(sb, 3, "<CullingPlaneOffset value=\"0\" />");
            Line(sb, 3, "<Unknown45 value=\"0\" />");
            Line(sb, 3, "<Unknown46 value=\"0\" />");
            Line(sb, 3, $"<VolumeIntensity value=\"{F(l.VolumeIntensity)}\" />");
            Line(sb, 3, "<VolumeSizeScale value=\"1\" />");
            Line(sb, 3, $"<VolumeOuterColour r=\"{l.R}\" g=\"{l.G}\" b=\"{l.B}\" />");
            Line(sb, 3, "<LightHash value=\"0\" />");
            Line(sb, 3, "<VolumeOuterIntensity value=\"1\" />");
            Line(sb, 3, $"<CoronaSize value=\"{F(l.CoronaSize)}\" />");
            Line(sb, 3, "<VolumeOuterExponent value=\"1\" />");
            Line(sb, 3, "<LightFadeDistance value=\"0\" />");
            Line(sb, 3, "<ShadowBlur value=\"0\" />");
            Line(sb, 3, $"<ShadowFadeDistance value=\"{(l.CastShadows ? 20 : 0)}\" />");
            Line(sb, 3, "<SpecularFadeDistance value=\"0\" />");
            Line(sb, 3, "<VolumetricFadeDistance value=\"0\" />");
            Line(sb, 3, "<ShadowNearClip value=\"0.01\" />");
            Line(sb, 3, $"<CoronaIntensity value=\"{F(l.CoronaIntensity)}\" />");
            Line(sb, 3, "<CoronaZBias value=\"0.1\" />");
            Line(sb, 3, $"<Direction {V3(dir)} />");
            Line(sb, 3, $"<Tangent {V3(tangent)} />");
            Line(sb, 3, $"<ConeInnerAngle value=\"{F(inner)}\" />");
            Line(sb, 3, $"<ConeOuterAngle value=\"{F(outer)}\" />");
            Line(sb, 3, $"<Extent x=\"{F(ext)}\" y=\"{F(ext)}\" z=\"{F(ext)}\" />");
            Line(sb, 3, "<ProjectedTextureHash />");
            Line(sb, 2, "</Item>");
        }
        Line(sb, 1, "</Lights>");
    }

    const string Flags1 = "MAP_WEAPON, MAP_DYNAMIC, MAP_ANIMAL, MAP_COVER, MAP_VEHICLE";
    const string Flags2 = "VEHICLE_NOT_BVH, VEHICLE_BVH, PED, RAGDOLL, ANIMAL, ANIMAL_RAGDOLL, OBJECT, PLANT, PROJECTILE, EXPLOSION, FORKLIFT_FORKS, TEST_WEAPON, TEST_CAMERA, TEST_AI, TEST_SCRIPT, TEST_VEHICLE_WHEEL, GLASS";

    /// <summary>Collision intégrée : un Composite contenant soit une boîte, soit un maillage BVH.
    /// Marges reprises des exports R* : composite 0, boîte min(0,04 ; plus petit côté / 8), BVH 0,04.</summary>
    static void WriteBounds(StringBuilder sb, Vector3 min, Vector3 max, MeshPart? mesh)
    {
        // Évite une boîte plate (plan, poster...) qui ne collisionnerait pas.
        var size = Vector3.Max(max - min, new Vector3(0.02f));
        var center = (min + max) * 0.5f;
        min = center - size * 0.5f;
        max = center + size * 0.5f;
        float volume = size.X * size.Y * size.Z;
        var inertia = new Vector3(
            (size.Y * size.Y + size.Z * size.Z) / 12f,
            (size.X * size.X + size.Z * size.Z) / 12f,
            (size.X * size.X + size.Y * size.Y) / 12f);

        float childMargin;
        Vector3 cmin = min, cmax = max;
        if (mesh == null)
        {
            childMargin = MathF.Min(0.04f, MathF.Min(size.X, MathF.Min(size.Y, size.Z)) / 8f);
        }
        else
        {
            childMargin = 0.04f;
            // Pour un BVH, la boîte englobante inclut la marge.
            cmin = min - new Vector3(childMargin);
            cmax = max + new Vector3(childMargin);
        }

        void Common(int ind, Vector3 bmin, Vector3 bmax, float margin)
        {
            var c = (bmin + bmax) * 0.5f;
            Line(sb, ind, $"<BoxMin {V3(bmin)} />");
            Line(sb, ind, $"<BoxMax {V3(bmax)} />");
            Line(sb, ind, $"<BoxCenter {V3(c)} />");
            Line(sb, ind, $"<SphereCenter {V3(c)} />");
            Line(sb, ind, $"<SphereRadius value=\"{F(((bmax - bmin) * 0.5f).Length())}\" />");
            Line(sb, ind, $"<Margin value=\"{F(margin)}\" />");
            Line(sb, ind, $"<Volume value=\"{F(volume)}\" />");
            Line(sb, ind, $"<Inertia {V3(inertia)} />");
            Line(sb, ind, "<MaterialIndex value=\"0\" />");
            Line(sb, ind, "<MaterialColourIndex value=\"0\" />");
            Line(sb, ind, "<ProceduralID value=\"0\" />");
            Line(sb, ind, "<RoomID value=\"0\" />");
            Line(sb, ind, "<PedDensity value=\"0\" />");
            Line(sb, ind, "<UnkFlags value=\"0\" />");
            Line(sb, ind, "<PolyFlags value=\"0\" />");
            Line(sb, ind, "<UnkType value=\"1\" />");
        }

        Line(sb, 1, "<Bounds type=\"Composite\">");
        Common(2, cmin, cmax, 0f);
        Line(sb, 2, "<Children>");
        Line(sb, 3, $"<Item type=\"{(mesh == null ? "Box" : "GeometryBVH")}\">");
        Common(4, cmin, cmax, childMargin);
        Line(sb, 4, "<CompositeTransform>");
        Line(sb, 5, "1 0 0 0");
        Line(sb, 5, "0 1 0 0");
        Line(sb, 5, "0 0 1 0");
        Line(sb, 5, "0 0 0 1");
        Line(sb, 4, "</CompositeTransform>");
        // Type : objet de map ; collisionne avec véhicules, peds, objets, projectiles, caméra...
        Line(sb, 4, $"<CompositeFlags1>{Flags1}</CompositeFlags1>");
        Line(sb, 4, $"<CompositeFlags2>{Flags2}</CompositeFlags2>");

        if (mesh != null)
        {
            // Les sommets sont relatifs au centre de la géométrie.
            Line(sb, 4, $"<GeometryCenter {V3(center)} />");
            Line(sb, 4, "<UnkFloat1 value=\"0\" />");
            Line(sb, 4, "<UnkFloat2 value=\"0\" />");
            Line(sb, 4, "<Materials>");
            Line(sb, 5, "<Item>");
            Line(sb, 6, "<Type value=\"0\" />");
            Line(sb, 6, "<ProceduralID value=\"0\" />");
            Line(sb, 6, "<RoomID value=\"0\" />");
            Line(sb, 6, "<PedDensity value=\"0\" />");
            Line(sb, 6, "<Flags>NONE</Flags>");
            Line(sb, 6, "<MaterialColourIndex value=\"0\" />");
            Line(sb, 6, "<Unk value=\"0\" />");
            Line(sb, 5, "</Item>");
            Line(sb, 4, "</Materials>");
            Line(sb, 4, "<Vertices>");
            foreach (var v in mesh.Positions)
            {
                var r = v - center;
                Line(sb, 5, $"{F(r.X)}, {F(r.Y)}, {F(r.Z)}");
            }
            Line(sb, 4, "</Vertices>");
            Line(sb, 4, "<Polygons>");
            for (int i = 0; i < mesh.Indices.Count; i += 3)
                Line(sb, 5, $"<Triangle m=\"0\" v1=\"{mesh.Indices[i]}\" v2=\"{mesh.Indices[i + 1]}\" v3=\"{mesh.Indices[i + 2]}\" f1=\"0\" f2=\"0\" f3=\"0\" />");
            Line(sb, 4, "</Polygons>");
        }

        Line(sb, 3, "</Item>");
        Line(sb, 2, "</Children>");
        Line(sb, 1, "</Bounds>");
    }
}
