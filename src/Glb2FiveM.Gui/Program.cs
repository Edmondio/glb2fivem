using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Glb2FiveM;

// glb2fivem-gui : petit serveur local (127.0.0.1 uniquement) qui sert l'interface web embarquée
// et appelle le convertisseur. Le navigateur s'ouvre tout seul.

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var noBrowser = args.Contains("--no-browser");
var forceBrowser = args.Contains("--browser"); // onglet du navigateur par défaut au lieu d'une fenêtre dédiée
int port = args.SkipWhile(a => a != "--port").Skip(1).Select(int.Parse).FirstOrDefault();
if (port == 0)
{
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
}

// Jeton aléatoire : empêche un site web ouvert dans le navigateur d'appeler l'API locale.
var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
var host = $"127.0.0.1:{port}";
var url = $"http://{host}/?t={token}";

var assets = LoadAssets();
var convertLock = new SemaphoreSlim(1, 1);
var analyzeLock = new SemaphoreSlim(1, 1);
// Fichiers envoyés par la page : envoyés une fois, réutilisés pour l'aperçu et la conversion.
var uploads = new Dictionary<string, string>();
// Retouches de textures envoyées par la page : id du fichier -> (index d'image glTF -> PNG).
var imageEdits = new Dictionary<string, Dictionary<int, byte[]>>();
var uploadRoot = Path.Combine(Path.GetTempPath(), "glb2fivem_uploads_" + Environment.ProcessId);
Directory.CreateDirectory(uploadRoot);
AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(uploadRoot, true); } catch { } };
var lastPing = DateTime.MinValue; // dernier battement de la page
var byeAt = DateTime.MinValue;    // la page a signalé sa fermeture
AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject?.ToString());
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

var listener = new HttpListener();
listener.Prefixes.Add($"http://{host}/");
listener.Start();

Console.WriteLine($"{AppInfo.FullName} - interface");
Console.WriteLine(AppInfo.GitHubUrl);
Console.WriteLine($"Ouvre : {url}");
Console.WriteLine("(laisse cette fenêtre ouverte ; ferme-la pour quitter)");
if (!noBrowser)
{
    var window = forceBrowser ? null : OpenAppWindow(url);
    if (window == null) OpenInShell(url);
    else
    {
        // Fenêtre dédiée : quand on la ferme, l'application s'arrête.
        // On ne surveille PAS le processus du navigateur : si Edge/Chrome tourne déjà en arrière-plan,
        // le processus lancé passe la main et se termine tout de suite, ce qui coupait le serveur.
        // La page envoie un battement régulier ; sans battement, on quitte.
        Console.WriteLine("(fermer la fenêtre glb2fivem quitte l'application)");
        _ = Task.Run(async () =>
        {
            var started = DateTime.UtcNow;
            while (true)
            {
                await Task.Delay(2000);
                if (convertLock.CurrentCount == 0 || analyzeLock.CurrentCount == 0) continue; // travail en cours
                var now = DateTime.UtcNow;
                var last = lastPing;
                bool neverConnected = last == DateTime.MinValue && now - started > TimeSpan.FromMinutes(3);
                bool closed = byeAt > last && now - byeAt > TimeSpan.FromSeconds(5);
                // 150 s : un navigateur réduit ralentit les minuteurs de la page jusqu'à une fois par minute.
                bool silent = last != DateTime.MinValue && now - last > TimeSpan.FromSeconds(150);
                if (neverConnected || closed || silent) Environment.Exit(0);
            }
        });
    }
}

while (listener.IsListening)
{
    var ctx = await listener.GetContextAsync();
    _ = Task.Run(() => Handle(ctx));
}

