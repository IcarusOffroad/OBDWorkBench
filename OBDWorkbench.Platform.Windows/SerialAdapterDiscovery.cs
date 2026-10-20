using Microsoft.Win32;
namespace OBDWorkbench.Services;
public static class SerialAdapterDiscovery
{
    private static Dictionary<string, string> ReadDescriptions()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum");
            if (root is not null) Visit(root, 0, result);
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        return result;
    }
    private static void Visit(RegistryKey key, int depth, Dictionary<string, string> result)
    {
        if (depth > 3) return;
        try
        {
            using var parameters = key.OpenSubKey("Device Parameters");
            if (parameters?.GetValue("PortName") is string port)
                result[port] = $"{key.GetValue("FriendlyName")} {key.GetValue("DeviceDesc")} {key.Name}";
            foreach (var name in key.GetSubKeyNames())
            {
                if (name == "Device Parameters") continue;
                using var child = key.OpenSubKey(name);
                if (child is not null) Visit(child, depth + 1, result);
            }
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        catch (System.IO.IOException) { }
    }
    public static bool IsLikelyAdapter(string description) => new[] { "CH340", "CH341", "CH430", "USB-SERIAL", "USB Serial", "FTDI", "CP210", "Bluetooth", "BTHENUM", "ELM", "OBD" }.Any(x => description.Contains(x, StringComparison.OrdinalIgnoreCase));
    public static string Description(string port)
    {
        var descriptions = ReadDescriptions();
        return descriptions.TryGetValue(port, out var text) ? text.Split("HKEY_")[0].Trim() : "serial port";
    }
    public static IReadOnlyList<string> Candidates(IEnumerable<string> available, string preferred)
    {
        var descriptions = ReadDescriptions();
        return new[] { preferred }.Concat(available.Where(x => descriptions.TryGetValue(x, out var text) && IsLikelyAdapter(text)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
