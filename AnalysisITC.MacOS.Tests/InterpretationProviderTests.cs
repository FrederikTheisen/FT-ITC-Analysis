using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using AppKit;
using AnalysisITC;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Presentation;

static class InterpretationProviderTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int Main()
    {
        NSApplication.Init(); var original = AppSettings.InterpretationProvider;
        try
        {
            AppSettings.InterpretationProvider = "openai";
            var type = typeof(MainWindowController).Assembly.GetType("AnalysisITC.AnalysisInterpretationViewController", true);
            var report = new AnalysisReport(); report.MarkClean();
            using (var client = new HttpClient())
            using (var controller = (NSViewController)Activator.CreateInstance(type, new object[] {
                report, new Func<string, AnalysisResult>(_ => null), new Func<string, ExperimentData>(_ => null), client,
                new Action(() => { throw new Exception("Provider change saved the report"); }), new Action<AnalysisInterpretationRecord>(_ => { }) }))
            {
                var view = controller.View;
                var options = new InterpretationOperatorOptionsResponse { Mode="custom", AccessTier="administrator", DefaultModel="open-model", DefaultReasoningEffort="high" };
                options.Providers.Add(new InterpretationProviderOption { Id="mistral", Name="Mistral" });
                options.Models.Add(new InterpretationOperatorModelOption { Id="open-model", Provider="openai", ReasoningEfforts=new List<string>{"high"} });
                options.Models.Add(new InterpretationOperatorModelOption { Id="flash-model", Provider="mistral", ReasoningEfforts=new List<string>{"default"} });
                type.GetMethod("PopulateInterpretationChoices",Private).Invoke(controller,new object[]{options});
                var provider=(NSPopUpButton)type.GetField("interpretationProviderPopup",Private).GetValue(controller);
                var model=(NSPopUpButton)type.GetField("interpretationModelPopup",Private).GetValue(controller);
                if(model.TitleOfSelectedItem!="open-model") throw new Exception("Wrong initial provider model");
                provider.SelectItem(1); provider.SendAction(provider.Action,provider.Target);
                if(model.TitleOfSelectedItem!="flash-model") throw new Exception("Provider menu did not filter models");
                var selection=(AnalysisInterpretationGenerationSelection)type.GetMethod("CurrentGenerationSelection",Private).Invoke(controller,null);
                if(selection.Provider!="mistral" || selection.Model!="flash-model" || selection.ReasoningEffort!="default") throw new Exception("Wrong provider request snapshot");
                var reasoning=(NSPopUpButton)type.GetField("interpretationReasoningPopup",Private).GetValue(controller);
                if(reasoning.TitleOfSelectedItem!="Provider default") throw new Exception("Provider default reasoning was not labelled");
                if(report.IsModified) throw new Exception("Provider selection dirtied the report");
                options.Providers.RemoveAt(1);
                type.GetMethod("PopulateInterpretationChoices",Private).Invoke(controller,new object[]{options});
                if((bool)type.GetField("interpretationSelectionEnabled",Private).GetValue(controller)) throw new Exception("Unavailable provider allowed generation");
                var setBusy=type.GetMethod("SetBusy",Private,null,new[]{typeof(bool)},null);
                setBusy.Invoke(controller,new object[]{false});
                if(!provider.Enabled) throw new Exception("Unavailable saved provider locked the provider menu");
                setBusy.Invoke(controller,new object[]{true});
                if(provider.Enabled) throw new Exception("Provider menu stayed enabled during generation");
            }
            Console.WriteLine("PASS: native provider selection and model filtering"); return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { AppSettings.InterpretationProvider=original; }
    }
}
