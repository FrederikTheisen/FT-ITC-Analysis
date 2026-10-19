using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Viewer;
using Xunit;
using Buffer = AnalysisITC.Core.Data.Buffer;

namespace AnalysisITC.Core.Tests
{
    [Collection("AutoSaveManager")]
    public sealed class ViewerAttributePresentationTests
    {
        [Fact]
        public async Task SavedConditionsRetainTheirTypedMeaningInViewerMetadata()
        {
            var experiment = new ExperimentData("conditions.itc");
            var buffer = ExperimentAttribute.FromKey(AttributeKey.Buffer);
            buffer.IntValue = (int)Buffer.Hepes;
            buffer.ParameterValue = new FloatWithError(0.02);
            buffer.DoubleValue = 7.4;
            var salt = ExperimentAttribute.FromKey(AttributeKey.Salt);
            salt.IntValue = (int)Salt.NaCl;
            salt.ParameterValue = new FloatWithError(0.15);
            experiment.Attributes.AddRange(new[] { buffer, salt });
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { experiment });
            package.Position = 0;

            var document = await new ViewerDocumentReader().ReadAsync(package, "conditions.ftxtc", ViewerFileFormat.Ftxtc);

            var metadata = Assert.Single(document.Experiments).Metadata;
            Assert.Equal("20 mM Hepes pH 7.4", Assert.Single(metadata, row => row.Label == "Buffer").Value);
            Assert.Equal("150 mM NaCl", Assert.Single(metadata, row => row.Label == "Salt").Value);
        }

        [Fact]
        public async Task RecordedAttributesKeepOrderUnitsLocationsAndCustomLabels()
        {
            var experiment = new ExperimentData("typed.itc");
            var buffer = Condition(AttributeKey.Buffer, 0.02, (int)Buffer.SodiumPhosphate, 7.4);
            var salt = Condition(AttributeKey.Salt, 0.15, (int)Salt.NaCl);
            salt.OptionName = "Added salt";
            var ionic = Condition(AttributeKey.IonicStrength, 0.15, scalar: 999);
            experiment.Attributes.AddRange(new[]
            {
                buffer, salt, Condition(AttributeKey.Salt, 0.01, (int)Salt.Na2SO4), ionic,
                Condition(AttributeKey.PreboundLigandConc, 2e-6),
                Condition(AttributeKey.PreboundLigandConc, 0),
                ExperimentAttribute.Species(ExperimentSpeciesLocation.Cell, "Protein"),
                ExperimentAttribute.Species(ExperimentSpeciesLocation.Syringe, "Ligand"),
                ExperimentAttribute.Species(ExperimentSpeciesLocation.Cell, " "),
            });

            var viewer = await Read(experiment);

            // Native persistence sorts keys; metadata retains that read order, including repeats.
            Assert.Equal(new[] { "Buffer", "Ionic Strength", "[Ligand]", "[Ligand]", "Added salt", "Salt", "Species", "Species" },
                viewer.Metadata.TakeLast(8).Select(item => item.Label));
            Assert.Equal(new[] { "20 mM NaHPO4 pH 7.4", "150 mM", "2 µM", "0 µM", "150 mM NaCl",
                "10 mM Na2SO4", "Cell: Protein", "Syringe: Ligand" }, viewer.Metadata.TakeLast(8).Select(item => item.Value));
        }

        [Fact]
        public async Task LocalReferencesAndExperimentCapturesIgnoreGlobalDecoysAcrossDocuments()
        {
            var source = IndependentAssessmentTests.CreateIndependentResult(out var members);
            source.Name = "Reference";
            var reference = new ExperimentData("blank.itc") { Name = "Blank" };
            var target = new ExperimentData("target.itc");
            var subtraction = new BufferSubtractionSettings(reference.UniqueID, BufferSubtractionMethod.Linear).ToAttribute();
            var competitor = ExperimentAttribute.CompetitorResultReference(source.UniqueID);
            competitor.CapturedAffinity = new FloatWithError(2e-6);
            competitor.CapturedEnthalpy = new FloatWithError(-40000);
            target.Attributes.AddRange(new[] { subtraction, competitor });
            // The source has independently different parameter values; metadata must use captures.
            Assert.NotEqual(2e-6, members[0].Parameters[ParameterType.Affinity1].Value);
            var decoyExperiment = new ExperimentData("decoy.itc") { Name = "GLOBAL EXPERIMENT" };
            decoyExperiment.SetID(reference.UniqueID);
            var decoyResult = IndependentAssessmentTests.CreateIndependentResult(out _);
            decoyResult.SetID(source.UniqueID);
            decoyResult.Name = "GLOBAL RESULT";
            var previousItems = DataManager.SourceItems.ToArray();
            var previousSelection = DataManager.SelectedContentIndex;
            try
            {
                DataManager.Clear(DataClearMode.ResetSession);
                DataManager.AddData(new ITCDataContainer[] { decoyExperiment, decoyResult });
                Assert.Same(decoyExperiment, Assert.Single(DataManager.Data));
                Assert.Same(decoyResult, Assert.Single(DataManager.Results));

                var withLocalSources = await Read(target,
                    members.Select(member => member.Data).Append(reference).ToArray(), new[] { source });
                Assert.Equal("Blank (Linear)", Value(withLocalSources, "Reference"));
                Assert.Equal("From result \"Reference\"; saved on experiment: Kd 2 µM · ΔH -40 kJ/mol",
                    Value(withLocalSources, "Competitor properties"));

                var withoutLocalSources = await Read(target);
                Assert.Equal("Missing reference experiment (Linear)", Value(withoutLocalSources, "Reference"));
                Assert.Equal("Source result unavailable; saved on experiment: Kd 2 µM · ΔH -40 kJ/mol",
                    Value(withoutLocalSources, "Competitor properties"));
                Assert.Same(decoyExperiment, Assert.Single(DataManager.Data));
                Assert.Same(decoyResult, Assert.Single(DataManager.Results));
            }
            finally
            {
                DataManager.Clear(DataClearMode.ResetSession);
                DataManager.AddData(previousItems);
                DataManager.SelectIndex(previousSelection);
            }
        }

