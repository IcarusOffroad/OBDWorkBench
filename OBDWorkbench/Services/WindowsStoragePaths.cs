namespace OBDWorkbench.Services;
internal static class WindowsStoragePaths
{
    public static string Root => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OBDWorkbench");
    public static string Settings(string fileName = "settings.json") => System.IO.Path.Combine(Root, fileName);
    public static string ReplayCache => System.IO.Path.Combine(Root, "ReplayCache");
}
