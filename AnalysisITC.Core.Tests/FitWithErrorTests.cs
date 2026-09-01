using System;

using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class FitWithErrorTests
    {
        [Fact]
        public void LinearEvaluationPropagatesAsymmetricUncertaintyWithoutSampling()
        {
            var slope = new FloatWithError(2.0, 0.5, 1.0, 4.0);
            var intercept = new FloatWithError(10.0, 1.0, 8.0, 13.0);
            var fit = new LinearFitWithError(slope, intercept, referencex: 1.0);

            var result = fit.Evaluate(3.0, iterations: 100_000);

            Assert.Equal(14.0, result.Value);
            Assert.Equal(Math.Sqrt(2.0), result.SD, 12);
            Assert.Equal(14.0 - Math.Sqrt(8.0), result.Lower, 12);
            Assert.Equal(19.0, result.Upper, 12);
        }

        [Fact]
        public void LinearEvaluationBelowReferenceKeepsConfidenceBoundsOrdered()
        {
            var slope = new FloatWithError(2.0, 0.5, 1.0, 4.0);
            var intercept = new FloatWithError(10.0, 1.0, 8.0, 13.0);
            var fit = new LinearFitWithError(slope, intercept, referencex: 1.0);

            var result = fit.Evaluate(-1.0);

            Assert.Equal(6.0, result.Value);
            Assert.Equal(Math.Sqrt(2.0), result.SD, 12);
            Assert.Equal(6.0 - Math.Sqrt(20.0), result.Lower, 12);
            Assert.Equal(6.0 + Math.Sqrt(13.0), result.Upper, 12);
        }

        [Fact]
        public void LinearEvaluationDoesNotDependOnIterationArgument()
        {
            var fit = new LinearFitWithError(
                new FloatWithError(2.0, 0.5, 1.0, 4.0),
                new FloatWithError(10.0, 1.0, 8.0, 13.0),
                referencex: 1.0);

            var oneIteration = fit.Evaluate(3.0, iterations: 1);
            var manyIterations = fit.Evaluate(3.0, iterations: 100_000);

            Assert.Equal(oneIteration.Value, manyIterations.Value);
            Assert.Equal(oneIteration.SD, manyIterations.SD);
            Assert.Equal(oneIteration.Lower, manyIterations.Lower);
            Assert.Equal(oneIteration.Upper, manyIterations.Upper);
        }
    }
}
