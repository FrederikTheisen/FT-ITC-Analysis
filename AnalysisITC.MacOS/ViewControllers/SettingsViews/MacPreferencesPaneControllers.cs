using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

using AppKit;
using Foundation;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Core.Interpretation;

namespace AnalysisITC
{
    public sealed partial class MacGeneralPreferencesViewController : MacPreferencesPaneController
    {
        static readonly int[] AutoSaveIntervalValues = { 1, 2, 5, 10, 20, 30 };

        int loadedAutoSaveInterval;
        bool autoSaveIntervalChanged;
        InterpretationOperatorOptionsResponse interpretationOptions;
        InterpretationAccountResponse interpretationAccount;
        DateTime? interpretationAccountFetchedAtUtc;
        CancellationTokenSource accountRefreshCancellation;
        bool loadingInterpretationState;
        NSPopUpButton InterpretationGuidancePopup;
        NSStackView InterpretationGuidanceRow;

        public MacGeneralPreferencesViewController(IntPtr handle) : base(handle) { }

        internal override int PaneIndex => 0;

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            PopulatePopup(EnergyUnitPopup,
                new[] { EnergyUnitFamily.Joules, EnergyUnitFamily.Calories },
                value => value == EnergyUnitFamily.Joules
                    ? "Joule"
                    : "Calories");
            PopulatePopup(ConcentrationUnitPopup, EnumValues<ConcentrationUnit>(),
                value => value.GetProperties().Name);
            PopulatePopup(NumberPrecisionPopup, EnumValues<NumberPrecision>(), FriendlyName);
            PopulatePopup(UncertaintyPopup, EnumValues<UncertaintyDisplayStyle>(), FriendlyName);
            PopulatePopup(InstrumentPopup, ITCInstrumentAttribute.GetITCInstruments().ToArray(),
                value => value.GetProperties().Name);
            ConfigureDiscreteSlider(AutoSaveIntervalSlider, AutoSaveIntervalValues.Length);
            CreateInterpretationGuidanceControl();
            ConfigureInterpretationEvaluationControls();
            InterpretationAccessDetailsLabel.Hidden = false;
            InterpretationAccessDetailsLabel.Cell.Wraps = true;
            InterpretationAccessDetailsLabel.Cell.UsesSingleLineMode = false;
            var accountInfoHeight = InterpretationAccessDetailsLabel.Constraints.FirstOrDefault(c => c.FirstAttribute == NSLayoutAttribute.Height);
            if (accountInfoHeight != null) accountInfoHeight.Constant = 96;
            UpdateAutoSaveControls();
        }

        public override void ViewWillDisappear()
        {
            accountRefreshCancellation?.Cancel();
            base.ViewWillDisappear();
        }

        internal override void LoadState(PreferencesState state)
        {
            loadingInterpretationState = true;
            SelectPopup(EnergyUnitPopup, state.EnergyUnitFamily);
            SelectPopup(ConcentrationUnitPopup, state.DefaultConcentrationUnit);
            SelectPopup(NumberPrecisionPopup, state.NumberPrecision);
            SelectPopup(UncertaintyPopup, state.UncertaintyDisplayStyle);
            SelectPopup(InstrumentPopup, state.DefaultDesignerInstrument);
            ReferenceTemperatureField.StringValue = Format(state.ReferenceTemperature);
            MinimumTemperatureSpanField.StringValue = Format(state.MinimumTemperatureSpanForFitting);
            MinimumIonSpanField.StringValue = Format(state.MinimumIonSpanForFitting * 1000);
            Set(IncludeBufferCheck, state.IncludeBufferInIonicStrengthCalc);
            Set(OnlineChecksCheck, state.PerformOnlineChecksOnLaunch);
            Set(ConfirmDeleteCheck, state.ConfirmRemoveDelete);
            Set(DiscardOrphanCheck, state.AutomaticallyDiscardOrphanInjectionsOnLoad);
            Set(AutoSaveEnabledCheck, state.AutoSaveEnabled);
            loadedAutoSaveInterval = state.AutoSaveIntervalMinutes;
            autoSaveIntervalChanged = false;
            AutoSaveIntervalSlider.DoubleValue = NearestIndex(AutoSaveIntervalValues, loadedAutoSaveInterval);
            UpdateAutoSaveIntervalLabel();
            AutoSaveLimitField.IntValue = state.AutoSaveFileLimit;
            Set(RecoveryPromptCheck, state.PromptForAutoSaveRecovery);
            InterpretationOperatorCodeField.StringValue = state.InterpretationOperatorCode ?? "";
            interpretationOptions = null;
            interpretationAccount = null;
            interpretationAccountFetchedAtUtc = null;
            if (state.TryGetInterpretationAccessOptions(out var cached))
            {
                interpretationOptions = cached;
                PopulateInterpretationChoices(state.InterpretationGenerationPreset,state.InterpretationEvaluationModel,state.InterpretationEvaluationReasoningEffort,state.InterpretationEvaluationGuidanceVariant);
                InterpretationAccessLabel.StringValue = "Access: Verified (cached)";
                if (state.TryGetInterpretationAccount(out var cachedAccount, out var fetchedAtUtc))
                {
                    interpretationAccount = cachedAccount;
                    interpretationAccountFetchedAtUtc = fetchedAtUtc;
                }
                UpdateInterpretationAccountSummary(cached: interpretationAccount != null);
            }
            else
            {
                SetPopupText(InterpretationModelPopup, state.InterpretationEvaluationModel);
                SetPopupText(InterpretationReasoningPopup, state.InterpretationEvaluationReasoningEffort);
                InterpretationAccessLabel.StringValue = "Access: Not verified";
                UpdateInterpretationAccountSummary(cached: false);
            }
            loadingInterpretationState = false;
            UpdateInterpretationControlVisibility();
            UpdateAutoSaveControls();
            if (interpretationOptions != null && !string.IsNullOrWhiteSpace(InterpretationOperatorCodeField.StringValue))
                _ = RefreshInterpretationAccountAsync(InterpretationOperatorCodeField.StringValue);
            else if (string.IsNullOrWhiteSpace(InterpretationOperatorCodeField.StringValue)) _ = RefreshPublicInterpretationOptionsAsync();
        }

