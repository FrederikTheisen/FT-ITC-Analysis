using System;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    [CollectionDefinition("Publication font settings", DisableParallelization = true)]
    public sealed class PublicationFontSettingsCollectionDefinition
    {
    }

    [Collection("Publication font settings")]
    public sealed class PublicationFontPreferenceTests : IDisposable
    {
        readonly InMemorySettingsStore store = new InMemorySettingsStore();

        public PublicationFontPreferenceTests()
        {
            PlatformServices.RegisterSettingsStore(store);
            AppSettings.Reset();
        }

        public void Dispose()
        {
            AppSettings.Reset();
            PlatformServices.RegisterSettingsStore(null);
        }

        [Fact]
        public void MissingAndInvalidStoredValuesNormalizeToNative()
        {
            store.SetBool("IsSaved", true);
            AppSettings.PublicationFigureFont = PublicationFont.Inter;

            AppSettings.Load();

            Assert.Equal(PublicationFont.Native, AppSettings.PublicationFigureFont);

            store.SetInt("PublicationFigureFont", 999);
            AppSettings.PublicationFigureFont = PublicationFont.LiberationSans;

            AppSettings.Load();

            Assert.Equal(PublicationFont.Native, AppSettings.PublicationFigureFont);
        }

        [Fact]
        public void SettingSavesLoadsAndResets()
        {
            AppSettings.PublicationFigureFont = PublicationFont.Inter;
            AppSettings.Save();
            Assert.Equal((int)PublicationFont.Inter, store.GetInt("PublicationFigureFont"));

            AppSettings.PublicationFigureFont = PublicationFont.Native;
            AppSettings.Load();
            Assert.Equal(PublicationFont.Inter, AppSettings.PublicationFigureFont);

            AppSettings.Reset();
            Assert.Equal(PublicationFont.Native, AppSettings.PublicationFigureFont);
        }

        [Fact]
        public void InvalidRuntimeValueIsNormalizedBeforeSaving()
        {
            AppSettings.PublicationFigureFont = (PublicationFont)(-12);

            AppSettings.Save();

            Assert.Equal(PublicationFont.Native, AppSettings.PublicationFigureFont);
            Assert.Equal((int)PublicationFont.Native, store.GetInt("PublicationFigureFont"));
        }

        [Fact]
        public void NewOptionsCapturePreferenceAndFontChangesCacheKey()
        {
            AppSettings.PublicationFigureFont = PublicationFont.Inter;
            var captured = new PublicationFigureOptions();
            Assert.Equal(PublicationFont.Inter, captured.Font);

            var native = new PublicationFigureOptions { Font = PublicationFont.Native };
            var liberation = new PublicationFigureOptions { Font = PublicationFont.LiberationSans };

            Assert.NotEqual(native.CacheKey, liberation.CacheKey);
        }
    }
}
