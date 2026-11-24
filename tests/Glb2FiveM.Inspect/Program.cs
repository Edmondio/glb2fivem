// Relit une ressource produite par glb2fivem avec CodeWalker :
// YDR / YTYP -> XML, textures du YTD -> PNG. Sert à vérifier les sorties.
using System.IO.Compression;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

if (args.Length >= 1 && args[0] == "render")
{
    // render <ydr> <ytd> <out.png> [lod 0-3]
    Render.Run(args[1], args[2], args[3], args.Length > 4 ? int.Parse(args[4]) : 0);
    return 0;
}
if (args.Length < 2) { Console.WriteLine("Usage : inspect <dossier stream> <dossier sortie> | render <ydr> <ytd> <png> [lod]"); return 1; }
var src = args[0];
var dst = args[1];
Directory.CreateDirectory(dst);

foreach (var f in Directory.GetFiles(src))
{
    var data = File.ReadAllBytes(f);
    var name = Path.GetFileName(f);
    switch (Path.GetExtension(f).ToLowerInvariant())
    {
        case ".ydr":
        {
            var ydr = new YdrFile(); ydr.Load(data);
            File.WriteAllText(Path.Combine(dst, name + ".xml"), YdrXml.GetXml(ydr, ""));
            Console.WriteLine($"{name}: OK");
            break;
        }
        case ".ytyp":
        {
            var ytyp = new YtypFile(); ytyp.Load(data);
            File.WriteAllText(Path.Combine(dst, name + ".xml"), MetaXml.GetXml(ytyp, out _));
            Console.WriteLine($"{name}: {ytyp.AllArchetypes?.Length ?? 0} archétype(s)");
            break;
        }
        case ".ytd":
        {
            var ytd = new YtdFile(); ytd.Load(data);
            foreach (var t in ytd.TextureDict.Textures.data_items)
            {
                var px = DDSIO.GetPixels(t, 0);
                for (int i = 0; i < px.Length; i += 4) (px[i], px[i + 2]) = (px[i + 2], px[i]); // BGRA -> RGBA
                Png.Write(Path.Combine(dst, t.Name + ".png"), t.Width, t.Height, px);
                Console.WriteLine($"{name}: {t.Name} {t.Width}x{t.Height} {t.Format} mips={t.Levels}");
            }
            break;
        }
    }
}
return 0;

static class Png
{
    public static void Write(string path, int w, int h, byte[] rgba)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BE(ihdr, 0, (uint)w); BE(ihdr, 4, (uint)h);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(fs, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, true))
            for (int y = 0; y < h; y++) { z.WriteByte(0); z.Write(rgba, y * w * 4, w * 4); }
        Chunk(fs, "IDAT", raw.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, (uint)data.Length); s.Write(len);
        var td = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(td);
        var crc = new byte[4]; BE(crc, 0, Crc(td)); s.Write(crc);
    }
    static void BE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    static uint Crc(byte[] d)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in d) { c ^= b; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; }
        return ~c;
    }
}
