using System.Numerics;
using Glb2FiveM.Geometry;
using SharpGLTF.Schema2;

namespace Glb2FiveM.Import;

public sealed class ImportResult
{
    public SceneMesh Mesh = new();
    public List<MaterialDef> Materials = new();
}

/// <summary>Lit un .glb / .gltf et l'aplatit en une liste de maillages par matériau,
/// avec les transformations des nœuds déjà appliquées (espace glTF : Y-up, mètres).</summary>
public static class GlbImporter
{
    public static ImportResult Load(string path, ConversionLog log,
        IReadOnlyDictionary<int, byte[]>? imageOverrides = null,
        IReadOnlyDictionary<int, Vector4>? colorOverrides = null)
    {
        _imageOverrides = imageOverrides;
        _colorOverrides = colorOverrides;
        var settings = new ReadSettings { Validation = SharpGLTF.Validation.ValidationMode.TryFix };
        var model = ModelRoot.Load(path, settings);

        var result = new ImportResult();
        var materialMap = new Dictionary<int, int>(); // index logique glTF -> index local
        var partsByMaterial = new Dictionary<int, MeshPart>();

        int MaterialFor(Material? mat)
        {
            int key = mat?.LogicalIndex ?? -1;
            if (materialMap.TryGetValue(key, out var idx)) return idx;
            idx = result.Materials.Count;
            result.Materials.Add(ReadMaterial(mat, model, log));
            materialMap[key] = idx;
            return idx;
        }

        var scene = model.DefaultScene ?? model.LogicalScenes.FirstOrDefault();
        if (scene == null) throw new InvalidDataException(L.T("Le fichier ne contient aucune scène.", "The file contains no scene."));

        bool warnedSkin = false;
        foreach (var node in EnumerateNodes(scene.VisualChildren))
        {
            if (node.Mesh == null) continue;
            if (node.Skin != null && !warnedSkin)
            {
                log.Warn(L.T("Le modèle est riggé (skin) : il sera exporté dans sa pose de repos, sans os.", "The model is rigged (skin): it is exported in its rest pose, without bones."));
                warnedSkin = true;
            }

            var world = node.WorldMatrix;
            Matrix4x4.Invert(world, out var inv);
            var normalMatrix = Matrix4x4.Transpose(inv);
            bool mirrored = world.GetDeterminant() < 0;

            foreach (var prim in node.Mesh.Primitives)
            {
                if (prim.DrawPrimitiveType is not (PrimitiveType.TRIANGLES or PrimitiveType.TRIANGLE_STRIP or PrimitiveType.TRIANGLE_FAN))
                {
                    log.Warn(L.T($"Primitive ignorée ({prim.DrawPrimitiveType}) dans le mesh '{node.Mesh.Name}' : seuls les triangles sont pris en charge.", $"Primitive skipped ({prim.DrawPrimitiveType}) in mesh '{node.Mesh.Name}': only triangles are supported."));
                    continue;
                }

                var posAcc = prim.GetVertexAccessor("POSITION");
                if (posAcc == null) continue;

                int matIndex = MaterialFor(prim.Material);
                var mat = result.Materials[matIndex];
                if (!partsByMaterial.TryGetValue(matIndex, out var part))
                {
                    part = new MeshPart { MaterialIndex = matIndex };
                    partsByMaterial[matIndex] = part;
                    result.Mesh.Parts.Add(part);
                }

                var positions = posAcc.AsVector3Array();
                var normals = prim.GetVertexAccessor("NORMAL")?.AsVector3Array();
                var tangents = prim.GetVertexAccessor("TANGENT")?.AsVector4Array();
                int uvSet = GetUvSet(prim.Material);
                var uvs = (prim.GetVertexAccessor($"TEXCOORD_{uvSet}") ?? prim.GetVertexAccessor("TEXCOORD_0"))?.AsVector2Array();
                var colors = prim.GetVertexAccessor("COLOR_0")?.AsColorArray();
                var uvTransform = GetUvTransform(prim.Material);

                // Si une primitive n'a pas de tangentes, on recalculera tout pour cette partie.
                bool partHadTangents = part.VertexCount == 0 || part.Tangents.Count == part.VertexCount;
                bool useTangents = partHadTangents && tangents != null && normals != null;
                if (!useTangents) part.Tangents.Clear();

                int baseVertex = part.VertexCount;
                for (int i = 0; i < positions.Count; i++)
                {
                    part.Positions.Add(Vector3.Transform(positions[i], world));

                    var n = normals != null ? Vector3.TransformNormal(normals[i], normalMatrix) : Vector3.Zero;
                    part.Normals.Add(n.LengthSquared() > 0 ? Vector3.Normalize(n) : Vector3.Zero);

                    var uv = uvs != null ? uvs[i] : Vector2.Zero;
                    if (uvTransform.HasValue) uv = Vector2.Transform(uv, uvTransform.Value);
                    part.Uvs.Add(uv);

                    part.Colors.Add(colors != null ? colors[i] : Vector4.One);

                    if (useTangents)
                    {
                        var t = tangents![i];
                        var t3 = Vector3.TransformNormal(new Vector3(t.X, t.Y, t.Z), world);
                        t3 = t3.LengthSquared() > 0 ? Vector3.Normalize(t3) : Vector3.UnitX;
                        part.Tangents.Add(new Vector4(t3, (t.W < 0 ? -1f : 1f) * (mirrored ? -1f : 1f)));
                    }
                }

                foreach (var (a, b, c) in prim.GetTriangleIndices())
                {
                    if (a == b || b == c || a == c) continue; // triangles dégénérés
                    part.Indices.Add(baseVertex + a);
                    if (mirrored) { part.Indices.Add(baseVertex + c); part.Indices.Add(baseVertex + b); }
                    else { part.Indices.Add(baseVertex + b); part.Indices.Add(baseVertex + c); }
                }

                if (normals == null) MeshOps.RecomputeNormals(part);
            }
        }

        result.Mesh.Parts.RemoveAll(p => p.TriangleCount == 0);
        if (result.Mesh.Parts.Count == 0) throw new InvalidDataException(L.T("Aucun triangle trouvé dans le modèle.", "No triangles found in the model."));
        return result;
    }

