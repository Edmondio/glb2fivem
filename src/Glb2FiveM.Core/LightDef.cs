using System.Numerics;

namespace Glb2FiveM;

public enum GtaLightType { Point = 1, Spot = 2, Capsule = 4 }

/// <summary>Lumière intégrée au YDR (coordonnées GTA finales du prop, en mètres).</summary>
public sealed class LightDef
{
    public GtaLightType Type { get; set; } = GtaLightType.Point;
    public Vector3 Position { get; set; }
    /// <summary>Longueur du tube (type Capsule, néon), en mètres.</summary>
    public float Length { get; set; } = 1f;
    /// <summary>Direction d'éclairage (spot) ou axe du tube (capsule). Ignorée pour une lumière ponctuelle.</summary>
    public Vector3 Direction { get; set; } = -Vector3.UnitZ;
    public byte R { get; set; } = 255;
    public byte G { get; set; } = 220;
    public byte B { get; set; } = 170;
    public float Intensity { get; set; } = 5f;
    /// <summary>Portée en mètres.</summary>
    public float Falloff { get; set; } = 5f;
    public float FalloffExponent { get; set; } = 8f;
    /// <summary>Angle du cône (demi-angle extérieur, degrés) pour un spot.</summary>
    public float ConeAngle { get; set; } = 45f;
    public float CoronaSize { get; set; } = 0.3f;
    public float CoronaIntensity { get; set; } = 1f;
    /// <summary>Heures d'allumage, 1 bit par heure (bit 0 = 0h-1h). 0xFFFFFF = toujours.</summary>
    public uint TimeFlags { get; set; } = 0xFFFFFF;
    /// <summary>Effet (0 = constant, 15 = bougie, 17 = feu, 19 = électrique...).</summary>
    public byte Flashiness { get; set; }
    public bool CastShadows { get; set; }
    public float VolumeIntensity { get; set; }

    public const uint TimeAlways = 0xFFFFFF;
    /// <summary>20h -> 6h, comme les lampadaires de R*.</summary>
    public const uint TimeNight = 0xF0003F;
}
