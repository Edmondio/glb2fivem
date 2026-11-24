using System.Xml;

namespace Glb2FiveM.Gta;

public enum ShaderParamKind { Texture, Vector, Array }

public sealed record ShaderParamDef(string Name, ShaderParamKind Kind, int ArrayCount);

/// <summary>Liste des paramètres "legacy" (format PC classique utilisé par FiveM) de chaque shader GTA,
/// lue depuis ShadersGen9Conversion.xml fourni par CodeWalker.</summary>
public sealed class ShaderCatalog
{
    readonly Dictionary<string, List<ShaderParamDef>> _shaders = new(StringComparer.OrdinalIgnoreCase);

    public static ShaderCatalog Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ShadersGen9Conversion.xml");
        if (!File.Exists(path))
            throw new FileNotFoundException(L.T("ShadersGen9Conversion.xml introuvable à côté de l'exécutable.", "ShadersGen9Conversion.xml not found next to the executable."), path);
        var cat = new ShaderCatalog();
        var doc = new XmlDocument();
        doc.Load(path);
        foreach (XmlNode item in doc.DocumentElement!.SelectNodes("Item")!)
        {
            var name = item.SelectSingleNode("Name")?.InnerText;
            if (string.IsNullOrEmpty(name) || cat._shaders.ContainsKey(name)) continue;
            var list = new List<ShaderParamDef>();
            foreach (XmlNode p in item.SelectNodes("Parameters/Item")!)
            {
                var type = p.Attributes?["type"]?.Value;
                var old = p.Attributes?["old"]?.Value;
                if (string.IsNullOrEmpty(old)) continue; // paramètre propre au format Gen9 (Enhanced)
                if (type == "Texture") list.Add(new ShaderParamDef(old, ShaderParamKind.Texture, 0));
                else if (type == "CBuffer")
                {
                    int len = int.TryParse(p.Attributes?["length"]?.Value, out var l) ? l : 16;
                    if (len > 16) list.Add(new ShaderParamDef(old, ShaderParamKind.Array, (len + 15) / 16));
                    else list.Add(new ShaderParamDef(old, ShaderParamKind.Vector, 0));
                }
            }
            cat._shaders[name] = list;
        }
        return cat;
    }

    public IReadOnlyList<ShaderParamDef> GetParameters(string shaderName) =>
        _shaders.TryGetValue(shaderName, out var l) ? l : throw new KeyNotFoundException(L.T($"Shader GTA inconnu : {shaderName}", $"Unknown GTA shader: {shaderName}"));
}