async Task Handle(HttpListenerContext ctx)
{
    var req = ctx.Request;
    var res = ctx.Response;
    try
    {
        // Langue de l'interface (en-tête X-Lang ou ?lang=) : messages et journal de conversion.
        L.English = (req.Headers["X-Lang"] ?? req.QueryString["lang"]) == "en";
        // Protection contre le DNS rebinding : seul l'hôte exact est accepté.
        if (!string.Equals(req.UserHostName, host, StringComparison.OrdinalIgnoreCase))
        {
            res.StatusCode = 403;
            return;
        }

        var path = req.Url!.AbsolutePath;
        if (path == "/api/bye" && req.QueryString["t"] == token)
        {
            // Envoyé par navigator.sendBeacon à la fermeture (pas d'en-tête possible, jeton dans l'URL).
            byeAt = DateTime.UtcNow;
            res.StatusCode = 204;
            return;
        }
        if (path.StartsWith("/api/"))
        {
            if (req.Headers["X-Token"] != token)
            {
                await Json(res, new { error = L.T("Jeton invalide : relance l'interface.", "Invalid token: restart the app.") }, 403);
                return;
            }
            switch (path)
            {
                case "/api/info" when req.HttpMethod == "GET":
                    await Json(res, new
                    {
                        defaultOutput = LoadSetting("outDir") ?? DefaultOutputDir(),
                        canBrowse = OperatingSystem.IsWindows(),
                        github = AppInfo.GitHubUrl,
                        version = (Assembly.GetEntryAssembly() ?? typeof(Converter).Assembly).GetName().Version?.ToString(3),
                    });
                    return;
                case "/api/github" when req.HttpMethod == "POST":
                    OpenInShell(AppInfo.GitHubUrl);
                    await Json(res, new { ok = true });
                    return;
                case "/api/ping":
                    lastPing = DateTime.UtcNow;
                    await Json(res, new { ok = true });
                    return;
                case "/api/upload" when req.HttpMethod == "POST":
                    await HandleUpload(req, res);
                    return;
                case "/api/texture" when req.HttpMethod == "POST":
                {
                    var id = req.QueryString["id"];
                    if (UploadPath(id) == null || !int.TryParse(req.QueryString["image"], out var imageIndex))
                    { await Json(res, new { error = L.T("Paramètres invalides.", "Invalid parameters.") }, 400); return; }
                    using var ms = new MemoryStream();
                    await req.InputStream.CopyToAsync(ms);
                    lock (imageEdits)
                    {
                        if (!imageEdits.TryGetValue(id!, out var map)) imageEdits[id!] = map = new();
                        map[imageIndex] = ms.ToArray();
                    }
                    await Json(res, new { ok = true });
                    return;
                }
                case "/api/texture-clear" when req.HttpMethod == "POST":
                    lock (imageEdits) imageEdits.Remove(req.QueryString["id"] ?? "");
                    await Json(res, new { ok = true });
                    return;
                case "/api/analyze" when req.HttpMethod == "POST":
                    await HandleAnalyze(req, res);
                    return;
                case "/api/convert" when req.HttpMethod == "POST":
                    await HandleConvert(req, res);
                    return;
                case "/api/open" when req.HttpMethod == "POST":
                {
                    var body = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(req.InputStream);
                    var dir = body?.GetValueOrDefault("path");
                    if (dir == null || !Directory.Exists(dir)) { await Json(res, new { error = L.T("Dossier introuvable.", "Folder not found.") }, 404); return; }
                    OpenInShell(dir);
                    await Json(res, new { ok = true });
                    return;
                }
                case "/api/browse" when req.HttpMethod == "POST":
                    await Json(res, new { path = BrowseFolder() });
                    return;
            }
            await Json(res, new { error = L.T("Route inconnue.", "Unknown route.") }, 404);
            return;
        }

        // Fichiers statiques embarqués.
        var key = path == "/" ? "index.html" : path.TrimStart('/');
        if (req.HttpMethod == "GET" && assets.TryGetValue(key, out var data))
        {
            res.ContentType = ContentType(key);
            res.Headers["Cache-Control"] = "no-store";
            res.ContentLength64 = data.Length;
            await res.OutputStream.WriteAsync(data);
            return;
        }
        res.StatusCode = 404;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erreur : {ex.Message}");
        try { await Json(res, new { error = ex.Message }, 500); } catch { }
    }
    finally
    {
        try { res.Close(); } catch { }
    }
}

