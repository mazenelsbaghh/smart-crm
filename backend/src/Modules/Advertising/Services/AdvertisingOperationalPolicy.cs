namespace Modules.Advertising.Services;

public static class AdvertisingOperationalPolicy
{
    public static bool MustFreezeFinance(bool connectionReady, bool trackingHealthy, bool emergencyStopActive) =>
        !connectionReady || !trackingHealthy || emergencyStopActive;

    public static bool MustEmergencyStop(decimal observedSpend, decimal dailyCap, decimal abnormalPercent = 105m) =>
        dailyCap > 0 && observedSpend >= dailyCap * Math.Min(100m, abnormalPercent) / 100m;
}
