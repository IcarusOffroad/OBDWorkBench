using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OBDWorkbench.Models;

namespace OBDWorkbench.Services;

public sealed class ReplayCacheStore
{
    public const int FormatVersion = 2;
    public const int ChunkSeconds = 30;
    private static readonly long ChunkTicks = TimeSpan.FromSeconds(ChunkSeconds).Ticks;
    private readonly List<long> _chunkKeys;

    public string CacheDirectory { get; }
    public int ChunkCount => _chunkKeys.Count;
    public long CacheBytes => Directory.Exists(CacheDirectory)
        ? Directory.EnumerateFiles(CacheDirectory, "*", SearchOption.TopDirectoryOnly).Sum(x => new FileInfo(x).Length)
        : 0;

    internal ReplayCacheStore(string cacheDirectory, IEnumerable<long> chunkKeys)
    {
        CacheDirectory = cacheDirectory;
        _chunkKeys = chunkKeys.Distinct().OrderBy(x => x).ToList();
    }

    public List<ReplayRecord> ReadRange(DateTime startUtc, DateTime endUtc)
    {
        if (endUtc < startUtc) (startUtc, endUtc) = (endUtc, startUtc);
        long first = BucketKey(startUtc);
        long last = BucketKey(endUtc);
        var records = new List<ReplayRecord>();
        foreach (long key in _chunkKeys)
        {
            if (key < first) continue;
            if (key > last) break;
            foreach (ReplayRecord record in ReadChunk(key))
            {
                if (record.TimestampUtc >= startUtc && record.TimestampUtc <= endUtc)
                    records.Add(record);
            }
        }
        records.Sort(CompareRecords);
        return records;
    }

