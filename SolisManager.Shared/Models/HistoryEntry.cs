using System.Globalization;

namespace SolisManager.Shared.Models;

public class HistoryEntry
{
    const string dateFormat = "dd-MMM-yyyy HH:mm";

    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public decimal Price { get; set; }
    public int BatterySOC { get; set; }
    public PriceType Type { get; set; }
    public SlotAction Action { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal ForecastKWH { get; set; }    

    // The next 3 are cumulative totals - to find the per-slot
    // value you'll need to subtract the previous slot's value
    public decimal ActualKWH { get; set; }
    public decimal ImportedKWH { get; set; }
    public decimal ExportedKWH { get; set; }
    public decimal HouseLoadKWH { get; set; }
    

    public HistoryEntry() { }

    public HistoryEntry(OctopusPriceSlot slot, SolisManagerState state)
    {
        Start = slot.valid_from;
        End = slot.valid_to;
        Price = slot.value_inc_vat;
        Type = slot.PriceType;
        Action = slot.ActionToExecute.action;
        ForecastKWH = slot.pv_est_kwh ?? 0;
        Reason = slot.ActionReason;
        BatterySOC = state.BatterySOC;
    }

    public override string ToString()
    {
        return $"{Start:dd-MMM HH:mm]}: {Price}p/kWh, PV: {ActualKWH}kWh, In: {ImportedKWH}kWh, Out: {ExportedKWH}kWh, Fore: {ForecastKWH}kWh, Battery: {BatterySOC:P}";
    }

    private static void ReadCoreValues(HistoryEntry entry, string[] parts)
    {
        entry.Start = DateTime.ParseExact(parts[0], dateFormat, CultureInfo.InvariantCulture);
        entry.End = DateTime.ParseExact(parts[1], dateFormat, CultureInfo.InvariantCulture);
        entry.Price = decimal.Parse(parts[2], CultureInfo.InvariantCulture);
        entry.Action = Enum.Parse<SlotAction>(parts[3], true);
        entry.Type = Enum.Parse<PriceType>(parts[4], true);
        entry.BatterySOC = int.Parse(parts[5].Replace("%", ""));
    }
    
    public static HistoryEntry? TryParse(string logLine)
    {
        var entry = new HistoryEntry();

        var parts = logLine.Split(",", 7, StringSplitOptions.TrimEntries);

        ReadCoreValues(entry, parts);
        
        if (parts.Last().StartsWith('\"'))
        {
            // Version 1 just had the reason
            entry.Reason = parts[6].Trim('\"');
            return entry;
        }
        
        parts = logLine.Split(",", 9, StringSplitOptions.TrimEntries);

        if (parts.Last().StartsWith('\"'))
        {
            // Version 2 had the Actual/Forecast parts.
            entry.ActualKWH = decimal.Parse(parts[6], CultureInfo.InvariantCulture);
            entry.ForecastKWH = decimal.Parse(parts[7], CultureInfo.InvariantCulture);
            entry.Reason = parts[8].Trim('\"');
            return entry;
        }
        
        parts = logLine.Split(",", 12, StringSplitOptions.TrimEntries);

        if (parts.Last().StartsWith('\"'))
        {
            // Version 3 had the Actual/Forecast parts.
            entry.ActualKWH = decimal.Parse(parts[6], CultureInfo.InvariantCulture);
            entry.ForecastKWH = decimal.Parse(parts[7], CultureInfo.InvariantCulture);
            entry.ImportedKWH = decimal.Parse(parts[8], CultureInfo.InvariantCulture);
            entry.ExportedKWH = decimal.Parse(parts[9], CultureInfo.InvariantCulture);
            entry.HouseLoadKWH = decimal.Parse(parts[10], CultureInfo.InvariantCulture);
            entry.Reason = parts[11].Trim('\"');
            return entry;
        }

        return null;
    }
    
    public string GetAsCSV()
    {
        return string.Join(", ", 
            Start.ToString(dateFormat),
            End.ToString(dateFormat),
            Price.ToString("0.00"),
            Action.ToString(), 
            Type.ToString(),
            BatterySOC.ToString() + '%',
            ActualKWH.ToString("0.00"),
            ForecastKWH.ToString("0.00"),
            ImportedKWH.ToString("0.00"),
            ExportedKWH.ToString("0.00"),
            HouseLoadKWH.ToString("0.00"),
            $"\"{Reason}\""
        );
    }
}