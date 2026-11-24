using StbImageSharp;

namespace Glb2FiveM.Textures;

/// <summary>Image 8 bits RGBA en mémoire.</summary>
public sealed class ImageRgba
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public ImageRgba(int w, int h, byte[]? data = null)
    {
        Width = w;
        Height = h;
        Data = data ?? new byte[w * h * 4];
        if (Data.Length != w * h * 4) throw new ArgumentException("Taille de buffer invalide.");
    }

    public static ImageRgba Decode(byte[] encoded)
    {
        var res = ImageResult.FromMemory(encoded, ColorComponents.RedGreenBlueAlpha);
        return new ImageRgba(res.Width, res.Height, res.Data);
    }

    public static ImageRgba Solid(int w, int h, byte r, byte g, byte b, byte a)
    {
        var img = new ImageRgba(w, h);
        for (int i = 0; i < img.Data.Length; i += 4)
        {
            img.Data[i] = r; img.Data[i + 1] = g; img.Data[i + 2] = b; img.Data[i + 3] = a;
        }
        return img;
    }

    public bool HasTransparency(byte threshold = 250)
    {
        for (int i = 3; i < Data.Length; i += 4) if (Data[i] < threshold) return true;
        return false;
    }
}