    public ReplayCheckpoint LoadCheckpoint(DateTime targetUtc)
    {
        if (_chunkKeys.Count == 0) return new ReplayCheckpoint { TimestampUtc = targetUtc };
        long targetKey = BucketKey(targetUtc);
        long chosen = _chunkKeys[0];
        foreach (long key in _chunkKeys)
        {
            if (key > targetKey) break;
            chosen = key;
        }
        string path = CheckpointPath(chosen);
        if (!File.Exists(path)) return new ReplayCheckpoint { TimestampUtc = BucketStart(chosen) };
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, false);
        return ReadCheckpoint(reader);
    }

    public DateTime? FindNextTimestamp(DateTime currentUtc)
    {
        long startKey = BucketKey(currentUtc);
        foreach (long key in _chunkKeys)
        {
            if (key < startKey) continue;
            DateTime? found = ReadChunk(key).Where(x => x.TimestampUtc > currentUtc.AddTicks(1)).Select(x => (DateTime?)x.TimestampUtc).Min();
            if (found.HasValue) return found.Value;
        }
        return null;
    }

    public DateTime? FindPreviousTimestamp(DateTime currentUtc)
    {
        long startKey = BucketKey(currentUtc);
        for (int i = _chunkKeys.Count - 1; i >= 0; i--)
        {
            long key = _chunkKeys[i];
            if (key > startKey) continue;
            DateTime? found = ReadChunk(key).Where(x => x.TimestampUtc < currentUtc.AddTicks(-1)).Select(x => (DateTime?)x.TimestampUtc).Max();
            if (found.HasValue) return found.Value;
        }
        return null;
    }

    public static long BucketKey(DateTime utc)
    {
        DateTime normalized = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
        return normalized.Ticks / ChunkTicks;
    }

    public static DateTime BucketStart(long key) => new(key * ChunkTicks, DateTimeKind.Utc);
    private string ChunkPath(long key) => Path.Combine(CacheDirectory, $"chunk_{key}.bin");
    private string CheckpointPath(long key) => Path.Combine(CacheDirectory, $"checkpoint_{key}.bin");

    private List<ReplayRecord> ReadChunk(long key)
    {
        string path = ChunkPath(key);
        var records = new List<ReplayRecord>();
        if (!File.Exists(path)) return records;
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, false);
        while (stream.Position < stream.Length) records.Add(ReadRecord(reader));
        records.Sort(CompareRecords);
        return records;
    }

    internal static int CompareRecords(ReplayRecord a, ReplayRecord b)
    {
        int cmp = a.TimestampUtc.CompareTo(b.TimestampUtc);
        return cmp != 0 ? cmp : a.Kind.CompareTo(b.Kind);
    }

    internal sealed class Writer : IDisposable
    {
        private readonly string _cacheDirectory;
        private readonly SortedSet<long> _chunkKeys = new();
        private BinaryWriter? _writer;
        private long? _writerBucket;

        public Writer(string cacheDirectory)
        {
            _cacheDirectory = cacheDirectory;
            Directory.CreateDirectory(cacheDirectory);
        }

        public void Append(ReplayRecord record)
        {
            long key = BucketKey(record.TimestampUtc);
            if (_writer is null || _writerBucket != key)
            {
                CloseWriter();
                string path = Path.Combine(_cacheDirectory, $"chunk_{key}.bin");
                _writer = new BinaryWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8, false);
                _writerBucket = key;
            }
            WriteRecord(_writer, record);
            _chunkKeys.Add(key);
        }

        public ReplayCacheStore FinalizeAndBuildCheckpoints()
        {
            CloseWriter();
            var store = new ReplayCacheStore(_cacheDirectory, _chunkKeys);
            var signalState = new Dictionary<string, MutableSignalCheckpoint>(StringComparer.OrdinalIgnoreCase);
            var activeDtcs = new Dictionary<string, ReplayRecord>(StringComparer.OrdinalIgnoreCase);

            foreach (long key in _chunkKeys)
            {
                var checkpoint = new ReplayCheckpoint { TimestampUtc = BucketStart(key) };
                foreach (var pair in signalState.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    checkpoint.Signals.Add(new ReplaySignalCheckpoint
                    {
                        Key = pair.Key,
                        LastSample = pair.Value.LastSample,
                        ObservedMin = pair.Value.Min,
                        ObservedMax = pair.Value.Max
                    });
                }
                checkpoint.ActiveDtcs.AddRange(activeDtcs.Values.OrderBy(x => x.Source).ThenBy(x => x.DtcCode));
                string cpPath = Path.Combine(_cacheDirectory, $"checkpoint_{key}.bin");
                using (var stream = File.Create(cpPath))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, false))
                    WriteCheckpoint(writer, checkpoint);

                foreach (ReplayRecord record in store.ReadChunk(key))
                {
                    if (record.Kind == ReplayRecordKind.Sample && record.Value.HasValue)
                    {
                        string signalKey = ReplayKey(record);
                        if (!signalState.TryGetValue(signalKey, out MutableSignalCheckpoint? state))
                            signalState[signalKey] = new MutableSignalCheckpoint(record);
                        else
                        {
                            state.LastSample = record;
                            state.Min = Math.Min(state.Min, record.Value.Value);
                            state.Max = Math.Max(state.Max, record.Value.Value);
                        }
                    }
                    else if (record.Kind == ReplayRecordKind.Dtc && !string.IsNullOrWhiteSpace(record.DtcCode))
                    {
                        string dtcKey = record.Source + "|" + record.DtcCode;
                        if (record.DtcChange.Equals("CLEARED", StringComparison.OrdinalIgnoreCase)) activeDtcs.Remove(dtcKey);
                        else activeDtcs[dtcKey] = record;
                    }
                }
            }
            return store;
        }

        public IReadOnlyList<long> ChunkKeys => _chunkKeys.ToList();

        private void CloseWriter()
        {
            if (_writer is null) return;
            _writer.Flush();
            _writer.Dispose();
            _writer = null;
            _writerBucket = null;
        }

        public void Dispose() => CloseWriter();

        private sealed class MutableSignalCheckpoint
        {
            public ReplayRecord LastSample;
            public double Min;
            public double Max;
            public MutableSignalCheckpoint(ReplayRecord sample)
            {
                LastSample = sample;
                Min = sample.Value ?? 0;
                Max = sample.Value ?? 0;
            }
        }
    }

    internal static string ReplayKey(ReplayRecord record)
    {
        string source = string.IsNullOrWhiteSpace(record.Source) ? "Replay" : record.Source.Trim().ToUpperInvariant();
        string request = string.IsNullOrWhiteSpace(record.Request) ? "REPLAY:" + record.Name.Trim().ToUpperInvariant() : record.Request.Trim().ToUpperInvariant();
        return $"{request}|{record.Name.Trim()}|{source}";
    }

    private static void WriteCheckpoint(BinaryWriter writer, ReplayCheckpoint checkpoint)
    {
        writer.Write(checkpoint.TimestampUtc.Ticks);
        writer.Write(checkpoint.Signals.Count);
        foreach (ReplaySignalCheckpoint state in checkpoint.Signals)
        {
            writer.Write(state.Key ?? "");
            writer.Write(state.ObservedMin);
            writer.Write(state.ObservedMax);
            WriteRecord(writer, state.LastSample);
        }
        writer.Write(checkpoint.ActiveDtcs.Count);
        foreach (ReplayRecord dtc in checkpoint.ActiveDtcs) WriteRecord(writer, dtc);
    }

    private static ReplayCheckpoint ReadCheckpoint(BinaryReader reader)
    {
        var checkpoint = new ReplayCheckpoint { TimestampUtc = new DateTime(reader.ReadInt64(), DateTimeKind.Utc) };
        int signalCount = reader.ReadInt32();
        for (int i = 0; i < signalCount; i++)
        {
            string key = reader.ReadString();
            double min = reader.ReadDouble();
            double max = reader.ReadDouble();
            checkpoint.Signals.Add(new ReplaySignalCheckpoint { Key = key, ObservedMin = min, ObservedMax = max, LastSample = ReadRecord(reader) });
        }
        int dtcCount = reader.ReadInt32();
        for (int i = 0; i < dtcCount; i++) checkpoint.ActiveDtcs.Add(ReadRecord(reader));
        return checkpoint;
    }

    internal static void WriteRecord(BinaryWriter writer, ReplayRecord record)
    {
        writer.Write(record.TimestampUtc.Ticks);
        writer.Write((byte)record.Kind);
        writer.Write(record.Source ?? ""); writer.Write(record.Vehicle ?? ""); writer.Write(record.Name ?? ""); writer.Write(record.Request ?? "");
        writer.Write(record.Value.HasValue); if (record.Value.HasValue) writer.Write(record.Value.Value);
        writer.Write(record.Units ?? "");
        writer.Write(record.NativeValue.HasValue); if (record.NativeValue.HasValue) writer.Write(record.NativeValue.Value);
        writer.Write(record.NativeUnits ?? "");
        writer.Write(record.RttMs); writer.Write(record.FirstByteMs); writer.Write(record.TailMs);
        writer.Write(record.Raw ?? ""); writer.Write(record.Status ?? ""); writer.Write(record.Message ?? ""); writer.Write(record.DisplayText ?? "");
        writer.Write(record.DtcCode ?? ""); writer.Write(record.DtcChange ?? ""); writer.Write(record.DtcDescription ?? "");
    }

    internal static ReplayRecord ReadRecord(BinaryReader reader)
    {
        long ticks = reader.ReadInt64();
        ReplayRecordKind kind = (ReplayRecordKind)reader.ReadByte();
        string source = reader.ReadString(), vehicle = reader.ReadString(), name = reader.ReadString(), request = reader.ReadString();
        double? value = reader.ReadBoolean() ? reader.ReadDouble() : null;
        string units = reader.ReadString();
        double? nativeValue = reader.ReadBoolean() ? reader.ReadDouble() : null;
        string nativeUnits = reader.ReadString();
        double rtt = reader.ReadDouble(), first = reader.ReadDouble(), tail = reader.ReadDouble();
        string raw = reader.ReadString(), status = reader.ReadString(), message = reader.ReadString(), displayText = reader.ReadString();
        string dtcCode = reader.ReadString(), dtcChange = reader.ReadString(), dtcDescription = reader.ReadString();
        return new ReplayRecord { TimestampUtc = new DateTime(ticks, DateTimeKind.Utc), Kind = kind, Source = source, Vehicle = vehicle, Name = name, Request = request, Value = value, Units = units, NativeValue = nativeValue, NativeUnits = nativeUnits, RttMs = rtt, FirstByteMs = first, TailMs = tail, Raw = raw, Status = status, Message = message, DisplayText = displayText, DtcCode = dtcCode, DtcChange = dtcChange, DtcDescription = dtcDescription };
    }
}

internal sealed class ReplayCacheManifest
{
    public int Version { get; set; }
    public DateTime CreatedUtc { get; set; }
    public List<ReplaySourceSignature> Sources { get; set; } = new();
    public List<long> ChunkKeys { get; set; } = new();
    public List<ReplaySignalDescriptor> Signals { get; set; } = new();
    public List<ReplayRecord> Markers { get; set; } = new();
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string Vehicle { get; set; } = "";
    public long SampleCount { get; set; }
    public long EventCount { get; set; }
    public long DtcCount { get; set; }
    public long SkippedRows { get; set; }
}

internal sealed class ReplaySourceSignature
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public long LastWriteUtcTicks { get; set; }
}
