using System;
using System.Collections.Generic;

namespace OBDWorkbench.Models;

public sealed class DetectedModule
{
    public string Source { get; set; } = "";
    public string Name { get; set; } = "Unknown ECU";
    public string Protocol { get; set; } = "";
    public string SupportedPids { get; set; } = "";
    public string CalibrationId { get; set; } = "";
    public string Cvn { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class DiagnosticTroubleCode
{
    public string Source { get; set; } = "";
    public string Module { get; set; } = "";
    public string Code { get; set; } = "";
    public string Status { get; set; } = "Confirmed";
    public string Description { get; set; } = "";
    public string DefinitionSource { get; set; } = "";
    public DateTime FirstRead { get; set; }
    public DateTime LastRead { get; set; }
}

public sealed class DiscoveryResult
{
    public string Protocol { get; set; } = "";
    public string Vin { get; set; } = "";
    public string ManufacturerFamily { get; set; } = "Generic";
    public string CalibrationSignature { get; set; } = "";
    public List<DetectedModule> Modules { get; } = new();
    public List<DiagnosticTroubleCode> Dtcs { get; } = new();
}
