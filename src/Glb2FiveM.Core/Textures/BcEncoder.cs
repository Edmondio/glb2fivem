namespace Glb2FiveM.Textures;

/// <summary>Encodeur BC1 (DXT1) / BC3 (DXT5) en C# pur.
/// Couleur : axe principal (ACP) + 2 passes de moindres carrés. Alpha BC3 : mode 8 valeurs.</summary>
public static class BcEncoder
{
    public static byte[] EncodeBc1(ImageRgba img) => Encode(img, bc3: false);
    public static byte[] EncodeBc3(ImageRgba img) => Encode(img, bc3: true);

    static byte[] Encode(ImageRgba img, bool bc3)
    {
        int bw = Math.Max(1, (img.Width + 3) / 4);
        int bh = Math.Max(1, (img.Height + 3) / 4);
        int blockSize = bc3 ? 16 : 8;
        var output = new byte[bw * bh * blockSize];

        Parallel.For(0, bh, by =>
        {
            Span<byte> block = stackalloc byte[64];
            for (int bx = 0; bx < bw; bx++)
            {
                // Extraction du bloc 4x4 (bords répétés si l'image est plus petite).
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int sx = Math.Min(bx * 4 + x, img.Width - 1);
                        int sy = Math.Min(by * 4 + y, img.Height - 1);
                        int so = (sy * img.Width + sx) * 4;
                        int d = (y * 4 + x) * 4;
                        block[d] = img.Data[so];
                        block[d + 1] = img.Data[so + 1];
                        block[d + 2] = img.Data[so + 2];
                        block[d + 3] = img.Data[so + 3];
                    }
                var dst = output.AsSpan((by * bw + bx) * blockSize, blockSize);
                if (bc3)
                {
                    EncodeAlphaBlock(block, dst[..8]);
                    EncodeColorBlock(block, dst[8..]);
                }
                else
                {
                    EncodeColorBlock(block, dst);
                }
            }
        });
        return output;
    }

    // ---------------------------------------------------------------- couleur

    static void EncodeColorBlock(ReadOnlySpan<byte> px, Span<byte> dst)
    {
        Span<float> r = stackalloc float[16], g = stackalloc float[16], b = stackalloc float[16];
        float mr = 0, mg = 0, mb = 0;
        for (int i = 0; i < 16; i++)
        {
            r[i] = px[i * 4]; g[i] = px[i * 4 + 1]; b[i] = px[i * 4 + 2];
            mr += r[i]; mg += g[i]; mb += b[i];
        }
        mr /= 16; mg /= 16; mb /= 16;

        // Matrice de covariance.
        float crr = 0, cgg = 0, cbb = 0, crg = 0, crb = 0, cgb = 0;
        for (int i = 0; i < 16; i++)
        {
            float dr = r[i] - mr, dg = g[i] - mg, db = b[i] - mb;
            crr += dr * dr; cgg += dg * dg; cbb += db * db;
            crg += dr * dg; crb += dr * db; cgb += dg * db;
        }

        // Axe principal par itération de puissance.
        float ar = 1, ag = 1, ab = 1;
        for (int it = 0; it < 8; it++)
        {
            float nr = crr * ar + crg * ag + crb * ab;
            float ng = crg * ar + cgg * ag + cgb * ab;
            float nb = crb * ar + cgb * ag + cbb * ab;
            float len = MathF.Sqrt(nr * nr + ng * ng + nb * nb);
            if (len < 1e-6f) { ar = 0; ag = 0; ab = 0; break; }
            ar = nr / len; ag = ng / len; ab = nb / len;
        }

        float minP = float.MaxValue, maxP = float.MinValue;
        for (int i = 0; i < 16; i++)
        {
            float p = (r[i] - mr) * ar + (g[i] - mg) * ag + (b[i] - mb) * ab;
            minP = MathF.Min(minP, p); maxP = MathF.Max(maxP, p);
        }

        ushort c0 = To565(mr + ar * maxP, mg + ag * maxP, mb + ab * maxP);
        ushort c1 = To565(mr + ar * minP, mg + ag * minP, mb + ab * minP);

        Span<int> idx = stackalloc int[16];
        float err = BuildIndices(r, g, b, ref c0, ref c1, idx);

        // Raffinement par moindres carrés.
        Span<int> idx2 = stackalloc int[16];
        for (int pass = 0; pass < 2 && err > 0; pass++)
        {
            if (!LeastSquares(r, g, b, idx, out var n0, out var n1)) break;
            float err2 = BuildIndices(r, g, b, ref n0, ref n1, idx2);
            if (err2 < err)
            {
                err = err2; c0 = n0; c1 = n1;
                idx2.CopyTo(idx);
            }
            else break;
        }

        WriteColorBlock(dst, c0, c1, idx);
    }

    /// <summary>Calcule les indices les plus proches ; garantit c0 &gt; c1 (mode 4 couleurs).</summary>
    static float BuildIndices(ReadOnlySpan<float> r, ReadOnlySpan<float> g, ReadOnlySpan<float> b,
        ref ushort c0, ref ushort c1, Span<int> idx)
    {
        if (c0 < c1) (c0, c1) = (c1, c0);
        if (c0 == c1)
        {
            // Bloc uni : tous les indices à 0.
            idx.Clear();
            From565(c0, out var sr, out var sg, out var sb);
            float e = 0;
            for (int i = 0; i < 16; i++) e += Sq(r[i] - sr) + Sq(g[i] - sg) + Sq(b[i] - sb);
            return e;
        }

        Span<float> pr = stackalloc float[4], pg = stackalloc float[4], pb = stackalloc float[4];
        From565(c0, out pr[0], out pg[0], out pb[0]);
        From565(c1, out pr[1], out pg[1], out pb[1]);
        pr[2] = (2 * pr[0] + pr[1]) / 3; pg[2] = (2 * pg[0] + pg[1]) / 3; pb[2] = (2 * pb[0] + pb[1]) / 3;
        pr[3] = (pr[0] + 2 * pr[1]) / 3; pg[3] = (pg[0] + 2 * pg[1]) / 3; pb[3] = (pb[0] + 2 * pb[1]) / 3;

        float total = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 0; float bestE = float.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                float e = Sq(r[i] - pr[k]) + Sq(g[i] - pg[k]) + Sq(b[i] - pb[k]);
                if (e < bestE) { bestE = e; best = k; }
            }
            idx[i] = best;
            total += bestE;
        }
        return total;
    }

    static bool LeastSquares(ReadOnlySpan<float> r, ReadOnlySpan<float> g, ReadOnlySpan<float> b,
        ReadOnlySpan<int> idx, out ushort c0, out ushort c1)
    {
        // Poids de c0 pour chaque index : 0 -> 1, 1 -> 0, 2 -> 2/3, 3 -> 1/3.
        ReadOnlySpan<float> w0 = stackalloc float[] { 1f, 0f, 2f / 3f, 1f / 3f };
        float aa = 0, bb = 0, ab = 0;
        float axr = 0, axg = 0, axb = 0, bxr = 0, bxg = 0, bxb = 0;
        for (int i = 0; i < 16; i++)
        {
            float a = w0[idx[i]], bw = 1 - a;
            aa += a * a; bb += bw * bw; ab += a * bw;
            axr += a * r[i]; axg += a * g[i]; axb += a * b[i];
            bxr += bw * r[i]; bxg += bw * g[i]; bxb += bw * b[i];
        }
        float det = aa * bb - ab * ab;
        if (MathF.Abs(det) < 1e-6f) { c0 = c1 = 0; return false; }
        float f = 1f / det;
        c0 = To565((axr * bb - bxr * ab) * f, (axg * bb - bxg * ab) * f, (axb * bb - bxb * ab) * f);
        c1 = To565((bxr * aa - axr * ab) * f, (bxg * aa - axg * ab) * f, (bxb * aa - axb * ab) * f);
        return true;
    }

    static void WriteColorBlock(Span<byte> dst, ushort c0, ushort c1, ReadOnlySpan<int> idx)
    {
        dst[0] = (byte)c0; dst[1] = (byte)(c0 >> 8);
        dst[2] = (byte)c1; dst[3] = (byte)(c1 >> 8);
        uint bits = 0;
        for (int i = 0; i < 16; i++) bits |= (uint)idx[i] << (i * 2);
        dst[4] = (byte)bits; dst[5] = (byte)(bits >> 8); dst[6] = (byte)(bits >> 16); dst[7] = (byte)(bits >> 24);
    }

    static ushort To565(float r, float g, float b)
    {
        int ir = Math.Clamp((int)MathF.Round(r * 31f / 255f), 0, 31);
        int ig = Math.Clamp((int)MathF.Round(g * 63f / 255f), 0, 63);
        int ib = Math.Clamp((int)MathF.Round(b * 31f / 255f), 0, 31);
        return (ushort)((ir << 11) | (ig << 5) | ib);
    }

    static void From565(ushort c, out float r, out float g, out float b)
    {
        int ir = (c >> 11) & 31, ig = (c >> 5) & 63, ib = c & 31;
        r = (ir << 3) | (ir >> 2);
        g = (ig << 2) | (ig >> 4);
        b = (ib << 3) | (ib >> 2);
    }

    static float Sq(float v) => v * v;

    // ---------------------------------------------------------------- alpha (BC3)

    static void EncodeAlphaBlock(ReadOnlySpan<byte> px, Span<byte> dst)
    {
        byte min = 255, max = 0;
        for (int i = 0; i < 16; i++)
        {
            byte a = px[i * 4 + 3];
            if (a < min) min = a;
            if (a > max) max = a;
        }
        dst[0] = max; dst[1] = min;
        ulong bits = 0;
        if (max != min)
        {
            // Mode 8 valeurs (a0 > a1) : 0 = a0, 1 = a1, 2..7 = interpolations.
            Span<int> pal = stackalloc int[8];
            pal[0] = max; pal[1] = min;
            for (int k = 1; k <= 6; k++) pal[k + 1] = ((7 - k) * max + k * min) / 7;
            for (int i = 0; i < 16; i++)
            {
                int a = px[i * 4 + 3];
                int best = 0, bestE = int.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    int e = Math.Abs(a - pal[k]);
                    if (e < bestE) { bestE = e; best = k; }
                }
                bits |= (ulong)best << (i * 3);
            }
        }
        for (int i = 0; i < 6; i++) dst[2 + i] = (byte)(bits >> (i * 8));
    }
}
