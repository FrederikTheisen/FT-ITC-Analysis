using System;
using System.Net.Http;
using System.Reflection;
using System.Linq;
using Avalonia.Controls;
using Xunit;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class InterpretationProviderSelectionTests
{
    [AvaloniaFact]
    public void ProviderSwitchFiltersModelsAndCapturedGenerationWithoutSavingReport()
    {
        var original=AppSettings.InterpretationProvider;
        try
        {
            AppSettings.InterpretationProvider="openai";
            var report=new AnalysisReport(); report.MarkClean(); using var http=new HttpClient();
            var type=typeof(AnalysisReportWindow).Assembly.GetTypes().Single(value=>value.Name=="AnalysisInterpretationDialog");
            var dialog=(Window)Activator.CreateInstance(type,new object?[] {report,new Func<string,AnalysisResult>(_=>null!),new Func<string,ExperimentData>(_=>null!),http,new Action(()=>throw new Exception("Saved report")),null})!; const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var options=new InterpretationOperatorOptionsResponse { Mode="custom",AccessTier="administrator",DefaultModel="open-model",DefaultReasoningEffort="high" };
            options.Providers.Add(new() { Id="mistral",Name="Mistral" });
            options.Models.Add(new() { Id="open-model",Provider="openai",ReasoningEfforts=["high"] });
            options.Models.Add(new() { Id="flash-model",Provider="mistral",ReasoningEfforts=["default"] });
            type.GetMethods(flags).Single(value=>value.Name=="PopulateInterpretationChoices" && value.GetParameters().Length==1).Invoke(dialog,[options]);
            var provider=(ComboBox)type.GetField("interpretationProviderCombo",flags)!.GetValue(dialog)!;
            var model=(ComboBox)type.GetField("interpretationModelCombo",flags)!.GetValue(dialog)!;
            Assert.Equal("open-model",model.SelectedItem);
            provider.SelectedItem=options.Providers[1];
            Assert.Equal("flash-model",model.SelectedItem);
            var selection=(AnalysisInterpretationGenerationSelection)type.GetMethod("CurrentGenerationSelection",flags)!.Invoke(dialog,null)!;
            Assert.Equal("mistral",selection.Provider); Assert.Equal("flash-model",selection.Model); Assert.Equal("default",selection.ReasoningEffort);
            Assert.False(report.IsModified);
            options.Providers.RemoveAt(1); type.GetMethods(flags).Single(value=>value.Name=="PopulateInterpretationChoices" && value.GetParameters().Length==1).Invoke(dialog,[options]);
            Assert.False((bool)type.GetField("interpretationSelectionEnabled",flags)!.GetValue(dialog)!);
            dialog.Close();
        }
        finally { AppSettings.InterpretationProvider=original; }
    }
}
