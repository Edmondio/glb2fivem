namespace Glb2FiveM.Textures;

public enum DxtFormat { Dxt1, Dxt5 }

/// <summary>Écrit un .dds DX9 (FourCC DXT1/DXT5) avec sa chaîne de mipmaps, lisible par CodeWalker.</summary>
public static class DdsWriter
{
    public static byte[] Write(IReadOnlyList<ImageRgba> mips, DxtFormat format)
    {
        var top = mips[0];
        var blocks = mips.Select(m => format == DxtFormat.Dxt1 ? BcEncoder.EncodeBc1(m) : BcEncoder.EncodeBc3(m)).ToList();

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        const uint DDSD_CAPS = 0x1, DDSD_HEIGHT = 0x2, DDSD_WIDTH = 0x4, DDSD_PIXELFORMAT = 0x1000,
                   DDSD_MIPMAPCOUNT = 0x20000, DDSD_LINEARSIZE = 0x80000;
        const uint DDPF_FOURCC = 0x4;
        const uint DDSCAPS_COMPLEX = 0x8, DDSCAPS_TEXTURE = 0x1000, DDSCAPS_MIPMAP = 0x400000;

        w.Write(0x20534444u); // "DDS "
        w.Write(124u);
        uint flags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT | DDSD_LINEARSIZE;
        if (mips.Count > 1) flags |= DDSD_MIPMAPCOUNT;
        w.Write(flags);
        w.Write((uint)top.Height);
        w.Write((uint)top.Width);
        w.Write((uint)blocks[0].Length);   // pitchOrLinearSize
        w.Write(0u);                       // depth
        w.Write((uint)mips.Count);         // mipMapCount
        for (int i = 0; i < 11; i++) w.Write(0u);
        // DDS_PIXELFORMAT
        w.Write(32u);
        w.Write(DDPF_FOURCC);
        w.Write(format == DxtFormat.Dxt1 ? 0x31545844u : 0x35545844u); // 'DXT1' / 'DXT5'
        w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u);
        uint caps = DDSCAPS_TEXTURE;
        if (mips.Count > 1) caps |= DDSCAPS_COMPLEX | DDSCAPS_MIPMAP;
        w.Write(caps);
        w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u);

        foreach (var b in blocks) w.Write(b);
        w.Flush();
        return ms.ToArray();
    }
}
