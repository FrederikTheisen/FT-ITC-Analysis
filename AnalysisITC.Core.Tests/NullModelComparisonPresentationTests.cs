using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class NullModelComparisonPresentationTests
{
    [Fact]
    public void AnalysisInspectorRowsListModelNullRmsdDeltaAndConclusionInOrder()
    {
        var rows = NullModelComparisonPresentation.AnalysisInspectorRows(Comparison(10, 4.184), EnergyUnitFamily.Joules);

        Assert.Equal(new[] { "Model", "Null RMSD", "ΔAICc", "Conclusion" }, rows.Select(row => row.Label));
        Assert.Equal("Null hypothesis test", NullModelComparisonPresentation.AnalysisInspectorTitle);
    }

    [Fact]
    public void AnalysisInspectorRowsShowSavedEvidenceInSelectedHeatUnit()
    {
        var comparison = Comparison(10, 4.184);

        var joules = NullModelComparisonPresentation.AnalysisInspectorRows(comparison, EnergyUnitFamily.Joules);
        Assert.Equal("Offset", joules[0].Value);
        Assert.Equal(4.184.ToString("G4", CultureInfo.CurrentCulture), joules[1].Value);
        Assert.Equal("+10", joules[2].Value);
        Assert.Equal("Binding detected", joules[3].Value);

        // 4.184 µJ = 1 µcal.
        var calories = NullModelComparisonPresentation.AnalysisInspectorRows(comparison, EnergyUnitFamily.Calories);
        Assert.Equal("1", calories[1].Value);
    }

    [Fact]
    public void AnalysisInspectorRowsPlaceEvidenceTooltipOnNumericRowsOnly()
    {
        var rows = NullModelComparisonPresentation.AnalysisInspectorRows(Comparison(-2, 4.184), EnergyUnitFamily.Joules);

        Assert.Equal("", rows[0].Tooltip);
        Assert.Contains("RMSD unit: µJ", rows[1].Tooltip);
        Assert.Equal(rows[1].Tooltip, rows[2].Tooltip);
        Assert.Equal("", rows[3].Tooltip);
        Assert.Equal("No binding detected", rows[3].Value);
    }

    [Fact]
    public void AnalysisInspectorRowsReportMissingAndFailedComparisons()
    {
        var missing = NullModelComparisonPresentation.AnalysisInspectorRows(null, EnergyUnitFamily.Joules);
        Assert.Equal(new[] { "Offset (not calculated)", "Unavailable", "Not calculated", "Not assessed" },
            missing.Select(row => row.Value));

        var failed = Comparison(10, 4.184);
        failed.NullFitSucceeded = false;
        failed.NullFitReason = "Offset fit did not converge.";
        failed.DeltaAicc = null;
        failed.NullInformationCriteria = null;
        var failedRows = NullModelComparisonPresentation.AnalysisInspectorRows(failed, EnergyUnitFamily.Joules);
        Assert.Equal(new[] { "Offset (failed)", "Unavailable", "Unavailable", "Not assessed" },
            failedRows.Select(row => row.Value));
        Assert.Contains("Offset fit did not converge.", failedRows[2].Tooltip);
    }

    static NullModelComparison Comparison(double delta, double nullRmsdMicrojoules) => new()
    {
        BindingFitSucceeded = true,
        NullFitSucceeded = true,
        DeltaAicc = delta,
        BindingInformationCriteria = Criteria(100, 1, 1),
        NullInformationCriteria = Criteria(100 + delta, 2, nullRmsdMicrojoules),
    };

    static FitInformationCriteria Criteria(double aicc, int parameterCount, double residualRmsdMicrojoules)
        => FitInformationCriteria.Restore(
            observationCount: 20, fittedParameterCount: parameterCount,
            likelihoodParameterCount: parameterCount + 1,
            likelihoodMode: GaussianLikelihoodMode.EstimatedCommonVariance,
            minusTwoLogLikelihood: 90, aic: aicc - 4, aicc: aicc,
            isAicAvailable: true, isAiccAvailable: true, aicUnavailableReason: string.Empty,
            aiccUnavailableReason: string.Empty, rawResidualSumOfSquares: 0,
            residualRmsdMicrojoules: residualRmsdMicrojoules, standardizedResidualSumOfSquares: 0, logSigmaSquaredSum: 0);
}
