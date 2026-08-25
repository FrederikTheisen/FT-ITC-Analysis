using System;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class ParameterBoundaryContactTests
    {
        [Fact]
        public void DetectorReportsLowerAndUpperContactsUsingScaleAwareTolerance()
        {
            var data = new ExperimentData("boundary-test.itc");
            data.SetID("experiment-id");
            var model = new Model(data);

            const double lower = -30000;
            const double upper = 30000;
            var tolerance = 1e-6 * (upper - lower);

            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, lower + tolerance * 0.5);
            var lowerContact = Assert.Single(ParameterBoundaryDetector.Detect(model));
            Assert.Equal(ParameterBoundarySide.Lower, lowerContact.Side);
            Assert.Equal(lower + tolerance * 0.5, lowerContact.FinalValue);
            Assert.Equal(lower, lowerContact.BoundValue);
            Assert.Equal("experiment-id", lowerContact.ExperimentIdentity);
            Assert.Equal("boundary-test", lowerContact.ExperimentName);

            model.Parameters.Table[ParameterType.Offset].Update(upper - tolerance * 0.5);
            var upperContact = Assert.Single(ParameterBoundaryDetector.Detect(model));
            Assert.Equal(ParameterBoundarySide.Upper, upperContact.Side);
            Assert.Equal(upper, upperContact.BoundValue);

            model.Parameters.Table[ParameterType.Offset].Update(lower + tolerance * 1.01);
            Assert.Empty(ParameterBoundaryDetector.Detect(model));
        }

        [Fact]
        public void DetectorIgnoresLockedParametersAndReportsSharedIdentity()
        {
            var data = new ExperimentData("shared-test.itc");
            var model = new Model(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, -30000, islocked: true);
            Assert.Empty(ParameterBoundaryDetector.Detect(model));

            var global = new GlobalModel();
            global.Parameters.AddorUpdateGlobalParameter(
                ParameterType.Offset,
                -30000,
                limits: new[] { -30000d, 30000d });
            var contact = Assert.Single(ParameterBoundaryDetector.Detect(global));
            Assert.Equal(ParameterBoundaryScope.Shared, contact.Scope);
            Assert.Null(contact.ExperimentIdentity);
            Assert.Equal(ParameterType.Offset.ToString(), contact.ParameterIdentity);
        }

        [Fact]
        public void CopyPreservesContactsButSnapshotDoesNotSerializeThem()
        {
            var original = SolverConvergence.ReportFailed(DateTime.UtcNow);
            original.SetParameterBoundaryContacts(new[]
            {
                new ParameterBoundaryContact(
                    ParameterType.Offset,
                    ParameterBoundaryScope.Local,
                    "exp-id",
                    "experiment",
                    ParameterBoundarySide.Upper,
                    30000,
                    30000),
            });

            var copy = original.Copy();
            var copyContact = Assert.Single(copy.ParameterBoundaryContacts);
            Assert.NotSame(original.ParameterBoundaryContacts[0], copyContact);
            Assert.Equal(ParameterBoundarySide.Upper, copyContact.Side);
            Assert.Equal("exp-id", copyContact.ExperimentIdentity);

            var restored = SolverConvergence.FromSnapshot(original.ToSnapshot());
            Assert.Empty(restored.ParameterBoundaryContacts);
        }

        [Fact]
        public void FormatterUsesExperimentNamesAndAggregatesAfterThree()
        {
            var contacts = Enumerable.Range(1, 4)
                .Select(index => new ParameterBoundaryContact(
                    ParameterType.Offset,
                    ParameterBoundaryScope.Local,
                    $"id-{index}",
                    $"experiment-{index}",
                    ParameterBoundarySide.Lower,
                    -30000,
                    -30000))
                .ToArray();

            var warning = ParameterBoundaryWarningFormatter.Format(contacts);
            Assert.Contains("Warning", warning);
            Assert.Contains("experiment-1", warning);
            Assert.Contains("experiment-3", warning);
            Assert.Contains("and 1 more", warning);
            Assert.DoesNotContain("experiment-4", warning);
        }

    }
}
