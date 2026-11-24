using System.Numerics;
using Glb2FiveM.Import;
using Glb2FiveM.Textures;

namespace Glb2FiveM.Gta;

public enum TextureRole { Diffuse, Normal, Specular, Emissive }

public sealed record GtaTexture(string Name, string FileName, int Width, int Height, int MipLevels,
    DxtFormat Format, TextureRole Role, long ByteSize);

/// <summary>Produit les textures DDS du prop (dans un dossier de travail) et évite les doublons.</summary>
public sealed class TextureBank
{
    readonly string _workDir;
    readonly string _prefix;
    readonly ConvertOptions _opt;
    readonly ConversionLog _log;
    readonly Dictionary<string, GtaTexture> _cache = new();
    readonly Dictionary<string, ImageRgba?> _decoded = new();
    readonly Dictionary<TextureRole, int> _counters = new();

    public List<GtaTexture> Textures { get; } = new();

    public TextureBank(string workDir, string prefix, ConvertOptions opt, ConversionLog log)
    {
        _workDir = workDir;
        _prefix = prefix;
        _opt = opt;
        _log = log;
    }

    /// <summary>L'occlusion peut-elle être intégrée (même jeu d'UV que la couleur) ?</summary>
    public static bool CanBakeOcclusion(MaterialDef m, MaterialOptions o) =>
        o.BakeOcclusion && o.OcclusionStrength > 0 && m.OcclusionImage != null && m.OcclusionUvSet == m.BaseUvSet;

    public GtaTexture GetDiffuse(MaterialDef m, MaterialOptions o)
    {
        var f = m.BaseColorFactor;
        bool ao = CanBakeOcclusion(m, o);
        float aoStrength = Math.Clamp(o.OcclusionStrength * m.OcclusionStrength, 0f, 1f);
        string key = $"d|{m.BaseColorImage?.Key}|{f}|{m.Alpha}|{m.AlphaCutoff}|{(ao ? m.OcclusionImage!.Key + ":" + aoStrength : "")}";
        return GetOrCreate(key, TextureRole.Diffuse, () =>
        {
            var src = DecodeOrNull(m.BaseColorImage);
            ImageRgba img;
            if (src == null)
            {
                img = ImageRgba.Solid(4, 4, ImageOps.LinearToSrgb(f.X), ImageOps.LinearToSrgb(f.Y),
                    ImageOps.LinearToSrgb(f.Z), (byte)Math.Clamp((int)MathF.Round(f.W * 255), 0, 255));
            }
            else
            {
                img = new ImageRgba(src.Width, src.Height, (byte[])src.Data.Clone());
                if (f != Vector4.One) MultiplyColor(img, f);
            }

            if (ao && DecodeOrNull(m.OcclusionImage) is { } occ)
            {
                // Couleur unie + occlusion : la texture prend la taille de l'occlusion.
                if (src == null) img = ImageOps.Resize(img, occ.Width, occ.Height);
                BakeOcclusion(img, ImageOps.Resize(occ, img.Width, img.Height), aoStrength);
            }

            if (m.Alpha == AlphaKind.Opaque)
            {
                for (int i = 3; i < img.Data.Length; i += 4) img.Data[i] = 255;
            }
            else if (m.Alpha == AlphaKind.Mask)
            {
                byte cut = (byte)Math.Clamp((int)(m.AlphaCutoff * 255), 0, 255);
                for (int i = 3; i < img.Data.Length; i += 4) img.Data[i] = img.Data[i] >= cut ? (byte)255 : (byte)0;
            }
            var fmt = m.Alpha == AlphaKind.Opaque ? DxtFormat.Dxt1 : DxtFormat.Dxt5;
            return (img, fmt, false);
        });
    }

    public GtaTexture GetNormal(MaterialDef m)
    {
        string key = $"n|{m.NormalImage?.Key}|{_opt.FlipNormalGreen}";
        return GetOrCreate(key, TextureRole.Normal, () =>
        {
            var src = DecodeOrNull(m.NormalImage) ?? ImageRgba.Solid(4, 4, 128, 128, 255, 255);
            var img = new ImageRgba(src.Width, src.Height, (byte[])src.Data.Clone());
            for (int i = 0; i < img.Data.Length; i += 4)
            {
                if (_opt.FlipNormalGreen) img.Data[i + 1] = (byte)(255 - img.Data[i + 1]);
                img.Data[i + 3] = 255;
            }
            return (img, DxtFormat.Dxt1, true);
        });
    }

