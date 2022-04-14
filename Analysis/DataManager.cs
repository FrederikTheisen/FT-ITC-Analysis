using System;
using AnalysisITC;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    public static class DataManager
    {
        public static EnergyUnit Unit { get; set; } = EnergyUnit.Joule;

        public static List<ExperimentData> Data => DataSource.Data;
        public static IEnumerable<ExperimentData> IncludedData => Data.Where(d => d.Include);

        static int selectedIndex = 0;
        public static int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                if (value < 0) selectedIndex = 0;
                else if (value > Count) selectedIndex = Count - 1;
                else selectedIndex = value;
            }
        }

        public static ExperimentDataSource DataSource { get; private set; }

        public static event EventHandler<ExperimentData> DataDidChange;
        public static event EventHandler<ExperimentData> SelectionDidChange;

        public static int Count => Data.Count;

        public static bool DataIsLoaded => DataSource?.Data.Count > 0;
        public static bool AllDataIsBaselineProcessed => DataSource.Data.All(d => d.Processor.BaselineCompleted);
        public static bool AnyDataIsBaselineProcessed => DataSource.Data.Any(d => d.Processor.BaselineCompleted);
        public static bool AnyDataIsAnalyzed => DataSource.Data.Any(d => d.Solution != null);

        public static ExperimentData Current => SelectedIndex == -1 || (SelectedIndex >= Count) ? null : Data[SelectedIndex];

        public static void Init()
        {
            DataSource = new ExperimentDataSource();

            DataDidChange.Invoke(null, null);
        }

        public static void SelectIndex(int index)
        {
            SelectedIndex = index;

            SelectionChanged(index);
        }

        internal static void RemoveData(int index)
        {
            Data.RemoveAt(index);

            DataDidChange.Invoke(null, Current);
        }

        public static void AddData(ExperimentData data)
        {
            Data.Add(data);

            DataDidChange.Invoke(null, data);
        }

        public static void Clear()
        {
            Init();
        }

        public static void SelectionChanged(int index) => SelectionDidChange?.Invoke(null, Current);

        public static async void CopySelectedProcessToAll()
        {
            var curr = Current;

            if (curr == null) return;
            if (curr.Processor.Interpolator == null) return;

            var int_delay = curr.Injections.Select(inj => inj.IntegrationStartDelay).Average();
            var int_length = curr.Injections.Select(inj => inj.IntegrationLength).Average();
            var int_factor = curr.UseIntegrationFactorLength;

            var count = Count - 1; //Do not cound current data 

            StatusBarManager.SetStatus("Processing data...", 0);
            StatusBarManager.Progress = 0;

            foreach (var data in Data)
            {
                if (data == curr) continue;

                data.SetProcessor(new DataProcessor(data, curr.Processor));
            }

            float i = 0;

            foreach (var data in Data)
            {
                if (data == curr) continue;

                i++;

                data.Processor.WillProcessData();
                await data.Processor.InterpolateBaseline();
                data.UseIntegrationFactorLength = int_factor;
                data.SetCustomIntegrationTimes(int_delay, int_length);
                data.Processor.IntegratePeaks();
                data.Processor.DidProcessData();

                //await System.Threading.Tasks.Task.Run(() => data.Processor.Interpolator.Interpolate(new System.Threading.CancellationToken(false), true));
                //data.Processor.SubtractBaseline();
                //data.SetCustomIntegrationTimes(int_delay, int_length);
                //data.Processor.IterationCompleted();
                //data.Processor.IntegratePeaks();

                StatusBarManager.Progress = i / count;
            }

            StatusBarManager.Progress = 1;
            StatusBarManager.SetStatus("Data processed", 3000);

        }

        public static void IntegrateAllValidData()
        {
            foreach (var data in Data)
            {
                if (data.Processor.BaselineCompleted) data.Processor.IntegratePeaks();
            }
        }
    }


}
