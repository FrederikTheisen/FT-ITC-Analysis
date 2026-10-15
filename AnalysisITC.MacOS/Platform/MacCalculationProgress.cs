using System;
using System.Collections.Generic;
using System.Linq;
using AppKit;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;

namespace AnalysisITC
{
    // Application-owned reporting also follows solvers started outside Analysis.
    internal sealed class MacAnalysisProgressCoordinator
    {
        static MacAnalysisProgressCoordinator instance;
        SolverInterface solver;
        Guid operationId;
        bool stopping;

        internal static void EnsureCreated() => instance ??= new MacAnalysisProgressCoordinator();

        MacAnalysisProgressCoordinator()
        {
            SolverInterface.AnalysisStarted += Started;
            SolverInterface.ProgressChanged += ProgressChanged;
            SolverInterface.FittingCompleted += Completed;
        }

        void Started(object sender, TerminationFlag flag)
        {
            if (sender is not SolverInterface { Silent: false } owner) return;
            solver = owner;
            operationId = owner.OperationId;
            stopping = false;
        }

        void ProgressChanged(object sender, FittingProgress progress)
        {
            PlatformServices.MainThreadDispatcher.Invoke(() =>
            {
                if (!ReferenceEquals(sender, solver) || progress.OperationId != operationId
                    || !ProcessingActivity.IsCurrentOperation(progress.OperationId)) return;
                stopping |= progress.Phase == FittingPhase.Stopping || SolverInterface.TerminateAnalysisFlag.Up;
                if (progress.Fraction.HasValue) StatusBarManager.SetProgress(progress.Fraction.Value);
                StatusBarManager.SetStatus(stopping ? FittingStatusPresentation.StoppingAnalysis
                    : FittingStatusPresentation.Message(progress), 0, priority: 1);
                StatusBarManager.SetSecondaryStatus(stopping ? "" : FittingStatusPresentation.Counter(progress), 0);
            });
        }

        void Completed(object sender, FittingCompletion completion)
        {
            PlatformServices.MainThreadDispatcher.Invoke(() =>
            {
                if (!ReferenceEquals(completion.Solver, solver) || completion.OperationId != operationId
                    || !ProcessingActivity.IsCurrentOperation(completion.OperationId)) return;
                solver = null;
                operationId = Guid.Empty;
                StatusBarManager.ClearAppStatus();
                if (!completion.IsResultUpdate)
                    UncertaintyEstimationPresentation.PublishCompletion(completion.Solver, completion.Convergence,
                        completion.Elapsed, operationId: completion.OperationId);
            });
        }
    }

    // Applies busy state to calculation controls without changing their idle policy.
    internal sealed class MacCalculationControls : IDisposable
    {
        readonly Func<IEnumerable<NSControl>> controls;
        readonly Action availabilityChanged;
        readonly Dictionary<NSControl, (bool Enabled, string Tooltip)> saved = new();
        bool disposed;

        internal MacCalculationControls(Func<IEnumerable<NSControl>> controls, Action availabilityChanged = null)
        {
            this.controls = controls;
            this.availabilityChanged = availabilityChanged;
            ProcessingActivity.Changed += Changed;
            Refresh();
        }

        internal static IEnumerable<NSControl> ForActions(NSView view, params string[] actions) =>
            Descendants(view).Where(control => control.Action != null && actions.Contains(control.Action.Name));

        static IEnumerable<NSControl> Descendants(NSView view)
        {
            if (view == null) yield break;
            foreach (var child in view.Subviews)
            {
                if (child is NSControl control) yield return control;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        void Changed(object sender, EventArgs args) => PlatformServices.MainThreadDispatcher.Invoke(() =>
        {
            if (disposed) return;
            Refresh();
            if (!ProcessingActivity.IsBusy) availabilityChanged?.Invoke();
        });

        internal void Refresh()
        {
            if (disposed) return;
            if (ProcessingActivity.IsBusy)
            {
                foreach (var control in controls().Where(control => control != null))
                {
                    if (!saved.ContainsKey(control)) saved[control] = (control.Enabled, control.ToolTip);
                    control.Enabled = false;
                    control.ToolTip = FittingStatusPresentation.BusyTooltip;
                }
            }
            else
            {
                foreach (var item in saved)
                {
                    if (item.Key.Handle == IntPtr.Zero) continue;
                    item.Key.Enabled = item.Value.Enabled;
                    item.Key.ToolTip = item.Value.Tooltip;
                }
                saved.Clear();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ProcessingActivity.Changed -= Changed;
            saved.Clear();
        }
    }
}
