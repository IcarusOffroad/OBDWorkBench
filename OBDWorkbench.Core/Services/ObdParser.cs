using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OBDWorkbench.Models;

namespace OBDWorkbench.Services;

public static class ObdParser
{
    public static bool TryDecode(PidDefinition definition, string response, out double value, out string error)
    {
        value = 0;
        error = "";

        if (response.Contains("NO DATA", StringComparison.OrdinalIgnoreCase))
        {
            error = "NO DATA";
            return false;
        }

        if (!TryParseHexByte(definition.ResponseMode, out byte responseMode) ||
            !TryParseHexByte(definition.PID, out byte pid))
        { error = "Invalid ResponseMode/PID in CSV."; return false; }
        var messages = MultiEcuParser.ParseMessages(response)
            .Where(m => m.Payload.Length >= 2 && m.Payload[0] == responseMode && m.Payload[1] == pid).ToList();
        if (messages.Select(m => m.Source).Distinct().Count() > 1)
        { error = "Ambiguous ECU sources; decode each responder separately."; return false; }
        if (messages.Count == 0)
        { error = $"Expected {definition.ResponseMode} {definition.PID} not found."; return false; }
        var bytes = messages[0].Payload.ToList();
        int index = 2;
        var variables = new Dictionary<char, byte>();
        for (int i = 0; i < 8 && index + i < bytes.Count; i++)
            variables[(char)('A' + i)] = bytes[index + i];

        try
        {
            value = EvaluateFormula(definition.Formula, variables);
            return true;
        }
        catch (Exception ex)
        {
            error = "Formula error: " + ex.Message;
            return false;
        }
    }

    // Accepts both "48 6B E1 41 00 ..." and adapters that put one byte per line.
    // It also accepts a compact all-hex token. English status text such as SEARCHING...
    // is ignored because the whole token must consist only of hex characters.
    public static List<byte> ExtractHexBytes(string response, string? echoedCommand = null)
    {
        var bytes = new List<byte>();
        string command = (echoedCommand ?? "").Replace(" ", "").Trim().ToUpperInvariant();

        string normalized = response
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('>', ' ')
            .ToUpperInvariant();

        foreach (string rawToken in normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string token = rawToken.Trim();
            if (token.Length == 0)
                continue;

            if (command.Length > 0 && token == command)
                continue;

            if (token.Length < 2 || token.Length % 2 != 0 || !token.All(IsHexChar))
                continue;

            for (int i = 0; i < token.Length; i += 2)
            {
                if (byte.TryParse(token.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    bytes.Add(b);
            }
        }

        return bytes;
    }

    private static double EvaluateFormula(string formula, IReadOnlyDictionary<char, byte> variables)
    {
        // A few standard OBD values use bit fields rather than pure arithmetic. Keep those
        // formula tokens explicit so the CSV can still describe shared-request outputs.
        if (formula.Equals("DTC_COUNT", StringComparison.OrdinalIgnoreCase))
        {
            if (!variables.TryGetValue('A', out byte a))
                throw new InvalidOperationException("Formula needs byte A, but it was not present.");
            return a & 0x7F;
        }

        string expression = formula;
        foreach (char variable in "ABCDEFGH")
        {
            if (Regex.IsMatch(expression, $@"\b{variable}\b") && !variables.ContainsKey(variable))
                throw new InvalidOperationException($"Formula needs byte {variable}, but it was not present.");

            if (variables.TryGetValue(variable, out byte value))
                expression = Regex.Replace(expression, $@"\b{variable}\b", value.ToString(CultureInfo.InvariantCulture) + ".0");
        }

        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
        object computed = table.Compute(expression, "");
        return Convert.ToDouble(computed, CultureInfo.InvariantCulture);
    }

    private static bool TryParseHexByte(string text, out byte value) =>
        byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    private static bool IsHexChar(char c) =>
        (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
}
