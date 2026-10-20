using System.Diagnostics;
using System.Runtime.InteropServices;
using OBDWorkbench.Services;
namespace OBDWorkbench.Platform.Windows;

public sealed class WindowsKLineTransport : IKLineTransport
    {
        private IntPtr _handle = new(-1);
        private string _portName = "";
        public bool IsOpen => _handle != new IntPtr(-1) && _handle != IntPtr.Zero;
        public string PortName => _portName;

        public void Open(string portName, int baud)
        {
            Close();
            string path = portName.StartsWith("\\\\.\\", StringComparison.Ordinal) ? portName : "\\\\.\\" + portName;
            _handle = Native.CreateFile(path, Native.GENERIC_READ | Native.GENERIC_WRITE, 0, IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
            if (!IsOpen)
            {
                int err = Marshal.GetLastWin32Error(); _handle = new IntPtr(-1);
                throw new IOException($"CreateFile failed for {portName}. Win32 error {err}.");
            }
            _portName = portName;
            Native.SetupComm(_handle, 4096, 4096);
            Native.COMMTIMEOUTS to = new() { ReadIntervalTimeout = 0xFFFFFFFF, WriteTotalTimeoutConstant = 1000 };
            Check(Native.SetCommTimeouts(_handle, ref to), "SetCommTimeouts");
            SetBaud(baud); PurgeAll();
        }

        private void SetBaud(int baud)
        {
            Native.DCB dcb = new() { DCBlength = (uint)Marshal.SizeOf<Native.DCB>() };
            Check(Native.GetCommState(_handle, ref dcb), "GetCommState");
            dcb.BaudRate = (uint)baud; dcb.Flags |= 0x1; dcb.Flags &= ~0x00000002u; dcb.Flags &= ~0x00000004u; dcb.Flags &= ~0x00000010u; dcb.Flags &= ~0x00000030u; dcb.Flags &= ~0x00000300u; dcb.Flags &= ~0x00003000u;
            dcb.ByteSize = 8; dcb.Parity = 0; dcb.StopBits = 0;
            Check(Native.SetCommState(_handle, ref dcb), "SetCommState");
        }

        private void PurgeAll() => Check(Native.PurgeComm(_handle, Native.PURGE_RXABORT | Native.PURGE_TXABORT | Native.PURGE_RXCLEAR | Native.PURGE_TXCLEAR), "PurgeComm");
        private void WriteExact(byte[] bytes)
        {
            Check(Native.WriteFile(_handle, bytes, (uint)bytes.Length, out uint written, IntPtr.Zero), "WriteFile");
            if (written != bytes.Length) throw new IOException($"WriteFile wrote {written} of {bytes.Length} bytes.");
        }

        private byte[] ReadAvailable()
        {
            Check(Native.ClearCommError(_handle, out _, out Native.COMSTAT stat), "ClearCommError");
            if (stat.cbInQue == 0) return Array.Empty<byte>();
            int count = (int)Math.Min(stat.cbInQue, 4096u); byte[] buf = new byte[count];
            Check(Native.ReadFile(_handle, buf, (uint)buf.Length, out uint got, IntPtr.Zero), "ReadFile");
            if (got == buf.Length) return buf;
            byte[] actual = new byte[(int)got]; Buffer.BlockCopy(buf, 0, actual, 0, (int)got); return actual;
        }

        public Td5IoResult FastInitBreak(byte[] frame, int idleMs, double lowMs, double highMs, int listenMs)
        {
            SetBaud(10400); PurgeAll(); if (idleMs > 0) Thread.Sleep(idleMs);
            Check(Native.SetCommBreak(_handle), "SetCommBreak"); SpinWaitMs(lowMs); Check(Native.ClearCommBreak(_handle), "ClearCommBreak"); SpinWaitMs(highMs);
            Stopwatch op = Stopwatch.StartNew(); WriteExact(frame); Check(Native.FlushFileBuffers(_handle), "FlushFileBuffers");
            return ReadFor(listenMs, op);
        }

        public Td5IoResult SendOnlyAdaptive(byte[] frame, int minListenMs, int maxListenMs, int quietMs)
        {
            PurgeAll(); Stopwatch op = Stopwatch.StartNew(); WriteExact(frame); Check(Native.FlushFileBuffers(_handle), "FlushFileBuffers");
            Td5IoResult result = new();
            bool ecuResponseStarted = false;
            double lastEcuRx = -1;
            while (op.Elapsed.TotalMilliseconds < maxListenMs)
            {
                byte[] bytes = ReadAvailable();
                if (bytes.Length > 0)
                {
                    double now = op.Elapsed.TotalMilliseconds;
                    result.RxChunks.Add(new Td5RxChunk { Ms = now, Bytes = bytes });
                    byte[] accumulated = Flatten(result.RxChunks);
                    if (HasResponseBeyondEcho(accumulated, frame))
                    {
                        ecuResponseStarted = true;
                        lastEcuRx = now;
                    }
                }
                double elapsed = op.Elapsed.TotalMilliseconds;
                // KKL adapters echo our transmitted frame immediately. That echo is not
                // an ECU response and must never start the quiet-tail countdown.
                if (elapsed >= minListenMs && ecuResponseStarted && lastEcuRx >= 0 && elapsed - lastEcuRx >= quietMs) break;
                Thread.Sleep(1);
            }
            result.Rx = Flatten(result.RxChunks); return result;
        }

        private static bool HasResponseBeyondEcho(byte[] rx, byte[] tx)
        {
            int echo = IndexOfSequence(rx, tx);
            if (echo >= 0) return rx.Length > echo + tx.Length;

            // The KKL echo can itself arrive in more than one USB/serial chunk. If the
            // bytes received so far are merely a prefix of our TX frame, keep waiting.
            if (rx.Length <= tx.Length)
            {
                bool prefix = true;
                for (int i = 0; i < rx.Length; i++)
                    if (rx[i] != tx[i]) { prefix = false; break; }
                if (prefix) return false;
            }
            return rx.Length > 0; // non-echoing interface or ECU traffic arrived first
        }

        private static int IndexOfSequence(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }

        private Td5IoResult ReadFor(int listenMs, Stopwatch op)
        {
            Td5IoResult result = new(); Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < listenMs)
            {
                byte[] bytes = ReadAvailable();
                if (bytes.Length > 0) result.RxChunks.Add(new Td5RxChunk { Ms = op.Elapsed.TotalMilliseconds, Bytes = bytes });
                Thread.Sleep(1);
            }
            result.Rx = Flatten(result.RxChunks); return result;
        }

        private static byte[] Flatten(List<Td5RxChunk> chunks)
        {
            int total = 0; foreach (Td5RxChunk c in chunks) total += c.Bytes.Length;
            byte[] all = new byte[total]; int offset = 0;
            foreach (Td5RxChunk c in chunks) { Buffer.BlockCopy(c.Bytes, 0, all, offset, c.Bytes.Length); offset += c.Bytes.Length; }
            return all;
        }

        private static void SpinWaitMs(double ms)
        {
            long start = Stopwatch.GetTimestamp(); double ticks = ms * Stopwatch.Frequency / 1000.0;
            while (Stopwatch.GetTimestamp() - start < ticks) Thread.SpinWait(50);
        }

        private static void Check(bool ok, string operation)
        {
            if (!ok) throw new IOException($"{operation} failed. Win32 error {Marshal.GetLastWin32Error()}.");
        }

        public void Close()
        {
            if (IsOpen) { try { Native.ClearCommBreak(_handle); } catch { } Native.CloseHandle(_handle); }
            _handle = new IntPtr(-1); _portName = "";
        }
        public void Dispose() => Close();
        private static class Native
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, OPEN_EXISTING = 3, FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint PURGE_TXABORT = 0x0001, PURGE_RXABORT = 0x0002, PURGE_TXCLEAR = 0x0004, PURGE_RXCLEAR = 0x0008;
        [StructLayout(LayoutKind.Sequential)] public struct DCB { public uint DCBlength, BaudRate, Flags; public ushort wReserved, XonLim, XoffLim; public byte ByteSize, Parity, StopBits; public sbyte XonChar, XoffChar, ErrorChar, EofChar, EvtChar; public ushort wReserved1; }
        [StructLayout(LayoutKind.Sequential)] public struct COMMTIMEOUTS { public uint ReadIntervalTimeout, ReadTotalTimeoutMultiplier, ReadTotalTimeoutConstant, WriteTotalTimeoutMultiplier, WriteTotalTimeoutConstant; }
        [StructLayout(LayoutKind.Sequential)] public struct COMSTAT { public uint Flags, cbInQue, cbOutQue; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetCommState(IntPtr hFile, ref DCB lpDCB);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetCommState(IntPtr hFile, ref DCB lpDCB);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetCommTimeouts(IntPtr hFile, ref COMMTIMEOUTS lpCommTimeouts);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetupComm(IntPtr hFile, uint dwInQueue, uint dwOutQueue);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool PurgeComm(IntPtr hFile, uint dwFlags);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetCommBreak(IntPtr hFile);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool ClearCommBreak(IntPtr hFile);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FlushFileBuffers(IntPtr hFile);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool ClearCommError(IntPtr hFile, out uint lpErrors, out COMSTAT lpStat);
    }

}