    [ThreadStatic] static IReadOnlyDictionary<int, byte[]>? _imageOverrides;
    [ThreadStatic] static IReadOnlyDictionary<int, Vector4>? _colorOverrides;

    static IEnumerable<Node> EnumerateNodes(IEnumerable<Node> roots)
    {
        foreach (var n in roots)
        {
            yield return n;
            foreach (var c in EnumerateNodes(n.VisualChildren)) yield return c;
        }
    }

    static int GetUvSet(Material? mat)
    {
        var ch = mat?.FindChannel("BaseColor");
        return ch?.Texture != null ? ch.Value.TextureCoordinate : 0;
    }

    /// <summary>KHR_texture_transform de la couleur de base, appliqué directement aux UV.</summary>
    static Matrix3x2? GetUvTransform(Material? mat)
    {
        var ch = mat?.FindChannel("BaseColor");
        var tt = ch?.TextureTransform;
        if (tt == null) return null;
        if (tt.Offset == Vector2.Zero && tt.Scale == Vector2.One && tt.Rotation == 0) return null;
        // glTF : M = T * R * S, appliqué à des vecteurs colonnes. En Matrix3x2 (vecteurs lignes) : S * R * T.
        float c = MathF.Cos(tt.Rotation), s = MathF.Sin(tt.Rotation);
        var scale = Matrix3x2.CreateScale(tt.Scale);
        var rot = new Matrix3x2(c, -s, s, c, 0, 0);
        var trans = Matrix3x2.CreateTranslation(tt.Offset);
        return scale * rot * trans;
    }

    static MaterialDef ReadMaterial(Material? mat, ModelRoot model, ConversionLog log)
    {
        var def = new MaterialDef();
        if (mat == null) return def;

        def.Name = string.IsNullOrWhiteSpace(mat.Name) ? $"mat{mat.LogicalIndex}" : mat.Name;
        def.SourceIndex = mat.LogicalIndex;
        def.DoubleSided = mat.DoubleSided;
        def.AlphaCutoff = mat.AlphaCutoff;
        def.Alpha = mat.Alpha switch
        {
            AlphaMode.MASK => AlphaKind.Mask,
            AlphaMode.BLEND => AlphaKind.Blend,
            _ => AlphaKind.Opaque,
        };

        var baseCh = mat.FindChannel("BaseColor") ?? mat.FindChannel("Diffuse");
        if (baseCh != null)
        {
            def.BaseColorFactor = baseCh.Value.Color;
            def.BaseColorImage = ReadImage(baseCh.Value.Texture, log);
            def.BaseUvSet = baseCh.Value.Texture != null ? baseCh.Value.TextureCoordinate : 0;
        }
        if (_colorOverrides != null && _colorOverrides.TryGetValue(mat.LogicalIndex, out var forced))
            def.BaseColorFactor = forced;

        var nCh = mat.FindChannel("Normal");
        if (nCh != null)
        {
            def.NormalImage = ReadImage(nCh.Value.Texture, log);
            try { def.NormalScale = nCh.Value.GetFactor("NormalScale"); } catch { }
        }

        var mrCh = mat.FindChannel("MetallicRoughness");
        if (mrCh != null)
        {
            def.MetallicRoughnessImage = ReadImage(mrCh.Value.Texture, log);
            try { def.Metallic = mrCh.Value.GetFactor("MetallicFactor"); } catch { }
            try { def.Roughness = mrCh.Value.GetFactor("RoughnessFactor"); } catch { }
        }

        var eCh = mat.FindChannel("Emissive");
        if (eCh != null)
        {
            def.EmissiveImage = ReadImage(eCh.Value.Texture, log);
            def.EmissiveUvSet = eCh.Value.Texture != null ? eCh.Value.TextureCoordinate : def.BaseUvSet;
            try { def.EmissiveStrength = eCh.Value.GetFactor("EmissiveStrength"); } catch { }
            var c = eCh.Value.Color;
            def.EmissiveFactor = new Vector3(c.X, c.Y, c.Z);
        }

        var oCh = mat.FindChannel("Occlusion");
        if (oCh?.Texture != null)
        {
            def.OcclusionImage = ReadImage(oCh.Value.Texture, log);
            def.OcclusionUvSet = oCh.Value.TextureCoordinate;
            try { def.OcclusionStrength = oCh.Value.GetFactor("OcclusionStrength"); } catch { }
        }

        return def;
    }

    static SourceImage? ReadImage(Texture? tex, ConversionLog log)
    {
        if (tex == null) return null;
        var img = tex.PrimaryImage;
        if (img == null) return null;
        // Image retouchée / remplacée dans l'interface.
        if (_imageOverrides != null && _imageOverrides.TryGetValue(img.LogicalIndex, out var edited))
            return new SourceImage($"img{img.LogicalIndex}e", edited, "image/png");
        var content = img.Content;
        if (content.IsEmpty)
        {
            log.Warn(L.T($"Texture '{img.Name}' introuvable ou vide : ignorée.", $"Texture '{img.Name}' missing or empty: skipped."));
            return null;
        }
        var bytes = content.Content.ToArray();
        return new SourceImage($"img{img.LogicalIndex}", bytes, img.Content.MimeType);
    }
}
