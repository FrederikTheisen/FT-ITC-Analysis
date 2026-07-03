using AnalysisITC.Platform;
using AppKit;
using CoreGraphics;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.UI.MacOS
{
    public sealed class MacDataValidationPromptService : IDataValidationPromptService
    {
        const int AlertFirst = 1000;
        const int AlertSecond = 1001;
        const int AlertThird = 1002;

        public DataValidationPromptResult AskValidationIssue(string title, string message, bool canFix, bool requiresInput)
        {
            using var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Warning,
                MessageText = title,
                InformativeText = message
            };

            NSTextField input = null;
            if (requiresInput)
            {
                input = new NSTextField(new CGRect(0, 0, 220, 26))
                {
                    Alignment = NSTextAlignment.Center,
                    RefusesFirstResponder = true,
                };

                alert.AccessoryView = input;
            }

            if (canFix) alert.AddButton("Attempt Fix");
            alert.AddButton("Discard");
            alert.AddButton("Keep");

            var response = (int)alert.RunModal();
            var inputValue = input?.StringValue;

            if (!canFix)
            {
                return response == AlertFirst
                    ? new DataValidationPromptResult(DataValidationPromptAction.Discard, inputValue)
                    : new DataValidationPromptResult(DataValidationPromptAction.Keep, inputValue);
            }

            switch (response)
            {
                case AlertFirst:
                    return new DataValidationPromptResult(DataValidationPromptAction.AttemptFix, inputValue);
                case AlertSecond:
                    return new DataValidationPromptResult(DataValidationPromptAction.Discard, inputValue);
                case AlertThird:
                default:
                    return new DataValidationPromptResult(DataValidationPromptAction.Keep, inputValue);
            }
        }
    }
}