async Task HandleUpload(HttpListenerRequest req, HttpListenerResponse res)
{
    var fileName = Path.GetFileName(req.QueryString["file"] ?? "modele.glb");
    var ext = Path.GetExtension(fileName).ToLowerInvariant();
    if (ext is not (".glb" or ".gltf")) { await Json(res, new { error = L.T("Seuls les fichiers .glb / .gltf sont acceptés.", "Only .glb / .gltf files are accepted.") }, 400); return; }
    var id = Guid.NewGuid().ToString("N")[..12];
    var dir = Path.Combine(uploadRoot, id);
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, fileName);
    await using (var fs = File.Create(path)) await req.InputStream.CopyToAsync(fs);
    lock (uploads)
    {
        // On ne garde que les derniers fichiers envoyés.
        foreach (var old in uploads.Keys.ToList())
        {
            try { Directory.Delete(Path.GetDirectoryName(uploads[old])!, true); } catch { }
            uploads.Remove(old);
        }
        uploads[id] = path;
    }
    await Json(res, new { id });
}

string? UploadPath(string? id)
{
    if (id == null) return null;
    lock (uploads) return uploads.TryGetValue(id, out var p) && File.Exists(p) ? p : null;
}

ConvertOptions OptionsFrom(System.Collections.Specialized.NameValueCollection q, string input)
{
    var outDir = string.IsNullOrWhiteSpace(q["out"]) ? DefaultOutputDir() : q["out"]!;
    return new ConvertOptions
    {
        InputPath = input,
        OutputDir = Path.GetFullPath(outDir),
        Name = NullIfEmpty(q["name"]),
        MaxTriangles = Math.Max(12, Int(q["maxTris"], 150_000)),
        TargetHeight = Float(q["height"]) is > 0 and var h ? h : null,
        RotateZ = Float(q["rotate"]) ?? 0,
        MaxTextureSize = Int(q["maxTexture"], 2048),
        GenerateLods = q["lods"] == "1",
        FlipNormalGreen = q["flipNormal"] != "0",
        KeepXml = q["keepXml"] == "1",
        CodeWalkerPreview = q["cwPreview"] != "0",
        LodDistance = Float(q["lodDist"]) ?? 0,
        Pivot = q["pivot"] switch { "center" => PivotMode.Center, "origin" => PivotMode.Origin, _ => PivotMode.BaseCenter },
        Collision = q["collision"] switch { "none" => CollisionMode.None, "mesh" => CollisionMode.Mesh, _ => CollisionMode.Box },
        CollisionMaxTriangles = Int(q["collisionTris"], 2000),
    };
}

