// Rendu logiciel d'un YDR (relu depuis le binaire) à côté d'un mannequin de 1,80 m.
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using V3 = System.Numerics.Vector3;

static class Render
{
    sealed record Tex(int W, int H, byte[] Px);
    sealed record Tri(V3 A, V3 B, V3 C, V3 Na, V3 Nb, V3 Nc, System.Numerics.Vector2 Ta, System.Numerics.Vector2 Tb, System.Numerics.Vector2 Tc, Tex? T, V3 Col);

    public static void Run(string ydrPath, string ytdPath, string outPng, int lod)
    {
        var ydr = new YdrFile(); ydr.Load(File.ReadAllBytes(ydrPath));
        var ytd = new YtdFile(); ytd.Load(File.ReadAllBytes(ytdPath));
        var texCache = new Dictionary<string, Tex>();
        Tex? GetTex(string? name)
        {
            if (name == null) return null;
            if (texCache.TryGetValue(name, out var t)) return t;
            var tx = ytd.TextureDict.Textures.data_items.FirstOrDefault(x => x.Name == name);
            if (tx == null) return null;
            var px = DDSIO.GetPixels(tx, 0);
            for (int i = 0; i < px.Length; i += 4) (px[i], px[i + 2]) = (px[i + 2], px[i]);
            return texCache[name] = new Tex(tx.Width, tx.Height, px);
        }

        var d = ydr.Drawable;
        var tris = new List<Tri>();
        if (lod == 9)
        {
            // Mode collision : triangles du BVH intégré, en orange.
            var col = new V3(1f, 0.55f, 0.15f);
            void AddBound(Bounds bnd)
            {
                if (bnd is BoundComposite comp) { foreach (var ch in comp.Children.data_items) AddBound(ch); return; }
                if (bnd is BoundGeometry geo && geo.Polygons != null)
                    foreach (var poly in geo.Polygons)
                        if (poly is BoundPolygonTriangle tri)
                        {
                            V3 G(int i) { var v = geo.GetVertexPos(i); return new V3(v.X, v.Y, v.Z); }
                            var a = G(tri.vertIndex1); var b = G(tri.vertIndex2); var c = G(tri.vertIndex3);
                            var n = V3.Normalize(V3.Cross(b - a, c - a));
                            tris.Add(new Tri(a, b, c, n, n, n, default, default, default, null, col));
                        }
                if (bnd is BoundBox box)
                    Console.WriteLine($"Boîte : {box.BoxMin} -> {box.BoxMax}");
            }
            AddBound(ydr.Drawable.Bound);
            Console.WriteLine($"Collision : {tris.Count} triangles");
        }
        var models = lod == 9 ? null : lod switch { 0 => d.DrawableModels.High, 1 => d.DrawableModels.Med, 2 => d.DrawableModels.Low, _ => d.DrawableModels.VLow };
        var diffHash = (uint)JenkHash.GenHash("diffusesampler");
        foreach (var m in models ?? Array.Empty<DrawableModel>())
            foreach (var g in m.Geometries)
            {
                var sh = d.ShaderGroup.Shaders.data_items[g.ShaderID];
                string? texName = null;
                for (int i = 0; i < sh.ParametersList.Count; i++)
                    if ((uint)sh.ParametersList.Hashes[i] == diffHash && sh.ParametersList.Parameters[i].Data is TextureBase tb) texName = tb.Name;
                var tex = GetTex(texName);
                var vd = g.VertexData;
                var idx = g.IndexBuffer.Indices;
                V3 P(int v) { var p = vd.GetVector3(v, 0); return new V3(p.X, p.Y, p.Z); }
                V3 N(int v) { var p = vd.GetVector3(v, 3); return new V3(p.X, p.Y, p.Z); }
                System.Numerics.Vector2 T(int v) { var p = vd.GetVector2(v, 6); return new(p.X, p.Y); }
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                    tris.Add(new Tri(P(a), P(b), P(c), N(a), N(b), N(c), T(a), T(b), T(c), tex, V3.One));
                }
            }

