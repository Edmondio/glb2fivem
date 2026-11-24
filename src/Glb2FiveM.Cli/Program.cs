using System.Globalization;
using Glb2FiveM;

Console.OutputEncoding = System.Text.Encoding.UTF8; // accents corrects dans la console Windows
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

const string Usage = """
glb2fivem by Edmondio - convertit un modèle .glb / .gltf en prop FiveM (YDR + YTD + YTYP + fxmanifest)
https://github.com/Edmondio/glb2fivem

Usage :
  glb2fivem <fichier.glb> [options]

Options :
  -o, --out <dossier>        Dossier de sortie (défaut : ./output)
  -n, --name <nom>           Nom du prop (défaut : nom du fichier)
  --max-tris <n>             Plafond de triangles, décimation au-delà (défaut : 150000)
  --height <m>               Hauteur finale en mètres (remplace --scale)
  --scale <f>                Facteur d'échelle (défaut : 1)
  --rotate <deg>             Rotation autour de l'axe vertical (défaut : 0)
  --pivot <base|center|origin>  Point d'origine du prop (défaut : base)
  --max-texture <px>         Taille max des textures (défaut : 2048)
  --lods                     Génère les LOD Medium / Low / VeryLow
  --collision <box|mesh|none>  Collision : boîte, forme du modèle, aucune (défaut : box)
  --collision-tris <n>       Triangles max de la collision "mesh" (défaut : 2000)
  --lod-dist <m>             Distance d'affichage (défaut : automatique)
  --flags <n>                Flags de l'archétype YTYP (défaut : 32 = Static)
  --no-flip-normal           Ne pas inverser le vert des normal maps
  --keep-vertex-colors       Garde les couleurs de vertex du GLB
  --lights <fichier.json>    Lumières à intégrer (liste JSON de LightDef, coordonnées GTA du prop)
  --emissive <always|night|off>  Parties émissives : toujours, la nuit, désactivées (défaut : always)
  --emissive-intensity <f>   Intensité de l'émissif (défaut : 5)
  --no-ao                    N'intègre pas l'occlusion ambiante dans la couleur
  --codewalker               Crée aussi un .ydr d'aperçu (textures intégrées) pour CodeWalker
  --keep-xml                 Garde les XML CodeWalker + DDS (pour inspection)
  --lang <fr|en>             Langue des messages (défaut : celle de Windows)
  -h, --help                 Affiche cette aide
""";

const string UsageEn = """
glb2fivem by Edmondio - converts a .glb / .gltf model into a FiveM prop (YDR + YTD + YTYP + fxmanifest)
https://github.com/Edmondio/glb2fivem

Usage:
  glb2fivem <file.glb> [options]

Options:
  -o, --out <folder>         Output folder (default: ./output)
  -n, --name <name>          Prop name (default: file name)
  --max-tris <n>             Triangle cap, decimated above it (default: 150000)
  --height <m>               Final height in metres (overrides --scale)
  --scale <f>                Scale factor (default: 1)
  --rotate <deg>             Rotation around the vertical axis (default: 0)
  --pivot <base|center|origin>  Prop origin point (default: base)
  --max-texture <px>         Max texture size (default: 2048)
  --lods                     Generate Medium / Low / VeryLow LODs
  --collision <box|mesh|none>  Collision: box, model shape, none (default: box)
  --collision-tris <n>       Max triangles of the "mesh" collision (default: 2000)
  --lod-dist <m>             Draw distance (default: automatic)
  --flags <n>                YTYP archetype flags (default: 32 = Static)
  --no-flip-normal           Do not flip the normal map green channel
  --keep-vertex-colors       Keep the GLB vertex colors
  --lights <file.json>       Lights to embed (JSON list of LightDef, prop GTA coordinates)
  --emissive <always|night|off>  Emissive parts: always, at night, off (default: always)
  --emissive-intensity <f>   Emissive intensity (default: 5)
  --no-ao                    Do not bake ambient occlusion into the color
  --codewalker               Also create a preview .ydr (embedded textures) for CodeWalker
  --keep-xml                 Keep CodeWalker XML + DDS files (for inspection)
  --lang <fr|en>             Message language (default: Windows language)
  -h, --help                 Show this help
""";