/// <summary>Aperçu : géométrie finale (LOD + collision) sans écrire de fichiers.
/// Coordonnées GTA (Z en haut), tableaux binaires encodés en base64.</summary>
async Task HandleAnalyze(HttpListenerRequest req, HttpListenerResponse res)
{
    var input = UploadPath(req.QueryString["id"]);
    if (input == null) { await Json(res, new { error = L.T("Fichier introuvable, recharge le modèle.", "File not found, reload the model.") }, 404); return; }
    await analyzeLock.WaitAsync();
    var log = new ConversionLog();
    try
    {
        var opt = OptionsFrom(req.QueryString, input);
        var sw = Stopwatch.StartNew();
        var prep = await Task.Run(() => Converter.Prepare(opt, log));
        string[] names = { "High", "Medium", "Low", "VeryLow" };
        var lods = new List<object>();
        for (int i = 0; i < 4; i++)
        {
            var m = prep.Lods[i];
            if (m == null) continue;
            var (pos, idx) = Pack(m.Parts);
            lods.Add(new { level = names[i], triangles = m.TriangleCount, vertices = m.VertexCount, positions = pos, indices = idx });
        }
        object? collision = opt.Collision switch
        {
            CollisionMode.Box => new { type = "box", min = V(prep.Min), max = V(prep.Max), triangles = 12 },
            CollisionMode.Mesh when prep.CollisionMesh != null => new
            {
                type = "mesh",
                triangles = prep.CollisionMesh.TriangleCount,
                vertices = prep.CollisionMesh.VertexCount,
                positions = Pack(new[] { prep.CollisionMesh }).pos,
                indices = Pack(new[] { prep.CollisionMesh }).idx,
            },
            _ => null,
        };
        await Json(res, new
        {
            ok = true,
            ms = sw.ElapsedMilliseconds,
            sourceTriangles = prep.SourceTriangles,
            sourceVertices = prep.SourceVertices,
            decimated = prep.Decimated,
            size = V(prep.Max - prep.Min),
            lods,
            collision,
            logs = log.Entries.Where(e => e.Level != LogLevel.Info).Select(e => new { level = e.Level.ToString().ToLowerInvariant(), e.Message }),
        });
    }
    catch (Exception ex)
    {
        LogCrash("Analyse : " + ex);
        await Json(res, new { error = ex.Message }, 422);
    }
    finally
    {
        analyzeLock.Release();
    }

    static float[] V(System.Numerics.Vector3 v) => new[] { v.X, v.Y, v.Z };
    static (string pos, string idx) Pack(IEnumerable<Glb2FiveM.Geometry.MeshPart> parts)
    {
        var pos = new List<float>();
        var idx = new List<uint>();
        foreach (var p in parts)
        {
            uint baseV = (uint)(pos.Count / 3);
            foreach (var v in p.Positions) { pos.Add(v.X); pos.Add(v.Y); pos.Add(v.Z); }
            foreach (var i in p.Indices) idx.Add(baseV + (uint)i);
        }
        return (Convert.ToBase64String(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pos.ToArray().AsSpan())),
                Convert.ToBase64String(System.Runtime.InteropServices.MemoryMarshal.AsBytes(idx.ToArray().AsSpan())));
    }
}