        // Mannequin de 1,80 m à droite du prop.
        float maxX = tris.Count > 0 ? tris.Max(t => MathF.Max(t.A.X, MathF.Max(t.B.X, t.C.X))) : 0;
        float px0 = maxX + 0.45f;
        var skin = new V3(0.55f, 0.62f, 0.75f);
        void Box(float cx, float cy, float z0, float sx, float sy, float sz)
        {
            var mn = new V3(px0 + cx - sx / 2, cy - sy / 2, z0); var mx = new V3(px0 + cx + sx / 2, cy + sy / 2, z0 + sz);
            V3[] c = { new(mn.X,mn.Y,mn.Z), new(mx.X,mn.Y,mn.Z), new(mx.X,mx.Y,mn.Z), new(mn.X,mx.Y,mn.Z), new(mn.X,mn.Y,mx.Z), new(mx.X,mn.Y,mx.Z), new(mx.X,mx.Y,mx.Z), new(mn.X,mx.Y,mx.Z) };
            int[][] f = { new[]{0,1,5,4}, new[]{1,2,6,5}, new[]{2,3,7,6}, new[]{3,0,4,7}, new[]{4,5,6,7}, new[]{3,2,1,0} };
            foreach (var q in f)
            {
                var n = V3.Normalize(V3.Cross(c[q[1]] - c[q[0]], c[q[2]] - c[q[0]]));
                tris.Add(new Tri(c[q[0]], c[q[1]], c[q[2]], n, n, n, default, default, default, null, skin));
                tris.Add(new Tri(c[q[0]], c[q[2]], c[q[3]], n, n, n, default, default, default, null, skin));
            }
        }
        Box(-0.1f, 0, 0, 0.15f, 0.17f, 0.86f); Box(0.1f, 0, 0, 0.15f, 0.17f, 0.86f);
        Box(0, 0, 0.86f, 0.44f, 0.24f, 0.6f);
        Box(-0.28f, 0, 0.85f, 0.1f, 0.12f, 0.6f); Box(0.28f, 0, 0.85f, 0.1f, 0.12f, 0.6f);
        Box(0, 0, 1.46f, 0.1f, 0.1f, 0.08f); Box(0, 0.01f, 1.54f, 0.2f, 0.23f, 0.26f);

        // Caméra orthographique : de face (-Y), un peu de côté et en plongée.
        float yaw = 25f * MathF.PI / 180, pitch = 15f * MathF.PI / 180;
        var fwd = V3.Normalize(new V3(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Cos(yaw) * MathF.Cos(pitch), -MathF.Sin(pitch)));
        var right = V3.Normalize(V3.Cross(fwd, V3.UnitZ));
        var up = V3.Cross(right, fwd);
        var light = V3.Normalize(-fwd + up * 0.8f - right * 0.5f);

        var all = tris.SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
        all.Add(new V3(all.Min(p => p.X) - 0.3f, -0.5f, 0)); all.Add(new V3(all.Max(p => p.X) + 0.3f, 0.5f, 0));
        float sxMin = all.Min(p => V3.Dot(p, right)), sxMax = all.Max(p => V3.Dot(p, right));
        float syMin = all.Min(p => V3.Dot(p, up)), syMax = all.Max(p => V3.Dot(p, up));
        int W = 900, H = 700;
        float scale = MathF.Min((W - 60) / (sxMax - sxMin), (H - 60) / (syMax - syMin));
        float ox = (W - (sxMax - sxMin) * scale) / 2, oy = (H - (syMax - syMin) * scale) / 2;
        (float x, float y, float z) Proj(V3 p) => (ox + (V3.Dot(p, right) - sxMin) * scale, H - (oy + (V3.Dot(p, up) - syMin) * scale), V3.Dot(p, fwd));