        internal override bool TryUpdateState(PreferencesState state, out PreferencesValidationError error)
        {
            if (!ReadDouble(ReferenceTemperatureField, "reference temperature", -273.15, 500,
                out var referenceTemperature, out error)) return false;
            if (!ReadDouble(MinimumTemperatureSpanField, "minimum temperature span", 0, 100,
                out var minimumTemperatureSpan, out error)) return false;
            if (!ReadDouble(MinimumIonSpanField, "minimum ionic-strength span", 0, 10000,
                out var minimumIonSpan, out error)) return false;
            if (!ReadInt(AutoSaveLimitField, "autosave file limit", 1, 100,
                out var autoSaveLimit, out error)) return false;

            state.EnergyUnitFamily = PopupValue<EnergyUnitFamily>(EnergyUnitPopup);
            state.DefaultConcentrationUnit = PopupValue<ConcentrationUnit>(ConcentrationUnitPopup);
            state.NumberPrecision = PopupValue<NumberPrecision>(NumberPrecisionPopup);
            state.UncertaintyDisplayStyle = PopupValue<UncertaintyDisplayStyle>(UncertaintyPopup);
            state.DefaultDesignerInstrument = PopupValue<ITCInstrument>(InstrumentPopup);
            state.ReferenceTemperature = referenceTemperature;
            state.MinimumTemperatureSpanForFitting = minimumTemperatureSpan;
            state.MinimumIonSpanForFitting = minimumIonSpan / 1000;
            state.IncludeBufferInIonicStrengthCalc = IsOn(IncludeBufferCheck);
            state.PerformOnlineChecksOnLaunch = IsOn(OnlineChecksCheck);
            state.ConfirmRemoveDelete = IsOn(ConfirmDeleteCheck);
            state.AutomaticallyDiscardOrphanInjectionsOnLoad = IsOn(DiscardOrphanCheck);
            state.AutoSaveEnabled = IsOn(AutoSaveEnabledCheck);
            state.AutoSaveIntervalMinutes = autoSaveIntervalChanged
                ? AutoSaveIntervalValues[SliderIndex(AutoSaveIntervalSlider, AutoSaveIntervalValues.Length)]
                : loadedAutoSaveInterval;
            state.AutoSaveFileLimit = autoSaveLimit;
            state.PromptForAutoSaveRecovery = IsOn(RecoveryPromptCheck);
            state.InterpretationOperatorCode = InterpretationOperatorCodeField.StringValue ?? "";
            state.InterpretationEvaluationModel = InterpretationModelPopup.TitleOfSelectedItem ?? "";
            state.InterpretationEvaluationReasoningEffort = InterpretationReasoningPopup.TitleOfSelectedItem ?? "";
            state.InterpretationEvaluationGuidanceVariant = interpretationOptions?.GuidanceVariants
                .FirstOrDefault(x => x.DisplayName == InterpretationGuidancePopup?.TitleOfSelectedItem)?.Id ?? "standard";
            state.InterpretationGenerationPreset = interpretationOptions?.Presets.FirstOrDefault(x=>x.Name==InterpretationModelPopup.TitleOfSelectedItem)?.Id ?? "instant";
            state.InterpretationAccessVerified = interpretationOptions != null;
            state.InterpretationAccessCodeHash = state.InterpretationAccessVerified ? AppSettings.InterpretationAccessHash(state.InterpretationOperatorCode) : "";
            state.InterpretationAccessOptionsJson = state.InterpretationAccessVerified ? JsonSerializer.Serialize(interpretationOptions) : "";
            state.InterpretationAccessTier = state.InterpretationAccessVerified ? interpretationOptions?.AccessTier ?? "" : "";
            if (!state.InterpretationAccessVerified)
            {
                state.InterpretationAccountCodeHash = "";
                state.InterpretationAccountJson = "";
                state.InterpretationAccountFetchedAtUtc = "";
            }
            error = null;
            return true;
        }

        partial void AutoSaveEnabledChanged(NSObject sender) => UpdateAutoSaveControls();

        partial void AutoSaveIntervalChanged(NSObject sender)
        {
            autoSaveIntervalChanged = true;
            UpdateAutoSaveIntervalLabel();
        }

        partial void OpenAutoSaveFolder(NSObject sender)
        {
            try
            {
                Directory.CreateDirectory(AutoSaveManager.Shared.AutoSaveDirectory);
                AppDelegate.OpenAutoSaveFolder();
                Coordinator?.SetCurrentStatus("", false);
            }
            catch (Exception ex)
            {
                Coordinator?.SetCurrentStatus(ex.Message, true);
            }
        }