    /// <summary>Map spéculaire GTA depuis le metallic/roughness glTF :
    /// RGB = intensité, A = brillance (1 - roughness).</summary>
    public GtaTexture GetSpecular(MaterialDef m)
    {
        string key = $"s|{m.MetallicRoughnessImage?.Key}|{m.Metallic}|{m.Roughness}";
        return GetOrCreate(key, TextureRole.Specular, () =>
        {
            var src = DecodeOrNull(m.MetallicRoughnessImage);
            int w = src?.Width ?? 4, h = src?.Height ?? 4;
            var img = new ImageRgba(w, h);
            for (int i = 0; i < img.Data.Length; i += 4)
            {
                float rough = m.Roughness * (src != null ? src.Data[i + 1] / 255f : 1f);
                float metal = m.Metallic * (src != null ? src.Data[i + 2] / 255f : 1f);
                float gloss = 1f - Math.Clamp(rough, 0f, 1f);
                float intensity = gloss * (0.25f + 0.75f * Math.Clamp(metal, 0f, 1f));
                byte iv = (byte)Math.Clamp((int)MathF.Round(intensity * 255), 0, 255);
                img.Data[i] = iv; img.Data[i + 1] = iv; img.Data[i + 2] = iv;
                img.Data[i + 3] = (byte)Math.Clamp((int)MathF.Round(gloss * 255), 0, 255);
            }
            return (img, DxtFormat.Dxt5, false);
        });
    }

    /// <summary>Texture du décalque émissif : couleur de la lueur en RGB, masque (où ça brille) en alpha.</summary>
    public GtaTexture GetEmissive(MaterialDef m)
    {
        var f = m.EmissiveFactor * m.EmissiveStrength;
        string key = $"e|{m.EmissiveImage?.Key}|{f}";
        return GetOrCreate(key, TextureRole.Emissive, () =>
        {
            var src = DecodeOrNull(m.EmissiveImage);
            var img = src != null ? new ImageRgba(src.Width, src.Height, (byte[])src.Data.Clone()) : ImageRgba.Solid(4, 4, 255, 255, 255, 255);
            var d = img.Data;
            for (int i = 0; i < d.Length; i += 4)
            {
                float r = ImageOps.SrgbToLinear(d[i]) * f.X, g = ImageOps.SrgbToLinear(d[i + 1]) * f.Y, b = ImageOps.SrgbToLinear(d[i + 2]) * f.Z;
                float max = MathF.Max(r, MathF.Max(g, b));
                // La teinte est normalisée (la puissance vient de emissiveMultiplier), le masque garde l'intensité.
                float k = max > 1e-5f ? 1f / max : 0f;
                d[i] = ImageOps.LinearToSrgb(r * k);
                d[i + 1] = ImageOps.LinearToSrgb(g * k);
                d[i + 2] = ImageOps.LinearToSrgb(b * k);
                d[i + 3] = ImageOps.LinearToSrgb(MathF.Min(1f, max));
            }
            return (img, DxtFormat.Dxt5, false);
        });
    }

