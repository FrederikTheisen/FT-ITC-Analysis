using System.Globalization;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class PublicationFigureCultureTests
{
    [Fact]
    public void FigureAnnotationsTicksAndMetadataUseInvariantNumbersAcrossCultures()
    {
        var experiment = new ExperimentData("publication, sample.itc")
        {
            Name = "publication, sample",
            MeasuredTemperature = 25.35,
            CellConcentration = new FloatWithError(30.25e-6),
            SyringeConcentration = new FloatWithError(400.75e-6),
        };
        var model = new OneSetOfSites(experiment);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1.2345);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 5.1234);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -12345.67);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 12.3456);
        var solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Loss = 12.3456,
        }));
        solution.Parameters[ParameterType.Affinity1] = new FloatWithError(5.1234, .12, 4.8765, 5.3456);
        solution.Parameters[ParameterType.Enthalpy1] = new FloatWithError(-12345.67, 12.3, -12370.25, -12320.125);

        var options = new PublicationFigureOptions
        {
            ShowThermogram = false,
            ShowResiduals = false,
            DisplayParameters = FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Nvalue
                | FinalFigureDisplayParameters.Affinity | FinalFigureDisplayParameters.Enthalpy
                | FinalFigureDisplayParameters.Temperature,
            TextUncertaintyStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
            XAxisTitle = "User, selected axis",
        };
        var previousCulture = CultureInfo.CurrentCulture;
        string expectedMetadata = null;
        string expectedAnnotations = null;
        string expectedTicks = null;

        try
        {
            foreach (var cultureName in new[] { "en-US", "da-DK", "de-DE", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                var document = PublicationFigureBuilder.Build(new PublicationFigureSource(experiment, solution), options);
                var metadata = string.Join("\n", document.MetadataKeywords);
                var annotations = string.Join("\n", document.FitPanel.AnnotationBoxes.SelectMany(box => box.Lines));
                var ticks = string.Join("|", document.FitPanel.XAxis.MajorTicks.Select(document.FitPanel.XAxis.FormatTick));

                Assert.Equal(cultureName, CultureInfo.CurrentCulture.Name);
                Assert.Contains("User, selected axis", document.FitPanel.XAxis.Title);
                Assert.Contains("publication, sample", document.Title);
                Assert.Contains("N = 1.2345", annotations);
                Assert.Contains("∆*H* = -12.346 ± 0.012 [-12.370, -12.320] kJ/mol", annotations);
                Assert.Contains("Loss: 12.3", metadata);
                Assert.Contains("Kd =", metadata);
                Assert.Contains("[-12.370.. -12.320]", metadata);
                Assert.Contains("[5, 13]", annotations);
                Assert.DoesNotContain("5,1234", metadata);
                Assert.DoesNotContain("12,3", metadata);

                expectedMetadata ??= metadata;
                expectedAnnotations ??= annotations;
                expectedTicks ??= ticks;
                Assert.Equal(expectedMetadata, metadata);
                Assert.Equal(expectedAnnotations, annotations);
                Assert.Equal(expectedTicks, ticks);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void TickFormattingIsInvariantForDeferredCalls()
    {
        var axis = new PublicationAxis("", PublicationAxisPlacement.Bottom, 0, 10, 3);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var cultureName in new[] { "en-US", "da-DK", "de-DE", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                Assert.Equal("2.5", axis.FormatTick(2.5));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
