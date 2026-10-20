using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace OBDWorkbench.Platform.Windows;

public sealed class WindowsSsmTransport : OBDWorkbench.Services.ISsmTransport
{
    private SafeFileHandle? _handle;
    private readonly object _sync = new();

    public bool IsOpen => _handle is { IsInvalid: false, IsClosed: false };
    public string PortName { get; private set; } = string.Empty;

    public static IReadOnlyList<string> GetPortNames()
    {
        var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key != null)
            {
                foreach (var name in key.GetValueNames())
                    if (key.GetValue(name) is string port && !string.IsNullOrWhiteSpace(port)) ports.Add(port);
            }
        }
        catch { }

        return ports.OrderBy(PortSortKey).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static int PortSortKey(string port)
    {
        if (port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(port[3..], out int n)) return n;
        return int.MaxValue;
    }

    public void Open(string portName, int baudRate = 4800)
    {
        lock (_sync)
        {
            Close();
            string devicePath = portName.StartsWith(@"\\.\", StringComparison.Ordinal) ? portName : @"\\.\" + portName;
            var handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open {portName}.");

            try
            {
                var dcb = new DCB { DCBlength = (uint)Marshal.SizeOf<DCB>() };
                if (!GetCommState(handle, ref dcb)) throw new Win32Exception(Marshal.GetLastWin32Error(), "GetCommState failed.");

                string config = $"baud={baudRate} parity=N data=8 stop=1";
                if (!BuildCommDCB(config, ref dcb)) throw new Win32Exception(Marshal.GetLastWin32Error(), "BuildCommDCB failed.");
                dcb.DCBlength = (uint)Marshal.SizeOf<DCB>();
                dcb.BaudRate = (uint)baudRate;
                dcb.ByteSize = 8;
                dcb.Parity = 0;
                dcb.StopBits = 0;
                // fBinary=1, no CTS/DSR/XON/XOFF flow control, DTR enabled, RTS enabled.
                dcb.Flags = 0x00001011;
                if (!SetCommState(handle, ref dcb)) throw new Win32Exception(Marshal.GetLastWin32Error(), "SetCommState failed.");

                if (!SetupComm(handle, 4096, 4096)) throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupComm failed.");

                var timeouts = new COMMTIMEOUTS
                {
                    ReadIntervalTimeout = 20,
                    ReadTotalTimeoutMultiplier = 0,
                    ReadTotalTimeoutConstant = 25,
                    WriteTotalTimeoutMultiplier = 0,
                    WriteTotalTimeoutConstant = 500
                };
                if (!SetCommTimeouts(handle, ref timeouts)) throw new Win32Exception(Marshal.GetLastWin32Error(), "SetCommTimeouts failed.");

                PurgeComm(handle, PURGE_RXABORT | PURGE_RXCLEAR | PURGE_TXABORT | PURGE_TXCLEAR);
                _handle = handle;
                PortName = portName;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
    }

    public void PurgeRx()
    {
        lock (_sync)
        {
            EnsureOpen();
            if (!PurgeComm(_handle!, PURGE_RXABORT | PURGE_RXCLEAR))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "PurgeComm RX failed.");
        }
    }

    public void Write(byte[] data)
    {
        lock (_sync)
        {
            EnsureOpen();
            if (!WriteFile(_handle!, data, (uint)data.Length, out uint written, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Serial WriteFile failed.");
            if (written != data.Length) throw new IOException($"Serial write incomplete: {written}/{data.Length} bytes.");
            FlushFileBuffers(_handle!);
        }
    }

    public byte[] ReadForResponse(int totalTimeoutMs, Func<IReadOnlyList<byte>, bool>? stopWhen = null)
    {
        lock (_sync)
        {
            EnsureOpen();
            var bytes = new List<byte>(256);
            var sw = Stopwatch.StartNew();
            var temp = new byte[256];
            long lastByteAt = -1;

            while (sw.ElapsedMilliseconds < totalTimeoutMs)
            {
                if (!ReadFile(_handle!, temp, (uint)temp.Length, out uint read, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Serial ReadFile failed.");

                if (read > 0)
                {
                    for (int i = 0; i < read; i++) bytes.Add(temp[i]);
                    lastByteAt = sw.ElapsedMilliseconds;
                    if (stopWhen != null && stopWhen(bytes)) break;
                }
                else
                {
                    if (lastByteAt >= 0 && sw.ElapsedMilliseconds - lastByteAt > 180 && stopWhen == null) break;
                    Thread.Sleep(2);
                }
            }

            return bytes.ToArray();
        }
    }

    public void Close()
    {
        lock (_sync)
        {
            if (_handle != null)
            {
                try { if (!_handle.IsInvalid && !_handle.IsClosed) PurgeComm(_handle, PURGE_RXABORT | PURGE_RXCLEAR | PURGE_TXABORT | PURGE_TXCLEAR); } catch { }
                _handle.Dispose();
                _handle = null;
            }
            PortName = string.Empty;
        }
    }

    private void EnsureOpen()
    {
        if (!IsOpen) throw new InvalidOperationException("Serial port is not open.");
    }

    public void Dispose() => Close();

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint PURGE_TXABORT = 0x0001;
    private const uint PURGE_RXABORT = 0x0002;
    private const uint PURGE_TXCLEAR = 0x0004;
    private const uint PURGE_RXCLEAR = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct DCB
    {
        public uint DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public sbyte XonChar;
        public sbyte XoffChar;
        public sbyte ErrorChar;
        public sbyte EofChar;
        public sbyte EvtChar;
        public ushort wReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COMMTIMEOUTS
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCommState(SafeFileHandle hFile, ref DCB lpDCB);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BuildCommDCB(string lpDef, ref DCB lpDCB);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommState(SafeFileHandle hFile, ref DCB lpDCB);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommTimeouts(SafeFileHandle hFile, ref COMMTIMEOUTS lpCommTimeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupComm(SafeFileHandle hFile, uint dwInQueue, uint dwOutQueue);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PurgeComm(SafeFileHandle hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle hFile);
}
