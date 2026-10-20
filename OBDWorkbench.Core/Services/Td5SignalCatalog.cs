using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OBDWorkbench.Models;

namespace OBDWorkbench.Services;

public static class Td5SignalCatalog
{
    public static readonly byte[] ConfirmedPids =
    {
        0x01,0x03,0x04,0x05,0x07,0x09,0x0D,0x0E,0x10,0x18,0x1A,0x1B,0x1C,0x1D,0x1E,0x1F,0x20,0x21,0x23,0x24,
        0x32,0x33,0x36,0x37,0x38,0x39,0x3B,0x3C,0x3D,0x3E,0x3F,0x40,0x42,0x43,0x44,0x45,0x46,0x61
    };

    public static List<PidDefinition> CreateDefinitions()
    {
        var list = new List<PidDefinition>();
        foreach (byte pid in ConfirmedPids)
        {
            (int poll, int priority, bool snapshot) = Schedule(pid);
            Add(list, pid, $"PID {pid:X2} Raw", "RAWHEX", "hex", poll, priority, false, snapshot, "Raw Service 0x21 payload. Retained even when semantic mapping is unknown.");
        }

        Add(list,0x01,"Coolant Temperature","TEMP","°C",750,4,true);
        Add(list,0x03,"Intake Air Temperature (candidate)","TEMP","°C",1000,3,true);
        Add(list,0x04,"Temperature #04 (candidate)","TEMP","°C",10000,1,true);
        Add(list,0x05,"Fuel Temperature (candidate)","TEMP","°C",1200,3,true);
        Add(list,0x07,"Manifold Absolute Pressure","PRESSURE100","kPa",500,5,true,false,"Experimentally validated TD5 manifold absolute pressure signal.");
        Add(list,0x09,"Engine RPM","U16","rpm",250,5,true);
        Add(list,0x0D,"Vehicle Speed","U8","km/h",500,4,true);
        Add(list,0x0E,"PID 0E ASCII / config","ASCII","text",60000,1,true,true);
        Add(list,0x10,"Battery Voltage","W1_1000","V",750,4,true);
        Add(list,0x10,"Reference Voltage (candidate)","W2_1000","V",750,3,true);
        Add(list,0x18,"Unknown #18 Byte 01 Raw","B1","raw",5000,1,true);

        AddWords(list,0x1A,8,"PID 1A Word",750,4);
        Add(list,0x1B,"Accelerator Track 1 Voltage","W1_1000","V",350,5,true);
        Add(list,0x1B,"Accelerator Track 2 Voltage","W2_1000","V",350,5,true);
        Add(list,0x1B,"Accelerator Sensor Supply","W3_1000","V",1000,3,true);
        Add(list,0x1B,"PID 1B Word 04 Raw","W4_U16","raw",1000,2,true);
        Add(list,0x1B,"PID 1B Word 05 Raw","W5_U16","raw",1000,2,true);

        Add(list,0x1C,"Pressure 1 (candidate)","W1_100","kPa",500,5,true);
        Add(list,0x1C,"Pressure 2 (candidate)","W2_100","kPa",500,4,true);
        Add(list,0x1C,"PID 1C Word 03 Raw","W3_U16","raw",750,3,true);
        Add(list,0x1C,"PID 1C Word 04 Raw","W4_U16","raw",750,3,true);

        AddWords(list,0x1D,9,"PID 1D Word",500,4,signed:true);
        Add(list,0x1D,"Driver Demand (candidate)","W1_S100","mg/stk",350,5,true);
        Add(list,0x1D,"Air Mass / Word 02 (candidate)","W2_S100","mg/stk",500,4,true);
        Add(list,0x1D,"MAP-calculated Air Mass (candidate)","W3_S10","mg/stk",500,4,true);
        Add(list,0x1D,"Injected Fuel Quantity (candidate)","W4_S100","mg/stk",350,5,true);
        Add(list,0x1D,"Fuel Limit A (candidate)","W6_S100","mg/stk",500,4,true);
        Add(list,0x1D,"Fuel Limit B (candidate)","W7_S100","mg/stk",500,4,true);
        Add(list,0x1D,"Idle Fuel Demand (candidate)","W8_S100","mg/stk",500,4,true);

        Add(list,0x1E,"Input Switch Byte 1 Raw","B1","raw",300,4,true);
        Add(list,0x1E,"Input Switch Byte 2 Raw","B2","raw",300,4,true);
        Add(list,0x1E,"Clutch Pressed","CLUTCH","state",250,5,true);
        Add(list,0x1E,"Brake Pressed","BRAKE","state",300,5,true);
        Add(list,0x1E,"Low Range (candidate)","LOWRANGE","state",750,3,true);

        AddWords(list,0x1F,1,"PID 1F Word",3000,2);
        Add(list,0x1F,"PID 1F Byte 03 Raw","B3","raw",3000,2,true);
        AddWords(list,0x20,2,"PID 20 Word",5000,1);
        Add(list,0x21,"PID 21 Signed Value (candidate)","W1_S16","raw",750,3,true);
        Add(list,0x23,"Inlet / Airbox Pressure 1 (candidate)","W1_100","kPa",750,4,true);
        Add(list,0x23,"Inlet / Airbox Pressure 2 (candidate)","W2_100","kPa",750,3,true);
        Add(list,0x24,"PID 24 Byte 01 Raw","B1","raw",5000,1,true);
        Add(list,0x32,"PID 32 ASCII / config","ASCII","text",60000,1,true,true);
        Add(list,0x33,"PID 33 ASCII / config","ASCII","text",60000,1,true,true);
        Add(list,0x36,"PID 36 Status Word Raw","W1_U16","raw",500,4,true);
        Add(list,0x36,"Run / Fuel Enabled (candidate)","RUN36","state",350,5,true);
        Add(list,0x37,"EGR Position (candidate)","W1_100","%",1000,3,true);
        Add(list,0x38,"Wastegate Position (candidate)","W1_100","%",1000,3,true);
        Add(list,0x39,"PID 39 Status Byte Raw","B1","raw",500,4,true);
        Add(list,0x39,"Engine Active (candidate)","ACTIVE39","state",500,4,true);
        Add(list,0x3B,"TD5 Fault Bitmap Set Flags","DTC_TOTAL","count",5000,1,true);

        AddWords(list,0x3C,4,"Unknown #3C Word",8000,1);
        AddWords(list,0x3D,9,"Unknown #3D Word",8000,1);
        AddWords(list,0x3E,11,"Unknown #3E Word",8000,1);
        Add(list,0x3E,"Unknown #3E Byte 23 Raw","B23","raw",8000,1,true);
        AddWords(list,0x3F,2,"Unknown #3F Word",8000,1);
        for (int i=1;i<=5;i++) Add(list,0x40,$"Injector {i} Trim Raw","W"+i.ToString(CultureInfo.InvariantCulture)+"_S16","raw",1000,3,true);
        AddWords(list,0x42,2,"Unknown #42 Word",10000,1);
        AddWords(list,0x43,16,"Unknown #43 Word",3000,2);
        Add(list,0x43,"PID 43 Pressure ADC (candidate)","W13_U16","raw",3000,2,true);
        Add(list,0x43,"PID 43 Pedal Track 2 ADC (candidate)","W14_U16","raw",3000,2,true);
        Add(list,0x43,"PID 43 Word 15 ADC (candidate)","W15_U16","raw",3000,2,true);
        Add(list,0x43,"PID 43 Pedal Track 1 ADC (candidate)","W16_U16","raw",3000,2,true);
        AddWords(list,0x44,2,"Unknown #44 Word",10000,1);
        AddWords(list,0x45,1,"Unknown #45 Word",10000,1);
        AddWords(list,0x46,2,"Unknown #46 Word",10000,1);
        AddWords(list,0x61,1,"Unknown #61 Word",10000,1);
        return list;
    }

