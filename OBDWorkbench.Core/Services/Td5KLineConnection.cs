using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OBDWorkbench.Services;

public sealed class Td5PidReadResult
{
    public byte Pid { get; init; }
    public byte[] Frame { get; init; } = Array.Empty<byte>();
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public string RawHex { get; init; } = "";
    public double RttMs { get; init; }
    public double FirstByteMs { get; init; }
    public double TailMs { get; init; }
    public bool Retried { get; init; }
}


public sealed class Td5CommandResult
{
    public byte[] Request { get; init; } = Array.Empty<byte>();
    public byte[] ResponseFrame { get; init; } = Array.Empty<byte>();
    public string RawHex { get; init; } = "";
    public bool Positive { get; init; }
}

public sealed class Td5KLineConnection : IDisposable
{
    private readonly IKLineTransport _port;
    public Td5KLineConnection(IKLineTransport transport) => _port = transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastValidResponseUtc = DateTime.MinValue;
    public bool IsOpen => _port.IsOpen;
    public bool IsAuthenticated { get; private set; }
    public string PortName => _port.PortName;
    public string TransportDescription => IsOpen ? $"Raw K-Line {PortName} @ 10400 8N1" : "Raw K-Line closed";

    public async Task OpenAndAuthenticateAsync(string portName, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            CloseInternal();
            _port.Open(portName, 10400);
            await Task.Run(AuthenticateInternal, token).ConfigureAwait(false);
        }
        catch
        {
            CloseInternal();
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task EnsureAuthenticatedAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!IsOpen) throw new IOException("TD5 raw K-Line port is not open.");
            // Pre-operation validation must test the ECU now. A recent response timestamp
            // alone cannot prove that the diagnostic session survived a key cycle or timeout.
            if (IsAuthenticated && ProbeHealthInternal()) return;
            IsAuthenticated = false;
            AuthenticateInternal();
        }
        finally { _gate.Release(); }
    }

    public async Task RecoverSessionAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!IsOpen) throw new IOException("TD5 raw K-Line port is not open.");

            // A recovery request must never be satisfied merely because a response was
            // received recently. Probe the ECU now. If that fails, reopen the native
            // handle before issuing another timing-critical fast-init.
            if (IsAuthenticated && ProbeHealthInternal()) return;

            string portName = _port.PortName;
            CloseInternal();
            await Task.Delay(80, token).ConfigureAwait(false);
            _port.Open(portName, 10400);
            await Task.Run(AuthenticateInternal, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<Td5PidReadResult> ReadPidAsync(byte pid, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!IsOpen) throw new IOException("TD5 raw K-Line port is not open.");
            if (!IsAuthenticated)
                AuthenticateInternal();
            return await Task.Run(() => ReadPidInternal(pid), token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> ProbeHealthAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(ProbeHealthInternal, token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<Td5CommandResult> ClearFaultMemoryOnceAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!IsOpen) throw new IOException("TD5 raw K-Line port is not open.");
            if (!IsAuthenticated || !ProbeHealthInternal())
            {
                IsAuthenticated = false;
                AuthenticateInternal();
            }

            // Published TD5 routine: LEN=0x14, RoutineControl 0x31 / routine 0xDD,
            // followed by eighteen zero argument bytes. The final additive checksum is 0x22.
            // This write is deliberately sent ONCE. A lost acknowledgement must never
            // trigger an automatic retry because the clear may already have succeeded.
            byte[] request = new byte[22];
            request[0] = 0x14;
            request[1] = 0x31;
            request[2] = 0xDD;
            request[^1] = AdditiveChecksum(request.Take(request.Length - 1).ToArray());

            Td5IoResult io = _port.SendOnlyAdaptive(request, 25, 500, 35);
            if (TryFindProtocolFrame(io.Rx, 0x71, out byte[] positive, out _))
            {
                _lastValidResponseUtc = DateTime.UtcNow;
                return new Td5CommandResult { Request = request, ResponseFrame = positive, RawHex = Hex(io.Rx), Positive = true };
            }
            if (TryFindNegativeResponse(io.Rx, 0x31, out byte[] negative, out _))
            {
                _lastValidResponseUtc = DateTime.UtcNow;
                return new Td5CommandResult { Request = request, ResponseFrame = negative, RawHex = Hex(io.Rx), Positive = false };
            }
            return new Td5CommandResult { Request = request, RawHex = Hex(io.Rx), Positive = false };
        }
        finally { _gate.Release(); }
    }


    public void Close()
    {
        _gate.Wait();
        try { CloseInternal(); }
        finally { _gate.Release(); }
    }

    private void CloseInternal()
    {
        IsAuthenticated = false;
        _lastValidResponseUtc = DateTime.MinValue;
        _port.Close();
    }

    private void AuthenticateInternal()
    {
        if (!KeyAlgorithmSelfTest()) throw new InvalidOperationException("TD5 security key self-test failed.");
        byte[] init = { 0x81, 0x13, 0xF7, 0x81, 0x0C };

        byte[]? lastInitRx = null;
        bool started = false;
        for (int attempt = 1; attempt <= 4 && !started; attempt++)
        {
            Td5IoResult initResult = _port.FastInitBreak(init, attempt == 1 ? 350 : 550, 25.0, 25.0, 550);
            lastInitRx = initResult.Rx;
            if (TryFindProtocolFrame(initResult.Rx, 0xC1, out _, out _))
            {
                started = true;
                break;
            }

            // 7F 81 xx is still a checksum-valid answer from a TD5 ECU. In practice
            // NRC 0x10 appears when the ECU still considers a previous diagnostic
            // communication/session active. Do not mistake that for a wrong COM port.
            if (TryFindNegativeResponse(initResult.Rx, 0x81, out _, out _))
            {
                if (TryResumeExistingSessionInternal())
                {
                    IsAuthenticated = true;
                    _lastValidResponseUtc = DateTime.UtcNow;
                    return;
                }
                if (attempt < 4) Thread.Sleep(450);
                continue;
            }

            break;
        }

        if (!started)
            throw new IOException("TD5 ECU did not enter StartCommunication after retries. RX=" + Hex(lastInitRx));

        byte[] session = SendExpect(new byte[] { 0x02, 0x10, 0xA0, 0xB2 }, 0x50, 400);
        if (session.Length < 3)
            throw new IOException("TD5 StartDiagnosticSession returned an unexpected response.");

        byte[] seedFrame = SendExpect(new byte[] { 0x02, 0x27, 0x01, 0x2A }, 0x67, 400);
        if (seedFrame.Length < 6 || seedFrame[2] != 0x01)
            throw new IOException("TD5 SecurityAccess seed response had an unexpected layout: " + Hex(seedFrame));
        ushort seed = (ushort)((seedFrame[3] << 8) | seedFrame[4]);
        if (seed != 0)
        {
            ushort key = CalculateTd5Key(seed);
            byte[] keyFrame = BuildSendKeyFrame(key);
            byte[] keyResponse = SendExpect(keyFrame, 0x67, 400);
            if (keyResponse.Length < 4 || keyResponse[2] != 0x02)
                throw new IOException("TD5 SecurityAccess key response had an unexpected layout: " + Hex(keyResponse));
        }
        IsAuthenticated = true;
        _lastValidResponseUtc = DateTime.UtcNow;

        // Confirm that the native Service 0x21 data path is alive before handing the
        // connection to OBDWorkbench auto-discovery.
        _ = ReadPidInternal(0x09);
    }

    private bool TryResumeExistingSessionInternal()
    {
        try
        {
            byte[] tx = BuildPidRequest(0x09);
            Td5IoResult io = _port.SendOnlyAdaptive(tx, 25, 260, 30);
            if (!TryFindPidResponse(io.Rx, 0x09, out _, out _)) return false;
            _lastValidResponseUtc = DateTime.UtcNow;
            return true;
        }
        catch { return false; }
    }

    private byte[] SendExpect(byte[] request, byte positiveService, int maxListenMs)
    {
        Thread.Sleep(200);
        Td5IoResult result = _port.SendOnlyAdaptive(request, 25, maxListenMs, 30);
        if (TryFindProtocolFrame(result.Rx, positiveService, out byte[] positive, out _))
        {
            _lastValidResponseUtc = DateTime.UtcNow;
            return positive;
        }
        if (TryFindNegativeResponse(result.Rx, request.Length > 1 ? request[1] : (byte)0, out byte[] negative, out _))
        {
            _lastValidResponseUtc = DateTime.UtcNow;
            throw new IOException("TD5 ECU returned negative response " + Hex(negative));
        }
        throw new IOException("TD5 ECU did not answer request " + Hex(request) + ". RX=" + Hex(result.Rx));
    }

    private bool ProbeHealthInternal()
    {
        if (!IsOpen || !IsAuthenticated) return false;
        try
        {
            Td5PidReadResult r = ReadPidInternal(0x09);
            return r.Frame.Length > 0;
        }
        catch { return false; }
    }

    private Td5PidReadResult ReadPidInternal(byte pid)
    {
        byte[] tx = BuildPidRequest(pid);
        int max = pid == 0x3B ? 380 : pid == 0x43 ? 250 : 210;
        Td5IoResult first = _port.SendOnlyAdaptive(tx, 25, max, 30);
        if (TryFindPidResponse(first.Rx, pid, out byte[] frame, out _))
        {
            _lastValidResponseUtc = DateTime.UtcNow;
            return MakePidResult(pid, frame, first, false);
        }
        if (TryFindNegativeResponse(first.Rx, 0x21, out byte[] neg, out _))
        {
            _lastValidResponseUtc = DateTime.UtcNow;
            throw new IOException($"TD5 PID {pid:X2} negative response {Hex(neg)}");
        }

        Thread.Sleep(pid == 0x3B || pid == 0x43 ? 50 : 25);
        Td5IoResult second = _port.SendOnlyAdaptive(tx, 25, pid == 0x3B ? 420 : pid == 0x43 ? 280 : 210, 30);
        if (TryFindPidResponse(second.Rx, pid, out frame, out _))
        {
            _lastValidResponseUtc = DateTime.UtcNow;
            return MakePidResult(pid, frame, second, true);
        }
        if (TryFindNegativeResponse(second.Rx, 0x21, out neg, out _))
        {
            _lastValidResponseUtc = DateTime.UtcNow;
            throw new IOException($"TD5 PID {pid:X2} negative response {Hex(neg)}");
        }
        throw new IOException($"TD5 PID {pid:X2} received echo/no valid ECU response. first={Hex(first.Rx)} retry={Hex(second.Rx)}");
    }

    private static Td5PidReadResult MakePidResult(byte pid, byte[] frame, Td5IoResult io, bool retried)
    {
        byte[] data = FrameDataBytes(frame);
        double first = io.RxChunks.Count > 0 ? io.RxChunks[0].Ms : 0;
        double last = io.RxChunks.Count > 0 ? io.RxChunks[^1].Ms : first;
        return new Td5PidReadResult
        {
            Pid = pid,
            Frame = frame,
            Data = data,
            RawHex = Hex(frame),
            FirstByteMs = first,
            RttMs = last,
            TailMs = Math.Max(0, last - first),
            Retried = retried
        };
    }

    public static byte[] FrameDataBytes(byte[] frame)
    {
        if (frame is null || frame.Length <= 4) return Array.Empty<byte>();
        int len = frame.Length - 4;
        if (len <= 0) return Array.Empty<byte>();
        byte[] data = new byte[len];
        Buffer.BlockCopy(frame, 3, data, 0, len);
        return data;
    }

    public static ushort ReadBe16(byte[] data, int index) => (ushort)((data[index] << 8) | data[index + 1]);
    public static short ReadBeS16(byte[] data, int index) => unchecked((short)((data[index] << 8) | data[index + 1]));
    public static string Hex(byte[]? bytes) => bytes is null || bytes.Length == 0 ? "<none>" : string.Join(" ", Array.ConvertAll(bytes, b => b.ToString("X2")));

    private static byte[] BuildPidRequest(byte pid)
    {
        byte[] prefix = { 0x02, 0x21, pid };
        return new byte[] { prefix[0], prefix[1], prefix[2], AdditiveChecksum(prefix) };
    }

    private static byte AdditiveChecksum(params byte[] bytes)
    {
        int sum = 0;
        foreach (byte b in bytes) sum = (sum + b) & 0xFF;
        return (byte)sum;
    }

    private static byte[] BuildSendKeyFrame(ushort key)
    {
        byte[] prefix = { 0x04, 0x27, 0x02, (byte)(key >> 8), (byte)key };
        return new byte[] { prefix[0], prefix[1], prefix[2], prefix[3], prefix[4], AdditiveChecksum(prefix) };
    }

    private static ushort CalculateTd5Key(ushort seed)
    {
        ushort tmp = 0;
        int count = (((seed >> 0xC) & 0x8) | ((seed >> 0x5) & 0x4) | ((seed >> 0x3) & 0x2) | (seed & 0x1)) + 1;
        for (int idx = 0; idx < count; idx++)
        {
            int tap = ((seed >> 1) ^ (seed >> 2) ^ (seed >> 8) ^ (seed >> 9)) & 1;
            tmp = (ushort)((seed >> 1) | (tap << 0xF));
            seed = ((((seed >> 3) & 1) != 0) && (((seed >> 13) & 1) != 0)) ? (ushort)(tmp & 0xFFFE) : (ushort)(tmp | 1);
        }
        return seed;
    }

    private static bool KeyAlgorithmSelfTest() => CalculateTd5Key(0x34A5) == 0x54D3;

    private static bool ChecksumValid(byte[] frame)
    {
        if (frame.Length < 3) return false;
        int sum = 0;
        for (int i = 0; i < frame.Length - 1; i++) sum = (sum + frame[i]) & 0xFF;
        return (byte)sum == frame[^1];
    }

    private static bool TryFindProtocolFrame(byte[] rx, byte service, out byte[] frame, out int offset)
    {
        frame = Array.Empty<byte>(); offset = -1;
        for (int i = 0; i + 2 < rx.Length; i++)
        {
            int total = rx[i] + 2;
            if (total < 3 || i + total > rx.Length || rx[i + 1] != service) continue;
            byte[] candidate = new byte[total]; Buffer.BlockCopy(rx, i, candidate, 0, total);
            if (!ChecksumValid(candidate)) continue;
            frame = candidate; offset = i; return true;
        }
        return false;
    }

    private static bool TryFindPidResponse(byte[] rx, byte pid, out byte[] frame, out int offset)
    {
        frame = Array.Empty<byte>(); offset = -1;
        for (int i = 0; i + 3 < rx.Length; i++)
        {
            int total = rx[i] + 2;
            if (total < 5 || i + total > rx.Length || rx[i + 1] != 0x61 || rx[i + 2] != pid) continue;
            byte[] candidate = new byte[total]; Buffer.BlockCopy(rx, i, candidate, 0, total);
            if (!ChecksumValid(candidate)) continue;
            frame = candidate; offset = i; return true;
        }
        return false;
    }

    private static bool TryFindNegativeResponse(byte[] rx, byte requestedService, out byte[] frame, out int offset)
    {
        frame = Array.Empty<byte>(); offset = -1;
        for (int i = 0; i + 4 < rx.Length; i++)
        {
            int total = rx[i] + 2;
            if (total < 5 || i + total > rx.Length || rx[i + 1] != 0x7F || rx[i + 2] != requestedService) continue;
            byte[] candidate = new byte[total]; Buffer.BlockCopy(rx, i, candidate, 0, total);
            if (!ChecksumValid(candidate)) continue;
            frame = candidate; offset = i; return true;
        }
        return false;
    }

    public void Dispose()
    {
        Close();
        _port.Dispose();
        _gate.Dispose();
    }

}
