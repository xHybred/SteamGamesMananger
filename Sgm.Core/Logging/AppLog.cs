using System.Collections.Generic;
using System.IO;

namespace Sgm.Core.Logging;

/// <summary>
/// Lightweight application logger: bounded in-memory ring buffer, an optional
/// file writer, and a message event the UI can subscribe to.
/// </summary>
public static class AppLog
{
    private const int MaxBuffered = 2000;
    private static readonly object Gate = new();
    private static readonly Queue<string> Buffer = new();
    private static StreamWriter? _file;

    public static event Action<string>? Message;

    public static IReadOnlyList<string> Recent
    {
        get
        {
            lock (Gate)
                return Buffer.ToArray();
        }
    }

    public static void StartFile(string path)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            _file = new StreamWriter(path, append: true) { AutoFlush = true };
        }
        catch (IOException)
        {
            _file = null;
        }
        catch (UnauthorizedAccessException)
        {
            _file = null;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        lock (Gate)
        {
            Buffer.Enqueue(line);
            while (Buffer.Count > MaxBuffered)
                Buffer.Dequeue();
            try { _file?.WriteLine(line); }
            catch (IOException) { }
        }
        Message?.Invoke(line);
    }
}
