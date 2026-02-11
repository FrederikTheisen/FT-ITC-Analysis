using System;
using System.Collections.Generic;
using AnalysisITC.AppClasses.Analysis2;

namespace AnalysisITC
{
    public static class StateManager
    {
        public static event EventHandler<ProgramState> ProgramStateChanged;
        public static event EventHandler UpdateStateDependentUI;

        private static ProgramState currentState = ProgramState.Load;
        public static ProgramState saveState { get; private set; } = ProgramState.Load;
        public static ProgramState CurrentState
        {
            get => currentState;
            private set
            {
                currentState = value;
                ProgramStateChanged?.Invoke(null, currentState);
                UpdateStateDependentUI?.Invoke(null, null);
            }
        }
        public static string ProgramStateString
        {
            get
            {
                return Utils.MarkdownStrings.AppName + " 〉" + CurrentState.ToString();
            }
        }

        public static void Init()
        {
            DataManager.DataDidChange += OnDataDidChange;
            DataProcessor.ProcessingCompleted += OnAnyProcessingCompleted;
            SolverInterface.AnalysisFinished += OnAnalysisFinished;
        }

        private static void OnAnalysisFinished(object sender, SolverConvergence e)
        {
            ShouldUpdateStateDependentUI();
        }

        private static void OnAnyProcessingCompleted(object sender, EventArgs e)
        {
            ShouldUpdateStateDependentUI();
        }

        static void OnDataDidChange(object sender, ExperimentData e)
        {
            while (!StateIsAvailable(CurrentState)) currentState--;

            SetProgramState(CurrentState);

            ShouldUpdateStateDependentUI();
        }

        public static void ShouldUpdateStateDependentUI()
        {
            UpdateStateDependentUI?.Invoke(null, null);
        }

        public static bool StateIsAvailable(ProgramState state)
        {
            switch (state)
            {
                case ProgramState.Load: return true;
                case ProgramState.Process: return DataManager.DataIsLoaded;
                case ProgramState.Analyze: return DataManager.AnyDataIsBaselineProcessed;
                case ProgramState.Publish: return DataManager.AnyDataIsAnalyzed;
                default: return true;
            }
        }

        public static bool NextState(bool simulate = false)
        {
            if (CurrentState == ProgramState.Publish) return false;
            else if (currentState == ProgramState.AnalysisView) return false;
            else
            {
                var nextstate = CurrentState + 1;

                var valid = StateIsAvailable(nextstate);

                if (!simulate) CurrentState = nextstate;

                StatusBarManager.Invalidate();

                return valid;
            }
        }

        public static bool PreviousState(bool simulate = false)
        {
            if (CurrentState == ProgramState.Load) return false;
            else if (currentState == ProgramState.AnalysisView) return false;
            else
            {
                var nextstate = CurrentState - 1;

                var valid = StateIsAvailable(nextstate);

                if (!simulate) CurrentState = nextstate;

                StatusBarManager.Invalidate();

                return valid;
            }
        }

        public static void SetProgramState(ProgramState state)
        {
            if (StateIsAvailable(state))
            {
                if (CurrentState != ProgramState.AnalysisView && state == ProgramState.AnalysisView) { saveState = CurrentState; CurrentState = state; }
                else if (CurrentState == ProgramState.AnalysisView) { while (!StateIsAvailable(saveState)) { saveState--; } CurrentState = saveState; }
                else CurrentState = state;
            }

            StatusBarManager.Invalidate();
        }

        public static void GoToResultView()
        {
            if (CurrentState != ProgramState.AnalysisView) saveState = CurrentState;

            CurrentState = ProgramState.AnalysisView;
        }

        public static void ManagedReturnToAnalysisViewState()
        {
            if (CurrentState == ProgramState.AnalysisView) CurrentState = saveState;
        }

        public static bool StateCanPrint()
        {
            switch (CurrentState)
            {
                default:
                case ProgramState.Process: return true;
            }
        }

        public static bool StateCanUndo()
        {
            return DataManager.DeletedDataList.Count > 0;
        }

        public static void Undo()
        {
            DataManager.UndoDeleteData();
        }
    }

    public enum ProgramState
    {
        Load = 0,
        Process = 1,
        Analyze = 2,
        Publish = 3,
        AnalysisView = 4,
        AlwaysActive = 5,
    }

    public enum UndoTask
    {
        DataManager,
        Analysis
    }
}
