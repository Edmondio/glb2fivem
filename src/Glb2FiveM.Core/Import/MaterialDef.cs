using System.Numerics;

namespace Glb2FiveM.Import;

public enum AlphaKind { Opaque, Mask, Blend }

/// <summary>Image source (encodée PNG/JPEG...) avec une clé stable pour dédoublonner.</summary>
public sealed record SourceImage(string Key, byte[] Bytes, string? MimeType);

/// <summary>Matériau PBR simplifié lu depuis le glTF.</summary>
public sealed class MaterialDef
{
    public string Name = "default";
    /// <summary>Index du matériau dans le glTF (-1 = matériau par défaut).</summary>
    public int SourceIndex = -1;
    /// <summary>Jeu d'UV utilisé par la couleur de base (les autres textures doivent utiliser le même).</summary>
    public int BaseUvSet;
    /// <summary>Facteur de couleur de base, en linéaire (glTF).</summary>
    public Vector4 BaseColorFactor = Vector4.One;
    public SourceImage? BaseColorImage;
    public SourceImage? NormalImage;
    public float NormalScale = 1f;
    /// <summary>Texture metallic (B) / roughness (G) du glTF.</summary>
    public SourceImage? MetallicRoughnessImage;
    public float Metallic = 1f;
    public float Roughness = 1f;
    public SourceImage? EmissiveImage;
    public Vector3 EmissiveFactor = Vector3.Zero;
    /// <summary>KHR_materials_emissive_strength.</summary>
    public float EmissiveStrength = 1f;
    public int EmissiveUvSet;
    /// <summary>Occlusion ambiante (canal R), à intégrer dans la couleur.</summary>
    public SourceImage? OcclusionImage;
    public float OcclusionStrength = 1f;
    public int OcclusionUvSet;

    public bool HasEmissive => EmissiveImage != null || EmissiveFactor.X + EmissiveFactor.Y + EmissiveFactor.Z > 1e-4f;
    public AlphaKind Alpha = AlphaKind.Opaque;
    public float AlphaCutoff = 0.5f;
    public bool DoubleSided;
}
