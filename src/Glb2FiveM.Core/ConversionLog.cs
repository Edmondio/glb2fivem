namespace Glb2FiveM;

public enum LogLevel { Info, Warning, Error }

public sealed record LogEntry(LogLevel Level, string Message);

/// <summary>Journal de conversion. L'interface graphique pourra s'abonner à <see cref="Logged"/>.</summary>
public sealed class ConversionLog
{
    public List<LogEntry> Entries { get; } = new();
    public event Action<LogEntry>? Logged;

    public void Info(string msg) => Add(LogLevel.Info, msg);
    public void Warn(string msg) => Add(LogLevel.Warning, msg);
    public void Error(string msg) => Add(LogLevel.Error, msg);

    void Add(LogLevel level, string msg)
    {
        var e = new LogEntry(level, msg);
        Entries.Add(e);
        Logged?.Invoke(e);
    }
}
