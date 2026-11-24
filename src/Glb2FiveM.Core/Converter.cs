using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using CodeWalker.GameFiles;
using Glb2FiveM.Geometry;
using Glb2FiveM.Gta;
using Glb2FiveM.Import;

namespace Glb2FiveM;

public sealed class LodReport
{
    public required string Level { get; init; }
    public int Triangles { get; init; }
    public int Vertices { get; init; }
    public int Geometries { get; init; }
    public float Distance { get; init; }
}

public sealed class PreparedModel
{
    public required string Name { get; init; }
    public required ImportResult Import { get; init; }
    public int SourceTriangles { get; init; }
    public int SourceVertices { get; init; }
    public bool Decimated { get; init; }
    public float Scale { get; init; }
    public Vector3 Min { get; init; }
    public Vector3 Max { get; init; }
    /// <summary>High, Medium, Low, VeryLow (null = absent).</summary>
    public required SceneMesh?[] Lods { get; init; }
    public MeshPart? CollisionMesh { get; init; }
}

public sealed class ConversionResult
{
    public required string PropName { get; init; }
    public required string ResourceDir { get; init; }
    public int SourceTriangles { get; init; }
    public bool Decimated { get; init; }
    public Vector3 Size { get; init; }
    public float AppliedScale { get; init; }
    public List<LodReport> Lods { get; } = new();
    public List<GtaTexture> Textures { get; } = new();
    public List<GtaShader> Shaders { get; } = new();
    public Dictionary<string, long> Files { get; } = new();
    public float ArchetypeLodDist { get; init; }
    /// <summary>Dossier de l'aperçu CodeWalker (YDR avec textures intégrées), si demandé.</summary>
    public string? PreviewDir { get; set; }
}

