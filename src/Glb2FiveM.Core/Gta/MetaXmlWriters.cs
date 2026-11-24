using System.Numerics;
using System.Text;
using Glb2FiveM.Textures;
using static Glb2FiveM.Gta.XmlFmt;

namespace Glb2FiveM.Gta;

/// <summary>XML "TextureDictionary" de CodeWalker (compilé en .ytd, DDS lus dans le dossier de travail).</summary>
public static class YtdXmlWriter
{
    public static string Write(IEnumerable<GtaTexture> textures)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        Line(sb, 0, "<TextureDictionary>");
        WriteItems(sb, textures, 1);
        Line(sb, 0, "</TextureDictionary>");
        return sb.ToString();
    }

    /// <summary>Entrées de textures (réutilisées pour un dictionnaire intégré au YDR).</summary>
    public static void WriteItems(StringBuilder sb, IEnumerable<GtaTexture> textures, int indent)
    {
        foreach (var t in textures)
        {
            string usage = t.Role switch
            {
                TextureRole.Diffuse => "DIFFUSE",
                TextureRole.Normal => "NORMAL",
                _ => "SPECULAR",
            };
            Line(sb, indent, "<Item>");
            Line(sb, indent + 1, $"<Name>{Esc(t.Name)}</Name>");
            Line(sb, indent + 1, "<Unk32 value=\"0\" />");
            Line(sb, indent + 1, $"<Usage>{usage}</Usage>");
            Line(sb, indent + 1, "<ExtraFlags value=\"0\" />");
            Line(sb, indent + 1, $"<Width value=\"{t.Width}\" />");
            Line(sb, indent + 1, $"<Height value=\"{t.Height}\" />");
            Line(sb, indent + 1, $"<MipLevels value=\"{t.MipLevels}\" />");
            Line(sb, indent + 1, $"<Format>{(t.Format == DxtFormat.Dxt1 ? "D3DFMT_DXT1" : "D3DFMT_DXT5")}</Format>");
            Line(sb, indent + 1, $"<FileName>{Esc(t.FileName)}</FileName>");
            Line(sb, indent, "</Item>");
        }
    }
}

public sealed class ArchetypeInfo
{
    public required string Name { get; init; }
    public required string TextureDictionary { get; init; }
    public Vector3 BbMin { get; init; }
    public Vector3 BbMax { get; init; }
    public Vector3 BsCentre { get; init; }
    public float BsRadius { get; init; }
    public float LodDist { get; init; }
    public float HdTextureDist { get; init; }
    public uint Flags { get; init; }
    /// <summary>Nom du drawable qui porte la collision intégrée (vide = pas de collision).</summary>
    public string? PhysicsDictionary { get; init; }
}

/// <summary>XML "CMapTypes" de CodeWalker (compilé en .ytyp) : déclare l'archétype du prop.</summary>
public static class YtypXmlWriter
{
    public static string Write(string ytypName, ArchetypeInfo a)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        Line(sb, 0, "<CMapTypes>");
        Line(sb, 1, "<extensions />");
        Line(sb, 1, "<archetypes>");
        Line(sb, 2, "<Item type=\"CBaseArchetypeDef\">");
        Line(sb, 3, $"<lodDist value=\"{F(a.LodDist)}\" />");
        Line(sb, 3, $"<flags value=\"{a.Flags}\" />");
        Line(sb, 3, "<specialAttribute value=\"0\" />");
        Line(sb, 3, $"<bbMin {V3(a.BbMin)} />");
        Line(sb, 3, $"<bbMax {V3(a.BbMax)} />");
        Line(sb, 3, $"<bsCentre {V3(a.BsCentre)} />");
        Line(sb, 3, $"<bsRadius value=\"{F(a.BsRadius)}\" />");
        Line(sb, 3, $"<hdTextureDist value=\"{F(a.HdTextureDist)}\" />");
        Line(sb, 3, $"<name>{Esc(a.Name)}</name>");
        Line(sb, 3, $"<textureDictionary>{Esc(a.TextureDictionary)}</textureDictionary>");
        Line(sb, 3, "<clipDictionary />");
        Line(sb, 3, "<drawableDictionary />");
        // Indispensable : sans ça, le jeu ignore la collision intégrée au YDR.
        Line(sb, 3, string.IsNullOrEmpty(a.PhysicsDictionary) ? "<physicsDictionary />" : $"<physicsDictionary>{Esc(a.PhysicsDictionary)}</physicsDictionary>");
        Line(sb, 3, "<assetType>ASSET_TYPE_DRAWABLE</assetType>");
        Line(sb, 3, $"<assetName>{Esc(a.Name)}</assetName>");
        Line(sb, 3, "<extensions />");
        Line(sb, 2, "</Item>");
        Line(sb, 1, "</archetypes>");
        Line(sb, 1, $"<name>{Esc(ytypName)}</name>");
        Line(sb, 1, "<dependencies />");
        Line(sb, 1, "<compositeEntityTypes />");
        Line(sb, 0, "</CMapTypes>");
        return sb.ToString();
    }
}
