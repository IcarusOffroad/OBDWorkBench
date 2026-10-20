using System;
using System.Collections.Generic;
using OBDWorkbench.Services;

namespace OBDWorkbench.Models;

public enum ReplayRecordKind
{
    Sample,
    Event,
    Dtc
}

public sealed class ReplayRecord
{
    public DateTime TimestampUtc { get; init; }
    public ReplayRecordKind Kind { get; init; }
    public string Source { get; init; } = "Replay";
    public string Vehicle { get; init; } = "";
    public string Name { get; init; } = "";
    public string Request { get; init; } = "";
    public double? Value { get; init; }
    public string Units { get; init; } = "";
    public double? NativeValue { get; init; }
    public string NativeUnits { get; init; } = "";
    public double RttMs { get; init; }
    public double FirstByteMs { get; init; }
    public double TailMs { get; init; }
    public string Raw { get; init; } = "";
    public string Status { get; init; } = "";
    public string Message { get; init; } = "";
    public string DisplayText { get; init; } = "";
    public string DtcCode { get; init; } = "";
    public string DtcChange { get; init; } = "";
    public string DtcDescription { get; init; } = "";
}

public sealed class ReplaySignalDescriptor
{
    public string Key { get; set; } = "";
    public string Source { get; set; } = "Replay";
    public string Vehicle { get; set; } = "";
    public string Name { get; set; } = "";
    public string Request { get; set; } = "";
    public string Units { get; set; } = "";
    public string NativeUnits { get; set; } = "";
    public double Min { get; set; }
    public double Max { get; set; }
    public long SampleCount { get; set; }
}

public sealed class ReplaySignalCheckpoint
{
    public string Key { get; init; } = "";
    public ReplayRecord LastSample { get; init; } = new();
    public double ObservedMin { get; init; }
    public double ObservedMax { get; init; }
}

public sealed class ReplayCheckpoint
{
    public DateTime TimestampUtc { get; init; }
    public List<ReplaySignalCheckpoint> Signals { get; } = new();
    public List<ReplayRecord> ActiveDtcs { get; } = new();
}

public sealed class ReplayWindow
{
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public List<ReplayRecord> Records { get; init; } = new();
}

public sealed class ReplaySession
{
    public List<string> Files { get; } = new();
    public List<ReplaySignalDescriptor> Signals { get; } = new();
    public List<ReplayRecord> Markers { get; } = new();
    public ReplayCacheStore? Cache { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string Vehicle { get; set; } = "";
    public long SampleCount { get; set; }
    public long EventCount { get; set; }
    public long DtcCount { get; set; }
    public long SkippedRows { get; set; }
    public long CacheBytes { get; set; }
    public bool CacheWasReused { get; set; }
}