// Langue : --lang fr|en, sinon celle de Windows.
var langArg = args.SkipWhile(a => a != "--lang").Skip(1).FirstOrDefault();
L.English = langArg != null ? langArg == "en" : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "fr";
args = RemoveLangArg(args);

if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
{
    Console.WriteLine(L.T(Usage, UsageEn));
    return args.Length == 0 ? 1 : 0;
}

string? input = null;
string outDir = Path.Combine(Environment.CurrentDirectory, "output");
var o = new Dictionary<string, string?>();
var flagsOnly = new HashSet<string> { "--no-ao", "--codewalker", "--lods", "--no-flip-normal", "--keep-vertex-colors", "--keep-xml" };

for (int i = 0; i < args.Length; i++)
{
    var a = args[i];
    if (!a.StartsWith('-')) { input ??= a; continue; }
    if (flagsOnly.Contains(a)) { o[a] = null; continue; }
    if (i + 1 >= args.Length) return Fail(L.T($"Valeur manquante pour {a}", $"Missing value for {a}"));
    o[a] = args[++i];
}

if (input == null) return Fail(L.T("Aucun fichier d'entrée.", "No input file."));
if (!File.Exists(input)) return Fail($"Fichier introuvable : {input}");
if (o.TryGetValue("-o", out var od) || o.TryGetValue("--out", out od)) outDir = Path.GetFullPath(od!);

ConvertOptions opt;
try
{
    opt = new ConvertOptions
    {
        InputPath = Path.GetFullPath(input),
        OutputDir = outDir,
        Name = Get("-n") ?? Get("--name"),
        MaxTriangles = GetInt("--max-tris") ?? 150_000,
        TargetHeight = GetFloat("--height"),
        Scale = GetFloat("--scale") ?? 1f,
        RotateZ = GetFloat("--rotate") ?? 0f,
        MaxTextureSize = GetInt("--max-texture") ?? 2048,
        GenerateLods = o.ContainsKey("--lods"),
        FlipNormalGreen = !o.ContainsKey("--no-flip-normal"),
        KeepVertexColors = o.ContainsKey("--keep-vertex-colors"),
        KeepXml = o.ContainsKey("--keep-xml"),
        CodeWalkerPreview = o.ContainsKey("--codewalker"),
        LodDistance = GetFloat("--lod-dist") ?? 0f,
        ArchetypeFlags = (uint)(GetInt("--flags") ?? 32),
        CollisionMaxTriangles = GetInt("--collision-tris") ?? 2000,
        DefaultMaterial = new MaterialOptions
        {
            BakeOcclusion = !o.ContainsKey("--no-ao"),
            EmissiveIntensity = GetFloat("--emissive-intensity") ?? 5f,
            Emissive = (Get("--emissive") ?? "always").ToLowerInvariant() switch
            {
                "always" => EmissiveMode.Always,
                "night" => EmissiveMode.Night,
                "off" => EmissiveMode.Off,
                var e => throw new ArgumentException(L.T($"Mode émissif inconnu : {e}", $"Unknown emissive mode: {e}")),
            },
        },
        Pivot = (Get("--pivot") ?? "base").ToLowerInvariant() switch
        {
            "base" => PivotMode.BaseCenter,
            "center" => PivotMode.Center,
            "origin" => PivotMode.Origin,
            var p => throw new ArgumentException($"Pivot inconnu : {p}"),
        },
        Collision = (Get("--collision") ?? "box").ToLowerInvariant() switch
        {
            "box" => CollisionMode.Box,
            "none" => CollisionMode.None,
            "mesh" => CollisionMode.Mesh,
            var c => throw new ArgumentException($"Collision inconnue : {c}"),
        },
    };
    if (opt.MaxTriangles < 12) throw new ArgumentException(L.T("--max-tris doit être >= 12", "--max-tris must be >= 12"));
    if (Get("--lights") is { } lightsFile)
    {
        var jo = new System.Text.Json.JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        opt.Lights = System.Text.Json.JsonSerializer.Deserialize<List<LightDef>>(File.ReadAllText(lightsFile), jo)
            ?? throw new ArgumentException(L.T("Fichier de lumières vide.", "Lights file is empty."));
    }
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    return Fail(ex.Message);
}

