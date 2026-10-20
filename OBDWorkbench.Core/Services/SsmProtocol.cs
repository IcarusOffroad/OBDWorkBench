using System.Globalization;

namespace OBDWorkbench.Services;

public sealed class SsmFrame
{
    public required byte[] Raw { get; init; }
    public byte Destination => Raw[1];
    public byte Source => Raw[2];
    public byte DataLength => Raw[3];
    public byte[] Data => Raw.Skip(4).Take(DataLength).ToArray();
    public byte Checksum => Raw[^1];
    public bool ChecksumOk => SsmProtocol.Checksum(Raw.AsSpan(0, Raw.Length - 1)) == Checksum;
    public override string ToString() => SsmProtocol.Hex(Raw);
}

public static class SsmProtocol
{
    public const byte EcuAddress = 0x10;
    public const byte TesterAddress = 0xF0;

    // Candidate addresses for the KOEO/live test set. MainForm filters this list
    // against the BF capability flags before polling, so unsupported parameters
    // are displayed as N/A and do not consume request bytes.
    public static readonly int[] CandidateAddresses =
    {
        0x000008, // coolant
        0x00000D, // manifold absolute pressure
        0x00000E, // RPM high
        0x00000F, // RPM low
        0x000010, // speed
        0x000012, // intake air temp
        0x000013, // MAF high (only if BF says supported)
        0x000014, // MAF low  (only if BF says supported)
        0x000015, // throttle opening
        0x00001C, // battery voltage
        0x00001E, // throttle sensor voltage
        0x000023, // atmospheric pressure
        0x000024  // manifold relative pressure
    };

    // BF positive-response capability bytes begin at data[9]. RomRaider/SSM
    // definitions number these flag bytes starting at 1 and bits starting at 1
    // with bit 1 = LSB and bit 8 = MSB.
    public static bool CapabilitySupported(byte[] initData, int flagByte, int bit)
    {
        if (flagByte < 1 || bit < 1 || bit > 8) return false;
        int index = 8 + flagByte;
        if (index < 0 || index >= initData.Length) return false;
        int mask = 1 << (bit - 1);
        return (initData[index] & mask) != 0;
    }

    public static byte[] BuildInitRequest() => BuildPacket(new byte[] { 0xBF });

    public static byte[] BuildA8Request(IEnumerable<int> addresses)
    {
        var data = new List<byte> { 0xA8, 0x00 };
        foreach (var address in addresses)
        {
            if (address < 0 || address > 0xFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(address), $"SSM address 0x{address:X} is outside 24-bit range.");

            data.Add((byte)((address >> 16) & 0xFF));
            data.Add((byte)((address >> 8) & 0xFF));
            data.Add((byte)(address & 0xFF));
        }
        return BuildPacket(data.ToArray());
    }

    public static byte[] BuildPacket(byte[] data)
    {
        if (data.Length > 255)
            throw new ArgumentException("SSM2 data payload cannot exceed 255 bytes.", nameof(data));

        var packet = new byte[5 + data.Length];
        packet[0] = 0x80;
        packet[1] = EcuAddress;
        packet[2] = TesterAddress;
        packet[3] = (byte)data.Length;
        Buffer.BlockCopy(data, 0, packet, 4, data.Length);
        packet[^1] = Checksum(packet.AsSpan(0, packet.Length - 1));
        return packet;
    }

    public static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        int sum = 0;
        foreach (byte b in bytes) sum = (sum + b) & 0xFF;
        return (byte)sum;
    }

    public static IReadOnlyList<SsmFrame> ExtractFrames(IReadOnlyList<byte> stream)
    {
        var frames = new List<SsmFrame>();
        int i = 0;
        while (i <= stream.Count - 5)
        {
            if (stream[i] != 0x80)
            {
                i++;
                continue;
            }

            int totalLength = 5 + stream[i + 3];
            if (totalLength < 6 || i + totalLength > stream.Count)
            {
                i++;
                continue;
            }

            var raw = new byte[totalLength];
            for (int j = 0; j < totalLength; j++) raw[j] = stream[i + j];
            var frame = new SsmFrame { Raw = raw };
            if (frame.ChecksumOk)
            {
                frames.Add(frame);
                i += totalLength;
            }
            else
            {
                i++;
            }
        }
        return frames;
    }

    public static SsmFrame? FindEcuResponse(IReadOnlyList<byte> stream)
        => ExtractFrames(stream).FirstOrDefault(f => f.Destination == TesterAddress && f.Source == EcuAddress);

    public static bool IsEcho(SsmFrame frame)
        => frame.Destination == EcuAddress && frame.Source == TesterAddress;

    public static string Hex(IEnumerable<byte> bytes)
        => string.Join(" ", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));


    public static string SelfTest()
    {
        var init = BuildInitRequest();
        var expectedInit = new byte[] { 0x80, 0x10, 0xF0, 0x01, 0xBF, 0x40 };
        if (!init.SequenceEqual(expectedInit))
            throw new InvalidOperationException($"BF builder self-test failed: {Hex(init)}");

        var a8 = BuildA8Request(new[] { 0x000008, 0x00001C });
        var expectedA8 = new byte[] { 0x80, 0x10, 0xF0, 0x08, 0xA8, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x1C, 0x54 };
        if (!a8.SequenceEqual(expectedA8))
            throw new InvalidOperationException($"A8 builder self-test failed: {Hex(a8)}");

        var exampleResponse = new byte[] { 0x80, 0xF0, 0x10, 0x03, 0xE8, 0x7D, 0xB1, 0x99 };
        var mixed = new byte[] { 0x00 }.Concat(init).Concat(exampleResponse).ToArray();
        var parsed = FindEcuResponse(mixed);
        if (parsed == null || !parsed.ChecksumOk || parsed.Data.Length != 3 || parsed.Data[0] != 0xE8)
            throw new InvalidOperationException("RX parser/echo-filter self-test failed.");

        var values = DecodeA8Values(parsed, new[] { 0x000008, 0x00001C });
        if (values[0x000008] != 0x7D || values[0x00001C] != 0xB1)
            throw new InvalidOperationException("A8 value decoder self-test failed.");

        var capabilityExample = new byte[13];
        capabilityExample[9] = 0xFF;
        capabilityExample[10] = 0xEA;
        capabilityExample[11] = 0xAF;
        capabilityExample[12] = 0xA2;
        if (!CapabilitySupported(capabilityExample, 1, 2) ||
            CapabilitySupported(capabilityExample, 2, 5) ||
            !CapabilitySupported(capabilityExample, 3, 6) ||
            !CapabilitySupported(capabilityExample, 4, 8))
            throw new InvalidOperationException("BF capability decoder self-test failed.");

        return "PASS: BF builder, A8 builder, checksum/parser, echo filtering, A8 decoder, BF capability decoder";
    }

    public static Dictionary<int, byte> DecodeA8Values(SsmFrame response, IReadOnlyList<int> addresses)
    {
        var data = response.Data;
        if (data.Length < 1 || data[0] != 0xE8)
            throw new InvalidDataException($"Expected A8 positive response 0xE8; received data {Hex(data)}.");
        if (data.Length - 1 != addresses.Count)
            throw new InvalidDataException($"A8 response contains {data.Length - 1} value bytes for {addresses.Count} requested addresses.");

        var result = new Dictionary<int, byte>();
        for (int i = 0; i < addresses.Count; i++) result[addresses[i]] = data[i + 1];
        return result;
    }
}