        var img = new byte[W * H * 4];
        var zb = new float[W * H];
        Array.Fill(zb, float.MaxValue);
        for (int i = 0; i < W * H; i++) { img[i * 4] = 236; img[i * 4 + 1] = 238; img[i * 4 + 2] = 242; img[i * 4 + 3] = 255; }

        // Grille au sol tous les 0,5 m.
        for (float gx = MathF.Floor(all.Min(p => p.X) * 2) / 2; gx <= all.Max(p => p.X); gx += 0.5f) Line(new V3(gx, -1, 0), new V3(gx, 1, 0));
        for (float gy = -1; gy <= 1.001f; gy += 0.5f) Line(new V3(all.Min(p => p.X), gy, 0), new V3(all.Max(p => p.X), gy, 0));
        void Line(V3 a, V3 b)
        {
            var (x0, y0, _) = Proj(a); var (x1, y1, _) = Proj(b);
            int n = (int)MathF.Max(MathF.Abs(x1 - x0), MathF.Abs(y1 - y0)) + 1;
            for (int k = 0; k <= n; k++)
            {
                int x = (int)(x0 + (x1 - x0) * k / n), y = (int)(y0 + (y1 - y0) * k / n);
                if (x < 0 || y < 0 || x >= W || y >= H) continue;
                int o = (y * W + x) * 4; img[o] = 200; img[o + 1] = 203; img[o + 2] = 210;
            }
        }

        foreach (var t in tris)
        {
            var a = Proj(t.A); var b = Proj(t.B); var c = Proj(t.C);
            float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
            if (MathF.Abs(area) < 1e-9f) continue;
            int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.x, MathF.Min(b.x, c.x))));
            int maxXp = Math.Min(W - 1, (int)MathF.Ceiling(MathF.Max(a.x, MathF.Max(b.x, c.x))));
            int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.y, MathF.Min(b.y, c.y))));
            int maxY = Math.Min(H - 1, (int)MathF.Ceiling(MathF.Max(a.y, MathF.Max(b.y, c.y))));
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxXp; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float w0 = ((b.x - px) * (c.y - py) - (c.x - px) * (b.y - py)) / area;
                    float w1 = ((c.x - px) * (a.y - py) - (a.x - px) * (c.y - py)) / area;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float z = a.z * w0 + b.z * w1 + c.z * w2;
                    int zi = y * W + x;
                    if (z >= zb[zi]) continue;
                    var n = V3.Normalize(t.Na * w0 + t.Nb * w1 + t.Nc * w2);
                    if (V3.Dot(n, fwd) > 0) n = -n; // double face
                    float lit = 0.35f + 0.75f * MathF.Max(0, V3.Dot(n, light));
                    V3 col = t.Col;
                    if (t.T != null)
                    {
                        var uv = t.Ta * w0 + t.Tb * w1 + t.Tc * w2;
                        float u = uv.X - MathF.Floor(uv.X), v = uv.Y - MathF.Floor(uv.Y);
                        int tx = Math.Clamp((int)(u * t.T.W), 0, t.T.W - 1), ty = Math.Clamp((int)(v * t.T.H), 0, t.T.H - 1);
                        int to = (ty * t.T.W + tx) * 4;
                        if (t.T.Px[to + 3] < 128) continue; // cutout
                        col = new V3(t.T.Px[to], t.T.Px[to + 1], t.T.Px[to + 2]) / 255f;
                    }
                    zb[zi] = z;
                    int o = zi * 4;
                    img[o] = (byte)Math.Clamp(col.X * lit * 255, 0, 255);
                    img[o + 1] = (byte)Math.Clamp(col.Y * lit * 255, 0, 255);
                    img[o + 2] = (byte)Math.Clamp(col.Z * lit * 255, 0, 255);
                }
        }
        Png.Write(outPng, W, H, img);
        Console.WriteLine($"Rendu : {outPng} ({tris.Count} triangles)");
    }
}
