namespace PCTweaker.Models;

public sealed record GpuDriverIntegrationStatus(
    string Vendor,
    string GpuName,
    string DriverInfo,
    string IntegrationStatus,
    bool VendorAppAvailable,
    string VendorAppName,
    string QualitySafeSummary);