async Task HandleConvert(HttpListenerRequest req, HttpListenerResponse res)
{
    var q = req.QueryString;
    var input = UploadPath(q["id"]);
    if (input == null) { await Json(res, new { error = L.T("Fichier introuvable, recharge le modèle.", "File not found, reload the model.") }, 404); return; }
    if (!await convertLock.WaitAsync(0)) { await Json(res, new { error = L.T("Une conversion est déjà en cours.", "A conversion is already running.") }, 409); return; }
    var log = new ConversionLog();
    try
    {
        var opt = OptionsFrom(q, input);
        lock (imageEdits)
            if (imageEdits.TryGetValue(q["id"]!, out var edits)) opt.ImageOverrides = new(edits);
        if (!string.IsNullOrEmpty(q["colors"]))
        {
            var colors = JsonSerializer.Deserialize<Dictionary<string, float[]>>(q["colors"]!) ?? new();
            foreach (var (k, v) in colors)
                if (int.TryParse(k, out var mi) && v.Length == 4)
                    opt.BaseColorOverrides[mi] = new System.Numerics.Vector4(v[0], v[1], v[2], v[3]);
        }
        // Corps JSON facultatif : lumières placées dans le viewer (coordonnées GTA).
        if (req.HasEntityBody)
        {
            var body = await JsonSerializer.DeserializeAsync<ConvertBody>(req.InputStream, json);
            foreach (var (k, mb) in body?.Materials ?? new())
            {
                if (!int.TryParse(k, out var mi)) continue;
                opt.Materials[mi] = new MaterialOptions
                {
                    Emissive = mb.Emissive switch { "night" => EmissiveMode.Night, "off" => EmissiveMode.Off, _ => EmissiveMode.Always },
                    EmissiveIntensity = mb.EmissiveIntensity,
                    BakeOcclusion = mb.BakeAo,
                    OcclusionStrength = mb.AoStrength,
                };
            }
            foreach (var l in body?.Lights ?? new())
            {
                opt.Lights.Add(new LightDef
                {
                    Type = l.Type switch { "spot" => GtaLightType.Spot, "capsule" => GtaLightType.Capsule, _ => GtaLightType.Point },
                    Length = l.Length,
                    Position = new System.Numerics.Vector3(l.Position[0], l.Position[1], l.Position[2]),
                    Direction = new System.Numerics.Vector3(l.Direction[0], l.Direction[1], l.Direction[2]),
                    R = (byte)Math.Clamp(l.Color[0], 0, 255),
                    G = (byte)Math.Clamp(l.Color[1], 0, 255),
                    B = (byte)Math.Clamp(l.Color[2], 0, 255),
                    Intensity = l.Intensity,
                    Falloff = l.Falloff,
                    ConeAngle = l.ConeAngle,
                    CoronaSize = l.CoronaSize,
                    TimeFlags = l.TimeFlags,
                    Flashiness = (byte)Math.Clamp(l.Flashiness, 0, 20),
                    CastShadows = l.Shadows,
                });
            }
        }

        log.Logged += e => Console.WriteLine((e.Level == LogLevel.Warning ? "[!] " : "    ") + e.Message);
        var sw = Stopwatch.StartNew();
        var r = await Task.Run(() => Converter.Convert(opt, log));
        SaveSetting("outDir", opt.OutputDir);

        await Json(res, new
        {
            ok = true,
            seconds = Math.Round(sw.Elapsed.TotalSeconds, 1),
            lights = opt.Lights.Count,
            previewDir = r.PreviewDir,
            propName = r.PropName,
            resourceDir = r.ResourceDir,
            sourceTriangles = r.SourceTriangles,
            decimated = r.Decimated,
            size = new[] { r.Size.X, r.Size.Y, r.Size.Z },
            scale = r.AppliedScale,
            lodDist = r.ArchetypeLodDist,
            lods = r.Lods,
            textures = r.Textures.Select(t => new { t.Name, t.Width, t.Height, format = t.Format.ToString().ToUpperInvariant(), role = t.Role.ToString(), t.MipLevels }),
            shaders = r.Shaders.Select(s => new { material = s.SourceMaterial, s.FileName }),
            files = r.Files.Select(f => new { name = f.Key.Replace('\\', '/'), bytes = f.Value }),
            logs = log.Entries.Select(e => new { level = e.Level.ToString().ToLowerInvariant(), e.Message }),
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Échec : {ex.Message}");
        await Json(res, new
        {
            error = ex.Message,
            logs = log.Entries.Select(e => new { level = e.Level.ToString().ToLowerInvariant(), e.Message }),
        }, 422);
    }
    finally
    {
        convertLock.Release();
    }
}

async Task Json(HttpListenerResponse res, object payload, int status = 200)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, json);
    res.StatusCode = status;
    res.ContentType = "application/json; charset=utf-8";
    res.ContentLength64 = bytes.Length;
    await res.OutputStream.WriteAsync(bytes);
}

static Dictionary<string, byte[]> LoadAssets()
{
    var asm = Assembly.GetExecutingAssembly();
    var dict = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    foreach (var name in asm.GetManifestResourceNames())
    {
        var norm = name.Replace('\\', '/');
        if (!norm.StartsWith("wwwroot/")) continue;
        using var s = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        dict[norm["wwwroot/".Length..]] = ms.ToArray();
    }
    return dict;
}

static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".html" => "text/html; charset=utf-8",
    ".js" => "text/javascript; charset=utf-8",
    ".css" => "text/css; charset=utf-8",
    ".svg" => "image/svg+xml",
    ".png" => "image/png",
    _ => "application/octet-stream",
};

static string DefaultOutputDir()
{
    var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    if (string.IsNullOrEmpty(docs)) docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(docs, "glb2fivem");
}

