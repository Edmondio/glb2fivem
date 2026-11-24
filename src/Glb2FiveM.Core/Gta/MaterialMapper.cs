using System.Numerics;
using Glb2FiveM.Import;

namespace Glb2FiveM.Gta;

public sealed class GtaShaderParam
{
    public required string Name { get; init; }
    public ShaderParamKind Kind { get; init; }
    public string? TextureName { get; set; }
    public Vector4 Value { get; set; }
    public Vector4[]? Values { get; set; }
}

public sealed class GtaShader
{
    public required string Name { get; init; }        // ex. normal_spec
    public required string FileName { get; init; }    // ex. normal_spec_alpha.sps
    public byte RenderBucket { get; init; }
    public bool NeedsTangents { get; init; }
    public List<GtaShaderParam> Parameters { get; } = new();
    public string SourceMaterial { get; init; } = "";
}

/// <summary>Transforme un matériau PBR glTF en shader GTA V.
/// - Normal map présente  -> normal_spec (+ map spéculaire générée depuis metallic/roughness)
/// - Metallic/roughness   -> spec
/// - Sinon                -> default
/// Alpha : BLEND -> variante _alpha (bucket 1), MASK -> variante _cutout (bucket 3).</summary>
public sealed class MaterialMapper
{
    readonly ShaderCatalog _catalog;
    readonly TextureBank _textures;
    readonly ConversionLog _log;

    public MaterialMapper(ShaderCatalog catalog, TextureBank textures, ConversionLog log)
    {
        _catalog = catalog;
        _textures = textures;
        _log = log;
    }

    public GtaShader Map(MaterialDef m, MaterialOptions o)
    {
        bool hasNormal = m.NormalImage != null;
        bool hasSpec = m.MetallicRoughnessImage != null;
        string baseName = hasNormal ? "normal_spec" : hasSpec ? "spec" : "default";

        string preset;
        byte bucket;
        switch (m.Alpha)
        {
            case AlphaKind.Blend:
                preset = baseName == "default" ? "alpha" : baseName + "_alpha";
                bucket = 1;
                break;
            case AlphaKind.Mask:
                // spec n'a pas de variante cutout : on prend spec_alpha en alpha "dur".
                preset = baseName switch { "default" => "cutout", "spec" => "spec_alpha", _ => baseName + "_cutout" };
                bucket = baseName == "spec" ? (byte)1 : (byte)3;
                break;
            default:
                preset = baseName;
                bucket = 0;
                break;
        }


        var shader = new GtaShader
        {
            Name = baseName,
            FileName = preset + ".sps",
            RenderBucket = bucket,
            NeedsTangents = hasNormal,
            SourceMaterial = m.Name,
        };

        if (m.OcclusionImage != null && o.BakeOcclusion && m.OcclusionUvSet != m.BaseUvSet)
            _log.Warn(L.T($"Matériau '{m.Name}' : l'occlusion utilise un autre jeu d'UV que la couleur, elle n'est pas intégrée.", $"Material '{m.Name}': the occlusion map uses another UV set than the color, it is not baked."));
        var diffuse = _textures.GetDiffuse(m, o);
        var normal = hasNormal ? _textures.GetNormal(m) : null;
        var spec = (hasNormal || hasSpec) ? _textures.GetSpecular(m) : null;

        foreach (var def in _catalog.GetParameters(baseName))
        {
            var p = new GtaShaderParam { Name = def.Name, Kind = def.Kind };
            switch (def.Kind)
            {
                case ShaderParamKind.Texture:
                    p.TextureName = def.Name.ToLowerInvariant() switch
                    {
                        "diffusesampler" => diffuse.Name,
                        "bumpsampler" => normal?.Name,
                        "specsampler" => spec?.Name,
                        _ => null,
                    };
                    break;
                case ShaderParamKind.Vector:
                    p.Value = DefaultValue(def.Name, m, baseName, o);
                    break;
                case ShaderParamKind.Array:
                    p.Values = Enumerable.Repeat(Vector4.Zero, def.ArrayCount).ToArray();
                    break;
            }
            shader.Parameters.Add(p);
        }
        return shader;
    }

    /// <summary>Décalque émissif : copie des triangles qui brillent, superposée au modèle (shader de R* prévu pour ça).</summary>
    public GtaShader MapEmissive(MaterialDef m, MaterialOptions o)
    {
        string name = o.Emissive == EmissiveMode.Night ? "decal_emissivenight_only" : "decal_emissive_only";
        var shader = new GtaShader
        {
            Name = name,
            FileName = name + ".sps",
            RenderBucket = 2, // décalque
            NeedsTangents = false,
            SourceMaterial = m.Name + L.T(" (émissif)", " (emissive)"),
        };
        var tex = _textures.GetEmissive(m);
        foreach (var def in _catalog.GetParameters(name))
        {
            var p = new GtaShaderParam { Name = def.Name, Kind = def.Kind };
            if (def.Kind == ShaderParamKind.Texture) p.TextureName = def.Name.Equals("DiffuseSampler", StringComparison.OrdinalIgnoreCase) ? tex.Name : null;
            else if (def.Kind == ShaderParamKind.Vector) p.Value = DefaultValue(def.Name, m, name, o);
            else p.Values = Enumerable.Repeat(Vector4.Zero, def.ArrayCount).ToArray();
            shader.Parameters.Add(p);
        }
        return shader;
    }

    /// <summary>Valeurs par défaut alignées sur celles de R* (définitions de shaders de Sollumz/szio).</summary>
    static Vector4 DefaultValue(string name, MaterialDef m, string shader, MaterialOptions o) => name.ToLowerInvariant() switch
    {
        "matmaterialcolorscale" => new Vector4(1, 0, 0, 1),
        "hardalphablend" => new Vector4(m.Alpha == AlphaKind.Blend ? 0 : 1, 0, 0, 0),
        "usetessellation" => Vector4.Zero,
        "wetnessmultiplier" => new Vector4(1, 0, 0, 0),
        "globalanimuv0" => new Vector4(1, 0, 0, 0),
        "globalanimuv1" => new Vector4(0, 1, 0, 0),
        "specularfresnel" => new Vector4(shader == "normal_spec" ? 0.75f : 0.97f, 0, 0, 0),
        "specularfalloffmult" => new Vector4(100f, 0, 0, 0),
        "specularintensitymult" => new Vector4(1f, 0, 0, 0),
        "specmapintmask" => new Vector4(1, 0, 0, 0),
        "bumpiness" => new Vector4(Math.Clamp(m.NormalScale, 0f, 4f), 0, 0, 0),
        "emissivemultiplier" => new Vector4(Math.Clamp(o.EmissiveIntensity, 0f, 16f), 0, 0, 0),
        _ => Vector4.Zero,
    };
}
