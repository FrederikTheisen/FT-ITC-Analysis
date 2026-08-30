using System;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class FloatWithErrorArithmeticTests
    {
        [Fact]
        public void ScalarMultiplicationPreservesUncertaintyAtZero()
        {
            var value = new FloatWithError(0.0, 1.0);

            var leftProduct = 2.0 * value;
            var rightProduct = value * 2.0;

            Assert.Equal(0.0, leftProduct.Value);
            Assert.Equal(2.0, leftProduct.SD);
            Assert.Equal(0.0, rightProduct.Value);
            Assert.Equal(2.0, rightProduct.SD);
        }

        [Fact]
        public void ScalarDivisionPreservesUncertaintyAtZero()
        {
            var value = new FloatWithError(0.0, 1.0);

            var quotient = value / 2.0;

            Assert.Equal(0.0, quotient.Value);
            Assert.Equal(0.5, quotient.SD);
        }

        [Fact]
        public void UncertainOperationsPreserveUncertaintyForZeroCenteredNumerator()
        {
            var numerator = new FloatWithError(0.0, 1.0);
            var factor = new FloatWithError(3.0, 0.5);
            var divisor = new FloatWithError(2.0, 0.1);

            var product = numerator * factor;
            var quotient = numerator / divisor;

            Assert.Equal(0.0, product.Value);
            Assert.Equal(3.0, product.SD, 12);
            Assert.Equal(0.0, quotient.Value);
            Assert.Equal(0.5, quotient.SD, 12);
        }

        [Fact]
        public void NegativeScalarOperationsKeepPositiveSdAndOrderedIntervals()
        {
            var value = new FloatWithError(0.0, 1.0, -3.0, 2.0);

            var product = -2.0 * value;
            var quotient = value / -2.0;

            Assert.Equal(2.0, product.SD);
            Assert.Equal(-4.0, product.Lower);
            Assert.Equal(6.0, product.Upper);
            Assert.Equal(0.5, quotient.SD);
            Assert.Equal(-1.0, quotient.Lower);
            Assert.Equal(1.5, quotient.Upper);
        }

        [Fact]
        public void NonzeroOperationsRetainFirstOrderPropagation()
        {
            var first = new FloatWithError(4.0, 0.2);
            var second = new FloatWithError(5.0, 0.3);

            var product = first * second;
            var quotient = first / second;
            var reciprocalScale = 2.0 / first;

            Assert.Equal(20.0, product.Value);
            Assert.Equal(Math.Sqrt(2.44), product.SD, 12);
            Assert.Equal(0.8, quotient.Value, 12);
            Assert.Equal(Math.Sqrt(0.003904), quotient.SD, 12);
            Assert.Equal(0.5, reciprocalScale.Value, 12);
            Assert.Equal(0.025, reciprocalScale.SD, 12);
        }

        [Fact]
        public void UncertainDivisionByZeroStillThrows()
        {
            var numerator = new FloatWithError(1.0, 0.1);
            var denominator = new FloatWithError(0.0, 1.0);

            Assert.Throws<DivideByZeroException>(() => numerator / denominator);
        }
    }
}
