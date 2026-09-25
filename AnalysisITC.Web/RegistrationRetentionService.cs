namespace AnalysisITC.Web;

/// <summary>Removes personal details from clearly unverified registrations after 30 days.</summary>
public sealed class RegistrationRetentionService(SelfRegistrationStore store)
{
    public const int RetentionDays = 30;

    public int Run(DateTime utcNow) => store.ScrubExpiredUnverified(utcNow);
}