        [Fact]
        public async Task CulturePrecisionAndUncertaintyPreferencesDoNotChangeSavedPresentation()
        {
            var target = new ExperimentData("culture.itc");
            target.Attributes.Add(Condition(AttributeKey.Buffer, 0.02, (int)Buffer.Hepes, 7.4));
            target.Attributes.Add(Condition(AttributeKey.PreboundLigandConc, 1.23456e-6));
            var source = IndependentAssessmentTests.CreateIndependentResult(out var members);
            source.Name = "Reference";
            var competitor = ExperimentAttribute.CompetitorResultReference(source.UniqueID);
            competitor.CapturedAffinity = new FloatWithError(2e-6, 0.1e-6, 1.8e-6, 2.3e-6);
            competitor.CapturedEnthalpy = new FloatWithError(-40000, 1000, -42000, -38000);
            target.Attributes.Add(competitor);
            var previousCulture = CultureInfo.CurrentCulture;
            var previousPrecision = AppSettings.NumberPrecision;
            var previousStyle = AppSettings.UncertaintyDisplayStyle;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var baseline = await Read(target, members.Select(member => member.Data).ToArray(), new[] { source });
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
                AppSettings.NumberPrecision = NumberPrecision.SingleDecimal;
                AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.None;

                var viewer = await Read(target, members.Select(member => member.Data).ToArray(), new[] { source });

                Assert.Equal("20 mM Hepes pH 7.4", Value(viewer, "Buffer"));
                Assert.Equal("1.23456 µM", Value(viewer, "[Ligand]"));
                Assert.Equal("From result \"Reference\"; saved on experiment (± SD; brackets: 95% CI): Kd 2 ± 0.1 [1.8, 2.3] µM · ΔH -40 ± 1 [-42, -38] kJ/mol",
                    Value(viewer, "Competitor properties"));
                Assert.Equal(baseline.Metadata.Select(row => row.Value), viewer.Metadata.Select(row => row.Value));
                Assert.Equal("da-DK", CultureInfo.CurrentCulture.Name);
                Assert.Equal(NumberPrecision.SingleDecimal, AppSettings.NumberPrecision);
                Assert.Equal(UncertaintyDisplayStyle.None, AppSettings.UncertaintyDisplayStyle);
                Assert.Equal(1.8e-6, competitor.CapturedAffinity.Lower);
                Assert.Equal(2.3e-6, competitor.CapturedAffinity.Upper);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                AppSettings.NumberPrecision = previousPrecision;
                AppSettings.UncertaintyDisplayStyle = previousStyle;
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("missing")]
        public async Task MissingReferenceUsesStoredNameAndMethod(string referenceId)
        {
            var target = new ExperimentData("stored.itc");
            target.Attributes.Add(new BufferSubtractionSettings(referenceId, BufferSubtractionMethod.Linear,
                new BufferSubtractionReferenceSnapshot("Blank", Array.Empty<BufferSubtractionReferencePoint>())).ToAttribute());

            Assert.Equal("Blank (stored values, Linear)", Value(await Read(target), "Reference"));
        }

        [Theory]
        [InlineData(null, "Source result not recorded")]
        [InlineData("missing", "Source result unavailable")]
        public async Task MissingCompetitorValuesStayExplicit(string sourceId, string expectedAttribution)
        {
            var target = new ExperimentData("missing.itc");
            target.Attributes.Add(ExperimentAttribute.CompetitorResultReference(sourceId));

            Assert.Equal(expectedAttribution + "; saved on experiment: Kd unavailable · ΔH unavailable",
                Value(await Read(target), "Competitor properties"));
        }

        [Fact]
        public void PartialCompetitorCaptureDoesNotHideItsUsableValue()
        {
            var competitor = ExperimentAttribute.CompetitorResultReference(null);
            competitor.CapturedEnthalpy = new FloatWithError(-40000);
            Assert.Equal("Source result not recorded; saved on experiment: Kd unavailable · ΔH -40 kJ/mol", Format(competitor));
        }

