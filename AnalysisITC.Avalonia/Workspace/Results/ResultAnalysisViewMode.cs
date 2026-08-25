namespace AnalysisITC.Avalonia.Results
{
    public enum ResultAnalysisViewMode
    {
        /// <summary>The selected experiment's fit graph.</summary>
        Fit = 0,
        /// <summary>The parameter correlation matrix.</summary>
        Correlation = 1,
        /// <summary>The thermodynamic parameter overview graph.</summary>
        Summary = 2,
        // Source-compatible aliases for callers written before the named views.
        SelectedFit = Fit,
        Parameters = Summary,
        Temperature = 3,
        Salt = 4,
        Protonation = 5
    }
}
