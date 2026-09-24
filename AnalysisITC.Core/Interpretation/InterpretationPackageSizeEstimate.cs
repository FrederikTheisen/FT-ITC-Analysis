namespace AnalysisITC.Core.Interpretation
{
    /// <summary>Shared display estimate for the serialized relay request size.</summary>
    public static class InterpretationPackageSizeEstimate
    {
        public const int ReservedRequestBytes = 4 * 1024;

        public static bool Fits(long compactPackageBytes, long maximumRequestBytes) =>
            compactPackageBytes >= 0 && maximumRequestBytes > 0
            && compactPackageBytes <= maximumRequestBytes - ReservedRequestBytes;

        public static bool CanApplyPreview(int resultRevision, int currentRevision, bool cancelled) =>
            !cancelled && resultRevision == currentRevision;

        public static bool CanGenerate(bool serviceAvailable, bool accessAvailable, bool busy, bool sizeFits) =>
            serviceAvailable && accessAvailable && !busy && sizeFits;
    }
}