        void UpdateAutoSaveControls()
        {
            if (AutoSaveEnabledCheck == null) return;
            var enabled = IsOn(AutoSaveEnabledCheck);
            AutoSaveIntervalSlider.Enabled = enabled;
            AutoSaveIntervalValueLabel.Enabled = enabled;
            AutoSaveLimitField.Enabled = enabled;
            RecoveryPromptCheck.Enabled = enabled;
        }

        void UpdateAutoSaveIntervalLabel()
        {
            var value = autoSaveIntervalChanged
                ? AutoSaveIntervalValues[SliderIndex(AutoSaveIntervalSlider, AutoSaveIntervalValues.Length)]
                : loadedAutoSaveInterval;
            AutoSaveIntervalValueLabel.StringValue = $"{value} min";
        }

        void ConfigureInterpretationEvaluationControls()
        {
            InterpretationOperatorCodeField.Changed += (_, _) =>
            {
                if (loadingInterpretationState) return;
                InvalidateInterpretationAccess();
            };
            VerifyInterpretationAccessButton.Activated += async (_, _) => await VerifyInterpretationAccessAsync();
            InterpretationModelPopup.Activated += (_, _) => UpdateReasoningPopup();
        }

        async System.Threading.Tasks.Task VerifyInterpretationAccessAsync()
        {
            var code = InterpretationOperatorCodeField.StringValue ?? "";
            var previousModel = InterpretationModelPopup.TitleOfSelectedItem;
            accountRefreshCancellation?.Cancel();
            VerifyInterpretationAccessButton.Enabled = false; InterpretationAccessLabel.StringValue = "Verifying…";
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                var relay = new FtItcInterpretationClient(http, new Uri("https://app.ft-itc.org"));
                var options = await relay.GetInterpretationOptionsAsync(code);
                if (!string.Equals(code, InterpretationOperatorCodeField.StringValue ?? "", StringComparison.Ordinal)) return;
                interpretationOptions = options;
                AppSettings.PersistInterpretationAccessVerification(code, options);
                PopulateInterpretationChoices(AppSettings.InterpretationGenerationPreset,previousModel,AppSettings.InterpretationEvaluationReasoningEffort,AppSettings.InterpretationEvaluationGuidanceVariant);
                InterpretationAccessLabel.StringValue = "Access: Verified";
                UpdateInterpretationAccountSummary(cached: false);
                UpdateInterpretationControlVisibility();
                try
                {
                    var account = await relay.GetInterpretationAccountAsync(code);
                    if (!string.Equals(code, InterpretationOperatorCodeField.StringValue ?? "", StringComparison.Ordinal)) return;
                    interpretationAccount = account;
                    interpretationAccountFetchedAtUtc = DateTime.UtcNow;
                    AppSettings.PersistInterpretationAccount(code, account);
                    UpdateInterpretationAccountSummary(cached: false);
                }
                catch (AnalysisInterpretationProviderException accountDenied) when (accountDenied.Kind == AnalysisInterpretationFailureKind.AccessDenied)
                {
                    InvalidateInterpretationAccess(clearPersisted: true);
                }
                catch (Exception)
                {
                    UpdateInterpretationAccountSummary(cached: interpretationAccount != null);
                }
            }
            catch (Exception ex)
            {
                if (string.Equals(code, InterpretationOperatorCodeField.StringValue ?? "", StringComparison.Ordinal))
                {
                    if (ex is AnalysisInterpretationProviderException denied && denied.Kind == AnalysisInterpretationFailureKind.AccessDenied)
                        InvalidateInterpretationAccess(clearPersisted: true);
                    else
                        InterpretationAccessLabel.StringValue = ex.Message;
                }
            }
            finally { VerifyInterpretationAccessButton.Enabled = true; }
        }

        void InvalidateInterpretationAccess(bool clearPersisted = false)
        {
            accountRefreshCancellation?.Cancel();
            interpretationOptions = null;
            interpretationAccount = null;
            interpretationAccountFetchedAtUtc = null;
            InterpretationAccessLabel.StringValue = "Access: Not verified";
            UpdateInterpretationAccountSummary(cached: false);
            UpdateInterpretationControlVisibility();
            if (clearPersisted)
            {
                AppSettings.ClearInterpretationAccessVerification();
                AppSettings.Save();
            }
        }

        async System.Threading.Tasks.Task RefreshInterpretationAccountAsync(string code)
        {
            accountRefreshCancellation?.Cancel();
            accountRefreshCancellation?.Dispose();
            var cancellation = accountRefreshCancellation = new CancellationTokenSource();
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                var account = await new FtItcInterpretationClient(http, new Uri("https://app.ft-itc.org"))
                    .GetInterpretationAccountAsync(code, cancellation.Token);
                if (cancellation.IsCancellationRequested || !string.Equals(code, InterpretationOperatorCodeField.StringValue ?? "", StringComparison.Ordinal)) return;
                interpretationAccount = account;
                interpretationAccountFetchedAtUtc = DateTime.UtcNow;
                AppSettings.PersistInterpretationAccount(code, account);
                InterpretationAccessLabel.StringValue = "Access: Verified";
                UpdateInterpretationAccountSummary(cached: false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (AnalysisInterpretationProviderException ex) when (ex.Kind == AnalysisInterpretationFailureKind.AccessDenied)
            {
                if (string.Equals(code, InterpretationOperatorCodeField.StringValue ?? "", StringComparison.Ordinal))
                    InvalidateInterpretationAccess(clearPersisted: true);
            }
            catch
            {
                if (string.Equals(code, InterpretationOperatorCodeField.StringValue ?? "", StringComparison.Ordinal))
                    UpdateInterpretationAccountSummary(cached: interpretationAccount != null);
            }
            finally
            {
                if (ReferenceEquals(accountRefreshCancellation, cancellation)) accountRefreshCancellation = null;
                cancellation.Dispose();
            }
        }

        async System.Threading.Tasks.Task RefreshPublicInterpretationOptionsAsync()
        {
            try
            {
                using var client=new System.Net.Http.HttpClient { Timeout=TimeSpan.FromSeconds(20) };
                var current=await new FtItcInterpretationClient(client,new Uri("https://app.ft-itc.org")).GetInterpretationOptionsAsync("");
                if(!string.IsNullOrWhiteSpace(InterpretationOperatorCodeField.StringValue))return;
                interpretationOptions=current; UpdateInterpretationAccountSummary(cached:false);
            }
            catch { }
        }

        void UpdateReasoningPopup(string preferred = null)
        {
            if (interpretationOptions == null) return;
            var model = interpretationOptions.Models.FirstOrDefault(value => value.Id == InterpretationModelPopup.TitleOfSelectedItem);
            var previous = InterpretationReasoningPopup.TitleOfSelectedItem;
            InterpretationReasoningPopup.RemoveAllItems(); InterpretationReasoningPopup.AddItems((model?.ReasoningEfforts ?? new System.Collections.Generic.List<string>()).ToArray());
            SelectPopupText(InterpretationReasoningPopup, preferred ?? previous, interpretationOptions.DefaultReasoningEffort);
            InterpretationReasoningPopup.Enabled = model?.SelectionType != "summary";
        }

        void PopulateInterpretationChoices(string preset,string model,string reasoning,string guidance)
        {
            InterpretationModelPopup.RemoveAllItems();
            if(interpretationOptions?.Mode=="presets")
            {
                InterpretationModelPopup.AddItems(interpretationOptions.Presets.Select(x=>x.Name).ToArray());
                var selected=interpretationOptions.Presets.FirstOrDefault(x=>x.Id==preset)??interpretationOptions.Presets.FirstOrDefault(); if(selected!=null)InterpretationModelPopup.SelectItem(selected.Name);
            }
            else
            {
                InterpretationModelPopup.AddItems(interpretationOptions?.Models.Select(x=>x.Id).ToArray()??Array.Empty<string>()); SelectPopupText(InterpretationModelPopup,model,interpretationOptions?.DefaultModel);
                UpdateReasoningPopup(reasoning);
            }
            InterpretationGuidancePopup?.RemoveAllItems();
            InterpretationGuidancePopup?.AddItems(interpretationOptions?.GuidanceVariants.Select(x => x.DisplayName).ToArray() ?? Array.Empty<string>());
            var guidanceChoice = interpretationOptions?.GuidanceVariants.FirstOrDefault(x => x.Id == guidance)
                ?? interpretationOptions?.GuidanceVariants.FirstOrDefault(x => x.Id == interpretationOptions.DefaultGuidanceVariant)
                ?? interpretationOptions?.GuidanceVariants.FirstOrDefault();
            if (guidanceChoice != null) InterpretationGuidancePopup?.SelectItem(guidanceChoice.DisplayName);
        }

        void UpdateInterpretationControlVisibility()
        {
            var enabled=interpretationOptions != null; var custom=enabled&&interpretationOptions?.Mode=="custom";
            if(InterpretationModelPopup?.Superview!=null){InterpretationModelPopup.Superview.Hidden=!enabled;var label=InterpretationModelPopup.Superview.Subviews.OfType<NSTextField>().FirstOrDefault();if(label!=null)label.StringValue=custom?"Model":"Interpretation depth";}
            if(InterpretationReasoningPopup?.Superview!=null)InterpretationReasoningPopup.Superview.Hidden=!custom;
            if(InterpretationGuidanceRow!=null)InterpretationGuidanceRow.Hidden=!custom;
        }

        void CreateInterpretationGuidanceControl()
        {
            var parent = InterpretationReasoningPopup?.Superview?.Superview as NSStackView;
            if (parent == null) return;
            var label = NSTextField.CreateLabel("Scientific guidance");
            label.TranslatesAutoresizingMaskIntoConstraints = false;
            label.WidthAnchor.ConstraintEqualToConstant(350).Active = true;
            InterpretationGuidancePopup = new NSPopUpButton(CoreGraphics.CGRect.Empty, false)
            { TranslatesAutoresizingMaskIntoConstraints = false };
            InterpretationGuidancePopup.WidthAnchor.ConstraintEqualToConstant(240).Active = true;
            InterpretationGuidanceRow = new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Alignment = NSLayoutAttribute.FirstBaseline,
                Distribution = NSStackViewDistribution.Fill,
                Spacing = 10,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            InterpretationGuidanceRow.AddArrangedSubview(label);
            InterpretationGuidanceRow.AddArrangedSubview(InterpretationGuidancePopup);
            parent.AddArrangedSubview(InterpretationGuidanceRow);
        }

        static void SetPopupText(NSPopUpButton popup, string value)
        { popup.RemoveAllItems(); if (!string.IsNullOrWhiteSpace(value)) { popup.AddItem(value); popup.SelectItem(value); } }

        static void SelectPopupText(NSPopUpButton popup, string preferred, string fallback)
        { var titles = popup.ItemTitles(); var value = titles.Contains(preferred) ? preferred : fallback; if (titles.Contains(value)) popup.SelectItem(value); }

        void UpdateInterpretationAccountSummary(bool cached)
        {
            var options = interpretationOptions;
            var account = interpretationAccount;
            if (account == null && options == null)
            {
                InterpretationAccessDetailsLabel.StringValue = "Label: Not provided\nEmail: Not provided\nAccess level: Not available · Expires: Not available\nUsage: Not available · Prompts: Not available\nMost recent request: None · Status: Not available";
                return;
            }
            if (account != null)
            {
                InterpretationAccessDetailsLabel.StringValue = string.Join("\n", new[]
                {
                    FormatIdentity(account.Label, account.Name),
                    $"Email: {Display(account.Email)}",
                    $"Access level: {Display(account.AccessTierName ?? account.AccessTier)} · Expires: {FormatDate(account.ExpiresAtUtc)} · Request limit: {FormatRequestLimit(account.MaximumRequestBytes)}",
                    FormatUsage(account),
                    FormatMostRecentRequest(account),
                });
                if (cached && interpretationAccountFetchedAtUtc.HasValue)
                    InterpretationAccessLabel.StringValue = $"Access: Verified (cached; last checked {interpretationAccountFetchedAtUtc.Value.ToLocalTime():g})";
                return;
            }
            var tier = options?.AccessTierName ?? options?.AccessTier;
            InterpretationAccessDetailsLabel.StringValue = $"{FormatIdentity(options?.AccessDetails?.Name, null)}\nEmail: Not provided\nAccess level: {Display(tier)} · Expires: {(options?.AccessDetails == null ? "Not available" : FormatDate(options.AccessDetails.ExpiresAtUtc))} · Request limit: {FormatRequestLimit(options?.MaximumRequestBytes ?? 0)}\nUsage: Not available · Prompts: Not available\nMost recent request: None · Status: Not available";
        }

        static string FormatUsage(InterpretationAccountResponse account)
        {
            var usage = account?.Usage;
            if (usage == null) return "Usage: Not available · Prompts: Not available";
            var usageText = !usage.Limited ? "Usage: Unlimited" : usage.RemainingPercent.HasValue
                ? $"Usage: {100 - usage.RemainingPercent.Value}% used ({usage.RemainingPercent.Value}% remaining)"
                : "Usage: Not available";
            if (usage.Limited && usage.SpentUsd.HasValue && usage.LimitUsd.HasValue)
                usageText += $" · ${usage.SpentUsd.Value:0.##} / ${usage.LimitUsd.Value:0.##}";
            if (usage.Limited && usage.ResetsAtUtc.HasValue)
                usageText += $" · resets {usage.ResetsAtUtc.Value.ToLocalTime():d}";
            return usageText + $" · Prompts: {account.TotalRequests?.ToString() ?? "Not available"}";
        }

        static string FormatMostRecentRequest(InterpretationAccountResponse account)
        {
            var request = account?.MostRecentRequest;
            if (request == null) return "Most recent request: None · Status: Not available";
            var when = request.StartedAtUtc.HasValue ? request.StartedAtUtc.Value.ToLocalTime().ToString("g") : "Time unavailable";
            var outcome = string.IsNullOrWhiteSpace(request.Outcome) ? "Unknown" : request.Outcome;
            if (request.HttpStatus.HasValue) outcome += $" ({request.HttpStatus.Value})";
            return $"Most recent request: {when} · Status: {outcome}";
        }

        static string FormatDate(DateTime? value) => value.HasValue ? value.Value.ToLocalTime().ToString("d") : "No expiration";
        static string FormatRequestLimit(int bytes) => bytes <= 0 ? "Not available"
            : bytes % (1024 * 1024) == 0 ? $"{bytes / (1024 * 1024)} MiB" : $"{bytes / 1024} KiB";
        static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "Not provided" : value;
        static string FormatIdentity(string label, string name)
            => !string.IsNullOrWhiteSpace(name) ? $"Name: {name}" : $"Label: {Display(label)}";
    }

    public sealed partial class MacProcessingPreferencesViewController : MacPreferencesPaneController
    {
        public MacProcessingPreferencesViewController(IntPtr handle) : base(handle) { }

        internal override int PaneIndex => 1;

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            PopulatePopup(DilutionPopup, EnumValues<DilutionMethod>(), FriendlyName);
            PopulatePopup(BufferSubtractionPopup, EnumValues<BufferSubtractionMethod>(),
                value => value.GetDisplayName());
            PopulatePopup(SplineDensityPopup, EnumValues<SplineInterpolator.SplinePointDensity>(), FriendlyName);
            PopulatePopup(SplineHandlePopup, EnumValues<SplineInterpolator.SplineHandleMode>()
                .Where(mode => mode != SplineInterpolator.SplineHandleMode.MinVolatility)
                .ToArray(), FriendlyName);
        }

        internal override void LoadState(PreferencesState state)
        {
            SelectPopup(DilutionPopup, state.DilutionCalculationMethod);
            SelectPopup(BufferSubtractionPopup, state.BufferSubtractionDefaultMethod);
            SelectPopup(SplineDensityPopup, state.DefaultSplinePointDensity);
            SelectPopup(SplineHandlePopup, state.DefaultSplineHandleMode);
            Set(DiscardIntegrationCheck, state.DiscardIntegrationRegionForBaseline);
            Set(ReprocessIntegratedCheck, state.ReprocessIntegratedHeatDataOnLoad);
            Set(SplineTimeDraggingCheck, state.DefaultSplinePointTimeDragging);
            Set(CopyIntegrationStartCheck, state.IntegrationRegionCopyIncludesStart);
        }

        internal override bool TryUpdateState(PreferencesState state, out PreferencesValidationError error)
        {
            state.DilutionCalculationMethod = PopupValue<DilutionMethod>(DilutionPopup);
            state.BufferSubtractionDefaultMethod = PopupValue<BufferSubtractionMethod>(BufferSubtractionPopup);
            state.DefaultSplinePointDensity = PopupValue<SplineInterpolator.SplinePointDensity>(SplineDensityPopup);
            state.DefaultSplineHandleMode = PopupValue<SplineInterpolator.SplineHandleMode>(SplineHandlePopup);
            state.DiscardIntegrationRegionForBaseline = IsOn(DiscardIntegrationCheck);
            state.ReprocessIntegratedHeatDataOnLoad = IsOn(ReprocessIntegratedCheck);
            state.DefaultSplinePointTimeDragging = IsOn(SplineTimeDraggingCheck);
            state.IntegrationRegionCopyIncludesStart = IsOn(CopyIntegrationStartCheck);
            error = null;
            return true;
        }
    }

    public sealed partial class MacFittingPreferencesViewController : MacPreferencesPaneController
    {
        static readonly int[] BootstrapIterationValues =
            FittingOptionsController.BootstrapIterationPresets.ToArray();
        static readonly int[] MaximumIterationValues =
            PreferencesState.OptimizerIterationPresets.ToArray();
        static readonly double[] OptimizerToleranceValues = { 0, 0.25, 0.5, 0.8, 1 };
        static readonly string[] OptimizerToleranceLabels =
            { "Fast", "Relaxed", "Balanced", "Strict", "Very Strict" };

        int loadedBootstrapIterations;
        int loadedMaximumIterations;
        double loadedOptimizerTolerance;
        bool bootstrapIterationsChanged;
        bool maximumIterationsChanged;
        bool optimizerToleranceChanged;

        public MacFittingPreferencesViewController(IntPtr handle) : base(handle) { }

        internal override int PaneIndex => 2;

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            PopulatePopup(SolverPopup, EnumValues<SolverAlgorithm>(), value =>
                value == SolverAlgorithm.NelderMead ? "Nelder–Mead" : "Levenberg–Marquardt");
            PopulatePopup(ErrorMethodPopup, EnumValues<ErrorEstimationMethod>(), value => value.Description());
            PopulatePopup(ParameterLimitPopup, EnumValues<ParameterLimitSetting>(), FriendlyName);
            ConfigureDiscreteSlider(BootstrapIterationsSlider, BootstrapIterationValues.Length);
            ConfigureDiscreteSlider(OptimizerToleranceSlider, OptimizerToleranceValues.Length);
            ConfigureDiscreteSlider(MaximumIterationsSlider, MaximumIterationValues.Length);
        }

        internal override void LoadState(PreferencesState state)
        {
            SelectPopup(SolverPopup, state.DefaultSolverAlgorithm);
            SelectPopup(ErrorMethodPopup, state.DefaultErrorEstimationMethod);
            SelectPopup(ParameterLimitPopup, state.ParameterLimitSetting);
            loadedBootstrapIterations = state.DefaultBootstrapIterations;
            loadedOptimizerTolerance = state.OptimizerTolerance;
            loadedMaximumIterations = state.MaximumOptimizerIterations;
            bootstrapIterationsChanged = false;
            optimizerToleranceChanged = false;
            maximumIterationsChanged = false;
            BootstrapIterationsSlider.DoubleValue = NearestIndex(BootstrapIterationValues, loadedBootstrapIterations);
            OptimizerToleranceSlider.DoubleValue = NearestIndex(OptimizerToleranceValues, loadedOptimizerTolerance);
            MaximumIterationsSlider.DoubleValue = NearestIndex(MaximumIterationValues, loadedMaximumIterations);
            UpdateBootstrapIterationsLabel();
            UpdateOptimizerToleranceLabel();
            UpdateMaximumIterationsLabel();
            ConcentrationVarianceField.StringValue = Format(state.ConcentrationAutoVariance * 100);
            Set(ConcentrationBootstrapCheck, state.IncludeConcentrationErrorsInBootstrap);
            Set(WeightedFittingCheck, state.UseInjectionErrorWeightedFitting);
            Set(CreateSingleResultCheck, state.CreateSingleAnalysisResult);
            Set(CreateGlobalResultCheck, state.CreateGlobalAnalysisResult);
            Set(AutoOpenResultCheck, state.AutoOpenNewAnalysisResult);
        }

        internal override bool TryUpdateState(PreferencesState state, out PreferencesValidationError error)
        {
            if (!ReadDouble(ConcentrationVarianceField, "automatic concentration SD", 0, 100,
                out var concentrationVariance, out error)) return false;

            state.DefaultSolverAlgorithm = PopupValue<SolverAlgorithm>(SolverPopup);
            state.DefaultErrorEstimationMethod = PopupValue<ErrorEstimationMethod>(ErrorMethodPopup);
            state.ParameterLimitSetting = PopupValue<ParameterLimitSetting>(ParameterLimitPopup);
            state.DefaultBootstrapIterations = bootstrapIterationsChanged
                ? BootstrapIterationValues[SliderIndex(BootstrapIterationsSlider, BootstrapIterationValues.Length)]
                : loadedBootstrapIterations;
            state.OptimizerTolerance = optimizerToleranceChanged
                ? OptimizerToleranceValues[SliderIndex(OptimizerToleranceSlider, OptimizerToleranceValues.Length)]
                : loadedOptimizerTolerance;
            state.MaximumOptimizerIterations = maximumIterationsChanged
                ? MaximumIterationValues[SliderIndex(MaximumIterationsSlider, MaximumIterationValues.Length)]
                : loadedMaximumIterations;
            state.ConcentrationAutoVariance = concentrationVariance / 100;
            state.IncludeConcentrationErrorsInBootstrap = IsOn(ConcentrationBootstrapCheck);
            state.UseInjectionErrorWeightedFitting = IsOn(WeightedFittingCheck);
            state.CreateSingleAnalysisResult = IsOn(CreateSingleResultCheck);
            state.CreateGlobalAnalysisResult = IsOn(CreateGlobalResultCheck);
            state.AutoOpenNewAnalysisResult = IsOn(AutoOpenResultCheck);
            error = null;
            return true;
        }

        partial void BootstrapIterationsChanged(NSObject sender)
        {
            bootstrapIterationsChanged = true;
            UpdateBootstrapIterationsLabel();
        }

        partial void OptimizerToleranceChanged(NSObject sender)
        {
            optimizerToleranceChanged = true;
            UpdateOptimizerToleranceLabel();
        }

        partial void MaximumIterationsChanged(NSObject sender)
        {
            maximumIterationsChanged = true;
            UpdateMaximumIterationsLabel();
        }

        void UpdateBootstrapIterationsLabel()
        {
            var value = bootstrapIterationsChanged
                ? BootstrapIterationValues[SliderIndex(BootstrapIterationsSlider, BootstrapIterationValues.Length)]
                : loadedBootstrapIterations;
            BootstrapIterationsValueLabel.StringValue = value.ToString("0");
        }

        void UpdateOptimizerToleranceLabel() =>
            OptimizerToleranceValueLabel.StringValue = OptimizerToleranceLabels[
                SliderIndex(OptimizerToleranceSlider, OptimizerToleranceValues.Length)];

        void UpdateMaximumIterationsLabel()
        {
            var value = maximumIterationsChanged
                ? MaximumIterationValues[SliderIndex(MaximumIterationsSlider, MaximumIterationValues.Length)]
                : loadedMaximumIterations;
            MaximumIterationsValueLabel.StringValue = value.ToString("0");
        }
    }

    public sealed partial class MacExportPreferencesViewController : MacPreferencesPaneController
    {
        public MacExportPreferencesViewController(IntPtr handle) : base(handle) { }

        internal override int PaneIndex => 3;

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            PopulatePopup(ExportSelectionPopup, EnumValues<ExportDataSelection>(), FriendlyName);
            PopulatePopup(FitLinePopup, EnumValues<LineSmoothness>(), FriendlyName);
            PopulatePopup(AttributeDisplayPopup, new[]
            {
                DisplayAttributeOptions.UsedInAnalysis,
                DisplayAttributeOptions.All,
                DisplayAttributeOptions.None
            }, FriendlyName);
            UpdateResidualControls();
        }

        internal override void LoadState(PreferencesState state)
        {
            SelectPopup(ExportSelectionPopup, state.ExportSelectionMode);
            SelectPopup(FitLinePopup, state.FitLineSmoothness);
            SelectPopup(AttributeDisplayPopup, NormalizeAttributeOptions(state.DisplayAttributeOptions));
            ExportDecimalsField.IntValue = state.NumOfDecimalsToExport;
            FigureWidthField.StringValue = Format(state.FinalFigureWidthCentimeters);
            FigureHeightField.StringValue = Format(state.FinalFigureHeightCentimeters);
            Set(ExportCorrectedCheck, state.ExportBaselineCorrectedData);
            Set(ExportFitPointsCheck, state.ExportFitPointsWithPeaks);
            Set(ExportMolarRatioCheck, state.ExportColumns.HasFlag(ExportColumns.MolarRatio));
            Set(ExportInjectionInfoCheck, state.ExportColumns.HasFlag(ExportColumns.InjectionInfo));
            Set(ExportConcentrationsCheck, state.ExportColumns.HasFlag(ExportColumns.Concentrations));
            Set(ExportIncludedCheck, state.ExportColumns.HasFlag(ExportColumns.Included));
            Set(ExportPeakCheck, state.ExportColumns.HasFlag(ExportColumns.Peak));
            Set(ExportFitCheck, state.ExportColumns.HasFlag(ExportColumns.Fit));
            Set(ShowResidualCheck, state.ShowResidualGraph);
            Set(ResidualGapCheck, state.ShowResidualGraphGap);
            Set(UnifyResidualAxisCheck, state.UnifyResidualGraphAxis);
            Set(ParameterBoxCheck, state.FinalFigureShowParameterBoxAsDefault);
            Set(ExperimentDetailsCheck, state.FinalFigureShowDetailsAsDefault);
            Set(ModelInfoCheck, state.FinalFigureShowModelInfoAsDefault);
            Set(AutoAxesCheck, state.AutoAxesIgnoresBadData);
            Set(ThermodynamicCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Thermodynamic));
            Set(OffsetCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Offset));
            Set(DerivedCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Derived));
            Set(TemperatureCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Temperature));
            Set(ConcentrationsCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Concentrations));
            Set(InjectionDelayCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.InjectionDelay));
            Set(InstrumentInfoCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Instrument));
            Set(AttributesCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Attributes));
            UpdateResidualControls();
        }

        internal override bool TryUpdateState(PreferencesState state, out PreferencesValidationError error)
        {
            if (!ReadInt(ExportDecimalsField, "export decimals", 0, 12,
                out var exportDecimals, out error)) return false;
            if (!ReadDouble(FigureWidthField, "figure width", 1, 50,
                out var figureWidth, out error)) return false;
            if (!ReadDouble(FigureHeightField, "figure height", 1, 50,
                out var figureHeight, out error)) return false;

            state.ExportSelectionMode = PopupValue<ExportDataSelection>(ExportSelectionPopup);
            state.FitLineSmoothness = PopupValue<LineSmoothness>(FitLinePopup);
            state.DisplayAttributeOptions = PopupValue<DisplayAttributeOptions>(AttributeDisplayPopup);
            state.NumOfDecimalsToExport = exportDecimals;
            state.FinalFigureWidthCentimeters = figureWidth;
            state.FinalFigureHeightCentimeters = figureHeight;
            state.ExportBaselineCorrectedData = IsOn(ExportCorrectedCheck);
            state.ExportFitPointsWithPeaks = IsOn(ExportFitPointsCheck);
            state.ExportColumns = BuildExportColumns();
            state.ShowResidualGraph = IsOn(ShowResidualCheck);
            state.ShowResidualGraphGap = IsOn(ResidualGapCheck);
            state.UnifyResidualGraphAxis = IsOn(UnifyResidualAxisCheck);
            state.FinalFigureShowParameterBoxAsDefault = IsOn(ParameterBoxCheck);
            state.FinalFigureShowDetailsAsDefault = IsOn(ExperimentDetailsCheck);
            state.FinalFigureShowModelInfoAsDefault = IsOn(ModelInfoCheck);
            state.AutoAxesIgnoresBadData = IsOn(AutoAxesCheck);
            state.FinalFigureParameterDisplay = BuildFigureDisplay();
            error = null;
            return true;
        }

        partial void ResidualVisibilityChanged(NSObject sender) => UpdateResidualControls();

        void UpdateResidualControls()
        {
            if (ShowResidualCheck == null) return;
            var enabled = IsOn(ShowResidualCheck);
            ResidualGapCheck.Enabled = enabled;
            UnifyResidualAxisCheck.Enabled = enabled;
        }

        ExportColumns BuildExportColumns()
        {
            var columns = ExportColumns.None;
            if (IsOn(ExportMolarRatioCheck)) columns |= ExportColumns.MolarRatio;
            if (IsOn(ExportInjectionInfoCheck)) columns |= ExportColumns.InjectionInfo;
            if (IsOn(ExportConcentrationsCheck)) columns |= ExportColumns.Concentrations;
            if (IsOn(ExportIncludedCheck)) columns |= ExportColumns.Included;
            if (IsOn(ExportPeakCheck)) columns |= ExportColumns.Peak;
            if (IsOn(ExportFitCheck)) columns |= ExportColumns.Fit;
            return columns;
        }

        FinalFigureDisplayParameters BuildFigureDisplay()
        {
            var display = FinalFigureDisplayParameters.None;
            if (IsOn(ModelInfoCheck)) display |= FinalFigureDisplayParameters.Model;
            if (IsOn(ThermodynamicCheck)) display |= FinalFigureDisplayParameters.Thermodynamic;
            if (IsOn(OffsetCheck)) display |= FinalFigureDisplayParameters.Offset;
            if (IsOn(DerivedCheck)) display |= FinalFigureDisplayParameters.Derived;
            if (IsOn(TemperatureCheck)) display |= FinalFigureDisplayParameters.Temperature;
            if (IsOn(ConcentrationsCheck)) display |= FinalFigureDisplayParameters.Concentrations;
            if (IsOn(InjectionDelayCheck)) display |= FinalFigureDisplayParameters.InjectionDelay;
            if (IsOn(InstrumentInfoCheck)) display |= FinalFigureDisplayParameters.Instrument;
            if (IsOn(AttributesCheck)) display |= FinalFigureDisplayParameters.Attributes;
            return display;
        }

        static DisplayAttributeOptions NormalizeAttributeOptions(DisplayAttributeOptions options)
        {
            if (options == DisplayAttributeOptions.All || options == DisplayAttributeOptions.None) return options;
            return DisplayAttributeOptions.UsedInAnalysis;
        }
    }
}
