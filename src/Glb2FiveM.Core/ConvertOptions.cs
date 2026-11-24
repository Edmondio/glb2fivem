namespace Glb2FiveM;

public enum PivotMode
{
    /// <summary>Centre de la base : le prop se pose au sol (recommandé).</summary>
    BaseCenter,
    /// <summary>Centre de la boîte englobante.</summary>
    Center,
    /// <summary>Origine d'origine du GLB, sans déplacement.</summary>
    Origin,
}

public enum CollisionMode
{
    None,
    /// <summary>Une boîte qui englobe le modèle.</summary>
    Box,
    /// <summary>Maillage de collision simplifié qui suit la forme du modèle.</summary>
    Mesh,
}

public enum EmissiveMode { Off, Always, Night }

/// <summary>Réglages par matériau (index de matériau glTF), venant de l'interface.</summary>
public sealed class MaterialOptions
{
    public EmissiveMode Emissive { get; set; } = EmissiveMode.Always;
    /// <summary>emissiveMultiplier du shader GTA (R* : 5 de jour, 2,5 la nuit).</summary>
    public float EmissiveIntensity { get; set; } = 5f;
    /// <summary>Intègre l'occlusion ambiante dans la texture de couleur.</summary>
    public bool BakeOcclusion { get; set; } = true;
    /// <summary>Force de l'occlusion (0 = aucune, 1 = celle du GLB).</summary>
    public float OcclusionStrength { get; set; } = 1f;
}

public sealed class ConvertOptions
{
    public required string InputPath { get; init; }
    public required string OutputDir { get; init; }

    /// <summary>Nom du prop (modèle, YDR, YTD, YTYP). Généré depuis le fichier si vide.</summary>
    public string? Name { get; set; }

    /// <summary>Plafond de triangles pour le LOD High. Au-delà, le modèle est décimé.</summary>
    public int MaxTriangles { get; set; } = 150_000;

    /// <summary>Hauteur finale voulue en mètres (prioritaire sur Scale).</summary>
    public float? TargetHeight { get; set; }

    /// <summary>Facteur d'échelle appliqué si TargetHeight n'est pas défini.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>Rotation additionnelle autour de Z (degrés), appliquée après conversion d'axes.</summary>
    public float RotateZ { get; set; }

    public PivotMode Pivot { get; set; } = PivotMode.BaseCenter;

    /// <summary>Taille max (px) d'une texture. Les textures sont ramenées en puissance de 2.</summary>
    public int MaxTextureSize { get; set; } = 2048;

    /// <summary>Génère les LOD Medium/Low/VeryLow par décimation.</summary>
    public bool GenerateLods { get; set; }

    public CollisionMode Collision { get; set; } = CollisionMode.Box;

    /// <summary>Nombre max de triangles du maillage de collision (mode Mesh).</summary>
    public int CollisionMaxTriangles { get; set; } = 2000;

    /// <summary>Les normal maps glTF sont au format OpenGL (Y+) ; GTA attend du DirectX (Y-).</summary>
    public bool FlipNormalGreen { get; set; } = true;

    /// <summary>Garde les couleurs de vertex du GLB (sinon blanc, valeur standard GTA).</summary>
    public bool KeepVertexColors { get; set; }

    /// <summary>Distance d'affichage du prop (lodDist du YTYP), en mètres. 0 = automatique.</summary>
    public float LodDistance { get; set; }

    /// <summary>Flags de l'archétype YTYP (32 = Static).</summary>
    public uint ArchetypeFlags { get; set; } = 32;

    /// <summary>Réglages par matériau (émissif, occlusion). Absents = valeurs par défaut.</summary>
    public Dictionary<int, MaterialOptions> Materials { get; set; } = new();

    /// <summary>Réglages appliqués aux matériaux sans entrée dans <see cref="Materials"/>.</summary>
    public MaterialOptions DefaultMaterial { get; set; } = new();

    public MaterialOptions MaterialOptionsFor(int sourceIndex) =>
        Materials.TryGetValue(sourceIndex, out var o) ? o : DefaultMaterial;

    /// <summary>Lumières à intégrer au YDR.</summary>
    public List<LightDef> Lights { get; set; } = new();

    /// <summary>Images remplacées ou retouchées (index d'image glTF -> PNG/JPEG).</summary>
    public Dictionary<int, byte[]> ImageOverrides { get; set; } = new();

    /// <summary>Couleur de base forcée par matériau (index de matériau glTF -> RGBA linéaire).</summary>
    public Dictionary<int, System.Numerics.Vector4> BaseColorOverrides { get; set; } = new();

    /// <summary>Crée aussi un .ydr d'aperçu avec les textures intégrées, lisible directement par CodeWalker.</summary>
    public bool CodeWalkerPreview { get; set; }

    /// <summary>Garde les XML CodeWalker intermédiaires (debug / édition dans CodeWalker).</summary>
    public bool KeepXml { get; set; }
}
