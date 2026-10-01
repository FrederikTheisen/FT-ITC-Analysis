using System;
using System.Reflection;
using System.Threading.Tasks;
using AnalysisITC.Core.Data;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class BufferIterationTests
{
    static double IonicStrength(BufferAttribute buffer, double pH, double concentration, double temperature)
        => (double)typeof(BufferAttribute)
            .GetMethod("GetBufferIonicStrength", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(buffer, new object[] { pH, concentration, temperature });

    [Fact]
    public void ConvergingCorrectionRetainsIndependentSpeciesBalanceReference()
    {
        var buffer = new BufferAttribute("reference", 7, 0, 0, "", 0);
        var actual = IonicStrength(buffer, pH: 7, concentration: 0.1, temperature: 25);

        // Independent scalar root in ionic strength, using species balance
        // I = C / (1 + 10^(pKa(I) − pH)), rather than pKa fixed-point iteration.
        Assert.InRange(actual, 0.05526610144721031 - 1e-6, 0.05526610144721031 + 1e-6);
    }

    [Fact]
    public async Task NonConvergingCorrectionReturnsItsLastEstimateWithinTimeLimit()
    {
        var buffer = new BufferAttribute("phosphate", 7.198, -0.0028, -1, "", 0);

        // This input enters a repeating cycle. The cap promises termination,
        // not that its last iterate is a converged scientific result.
        var actual = await Task.Run(() => IonicStrength(buffer, 8.198, 5, 25))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(double.IsFinite(actual));
    }
}