/// <summary>Pipeline complet : GLB -> ressource FiveM (YDR + YTD + YTYP + fxmanifest).</summary>
public static class Converter
{
    public static ConversionResult Convert(ConvertOptions opt, ConversionLog log)
    {
        var prep = Prepare(opt, log);
        var name = prep.Name;
        var imp = prep.Import;
        var high = prep.Lods[0]!;
        var lodMeshes = prep.Lods;
        var (bmin, bmax) = (prep.Min, prep.Max);
        var size = bmax - bmin;
        int sourceTris = prep.SourceTriangles;
        bool decimated = prep.Decimated;
        float scale = prep.Scale;

        // 4. Matériaux -> shaders + textures ---------------------------------------
        var workDir = Path.Combine(Path.GetTempPath(), "glb2fivem_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workDir);
        try
        {
            var catalog = ShaderCatalog.Load();
            var textures = new TextureBank(workDir, name, opt, log);
            var mapper = new MaterialMapper(catalog, textures, log);

            var drawable = new GtaDrawable
            {
                Name = name,
                Collision = opt.Collision,
                BoundsMin = bmin,
                BoundsMax = bmax,
            };
            drawable.CollisionMesh = prep.CollisionMesh;
            drawable.Lights.AddRange(opt.Lights);
            if (opt.Lights.Count > 0) log.Info(L.T($"Lumières intégrées : {opt.Lights.Count}", $"Lights embedded: {opt.Lights.Count}"));
            // Émissif : copie des triangles qui brillent, rendue avec un décalque émissif, dans chaque LOD.
            var masks = new Dictionary<int, (float[] Mask, int W, int H)?>();
            int emissiveTris = 0;
            for (int li = 0; li < 4; li++)
            {
                var lm = lodMeshes[li];
                if (lm == null) continue;
                var extra = new List<MeshPart>();
                foreach (var part in lm.Parts)
                {
                    var mat = imp.Materials[part.MaterialIndex];
                    var mo = opt.MaterialOptionsFor(mat.SourceIndex);
                    if (!mat.HasEmissive || mo.Emissive == EmissiveMode.Off) continue;
                    if (!masks.TryGetValue(part.MaterialIndex, out var mask))
                    {
                        if (mat.EmissiveImage != null && mat.EmissiveUvSet != mat.BaseUvSet)
                            log.Warn(L.T($"Matériau '{mat.Name}' : l'émissif utilise un autre jeu d'UV, le résultat peut être décalé.", $"Material '{mat.Name}': the emissive map uses another UV set, the result may be offset."));
                        masks[part.MaterialIndex] = mask = textures.GetEmissiveMask(mat);
                    }
                    var overlay = EmissiveOverlay(part, mask);
                    if (overlay.TriangleCount == 0) continue;
                    overlay.MaterialIndex = part.MaterialIndex | EmissiveKey;
                    extra.Add(overlay);
                    if (li == 0) emissiveTris += overlay.TriangleCount;
                }
                lm.Parts.AddRange(extra);
            }
            if (emissiveTris > 0) log.Info(L.T($"Émissif : {emissiveTris:N0} triangle(s) en surimpression lumineuse", $"Emissive: {emissiveTris:N0} glowing overlay triangle(s)"));

            var shaderIndexByMaterial = new Dictionary<int, int>();
            foreach (var p in high.Parts)
            {
                if (shaderIndexByMaterial.ContainsKey(p.MaterialIndex)) continue;
                var srcMat = imp.Materials[p.MaterialIndex & ~EmissiveKey];
                var matOpt = opt.MaterialOptionsFor(srcMat.SourceIndex);
                var shader = (p.MaterialIndex & EmissiveKey) != 0 ? mapper.MapEmissive(srcMat, matOpt) : mapper.Map(srcMat, matOpt);
                shaderIndexByMaterial[p.MaterialIndex] = drawable.Shaders.Count;
                drawable.Shaders.Add(shader);
                log.Info(L.T($"Matériau '{shader.SourceMaterial}' -> {shader.FileName}", $"Material '{shader.SourceMaterial}' -> {shader.FileName}"));
            }

            // 5. Géométries par LOD --------------------------------------------------
            float radius = 0;
            var sphereCenter = (bmin + bmax) * 0.5f;
            foreach (var p in high.Parts)
                foreach (var v in p.Positions) radius = MathF.Max(radius, Vector3.Distance(v, sphereCenter));
            drawable.SphereCenter = sphereCenter;
            drawable.SphereRadius = radius;

            bool hasLods = lodMeshes.Skip(1).Any(l => l != null);
            float archetypeLodDist = opt.LodDistance > 0 ? opt.LodDistance : Math.Clamp(radius * 40f, 100f, 500f);
            float[] lodDists = hasLods
                ? new[] { Math.Clamp(radius * 8f, 15f, 100f), Math.Clamp(radius * 16f, 30f, 200f), Math.Clamp(radius * 30f, 60f, 350f), archetypeLodDist }
                : new[] { 9998f, 9998f, 9998f, 9998f };

            var result = new ConversionResult
            {
                PropName = name,
                ResourceDir = Path.Combine(opt.OutputDir, name),
                SourceTriangles = sourceTris,
                Decimated = decimated,
                Size = size,
                AppliedScale = scale,
                ArchetypeLodDist = archetypeLodDist,
            };

            string[] lodNames = { "High", "Medium", "Low", "VeryLow" };
            for (int li = 0; li < 4; li++)
            {
                var lm = lodMeshes[li];
                if (lm == null) continue;
                var lod = new GtaLod { Distance = lodDists[li] };
                int verts = 0;
                foreach (var p in lm.Parts)
                {
                    int si = shaderIndexByMaterial[p.MaterialIndex];
                    bool tangents = drawable.Shaders[si].NeedsTangents;
                    if (tangents && p.Tangents.Count != p.VertexCount) MeshOps.ComputeTangents(p);
                    foreach (var chunk in MeshOps.SplitByVertexLimit(p))
                    {
                        lod.Geometries.Add(new GtaGeometry { ShaderIndex = si, Mesh = chunk, WithTangents = tangents });
                        verts += chunk.VertexCount;
                    }
                }
                drawable.Lods[li] = lod;
                result.Lods.Add(new LodReport
                {
                    Level = lodNames[li],
                    Triangles = lm.TriangleCount,
                    Vertices = verts,
                    Geometries = lod.Geometries.Count,
                    Distance = lod.Distance,
                });
            }

            // 6. Compilation via CodeWalker -------------------------------------------
            var ytdXml = YtdXmlWriter.Write(textures.Textures);
            var ydrXml = YdrXmlWriter.Write(drawable);
            var ytypXml = YtypXmlWriter.Write(name, new ArchetypeInfo
            {
                Name = name,
                TextureDictionary = name,
                BbMin = bmin,
                BbMax = bmax,
                BsCentre = sphereCenter,
                BsRadius = radius,
                LodDist = archetypeLodDist,
                HdTextureDist = Math.Clamp(radius * 10f, 5f, 100f),
                Flags = opt.ArchetypeFlags,
                PhysicsDictionary = opt.Collision == CollisionMode.None ? null : name,
            });

            var ytd = XmlYtd.GetYtd(ytdXml, workDir);
            if (ytd.TextureDict == null) throw new InvalidOperationException(L.T("Échec de la création du YTD.", "Failed to build the YTD."));
            var ytdBytes = ytd.Save();

            var ydr = XmlYdr.GetYdr(ydrXml, workDir);
            if (ydr.Drawable == null) throw new InvalidOperationException(L.T("Échec de la création du YDR.", "Failed to build the YDR."));
            var ydrBytes = ydr.Save();

            var ytypDoc = new XmlDocument();
            ytypDoc.LoadXml(ytypXml);
            var ytypBytes = XmlMeta.GetData(ytypDoc, MetaFormat.RSC, workDir)
                ?? throw new InvalidOperationException(L.T("Échec de la création du YTYP.", "Failed to build the YTYP."));

            // 7. Vérification : relecture des binaires ----------------------------------
            Verify(ydrBytes, ytdBytes, ytypBytes, name, result, textures.Textures.Count, opt.Collision, opt.Lights.Count);

            // 8. Écriture de la ressource ----------------------------------------------
            var stream = Path.Combine(result.ResourceDir, "stream");
            Directory.CreateDirectory(stream);
            void Save(string file, byte[] data)
            {
                var path = Path.Combine(stream, file);
                File.WriteAllBytes(path, data);
                result.Files[Path.Combine("stream", file)] = data.LongLength;
            }
            Save(name + ".ydr", ydrBytes);
            Save(name + ".ytd", ytdBytes);
            Save(name + ".ytyp", ytypBytes);

            var manifest = BuildManifest(name);
            File.WriteAllText(Path.Combine(result.ResourceDir, "fxmanifest.lua"), manifest, new UTF8Encoding(false));
            result.Files["fxmanifest.lua"] = manifest.Length;

            if (opt.CodeWalkerPreview)
            {
                // Même modèle, textures intégrées : CodeWalker l'affiche sans avoir besoin du YTD ni du jeu modifié.
                var previewXml = YdrXmlWriter.Write(drawable, textures.Textures);
                var preview = XmlYdr.GetYdr(previewXml, workDir);
                if (preview.Drawable?.ShaderGroup?.TextureDictionary == null)
                    throw new InvalidOperationException(L.T("Échec de la création de l'aperçu CodeWalker.", "Failed to build the CodeWalker preview."));
                var previewDir = Path.Combine(opt.OutputDir, name + "_apercu_codewalker");
                Directory.CreateDirectory(previewDir);
                File.WriteAllBytes(Path.Combine(previewDir, name + ".ydr"), preview.Save());
                result.PreviewDir = previewDir;
                log.Info(L.T($"Aperçu CodeWalker : {previewDir}", $"CodeWalker preview: {previewDir}"));
            }

            if (opt.KeepXml)
            {
                var xmlDir = Path.Combine(opt.OutputDir, name + "_xml");
                Directory.CreateDirectory(xmlDir);
                File.WriteAllText(Path.Combine(xmlDir, name + ".ydr.xml"), ydrXml);
                File.WriteAllText(Path.Combine(xmlDir, name + ".ytd.xml"), ytdXml);
                File.WriteAllText(Path.Combine(xmlDir, name + ".ytyp.xml"), ytypXml);
                foreach (var t in textures.Textures)
                    File.Copy(Path.Combine(workDir, t.FileName), Path.Combine(xmlDir, t.FileName), true);
            }

            if (ytdBytes.Length > 16 * 1024 * 1024)
                log.Warn(L.T($"YTD de {ytdBytes.Length / 1048576.0:0.0} Mo : FiveM signale les assets > 16 Mo (oversized). Baisse --max-texture.", $"YTD is {ytdBytes.Length / 1048576.0:0.0} MB: FiveM flags assets > 16 MB (oversized). Lower the max texture size."));

            result.Textures.AddRange(textures.Textures);
            result.Shaders.AddRange(drawable.Shaders);
            return result;
        }
        finally
        {
            try { Directory.Delete(workDir, true); } catch { }
        }
    }

    /// <summary>Étapes géométriques communes à l'aperçu et à la conversion :
    /// import, axes, échelle, pivot, décimation, LOD et collision (sans textures ni fichiers).</summary>
    public static PreparedModel Prepare(ConvertOptions opt, ConversionLog log)
    {
        var name = SanitizeName(opt.Name ?? Path.GetFileNameWithoutExtension(opt.InputPath));
        log.Info(L.T($"Prop : {name}", $"Prop: {name}"));

        // 1. Import ----------------------------------------------------------------
        var imp = GlbImporter.Load(opt.InputPath, log, opt.ImageOverrides, opt.BaseColorOverrides);
        if (opt.ImageOverrides.Count > 0 || opt.BaseColorOverrides.Count > 0)
            log.Info(L.T($"Retouches appliquées : {opt.ImageOverrides.Count} image(s), {opt.BaseColorOverrides.Count} couleur(s)", $"Edits applied: {opt.ImageOverrides.Count} image(s), {opt.BaseColorOverrides.Count} color(s)"));
        var mesh = imp.Mesh;
        int sourceTris = mesh.TriangleCount;
        int sourceVerts = mesh.VertexCount;
        log.Info(L.T($"GLB lu : {sourceTris:N0} triangles, {mesh.VertexCount:N0} vertices, {imp.Materials.Count} matériau(x)", $"GLB read: {sourceTris:N0} triangles, {mesh.VertexCount:N0} vertices, {imp.Materials.Count} material(s)"));

        // 2. Axes, échelle, pivot ----------------------------------------------------
        var rot = MeshOps.GltfToGta * Matrix4x4.CreateRotationZ(opt.RotateZ * MathF.PI / 180f);
        MeshOps.Transform(mesh, rot);
        var (bmin, bmax) = mesh.GetBounds();
        float height = bmax.Z - bmin.Z;
        float scale = opt.Scale;
        if (opt.TargetHeight is > 0 && height > 1e-6f) scale = opt.TargetHeight.Value / height;
        bmin *= scale; bmax *= scale;
        var center = (bmin + bmax) * 0.5f;
        var pivot = opt.Pivot switch
        {
            PivotMode.BaseCenter => new Vector3(center.X, center.Y, bmin.Z),
            PivotMode.Center => center,
            _ => Vector3.Zero,
        };
        MeshOps.Transform(mesh, Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(-pivot));
        (bmin, bmax) = mesh.GetBounds();
        var size = bmax - bmin;
        log.Info(L.T($"Dimensions finales : {size.X:0.###} x {size.Y:0.###} x {size.Z:0.###} m (L x l x H), échelle x{scale:0.####}", $"Final size: {size.X:0.###} x {size.Y:0.###} x {size.Z:0.###} m (W x D x H), scale x{scale:0.####}"));
        if (size.Z > 50 || size.X > 50 || size.Y > 50)
            log.Warn(L.T("Le prop fait plus de 50 m : vérifie l'échelle (--height) ; le GLB est peut-être en centimètres.", "The prop is over 50 m tall: check the scale (--height); the GLB may be in centimetres."));

        // 3. Nettoyage + décimation ----------------------------------------------------
        foreach (var p in mesh.Parts)
        {
            if (!opt.KeepVertexColors) for (int i = 0; i < p.Colors.Count; i++) p.Colors[i] = Vector4.One;
            MeshOps.Weld(p);
        }

        var high = mesh;
        bool decimated = false;
        if (mesh.TriangleCount > opt.MaxTriangles)
        {
            log.Info(L.T($"Décimation : {mesh.TriangleCount:N0} -> {opt.MaxTriangles:N0} triangles max...", $"Decimating: {mesh.TriangleCount:N0} -> {opt.MaxTriangles:N0} triangles max..."));
            high = Simplifier.Decimate(mesh, opt.MaxTriangles, log);
            decimated = true;
            log.Info(L.T($"Résultat : {high.TriangleCount:N0} triangles", $"Result: {high.TriangleCount:N0} triangles"));
        }

        var lodMeshes = new SceneMesh?[4];
        lodMeshes[0] = high;
        if (opt.GenerateLods)
        {
            if (high.TriangleCount < 300)
                log.Info(L.T("Modèle trop léger pour des LOD utiles : seul le High est généré.", "Model too light for useful LODs: only High is generated."));
            else
            {
                float[] ratios = { 0.5f, 0.25f, 0.1f };
                for (int i = 0; i < 3; i++)
                {
                    int target = Math.Max(12, (int)(high.TriangleCount * ratios[i]));
                    lodMeshes[i + 1] = Simplifier.Decimate(high, target, log);
                }
            }
        }

        MeshPart? collisionMesh = null;
        if (opt.Collision == CollisionMode.Mesh)
        {
            collisionMesh = CollisionBuilder.Build(high, Math.Clamp(opt.CollisionMaxTriangles, 12, 30000), log);
            log.Info(L.T($"Collision : maillage de {collisionMesh.TriangleCount:N0} triangles", $"Collision: mesh of {collisionMesh.TriangleCount:N0} triangles"));
            if (collisionMesh.TriangleCount == 0)
                throw new InvalidOperationException(L.T("Impossible de construire le maillage de collision.", "Could not build the collision mesh."));
        }

        return new PreparedModel
        {
            Name = name,
            Import = imp,
            SourceTriangles = sourceTris,
            SourceVertices = sourceVerts,
            Decimated = decimated,
            Scale = scale,
            Min = bmin,
            Max = bmax,
            Lods = lodMeshes,
            CollisionMesh = collisionMesh,
        };
    }

    /// <summary>Bit ajouté à l'index de matériau pour les parties "décalque émissif".</summary>
    const int EmissiveKey = 1 << 20;

    /// <summary>Triangles dont la zone d'UV touche une partie émissive de la texture (copie pour le décalque).</summary>
    static MeshPart EmissiveOverlay(MeshPart p, (float[] Mask, int W, int H)? mask, float threshold = 0.02f)
    {
        var o = p.CloneEmpty();
        var map = new Dictionary<int, int>();
        int Map(int i)
        {
            if (map.TryGetValue(i, out var n)) return n;
            n = o.Positions.Count;
            map[i] = n;
            o.Positions.Add(p.Positions[i]);
            o.Normals.Add(p.Normals[i]);
            o.Uvs.Add(p.Uvs[i]);
            o.Colors.Add(p.Colors[i]);
            return n;
        }
        for (int t = 0; t < p.Indices.Count; t += 3)
        {
            int a = p.Indices[t], b = p.Indices[t + 1], c = p.Indices[t + 2];
            if (mask != null && !TriangleGlows(p.Uvs[a], p.Uvs[b], p.Uvs[c], mask.Value, threshold)) continue;
            o.Indices.Add(Map(a)); o.Indices.Add(Map(b)); o.Indices.Add(Map(c));
        }
        return o;
    }

    /// <summary>Teste les cases du masque couvertes par la boîte d'UV du triangle (textures répétées gérées).</summary>
    static bool TriangleGlows(Vector2 a, Vector2 b, Vector2 c, (float[] Mask, int W, int H) m, float threshold)
    {
        float minU = MathF.Min(a.X, MathF.Min(b.X, c.X)), maxU = MathF.Max(a.X, MathF.Max(b.X, c.X));
        float minV = MathF.Min(a.Y, MathF.Min(b.Y, c.Y)), maxV = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
        int x0 = (int)MathF.Floor(minU * m.W), x1 = (int)MathF.Floor(maxU * m.W);
        int y0 = (int)MathF.Floor(minV * m.H), y1 = (int)MathF.Floor(maxV * m.H);
        if (x1 - x0 >= m.W) { x0 = 0; x1 = m.W - 1; }
        if (y1 - y0 >= m.H) { y0 = 0; y1 = m.H - 1; }
        for (int y = y0; y <= y1; y++)
        {
            int yy = ((y % m.H) + m.H) % m.H;
            for (int x = x0; x <= x1; x++)
            {
                int xx = ((x % m.W) + m.W) % m.W;
                if (m.Mask[yy * m.W + xx] > threshold) return true;
            }
        }
        return false;
    }

    static void Verify(byte[] ydrBytes, byte[] ytdBytes, byte[] ytypBytes, string name, ConversionResult result, int textureCount, CollisionMode collision, int lightCount)
    {
        var ydr = new YdrFile();
        ydr.Load((byte[])ydrBytes.Clone());
        var d = ydr.Drawable ?? throw new InvalidDataException(L.T("Vérification : YDR illisible.", "Check: YDR unreadable."));
        var high = d.DrawableModels?.High;
        if (high == null || high.Length == 0) throw new InvalidDataException(L.T("Vérification : YDR sans modèle High.", "Check: YDR has no High model."));
        int tris = high.Sum(m => m.Geometries?.Sum(g => (int)g.IndicesCount / 3) ?? 0);
        if (tris != result.Lods[0].Triangles)
            throw new InvalidDataException(L.T($"Vérification : {tris} triangles relus au lieu de {result.Lods[0].Triangles}.", $"Check: read back {tris} triangles instead of {result.Lods[0].Triangles}."));

        if ((d.LightAttributes?.data_items?.Length ?? 0) != lightCount)
            throw new InvalidDataException(L.T($"Vérification : {d.LightAttributes?.data_items?.Length ?? 0} lumière(s) relue(s) au lieu de {lightCount}.", $"Check: read back {d.LightAttributes?.data_items?.Length ?? 0} light(s) instead of {lightCount}."));

        if (collision != CollisionMode.None)
        {
            if (d.Bound is not BoundComposite bc || bc.Children?.data_items?.Length != 1)
                throw new InvalidDataException(L.T("Vérification : collision absente du YDR.", "Check: collision missing from the YDR."));
            var child = bc.Children.data_items[0];
            if (collision == CollisionMode.Mesh && (child is not BoundBVH bvh || bvh.Polygons == null || bvh.Polygons.Length == 0))
                throw new InvalidDataException(L.T("Vérification : maillage de collision absent ou vide.", "Check: collision mesh missing or empty."));
        }

        var ytd = new YtdFile();
        ytd.Load((byte[])ytdBytes.Clone());
        if ((ytd.TextureDict?.Textures?.Count ?? 0) != textureCount)
            throw new InvalidDataException(L.T("Vérification : nombre de textures du YTD incorrect.", "Check: wrong texture count in the YTD."));

        var ytyp = new YtypFile();
        ytyp.Load((byte[])ytypBytes.Clone());
        var arch = ytyp.AllArchetypes?.FirstOrDefault();
        if (arch == null || arch.Hash != JenkHash.GenHash(name))
            throw new InvalidDataException(L.T("Vérification : archétype absent ou mal nommé dans le YTYP.", "Check: archetype missing or misnamed in the YTYP."));
        if (collision != CollisionMode.None && arch._BaseArchetypeDef.physicsDictionary != JenkHash.GenHash(name))
            throw new InvalidDataException(L.T("Vérification : physicsDictionary manquant dans le YTYP.", "Check: physicsDictionary missing in the YTYP."));
    }

    static string BuildManifest(string name) =>
        "fx_version 'cerulean'\n" +
        "game 'gta5'\n" +
        "\n" +
        $"author '{AppInfo.Author}'\n" +
        L.T($"description 'Prop {name} (généré par {AppInfo.FullName} - {AppInfo.GitHubUrl})'\n", $"description 'Prop {name} (generated by {AppInfo.FullName} - {AppInfo.GitHubUrl})'\n") +
        "\n" +
        "this_is_a_map 'yes'\n" +
        "\n" +
        $"data_file 'DLC_ITYP_REQUEST' 'stream/{name}.ytyp'\n";

    /// <summary>Nom GTA valide : minuscules, [a-z0-9_], commence par une lettre.</summary>
    public static string SanitizeName(string raw)
    {
        var s = raw.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '_');
        }
        var name = Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
        if (name.Length == 0) name = "prop";
        if (!char.IsLetter(name[0])) name = "prop_" + name;
        if (name.Length > 48) name = name[..48].TrimEnd('_');
        return name;
    }
}
