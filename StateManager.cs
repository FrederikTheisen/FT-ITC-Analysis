using System;
namespace AnalysisITC
{
    public static class StateManager
    {
        public static event EventHandler<ProgramState> ProgramStateChanged;
        public static event EventHandler UpdateStateDependentUI;

        private static ProgramState currentState = ProgramState.Load;
        public static ProgramState CurrentState { get => currentState; private set { currentState = value; ProgramStateChanged?.Invoke(null, currentState); UpdateStateDependentUI?.Invoke(null, null); } }
        public static string ProgramStateString
        {
            get
            {
                return "FT ITC-Analysis 〉" + CurrentState.ToString();
            }
        }

        public static void Init()
        {
            DataManager.DataDidChange += OnDataDidChange;
            DataProcessor.ProcessingCompleted += OnAnyProcessingCompleted;
            Analysis.AnalysisFinished += OnAnalysisFinished;
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
            else
            {
                var nextstate = CurrentState + 1;

                var valid = StateIsAvailable(nextstate);

                if (!simulate) CurrentState = nextstate;

                return valid;
            }
        }

        public static bool PreviousState(bool simulate = false)
        {
            if (CurrentState == ProgramState.Load) return false;
            else
            {
                var nextstate = CurrentState - 1;

                var valid = StateIsAvailable(nextstate);

                if (!simulate) CurrentState = nextstate;

                return valid;
            }
        }

        public static void SetProgramState(ProgramState state)
        {
            if (StateIsAvailable(state))
            {
                CurrentState = state;
            }
        }
    }

    public enum ProgramState
    {
        Load = 0,
        Process = 1,
        Analyze = 2,
        Publish = 3
    }
}
