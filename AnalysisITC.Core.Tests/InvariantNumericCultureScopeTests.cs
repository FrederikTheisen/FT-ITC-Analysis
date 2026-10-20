using System;
using System.Globalization;

using AnalysisITC.Core.Utilities;

using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class InvariantNumericCultureScopeTests
{
    [Fact]
    public void NestedScopesRestoreCurrentCultureOnceWithoutChangingOtherCultureSettings()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            var expected = CultureInfo.GetCultureInfo("da-DK");
            CultureInfo.CurrentCulture = expected;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            var outer = new InvariantNumericCultureScope();
            Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.CurrentCulture);
            using (var inner = new InvariantNumericCultureScope())
            {
                Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.CurrentCulture);
            }
            Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.CurrentCulture);

            outer.Dispose();
            outer.Dispose();

            Assert.Equal(expected, CultureInfo.CurrentCulture);
            Assert.Equal(CultureInfo.GetCultureInfo("fr-FR"), CultureInfo.CurrentUICulture);
            Assert.Equal(originalDefaultCulture, CultureInfo.DefaultThreadCurrentCulture);
            Assert.Equal(originalDefaultUiCulture, CultureInfo.DefaultThreadCurrentUICulture);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void ScopeRestoresCultureWhenSynchronousWorkThrows()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var scope = new InvariantNumericCultureScope();
                Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.CurrentCulture);
                throw new InvalidOperationException();
            }));
            Assert.Equal(CultureInfo.GetCultureInfo("fr-FR"), CultureInfo.CurrentCulture);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
