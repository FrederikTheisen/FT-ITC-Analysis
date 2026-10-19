using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class SettingsUpdateTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();
    readonly ISettingsStore originalStore = PlatformServices.SettingsStore;
    readonly IMainThreadDispatcher originalDispatcher = PlatformServices.MainThreadDispatcher;
    readonly string originalPublicCode = AppSettings.InterpretationPublicClientCode;
    readonly string originalPath = AppSettings.LastDocumentPath;
    readonly string[] originalPaths = AppSettings.LastDocumentPaths;
    readonly LiveOptions originalLive = LiveOptions.Capture();
    readonly RecordingStore store = new();

    public SettingsUpdateTests()
    {
        PlatformServices.RegisterSettingsStore(store);
        PlatformServices.RegisterMainThreadDispatcher(null);
        PreferencesState.Defaults().ApplyToSettings();
        AppSettings.DefaultSolverAlgorithm = SolverAlgorithm.NelderMead;
        AppSettings.DefaultErrorEstimationMethod = ErrorEstimationMethod.None;
        AppSettings.DefaultBootstrapIterations = 10;
        AppSettings.IncludeConcentrationErrorsInBootstrap = false;
        AppSettings.UseInjectionErrorWeightedFitting = false;
        new LiveOptions(SolverAlgorithm.LevenbergMarquardt, ErrorEstimationMethod.BootstrapResiduals,
            1000, true, true, true).Restore();
    }

    public void Dispose()
    {
        PlatformServices.RegisterMainThreadDispatcher(null);
        store.FailSynchronization = false;
        original.ApplyToSettings();
        AppSettings.InterpretationPublicClientCode = originalPublicCode;
        AppSettings.LastDocumentPath = originalPath;
        AppSettings.LastDocumentPaths = originalPaths;
        originalLive.Restore();
        PlatformServices.RegisterSettingsStore(originalStore);
        PlatformServices.RegisterMainThreadDispatcher(originalDispatcher);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("apply-settings")]
    [InlineData("preferences")]
    [InlineData("changed-fitting-preferences")]
    [InlineData("document-path")]
    [InlineData("document-paths")]
    [InlineData("export-preferences")]
    public void OrdinarySettingsChangesPreserveEveryLiveFittingOption(string action)
    {
        var expected = LiveOptions.Capture();
        switch (action)
        {
            case "apply-settings": AppSettings.ApplySettings(); break;
            case "preferences": PreferencesState.FromSettings().Apply(); break;
            case "changed-fitting-preferences":
                var state = PreferencesState.FromSettings();
                state.DefaultBootstrapIterations = 500;
                state.DefaultErrorEstimationMethod = ErrorEstimationMethod.LeaveOneOut;
                state.Apply();
                Assert.Equal(500, store.GetInt("DefaultBootstrapIterations"));
                Assert.Equal((int)ErrorEstimationMethod.LeaveOneOut, store.GetInt("DefaultErrorEstimationMethod"));
                break;
            case "document-path": AppSettings.LastDocumentPath = "/tmp/current.ftxtc"; break;
            case "document-paths": AppSettings.LastDocumentPaths = new[] { "/tmp/current.ftxtc" }; break;
            case "export-preferences":
                AppSettings.ExportBaselineCorrectedData = !AppSettings.ExportBaselineCorrectedData;
                AppSettings.Save();
                Assert.Equal(AppSettings.ExportBaselineCorrectedData, store.GetBool("ExportBaselineCorrectedData"));
                break;
            default: AppSettings.Save(); break;
        }
        Assert.Equal(expected, LiveOptions.Capture());
    }

    [Fact]
    public void LoadingAndExplicitResetUseSavedDefaultsRatherThanTheLiveOptions()
    {
        var staged = PreferencesState.FromSettings();
        staged.DefaultSolverAlgorithm = SolverAlgorithm.NelderMead;
        staged.DefaultErrorEstimationMethod = ErrorEstimationMethod.LeaveOneOut;
        staged.DefaultBootstrapIterations = 500;
        staged.IncludeConcentrationErrorsInBootstrap = false;
        staged.UseInjectionErrorWeightedFitting = false;
        staged.Apply();
        var expected = new LiveOptions(SolverAlgorithm.NelderMead, ErrorEstimationMethod.LeaveOneOut,
            500, false, false, false);
        FittingOptionsController.ResetToPreferenceDefaults();
        Assert.Equal(expected, LiveOptions.Capture());

        new LiveOptions(SolverAlgorithm.LevenbergMarquardt, ErrorEstimationMethod.None,
            50, true, true, true).Restore();
        AppSettings.Reset();
        AppSettings.Load();
        Assert.Equal(expected, LiveOptions.Capture());
    }

    [Theory]
    [InlineData("save", true, true)]
    [InlineData("apply", true, false)]
    [InlineData("verification", false, true)]
    [InlineData("account", false, true)]
    public async Task WorkerNotificationsRunOnDispatcherInOrderAfterPersistence(string action, bool applied, bool updated)
    {
        var dispatcher = new QueuedDispatcher();
        PlatformServices.RegisterMainThreadDispatcher(dispatcher);
        var observed = new List<string>();
        var thread = Environment.CurrentManagedThreadId;
        EventHandler onApplied = (_, _) => Observe("applied");
        EventHandler onUpdated = (_, _) => Observe("updated");
        AppSettings.SettingsApplied += onApplied;
        AppSettings.SettingsDidUpdate += onUpdated;
        try
        {
            await Task.Run(() =>
            {
                switch (action)
                {
                    case "apply": AppSettings.ApplySettings(); break;
                    case "verification": AppSettings.PersistInterpretationAccessVerification("test-code", AccessOptions()); break;
                    case "account": AppSettings.PersistInterpretationAccount("test-code", new InterpretationAccountResponse()); break;
                    default: AppSettings.Save(); break;
                }
            });
            Assert.Empty(observed);
            Assert.Equal(1, dispatcher.Count);
            // The dispatcher owner is the thread draining its queue, independent of xUnit continuations.
            thread = Environment.CurrentManagedThreadId;
            dispatcher.Drain();
            Assert.Equal(applied ? (updated ? new[] { "applied", "updated" } : new[] { "applied" })
                : new[] { "updated" }, observed);
        }
        finally
        {
            AppSettings.SettingsApplied -= onApplied;
            AppSettings.SettingsDidUpdate -= onUpdated;
        }

        void Observe(string name)
        {
            Assert.Equal(thread, Environment.CurrentManagedThreadId);
            if (action != "apply") Assert.True(store.Synchronizations > 0);
            observed.Add(name);
        }
    }

    [Fact]
    public void FailedPersistencePublishesNeitherSettingsEventAndPreservesLiveOptions()
    {
        var observed = new List<string>();
        EventHandler onApplied = (_, _) => observed.Add("applied");
        EventHandler onUpdated = (_, _) => observed.Add("updated");
        var expected = LiveOptions.Capture();
        AppSettings.SettingsApplied += onApplied;
        AppSettings.SettingsDidUpdate += onUpdated;
        try
        {
            store.FailSynchronization = true;
            Assert.Throws<IOException>(AppSettings.Save);
            Assert.Empty(observed);
            Assert.Equal(expected, LiveOptions.Capture());
        }
        finally
        {
            AppSettings.SettingsApplied -= onApplied;
            AppSettings.SettingsDidUpdate -= onUpdated;
        }
    }

    [Fact]
    public async Task AsynchronousEnrollmentDispatchesNotificationsPreservesFitAndReusesPersistedCredential()
    {
        var dispatcher = new QueuedDispatcher();
        PlatformServices.RegisterMainThreadDispatcher(dispatcher);
        AppSettings.InterpretationPublicClientCode = "";
        var expected = LiveOptions.Capture();
        using var handler = new EnrollmentHandler();
        using var http = new HttpClient(handler);
        var client = new FtItcInterpretationClient(http, new Uri("https://example.invalid"));
        var observed = new List<string>();
        EventHandler onApplied = (_, _) => observed.Add("applied");
        EventHandler onUpdated = (_, _) => observed.Add("updated");
        AppSettings.SettingsApplied += onApplied;
        AppSettings.SettingsDidUpdate += onUpdated;
        try
        {
            var enrollment = client.GetInterpretationOptionsAsync("");
            Assert.False(enrollment.IsCompleted);
            handler.Issued.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"publicAccessCode\":\"ftitc_pub_thread_test\"}") });
            await enrollment;
            Assert.Empty(observed);
            Assert.Equal("ftitc_pub_thread_test", store.GetString("InterpretationPublicClientCode"));
            Assert.Equal(expected, LiveOptions.Capture());
            dispatcher.Drain();
            Assert.Equal(new[] { "applied", "updated" }, observed);
            Assert.Equal(expected, LiveOptions.Capture());
            await client.GetInterpretationOptionsAsync("");
            Assert.Equal(1, handler.Enrollments);
            Assert.Equal(new[] { "Bearer ftitc_pub_thread_test", "Bearer ftitc_pub_thread_test" }, handler.Authorizations);
            Assert.Equal(0, dispatcher.Count);
        }
        finally
        {
            AppSettings.SettingsApplied -= onApplied;
            AppSettings.SettingsDidUpdate -= onUpdated;
        }
    }

    static InterpretationOperatorOptionsResponse AccessOptions() => new()
    {
        Mode = "presets", AccessTier = "standard",
        Presets = new List<InterpretationPresetOption> { new() { Id = "instant", Name = "Fast" } }
    };

    readonly record struct LiveOptions(SolverAlgorithm Algorithm, ErrorEstimationMethod Method, int Iterations,
        bool Concentration, bool Weighted, bool Unlock)
    {
        public static LiveOptions Capture() => new(FittingOptionsController.Algorithm,
            FittingOptionsController.ErrorEstimationMethod, FittingOptionsController.BootstrapIterations,
            FittingOptionsController.IncludeConcentrationVariance, FittingOptionsController.UseErrorWeightedFitting,
            FittingOptionsController.UnlockBootstrapParameters);
        public void Restore()
        {
            FittingOptionsController.Algorithm = Algorithm;
            FittingOptionsController.ErrorEstimationMethod = Method;
            FittingOptionsController.BootstrapIterations = Iterations;
            FittingOptionsController.IncludeConcentrationVariance = Concentration;
            FittingOptionsController.UseErrorWeightedFitting = Weighted;
            FittingOptionsController.UnlockBootstrapParameters = Unlock;
        }
    }

    sealed class QueuedDispatcher : IMainThreadDispatcher
    {
        readonly ConcurrentQueue<Action> actions = new();
        public int Count => actions.Count;
        public void Invoke(Action action) => actions.Enqueue(action);
        public void Drain() { while (actions.TryDequeue(out var action)) action(); }
    }

    sealed class EnrollmentHandler : HttpMessageHandler
    {
        public readonly TaskCompletionSource<HttpResponseMessage> Issued = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Enrollments;
        public readonly List<string> Authorizations = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri.AbsolutePath.EndsWith("public-access", StringComparison.Ordinal))
            {
                Enrollments++;
                return Issued.Task;
            }
            Authorizations.Add(request.Headers.Authorization.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"mode\":\"presets\",\"presets\":[{\"id\":\"instant\",\"name\":\"Fast\"}]}") });
        }
    }

    sealed class RecordingStore : ISettingsStore
    {
        readonly InMemorySettingsStore inner = new();
        public int Synchronizations;
        public bool FailSynchronization;
        public int Count => inner.Count;
        public bool Contains(string key) => inner.Contains(key);
        public bool GetBool(string key, bool defaultValue = false) => inner.GetBool(key, defaultValue);
        public int GetInt(string key, int defaultValue = 0) => inner.GetInt(key, defaultValue);
        public double GetDouble(string key, double defaultValue = 0) => inner.GetDouble(key, defaultValue);
        public string GetString(string key, string defaultValue = null) => inner.GetString(key, defaultValue);
        public double[] GetDoubleArray(string key, double[] defaultValue = null) => inner.GetDoubleArray(key, defaultValue);
        public string[] GetStringArray(string key, string[] defaultValue = null) => inner.GetStringArray(key, defaultValue);
        public void SetBool(string key, bool value) => inner.SetBool(key, value);
        public void SetInt(string key, int value) => inner.SetInt(key, value);
        public void SetDouble(string key, double value) => inner.SetDouble(key, value);
        public void SetString(string key, string value) => inner.SetString(key, value);
        public void SetDoubleArray(string key, double[] value) => inner.SetDoubleArray(key, value);
        public void SetStringArray(string key, string[] value) => inner.SetStringArray(key, value);
        public void Synchronize()
        {
            if (FailSynchronization) throw new IOException("Controlled settings write failure");
            Synchronizations++;
        }
    }
}
