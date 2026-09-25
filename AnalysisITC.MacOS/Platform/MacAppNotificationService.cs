using System;
using AnalysisITC.Platform;
using AppKit;
using CoreGraphics;
using Foundation;
using UserNotifications;

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
    public sealed class MacAppNotificationService : IAppNotificationService
    {
        static readonly NotificationCenterDelegate NotificationDelegate = new NotificationCenterDelegate();

        public MacAppNotificationService()
        {
            UNUserNotificationCenter.Current.Delegate = NotificationDelegate;
        }

        public void ShowInfoAlert(string title, string message, bool useLeftAlignedAccessory = false, string actionUrl = null)
        {
            NSApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                using var alert = new NSAlert
                {
                    AlertStyle = NSAlertStyle.Informational,
                    MessageText = title,
                    InformativeText = useLeftAlignedAccessory ? string.Empty : message
                };

                if (useLeftAlignedAccessory)
                    alert.AccessoryView = BuildLeftAlignedTextAccessory(message);

                if (!string.IsNullOrWhiteSpace(actionUrl))
                    alert.AddButton("View Release");

                alert.AddButton("OK");

                var response = alert.RunModal();
                if (response == (int)NSAlertButtonReturn.First && !string.IsNullOrWhiteSpace(actionUrl))
                    NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(actionUrl));
            });
        }

        public void ShowSystemNotification(string title, string message)
        {
            AppEventHandler.PrintAndLog($"[Notification] {title}: {message}");

            NSApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                RequestAuthorizationAndDeliver(title, message);
            });
        }

        public void ShowSystemNotificationIfBackground(string title, string message)
        {
            NSApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                if (!NSApplication.SharedApplication.Active)
                    ShowSystemNotification(title, message);
            });
        }

        static void RequestAuthorizationAndDeliver(string title, string message)
        {
            try
            {
                var center = UNUserNotificationCenter.Current;
                center.RequestAuthorization(UNAuthorizationOptions.Alert, (granted, error) =>
                {
                    try
                    {
                        if (error != null)
                        {
                            AppEventHandler.AddLog("Notification authorization failed: " + error.LocalizedDescription);
                            return;
                        }

                        if (!granted)
                        {
                            center.GetNotificationSettings(settings =>
                                AppEventHandler.PrintAndLog(
                                    "[Notification] macOS did not grant notification authorization; " +
                                    $"status={settings.AuthorizationStatus}, alertSetting={settings.AlertSetting}."));
                            return;
                        }

                        var content = new UNMutableNotificationContent
                        {
                            Title = title ?? string.Empty,
                            Body = message ?? string.Empty
                        };
                        var request = UNNotificationRequest.FromIdentifier(
                            Guid.NewGuid().ToString("N"), content, null);
                        center.AddNotificationRequest(request, addError =>
                        {
                            if (addError != null)
                                AppEventHandler.AddLog("Notification request failed: " + addError.LocalizedDescription);
                        });
                    }
                    catch (Exception ex)
                    {
                        AppEventHandler.AddLog(ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppEventHandler.AddLog(ex);
            }
        }

        sealed class NotificationCenterDelegate : UNUserNotificationCenterDelegate
        {
            public override void WillPresentNotification(
                UNUserNotificationCenter center,
                UNNotification notification,
                Action<UNNotificationPresentationOptions> completionHandler)
            {
                var options = UNNotificationPresentationOptions.Alert;
                if (NSProcessInfo.ProcessInfo.IsOperatingSystemAtLeastVersion(new NSOperatingSystemVersion(12, 0, 0)))
                    options = UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List;

                completionHandler(options);
            }
        }

        static NSView BuildLeftAlignedTextAccessory(string text, float width = 350)
        {
            var font = NSFont.SystemFontOfSize(NSFont.SystemFontSize);
            var paragraph = new NSMutableParagraphStyle
            {
                Alignment = NSTextAlignment.Left,
                LineBreakMode = NSLineBreakMode.ByWordWrapping
            };

            var attributes = new NSStringAttributes
            {
                Font = font,
                ParagraphStyle = paragraph
            };

            var attributedText = new NSAttributedString(text ?? string.Empty, attributes);
            var bounds = attributedText.BoundingRectWithSize(
                new CGSize(width, nfloat.MaxValue),
                NSStringDrawingOptions.UsesLineFragmentOrigin | NSStringDrawingOptions.UsesFontLeading);

            var height = (nfloat)Math.Ceiling(bounds.Height + 8);
            var container = new NSView(new CGRect(0, 0, width, height));
            var textField = new NSTextField(new CGRect(0, 0, width, height))
            {
                Alignment = NSTextAlignment.Left,
                AttributedStringValue = attributedText,
                Bordered = false,
                DrawsBackground = false,
                Editable = false,
                Selectable = true
            };

            textField.Cell.Alignment = NSTextAlignment.Left;
            textField.Cell.Wraps = true;
            textField.Cell.Scrollable = false;
            textField.Cell.UsesSingleLineMode = false;
            textField.Cell.LineBreakMode = NSLineBreakMode.ByWordWrapping;

            container.AddSubview(textField);
            return container;
        }
    }
}
