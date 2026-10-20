using System.IO;
using System.Text;

namespace OBDWorkbench.Services;

public sealed class SessionDiagnosticLogger : ISessionDiagnosticLogger
{
    private readonly object _sync = new();
    private StreamWriter? _writer;
    public string? CurrentPath { get; private set; }

    public string Start(string folder)
    {
        Dispose();
        Directory.CreateDirectory(folder);
        CurrentPath = Path.Combine(folder, $"Session_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log");
        var stream = new FileStream(CurrentPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        WriteLine("OBD Workbench v0.5.0.0 session log");
        WriteLine($"Started: {DateTime.Now:O}");
        WriteLine($"OS: {Environment.OSVersion}");
        WriteLine($".NET: {Environment.Version}");
        WriteLine("");
        return CurrentPath;
    }

    public void Write(string text)
    {
        lock (_sync)
        {
            _writer?.Write(text);
            _writer?.Flush();
        }
    }

    public void WriteLine(string text)
    {
        lock (_sync)
        {
            _writer?.WriteLine(text);
            _writer?.Flush();
        }
    }

    public string SnapshotText()
    {
        lock (_sync)
        {
            _writer?.Flush();
            if (string.IsNullOrWhiteSpace(CurrentPath) || !File.Exists(CurrentPath)) return "";
            try
            {
                using var stream = new FileStream(CurrentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch { return ""; }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_writer is not null)
            {
                try
                {
                    _writer.WriteLine();
                    _writer.WriteLine($"Ended: {DateTime.Now:O}");
                    _writer.Flush();
                }
                catch { }
                _writer.Dispose();
                _writer = null;
            }
        }
    }
}
