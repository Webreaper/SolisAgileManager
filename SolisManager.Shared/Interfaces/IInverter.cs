using SolisManager.Shared.Models;

namespace SolisManager.Shared.Interfaces;


public interface IInverter
{
    public Task UpdateInverterTime(bool simulateOnly);

    public Task SetCharge(DateTime? chargeStart, DateTime? chargeEnd,
        DateTime? dischargeStart, DateTime? dischargeEnd,
        bool holdCharge, bool simulateOnly);

    public Task<bool> UpdateInverterState(SolisManagerState inverterState);

    public Task<IEnumerable<InverterFiveMinData>?> GetHistoricData(int dayOffset = 0);
    public Task<IEnumerable<InverterFiveMinData>?> GetHistoricData(DateTime dayToQuery);
}