    /// <summary>Masque basse résolution (max par case) des zones émissives, pour choisir les triangles à dupliquer.
    /// Null = tout le matériau brille (pas de texture émissive).</summary>
    public (float[] Mask, int W, int H)? GetEmissiveMask(MaterialDef m, int size = 128)
    {
        var src = DecodeOrNull(m.EmissiveImage);
        if (src == null) return null;
        var f = m.EmissiveFactor * m.EmissiveStrength;
        int w = Math.Min(size, src.Width), h = Math.Min(size, src.Height);
        var mask = new float[w * h];
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                int o = (y * src.Width + x) * 4;
                float v = MathF.Max(ImageOps.SrgbToLinear(src.Data[o]) * f.X, MathF.Max(ImageOps.SrgbToLinear(src.Data[o + 1]) * f.Y, ImageOps.SrgbToLinear(src.Data[o + 2]) * f.Z));
                int mi = (y * h / src.Height) * w + (x * w / src.Width);
                if (v > mask[mi]) mask[mi] = v;
            }
        return (mask, w, h);
    }

    GtaTexture GetOrCreate(string key, TextureRole role, Func<(ImageRgba img, DxtFormat fmt, bool isNormal)> make)
    {
        if (_cache.TryGetValue(key, out var existing)) return existing;
        var (img, fmt, isNormal) = make();

        int w = ImageOps.NearestPow2(img.Width, _opt.MaxTextureSize);
        int h = ImageOps.NearestPow2(img.Height, _opt.MaxTextureSize);
        if (w != img.Width || h != img.Height)
        {
            if (img.Width > 4 || img.Height > 4)
                _log.Info(L.T($"Texture {role} redimensionnée {img.Width}x{img.Height} -> {w}x{h}", $"Texture {role} resized {img.Width}x{img.Height} -> {w}x{h}"));
            img = ImageOps.Resize(img, w, h);
        }
        var mips = ImageOps.BuildMips(img, isNormal);
        var dds = DdsWriter.Write(mips, fmt);

        _counters.TryGetValue(role, out var n);
        _counters[role] = n + 1;
        string suffix = role switch { TextureRole.Diffuse => "d", TextureRole.Normal => "n", TextureRole.Emissive => "e", _ => "s" };
        string name = $"{_prefix}_{suffix}{(n == 0 ? "" : n.ToString())}";
        var file = name + ".dds";
        File.WriteAllBytes(Path.Combine(_workDir, file), dds);

        var tex = new GtaTexture(name, file, w, h, mips.Count, fmt, role, dds.Length - 128);
        _cache[key] = tex;
        Textures.Add(tex);
        return tex;
    }

    ImageRgba? DecodeOrNull(SourceImage? src)
    {
        if (src == null) return null;
        if (_decoded.TryGetValue(src.Key, out var cached)) return cached;
        ImageRgba? img = null;
        try { img = ImageRgba.Decode(src.Bytes); }
        catch (Exception ex)
        {
            _log.Warn(L.T($"Impossible de décoder la texture {src.Key} ({src.MimeType ?? "format inconnu"}) : {ex.Message}. Formats pris en charge : PNG, JPEG, TGA, BMP.",
                      $"Could not decode texture {src.Key} ({src.MimeType ?? "unknown format"}): {ex.Message}. Supported formats: PNG, JPEG, TGA, BMP."));
        }
        _decoded[src.Key] = img;
        return img;
    }

    /// <summary>Multiplie la couleur par l'occlusion (canal R), en linéaire.</summary>
    static void BakeOcclusion(ImageRgba img, ImageRgba occ, float strength)
    {
        var d = img.Data;
        var a = occ.Data;
        for (int i = 0; i < d.Length; i += 4)
        {
            // L'occlusion glTF est une donnée linéaire : on lit l'octet tel quel.
            float k = 1f - strength + strength * (a[i] / 255f);
            d[i] = ImageOps.LinearToSrgb(ImageOps.SrgbToLinear(d[i]) * k);
            d[i + 1] = ImageOps.LinearToSrgb(ImageOps.SrgbToLinear(d[i + 1]) * k);
            d[i + 2] = ImageOps.LinearToSrgb(ImageOps.SrgbToLinear(d[i + 2]) * k);
        }
    }

    static void MultiplyColor(ImageRgba img, Vector4 f)
    {
        var d = img.Data;
        for (int i = 0; i < d.Length; i += 4)
        {
            d[i] = ImageOps.LinearToSrgb(ImageOps.SrgbToLinear(d[i]) * f.X);
            d[i + 1] = ImageOps.LinearToSrgb(ImageOps.SrgbToLinear(d[i + 1]) * f.Y);
            d[i + 2] = ImageOps.LinearToSrgb(ImageOps.SrgbToLinear(d[i + 2]) * f.Z);
            d[i + 3] = (byte)Math.Clamp((int)MathF.Round(d[i + 3] * f.W), 0, 255);
        }
    }
}
