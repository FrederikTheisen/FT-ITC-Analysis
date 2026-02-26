using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using AnalysisITC.AppClasses.Analysis2;

namespace AnalysisITC
{
    public static class StateManager
    {
        public static event EventHandler<ProgramState> ProgramStateChanged;
        public static event EventHandler UpdateStateDependentUI;

        private static ProgramState currentState = ProgramState.Load;
        private static ProgramSubState currentSubState = ProgramSubState.None;
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
                string status = Utilities.MarkdownStrings.AppName + " 〉" + CurrentState.GetEnumDescription().ToString();

                if (currentState == ProgramState.Analyze)
                {
                    if (ModelFactory.Factory.IsGlobalAnalysis) status += " 〉" + ProgramSubState.MultiAnalysis.GetEnumDescription() + $" [{DataManager.IncludedData.Count()} selected]";
                }
                else if (currentState == ProgramState.AnalysisView && currentSubState != ProgramSubState.None)
                {
                    status += " 〉" + currentSubState.GetEnumDescription().ToString();
                }

                return status;
            }          
        }
        public static string ProgramSubStateString(ProgramSubState substate) => Utilities.MarkdownStrings.AppName + " 〉" + substate.GetEnumDescription().ToString();

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

        public static void SetProgramSubState(ProgramSubState subState)
        {
            currentSubState = subState;

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

        public static void PromptHintPopup(Hint hint)
        {
            // Check if AppSettings shown hints contains hint string or int

            // Determine if hint conditions are met

            // Prompt hint

            // Save hint as shown
        }
    }

    public enum ProgramState
    {
        [Description("Overview")]
        Load = 0,
        [Description("Process")]
        Process = 1,
        [Description("Analyze")]
        Analyze = 2,
        [Description("Publish")]
        Publish = 3,
        [Description("Analysis View")]
        AnalysisView = 4,
        [Description("Always Active???")]
        AlwaysActive = 5,
    }

    public enum ProgramSubState
    {
        [Description("None")]
        None,
        [Description("Single")]
        SingleAnalysis,
        [Description("Multi")]
        MultiAnalysis,
        [Description("Structuring")]
        ResultStructuring,
        [Description("Salt Dependence")]
        ResultSalt,
        [Description("Protonation")]
        ResultProtonation,
        [Description("Tandem Merge Tool")]
        MergeTool = 6,
        [Description("Buffer Subtraction Tool")]
        SubtractionTool = 7,
    }

    public enum UndoTask
    {
        DataManager,
        Analysis
    }

    public enum Hint
    {
        ExperimentDetails,              // Data was loaded, maybe a random chance
        SpaceToCopyIntegrationLength,   // Peak selected, integration range changed
        MultiDataFit,                   // Single fit performed with more than one dataset loaded
        AnalysisDetails,                // Analysis result exists and random chance
        Tools,                          // More than one epxeriment loaded and random chance
        UseUnifiedAxes,                 // Final figure state and random chance
        ExportDataToExternalPrograms,   // Save file completed
    }
}