        [Fact]
        public void MissingConditionsAreUnavailableWhileBlankSpeciesIsOmittedAndZeroIsRetained()
        {
            Assert.Equal("Unavailable", Format(Condition(AttributeKey.Buffer, 0.02, 999, 7.4)));
            Assert.Equal("Unavailable", Format(Condition(AttributeKey.Buffer, 0.02, (int)Buffer.Hepes, double.NaN)));
            Assert.Equal("Unavailable", Format(Condition(AttributeKey.Salt, 0.15, 999)));
            foreach (var key in new[] { AttributeKey.Buffer, AttributeKey.Salt, AttributeKey.IonicStrength, AttributeKey.PreboundLigandConc })
            {
                var attribute = Condition(key, double.NaN, scalar: 7.4);
                Assert.Equal("Unavailable", Format(attribute));
                attribute.ParameterValue = FloatWithError.NaN;
                Assert.Equal("Unavailable", Format(attribute));
            }
            Assert.Null(Format(ExperimentAttribute.Species(ExperimentSpeciesLocation.Cell, "")));
            Assert.Equal("0 mM", Format(Condition(AttributeKey.IonicStrength, 0, scalar: 99)));
            Assert.Equal("Missing reference experiment (Matched)",
                Format(new BufferSubtractionSettings(null, BufferSubtractionMethod.MatchedInjection).ToAttribute()));
        }

        [Fact]
        public void LegacyFallbackReadsDeclaredFieldsAndKeepsZeroAndFalse()
        {
            var boolean = ExperimentAttribute.Bool(AttributeKey.LockDuplicateParameter, "", false);
            boolean.StringValue = "unused";
            boolean.ParameterValue = new FloatWithError(123);
            Assert.Equal("No", Format(boolean));
            Assert.Equal("0", Format(ExperimentAttribute.Parameter(AttributeKey.Percentage, "", new FloatWithError(0))));
            Assert.Equal("Unavailable", Format(ExperimentAttribute.Parameter(AttributeKey.Percentage, "", FloatWithError.NaN)));
        }

        [Theory]
        [InlineData("NumberOfSites1;12;I:0;D:1;FWE:0,0;name:Stoichiometry", "1")]
        [InlineData("NumberOfSites2;14;I:0;D:1;FWE:0,0;name:Stoichiometry", "1")]
        [InlineData("NumberOfSites1;12;I:0;D:0.25;FWE:0,0;name:Stoichiometry", "0.25")]
        [InlineData("NumberOfSites2;14;I:0;D:1.25;FWE:0,0;name:Stoichiometry", "1.25")]
        [InlineData("NumberOfSites1;12;I:7;D:0;FWE:0,0;name:Stoichiometry", "0")]
        [InlineData("NumberOfSites2;14;I:7;D:0;FWE:0,0;name:Stoichiometry", "0")]
        public async Task SupportedLegacyReaderRetainsDecimalStoichiometryAttributes(string attributeRow, string expected)
        {
            // Constructed reader-reachable input, not evidence of occurrence in historical files.
            var text = "FTITCVersion:1.1\nFILE:Experiment:legacy.itc\nName:Legacy\nID:legacy-id\n"
                + "LIST:ExpAttributes\n" + attributeRow + "\nENDLIST\nENDFILE\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
                var document = await new ViewerDocumentReader().ReadAsync(stream, "legacy.ftitc", ViewerFileFormat.Ftitc);

                Assert.Equal(expected, Value(Assert.Single(document.Experiments), "Stoichiometry"));
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
        }

        [Fact]
        public void SavedIntervalsWithoutSdAreDisplayedWithoutInventingSd()
        {
            var attribute = Condition(AttributeKey.PreboundLigandConc, 2e-6);
            attribute.ParameterValue = new FloatWithError(2e-6, 0, 1.8e-6, 2.3e-6);
            Assert.Equal("2 [1.8, 2.3] µM (brackets: 95% CI)", Format(attribute));
        }

        static ExperimentAttribute Condition(AttributeKey key, double concentration, int identity = 0, double scalar = 0)
        {
            var attribute = ExperimentAttribute.FromKey(key);
            attribute.ParameterValue = new FloatWithError(concentration);
            attribute.IntValue = identity;
            attribute.DoubleValue = scalar;
            return attribute;
        }

        static string Format(ExperimentAttribute attribute) => ViewerAttributePresentation.Format(attribute, _ => null, _ => null);
        static string Value(ViewerExperiment experiment, string label) => Assert.Single(experiment.Metadata, row => row.Label == label).Value;

        static async Task<ViewerExperiment> Read(ExperimentData target, ExperimentData[] dependencies = null, AnalysisResult[] results = null)
        {
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { target }.Concat(dependencies ?? Array.Empty<ExperimentData>()), results);
            package.Position = 0;
            var document = await new ViewerDocumentReader().ReadAsync(package, "attributes.ftxtc", ViewerFileFormat.Ftxtc);
            return Assert.Single(document.Experiments, experiment => experiment.Name == target.Name);
        }
    }
}
