namespace OBDWorkbench.Services;

public interface ISessionDiagnosticLogger : IDisposable
{
    string? CurrentPath { get; }
    string Start(string folder);
    void Write(string text);
    void WriteLine(string text);
    string SnapshotText();
}
