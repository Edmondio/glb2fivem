namespace Glb2FiveM.Textures;

public static class ImageOps
{
    static readonly float[] SrgbToLinearLut = Enumerable.Range(0, 256).Select(i =>
    {
        float c = i / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }).ToArray();

    public static float SrgbToLinear(byte v) => SrgbToLinearLut[v];

    public static byte LinearToSrgb(float l)
    {
        l = Math.Clamp(l, 0f, 1f);
        float c = l <= 0.0031308f ? l * 12.92f : 1.055f * MathF.Pow(l, 1f / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(c * 255f), 0, 255);
    }

    /// <summary>Puissance de 2 la plus proche (en log), bornée entre 4 et max.</summary>
    public static int NearestPow2(int v, int max)
    {
        max = Math.Max(4, max);
        int p = 1 << (int)Math.Round(Math.Log2(Math.Max(1, v)));
        return Math.Clamp(p, 4, HighestPow2AtMost(max));
    }

    static int HighestPow2AtMost(int v)
    {
        int p = 1;
        while (p * 2 <= v) p *= 2;
        return p;
    }

    /// <summary>Redimensionne : moyenne par zone en réduction, bilinéaire en agrandissement.</summary>
    public static ImageRgba Resize(ImageRgba src, int w, int h)
    {
        if (src.Width == w && src.Height == h) return src;
        var tmp = ResizeAxis(src, w, src.Height, horizontal: true);
        return ResizeAxis(tmp, w, h, horizontal: false);
    }

    static ImageRgba ResizeAxis(ImageRgba src, int w, int h, bool horizontal)
    {
        int srcLen = horizontal ? src.Width : src.Height;
        int dstLen = horizontal ? w : h;
        if (srcLen == dstLen) return src;
        var dst = new ImageRgba(w, h);
        int lines = horizontal ? h : w;
        float ratio = (float)srcLen / dstLen;
        var s = src.Data;
        var d = dst.Data;

        Parallel.For(0, lines, line =>
        {
            Span<float> acc = stackalloc float[4];
            for (int i = 0; i < dstLen; i++)
            {
                acc.Clear();
                float wsum = 0;
                if (ratio > 1f)
                {
                    // Réduction : moyenne de la zone source couverte.
                    float a = i * ratio, b = a + ratio;
                    int ia = (int)a, ib = Math.Min(srcLen, (int)MathF.Ceiling(b));
                    for (int k = ia; k < ib; k++)
                    {
                        float wgt = MathF.Min(k + 1, b) - MathF.Max(k, a);
                        if (wgt <= 0) continue;
                        int o = horizontal ? (line * src.Width + k) * 4 : (k * src.Width + line) * 4;
                        for (int c = 0; c < 4; c++) acc[c] += s[o + c] * wgt;
                        wsum += wgt;
                    }
                }
                else
                {
                    // Agrandissement : bilinéaire.
                    float x = (i + 0.5f) * ratio - 0.5f;
                    int x0 = Math.Clamp((int)MathF.Floor(x), 0, srcLen - 1);
                    int x1 = Math.Min(x0 + 1, srcLen - 1);
                    float f = Math.Clamp(x - x0, 0f, 1f);
                    int o0 = horizontal ? (line * src.Width + x0) * 4 : (x0 * src.Width + line) * 4;
                    int o1 = horizontal ? (line * src.Width + x1) * 4 : (x1 * src.Width + line) * 4;
                    for (int c = 0; c < 4; c++) acc[c] = s[o0 + c] * (1 - f) + s[o1 + c] * f;
                    wsum = 1;
                }
                int od = horizontal ? (line * w + i) * 4 : (i * w + line) * 4;
                for (int c = 0; c < 4; c++) d[od + c] = (byte)Math.Clamp((int)MathF.Round(acc[c] / wsum), 0, 255);
            }
        });
        return dst;
    }

    /// <summary>Chaîne de mipmaps (2x2 box) jusqu'à 4 px sur le plus petit côté.</summary>
    public static List<ImageRgba> BuildMips(ImageRgba top, bool isNormalMap)
    {
        var mips = new List<ImageRgba> { top };
        var cur = top;
        while (cur.Width >= 8 && cur.Height >= 8)
        {
            int w = cur.Width / 2, h = cur.Height / 2;
            var next = new ImageRgba(w, h);
            var s = cur.Data;
            var d = next.Data;
            int sw = cur.Width;
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int o00 = ((y * 2) * sw + x * 2) * 4;
                    int o01 = o00 + 4;
                    int o10 = o00 + sw * 4;
                    int o11 = o10 + 4;
                    int od = (y * w + x) * 4;
                    for (int c = 0; c < 4; c++)
                        d[od + c] = (byte)((s[o00 + c] + s[o01 + c] + s[o10 + c] + s[o11 + c] + 2) >> 2);
                    if (isNormalMap) RenormalizeNormal(d, od);
                }
            });
            mips.Add(next);
            cur = next;
        }
        return mips;
    }

    static void RenormalizeNormal(byte[] d, int o)
    {
        float x = d[o] / 127.5f - 1f, y = d[o + 1] / 127.5f - 1f, z = d[o + 2] / 127.5f - 1f;
        float l = MathF.Sqrt(x * x + y * y + z * z);
        if (l < 1e-6f) { x = 0; y = 0; z = 1; l = 1; }
        d[o] = (byte)Math.Clamp((int)MathF.Round((x / l + 1f) * 127.5f), 0, 255);
        d[o + 1] = (byte)Math.Clamp((int)MathF.Round((y / l + 1f) * 127.5f), 0, 255);
        d[o + 2] = (byte)Math.Clamp((int)MathF.Round((z / l + 1f) * 127.5f), 0, 255);
    }
}
