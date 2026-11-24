namespace Glb2FiveM;

/// <summary>Langue des messages (français par défaut). Suit les tâches async : à régler avant de lancer la conversion.</summary>
public static class L
{
    static readonly AsyncLocal<bool> _en = new();

    public static bool English { get => _en.Value; set => _en.Value = value; }

    /// <summary>Renvoie le texte dans la langue courante.</summary>
    public static string T(string fr, string en) => English ? en : fr;
}
