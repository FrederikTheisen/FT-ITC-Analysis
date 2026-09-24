using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class CompetitorResultPreviewTests
    {
        [Fact]
        public void MissingSourceShowsCapturedValuesWithoutChangingAttribute()
        {
            var attribute = ExperimentAttribute.CompetitorResultReference("removed-result");
            attribute.SourceSolutionId = "captured-solution";
            attribute.CapturedAffinity = new FloatWithError(2e-6, 0.2e-6, 1.6e-6, 2.4e-6);
            attribute.CapturedEnthalpy = new FloatWithError(-32000, 1200, -34500, -29500);
            var beforeId = attribute.SourceSolutionId;
            var beforeKd = attribute.CapturedAffinity;
            var beforeEnthalpy = attribute.CapturedEnthalpy;

            var preview = CompetitorResultPreviewBuilder.Build(attribute, new ExperimentData("target.itc"));

            Assert.Equal("Missing", preview.Status);
            Assert.Contains("Saved: Kd", preview.Tooltip);
            Assert.Contains("µM", preview.Tooltip);
            Assert.Contains("/mol", preview.Tooltip);
            Assert.DoesNotContain("95%", preview.Tooltip);
            Assert.Equal(beforeId, attribute.SourceSolutionId);
            Assert.Equal(beforeKd.Value, attribute.CapturedAffinity.Value);
            Assert.Equal(beforeEnthalpy.Value, attribute.CapturedEnthalpy.Value);
        }

        [Fact]
        public void NoSelectionRequestsOneSetOfSitesResult()
        {
            var preview = CompetitorResultPreviewBuilder.Build(
                ExperimentAttribute.CompetitorResultReference(null), new ExperimentData("target.itc"));

            Assert.Equal("Select", preview.Status);
            Assert.Contains("Select an Analysis Result", preview.Tooltip);
        }

        [Fact]
        public void ValidTemperatureDependentSourcePreviewsFitValuesWithoutRefreshingCapture()
        {
            var source = new AnalysisResult(LinkedThermodynamicUncertaintyTests.Create(
                VariableConstraint.SameForAll, gibbs: -25000, enthalpy: -40000,
                affinity: VariableConstraint.TemperatureDependent));
            var experiment = new ExperimentData("target.itc") { MeasuredTemperature = 35 };
            var attribute = ExperimentAttribute.CompetitorResultReference(source.UniqueID);
            attribute.SourceSolutionId = source.Solution.UniqueID;
            var (capturedKd, capturedEnthalpy) = CompetitorResultAttributeResolver.EvaluateSummary(source, experiment);
            attribute.CapturedAffinity = capturedKd;
            attribute.CapturedEnthalpy = capturedEnthalpy;

            var preview = CompetitorResultPreviewBuilder.Build(attribute, experiment, source);

            Assert.Equal("Valid", preview.Status);
            Assert.Contains("at 35 °C", preview.Tooltip);
            Assert.DoesNotContain("95%", preview.Tooltip);
            Assert.Equal(capturedKd.Value, attribute.CapturedAffinity.Value);
            Assert.Equal(capturedEnthalpy.Value, attribute.CapturedEnthalpy.Value);
        }

        [Fact]
        public void ValidSourceChangedSinceCaptureIsMarkedChanged()
        {
            var source = new AnalysisResult(LinkedThermodynamicUncertaintyTests.Create(
                VariableConstraint.SameForAll, gibbs: -25000, enthalpy: -40000));
            var attribute = ExperimentAttribute.CompetitorResultReference(source.UniqueID);
            attribute.SourceSolutionId = "older-solution";
            attribute.CapturedAffinity = new FloatWithError(2e-6);
            attribute.CapturedEnthalpy = new FloatWithError(-32000);

            var preview = CompetitorResultPreviewBuilder.Build(attribute, new ExperimentData("target.itc"), source);

            Assert.Equal("Changed", preview.Status);
            Assert.Contains("Saved: Kd", preview.Tooltip);
        }

        [Fact]
        public void InvalidSourceIsStaleEvenWhenItsSolutionDiffersFromCapture()
        {
            var source = new AnalysisResult(LinkedThermodynamicUncertaintyTests.Create(
                VariableConstraint.SameForAll, gibbs: -25000, enthalpy: -40000));
            source.Model.Models[0].Data.CellConcentration = new FloatWithError(99e-6);
            var attribute = ExperimentAttribute.CompetitorResultReference(source.UniqueID);
            attribute.SourceSolutionId = "older-solution";
            attribute.CapturedAffinity = new FloatWithError(2e-6);
            attribute.CapturedEnthalpy = new FloatWithError(-32000);

            var preview = CompetitorResultPreviewBuilder.Build(attribute, source.Model.Models[0].Data, source);

            Assert.Equal("Stale", preview.Status);
            Assert.Contains("Source result is stale", preview.Tooltip);
            Assert.Contains("Saved: Kd", preview.Tooltip);
        }

        [Fact]
        public void SourceSummaryTemperatureMismatchAppearsInPreview()
        {
            var source = new AnalysisResult(LinkedThermodynamicUncertaintyTests.Create(
                VariableConstraint.SameForAll, gibbs: -25000, enthalpy: -40000,
                affinity: VariableConstraint.TemperatureDependent,
                temperaturesOverride: new[] { 300.0, 300.0, 300.0 }));
            var experiment = new ExperimentData("target.itc") { MeasuredTemperature = 35 };
            var attribute = ExperimentAttribute.CompetitorResultReference(source.UniqueID);

            var preview = CompetitorResultPreviewBuilder.Build(attribute, experiment, source);

            Assert.Contains("Summary at", preview.Tooltip);
            Assert.Contains("experiment at 35 °C", preview.Tooltip);
        }

        [Fact]
        public void MissingValiditySnapshotIsUnknown()
        {
            var source = new AnalysisResult(LinkedThermodynamicUncertaintyTests.Create(
                VariableConstraint.SameForAll, gibbs: -25000, enthalpy: -40000), captureValiditySnapshot: false);
            var attribute = ExperimentAttribute.CompetitorResultReference(source.UniqueID);

            var preview = CompetitorResultPreviewBuilder.Build(attribute, new ExperimentData("target.itc"), source);

            Assert.Equal("Unknown", preview.Status);
            Assert.Contains(source.Name, preview.Tooltip);
        }

        [Fact]
        public void SourceWithParameterBoundaryWarningShowsWarning()
        {
            var source = new AnalysisResult(LinkedThermodynamicUncertaintyTests.Create(
                VariableConstraint.SameForAll, gibbs: -25000, enthalpy: -40000));
            source.Solution.Solutions[0].RestoreParameterBoundaryHit(true);
            var attribute = ExperimentAttribute.CompetitorResultReference(source.UniqueID);

            var preview = CompetitorResultPreviewBuilder.Build(attribute, new ExperimentData("target.itc"), source);

            Assert.Equal("Warning", preview.Status);
        }
    }
}
