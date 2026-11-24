using System.Globalization;
using System.Numerics;
using System.Text;

namespace Glb2FiveM.Gta;

/// <summary>Petits utilitaires d'écriture XML au format attendu par CodeWalker.</summary>
static class XmlFmt
{
    public static string F(float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) v = 0;
        return v.ToString("0.#######", CultureInfo.InvariantCulture);
    }

    public static string V3(Vector3 v) => $"x=\"{F(v.X)}\" y=\"{F(v.Y)}\" z=\"{F(v.Z)}\"";
    public static string V4(Vector4 v) => $"x=\"{F(v.X)}\" y=\"{F(v.Y)}\" z=\"{F(v.Z)}\" w=\"{F(v.W)}\"";

    public static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    public static void Line(StringBuilder sb, int indent, string text)
    {
        sb.Append(' ', indent * 2).Append(text).Append('\n');
    }
}
