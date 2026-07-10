namespace SolisManager.Shared.Models;

public record OctopusTariff(string code, decimal? standing_charge_inc_vat, decimal? standing_charge_exc_vat, 
        IEnumerable<OctopusProductLink> links,
        decimal day_unit_rate_inc_vat,
        decimal night_unit_rate_inc_vat,
        decimal ev_device_peak_unit_rate_inc_vat,
        decimal ev_device_off_peak_unit_rate_inc_vat);

public record OctopusTariffRegion(OctopusTariff direct_debit_monthly);

public record OctopusTariffResponse(
    string code,
    string full_name,
    string display_name,
    Dictionary<string, OctopusTariffRegion> single_register_electricity_tariffs,
    Dictionary<string, OctopusTariffRegion> dual_register_electricity_tariffs,
    Dictionary<string, OctopusTariffRegion> four_rate_ev_electricity_tariffs);

public record OctopusProductLink(string href);
public record OctopusProduct (string code, string direction, string display_name, string full_name, IEnumerable<OctopusProductLink> links, string brand);
public record OctopusProductResponse(int count, IEnumerable<OctopusProduct> results);    