    public static bool TryDecode(PidDefinition definition, byte[] data, out double? numeric, out string display)
    {
        numeric = null; display = "--";
        string token = Token(definition);
        if (token == "RAWHEX") { display = Td5KLineConnection.Hex(data); return true; }
        if (token == "ASCII") { display = Ascii(data); return true; }
        try
        {
            if (token == "TEMP") numeric = (W(data,1,false) - 2732.0) / 10.0;
            else if (token == "PRESSURE100") numeric = W(data,1,false) / 100.0;
            else if (token == "U16") numeric = W(data,1,false);
            else if (token == "U8") numeric = data.Length >= 1 ? data[0] : throw new IndexOutOfRangeException();
            else if (token == "B1") numeric = data.Length >= 1 ? data[0] : throw new IndexOutOfRangeException();
            else if (token == "B2") numeric = data.Length >= 2 ? data[1] : throw new IndexOutOfRangeException();
            else if (token == "B3") numeric = data.Length >= 3 ? data[2] : throw new IndexOutOfRangeException();
            else if (token == "B23") numeric = data.Length >= 23 ? data[22] : throw new IndexOutOfRangeException();
            else if (token == "W1_1000") numeric = W(data,1,false)/1000.0;
            else if (token == "W2_1000") numeric = W(data,2,false)/1000.0;
            else if (token == "W1_100") numeric = W(data,1,false)/100.0;
            else if (token == "W2_100") numeric = W(data,2,false)/100.0;
            else if (token == "W1_S100") numeric = W(data,1,true)/100.0;
            else if (token == "W2_S100") numeric = W(data,2,true)/100.0;
            else if (token == "W3_S10") numeric = W(data,3,true)/10.0;
            else if (token == "W4_S100") numeric = W(data,4,true)/100.0;
            else if (token == "W6_S100") numeric = W(data,6,true)/100.0;
            else if (token == "W7_S100") numeric = W(data,7,true)/100.0;
            else if (token == "W8_S100") numeric = W(data,8,true)/100.0;
            else if (token == "W1_S16") numeric = W(data,1,true);
            else if (token == "CLUTCH") numeric = data.Length >= 1 && (data[0] & 0x02) == 0 ? 1 : 0;
            else if (token == "BRAKE") numeric = data.Length >= 2 && (data[1] & 0x80) == 0 ? 1 : 0;
            else if (token == "LOWRANGE") numeric = data.Length >= 2 && (data[1] & 0x40) != 0 ? 1 : 0;
            else if (token == "RUN36") numeric = (((int)W(data,1,false)) & 0x0004) != 0 ? 1 : 0;
            else if (token == "ACTIVE39")
            {
                if (data.Length < 1) throw new IndexOutOfRangeException();
                numeric = data[0] == 0x10 ? 1 : data[0] == 0x20 ? 0 : null;
                display = data[0] == 0x10 ? "1 (active)" : data[0] == 0x20 ? "0 (stopped)" : $"unknown state 0x{data[0]:X2}";
                return true;
            }
            else if (token == "DTC_TOTAL") numeric = data.Take(35).Sum(b => PopCount(b));
            else if (token.StartsWith("W", StringComparison.Ordinal) && token.EndsWith("_U16", StringComparison.Ordinal)) numeric = W(data, ParseWord(token), false);
            else if (token.StartsWith("W", StringComparison.Ordinal) && token.EndsWith("_S16", StringComparison.Ordinal)) numeric = W(data, ParseWord(token), true);
            else return false;
            if (numeric.HasValue) display = numeric.Value.ToString("0.###", CultureInfo.InvariantCulture);
            return true;
        }
        catch { return false; }
    }

