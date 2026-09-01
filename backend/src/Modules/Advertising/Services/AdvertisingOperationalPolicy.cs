using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

public static class AdvertisingOperationalPolicy
{
    public static bool MustFreezeFinance(bool connectionReady, bool trackingHealthy, bool emergencyStopActive) =>
        !connectionReady || !trackingHealthy || emergencyStopActive;

    public static bool MustEmergencyStop(decimal guardedExposure, decimal usableCap) =>
        usableCap > 0 && guardedExposure >= usableCap;

    public static bool IsAbnormalForecast(decimal forecastSpend, decimal usableCap, decimal abnormalPercent = 105m) =>
        usableCap > 0 && abnormalPercent >= 100m && forecastSpend >= usableCap * abnormalPercent / 100m;

    public static bool HasFreshHealthyTracking(TrackingHealthSnapshot? snapshot, bool hasOpenIncident,
        DateTime nowUtc, TimeSpan maximumAge) =>
        !hasOpenIncident && snapshot?.State == TrackingHealthState.Healthy
        && snapshot.EvaluatedAtUtc >= nowUtc.Subtract(maximumAge);
}