var log = new ConversionLog();
log.Logged += e =>
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = e.Level switch { LogLevel.Warning => ConsoleColor.Yellow, LogLevel.Error => ConsoleColor.Red, _ => prev };
    Console.WriteLine((e.Level == LogLevel.Warning ? "[!] " : "    ") + e.Message);
    Console.ForegroundColor = prev;
};

try
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var r = Converter.Convert(opt, log);
    Console.WriteLine();
    Console.WriteLine(L.T($"OK en {sw.Elapsed.TotalSeconds:0.0} s -> {r.ResourceDir}", $"OK in {sw.Elapsed.TotalSeconds:0.0} s -> {r.ResourceDir}"));
    Console.WriteLine(L.T($"  Taille : {r.Size.X:0.##} x {r.Size.Y:0.##} x {r.Size.Z:0.##} m   (un ped fait ~1,80 m)", $"  Size: {r.Size.X:0.##} x {r.Size.Y:0.##} x {r.Size.Z:0.##} m   (a ped is ~1.80 m)"));
    foreach (var l in r.Lods)
        Console.WriteLine($"  LOD {l.Level,-8} {l.Triangles,9:N0} tris  {l.Vertices,9:N0} verts  {l.Geometries} {L.T("géométrie(s)", "geometry(ies)")}" +
                          (l.Distance < 9000 ? $"  {L.T("jusqu'à", "up to")} {l.Distance:0} m" : ""));
    foreach (var t in r.Textures)
        Console.WriteLine($"  Texture {t.Name,-28} {t.Width}x{t.Height} {t.Format} ({t.MipLevels} mips)");
    foreach (var f in r.Files)
        Console.WriteLine($"  {f.Key,-30} {f.Value / 1024.0,10:0.0} {L.T("Ko", "KB")}");
    Console.WriteLine();
    Console.WriteLine(L.T($"Dans server.cfg : ensure {r.PropName}", $"In server.cfg: ensure {r.PropName}"));
    Console.WriteLine(L.T("Spawn de test   : ", "Test spawn     : ") + $"CreateObject(`{r.PropName}`, coords.x, coords.y, coords.z, true, true, false)");
    return 0;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(L.T($"Échec : {ex.Message}", $"Failed: {ex.Message}"));
    Console.ResetColor();
    if (Environment.GetEnvironmentVariable("GLB2FIVEM_DEBUG") == "1") Console.Error.WriteLine(ex);
    return 2;
}

string? Get(string k) => o.TryGetValue(k, out var v) ? v : null;
int? GetInt(string k) => Get(k) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : null;
float? GetFloat(string k) => Get(k) is { } v ? float.Parse(v.Replace(',', '.'), CultureInfo.InvariantCulture) : null;

static int Fail(string msg)
{
    Console.Error.WriteLine(L.T($"Erreur : {msg}", $"Error: {msg}"));
    Console.Error.WriteLine(L.T("glb2fivem --help pour l'aide.", "glb2fivem --help for help."));
    return 1;
}

static string[] RemoveLangArg(string[] a)
{
    var list = a.ToList();
    int i = list.IndexOf("--lang");
    if (i >= 0) list.RemoveRange(i, Math.Min(2, list.Count - i));
    return list.ToArray();
}