    public static byte PidFromRequest(string request)
    {
        if (!request.StartsWith("TD5-", StringComparison.OrdinalIgnoreCase) || request.Length < 6) throw new FormatException("Not a TD5 request key: " + request);
        return byte.Parse(request.Substring(4,2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static int PopCount(byte b) { int n=0; for(int i=0;i<8;i++) if((b&(1<<i))!=0)n++; return n; }
    private static int ParseWord(string token)
    {
        int underscore=token.IndexOf('_');
        return int.Parse(token.Substring(1,underscore-1),CultureInfo.InvariantCulture);
    }
    private static double W(byte[] data,int word,bool signed)
    {
        int ix=(word-1)*2; if(ix<0||ix+1>=data.Length) throw new IndexOutOfRangeException();
        return signed ? Td5KLineConnection.ReadBeS16(data,ix) : Td5KLineConnection.ReadBe16(data,ix);
    }
    private static string Ascii(byte[] data)
    {
        var sb=new StringBuilder(); foreach(byte b in data) sb.Append(b>=32&&b<=126?(char)b:'.');
        return sb.ToString().TrimEnd(' ');
    }
    private static string Token(PidDefinition d)
    {
        const string p="TD5SIG:";
        if (!d.Notes.StartsWith(p,StringComparison.Ordinal)) return "";
        string value = d.Notes.Substring(p.Length);
        int note = value.IndexOf(" | ", StringComparison.Ordinal);
        return note >= 0 ? value.Substring(0, note) : value;
    }

    private static void AddWords(List<PidDefinition> list,byte pid,int count,string prefix,int poll,int priority,bool signed=false)
    {
        for(int i=1;i<=count;i++) Add(list,pid,$"{prefix} {i:00} Raw","W"+i.ToString(CultureInfo.InvariantCulture)+(signed?"_S16":"_U16"),"raw",poll,priority,true);
    }

    private static void Add(List<PidDefinition> list, byte pid, string name, string token, string units, int poll, int priority, bool derived, bool snapshot=false, string? note=null)
    {
        list.Add(new PidDefinition
        {
            Enabled=true, Name=name, Request=$"TD5-{pid:X2}", ResponseMode="61", PID=$"{pid:X2}", Formula="TD5", Units=units,
            Min=0, Max=0, PollMs=poll, DefaultPriority=priority, Priority=priority, DerivedOnly=derived, DiscoveryOnly=snapshot,
            DisplayKind=token=="RAWHEX"||token=="ASCII"?"RawText":"", Category=Category(pid), Responders="Engine", ExpectedResponses=1,
            Notes="TD5SIG:"+token+(string.IsNullOrWhiteSpace(note)?"":" | "+note)
        });
    }

    private static (int poll,int priority,bool snapshot) Schedule(byte pid)
    {
        if (pid==0x0E||pid==0x32||pid==0x33) return (60000,1,true);
        if (pid==0x09||pid==0x1E||pid==0x1D||pid==0x07) return (300,5,false);
        if (pid==0x0D||pid==0x10||pid==0x1A||pid==0x1B||pid==0x1C||pid==0x21||pid==0x23||pid==0x36||pid==0x37||pid==0x38||pid==0x39||pid==0x40) return (750,4,false);
        if (pid==0x01||pid==0x03||pid==0x05) return (1200,3,false);
        if (pid==0x43) return (3000,2,false);
        if (pid==0x3B) return (5000,1,false);
        return (8000,1,false);
    }

    private static string Category(byte pid)
    {
        if (pid==0x3B) return "TD5 DTC / Fault Memory";
        if (pid==0x0E||pid==0x32||pid==0x33) return "TD5 Identification";
        if (pid==0x1D||pid==0x40) return "TD5 Fueling";
        if (pid==0x1E||pid==0x36||pid==0x39) return "TD5 Status / Inputs";
        if (pid==0x43) return "TD5 Experimental / Raw";
        return "TD5 Engine Data";
    }
}