/// <summary>Ouvre l'interface dans une fenêtre d'application (Edge ou Chrome en mode --app, sans barre d'adresse),
/// avec un profil séparé pour que la fenêtre soit indépendante du navigateur habituel.</summary>
static Process? OpenAppWindow(string url)
{
    if (!OperatingSystem.IsWindows()) return null;
    string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    string[] candidates =
    {
        Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
        Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
        Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
        Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
        Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
    };
    var exe = candidates.FirstOrDefault(File.Exists);
    if (exe == null) return null;
    var profile = Path.Combine(local, "glb2fivem", "fenetre");
    try
    {
        Directory.CreateDirectory(profile);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in new[]
        {
            $"--app={url}", $"--user-data-dir={profile}", "--window-size=1500,950",
            "--no-first-run", "--no-default-browser-check", "--disable-sync",
        }) psi.ArgumentList.Add(a);
        return Process.Start(psi);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Fenêtre dédiée impossible ({ex.Message}), ouverture dans le navigateur.");
        return null;
    }
}

static void OpenInShell(string target)
{
    try
    {
        if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        else Process.Start("xdg-open", target);
    }
    catch (Exception ex) { Console.WriteLine($"Impossible d'ouvrir {target} : {ex.Message}"); }
}

/// <summary>Sélecteur de dossier Windows natif (via PowerShell, pour éviter une dépendance WinForms).</summary>
static string? BrowseFolder()
{
    if (!OperatingSystem.IsWindows()) return null;
    const string script =
        "Add-Type -AssemblyName System.Windows.Forms;" +
        "$f = New-Object System.Windows.Forms.FolderBrowserDialog;" +
        "$f.Description = 'Dossier de sortie des ressources FiveM';" +
        "$w = New-Object System.Windows.Forms.Form -Property @{TopMost=$true};" +
        "if ($f.ShowDialog($w) -eq 'OK') { [Console]::OutputEncoding=[Text.Encoding]::UTF8; $f.SelectedPath }";
    var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -STA -Command \"{script}\"")
    {
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
    };
    using var p = Process.Start(psi);
    var output = p?.StandardOutput.ReadToEnd().Trim();
    p?.WaitForExit();
    return string.IsNullOrEmpty(output) ? null : output;
}

// Réglages persistants (%LOCALAPPDATA%\glb2fivem\reglages.json) : le port change à chaque lancement,
// la mémoire du navigateur ne suffit donc pas.
static string SettingsPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "glb2fivem", "reglages.json");

static string? LoadSetting(string key)
{
    try
    {
        var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath()));
        return d != null && d.TryGetValue(key, out var v) ? v : null;
    }
    catch { return null; }
}

static void SaveSetting(string key, string value)
{
    try
    {
        var path = SettingsPath();
        Dictionary<string, string> d;
        try { d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(); } catch { d = new(); }
        d[key] = value;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(d));
    }
    catch { }
}

static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
static int Int(string? s, int def) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
static float? Float(string? s) => float.TryParse(s?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

/// <summary>Erreurs inattendues dans %LOCALAPPDATA%\glb2fivem\erreurs.txt (pour le diagnostic).</summary>
static void LogCrash(string? text)
{
    try
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "glb2fivem");
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, "erreurs.txt"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}{Environment.NewLine}");
    }
    catch { }
}

sealed class ConvertBody
{
    public List<LightBody> Lights { get; set; } = new();
    public Dictionary<string, MaterialBody> Materials { get; set; } = new();
}

sealed class MaterialBody
{
    public string Emissive { get; set; } = "always";
    public float EmissiveIntensity { get; set; } = 5;
    public bool BakeAo { get; set; } = true;
    public float AoStrength { get; set; } = 1;
}

sealed class LightBody
{
    public string Type { get; set; } = "point";
    public float[] Position { get; set; } = new float[3];
    public float[] Direction { get; set; } = { 0, 0, -1 };
    public int[] Color { get; set; } = { 255, 255, 255 };
    public float Intensity { get; set; } = 5;
    public float Falloff { get; set; } = 5;
    public float ConeAngle { get; set; } = 45;
    public float Length { get; set; } = 1;
    public float CoronaSize { get; set; } = 0.3f;
    public uint TimeFlags { get; set; } = LightDef.TimeAlways;
    public int Flashiness { get; set; }
    public bool Shadows { get; set; }
}
