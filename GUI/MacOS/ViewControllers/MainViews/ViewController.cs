using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppKit;
using Foundation;
using MathNet.Numerics.LinearAlgebra.Solvers;
using MathNet.Numerics.LinearAlgebra.Double;

namespace AnalysisITC
{
    public partial class ViewController : NSViewController
    {
        public static event EventHandler UpdateTable;
        public static event EventHandler<int> RemoveData;

        ExperimentData Data => DataManager.Current;

        public ViewController(IntPtr handle) : base(handle)
        {
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            DataManager.Init();

            DataManager.DataDidChange += OnDataChanged;
            DataManager.SelectionDidChange += OnSelectionChanged;
            StateManager.UpdateStateDependentUI += StateManager_UpdateStateDependentUI;
            AppDelegate.StartPrintOperation += AppDelegate_StartPrintOperation;
            ExperimentMenuButton.Activated += OnSourcePopupChanged;

            ShowLoadDataPrompt();
        }

        public override void ViewDidAppear()
        {
            base.ViewDidAppear();

            UpdateGraph();
        }

        private void OnSourcePopupChanged(object sender, EventArgs e)
        {
            var popup = (NSPopUpButton)sender;
            var item = popup.SelectedItem;

            if (item == null) return;

            var title = item.Title;
            var index = popup.IndexOfSelectedItem;
            var tag = item.Tag;
            var iden = item.Identifier;

            switch (iden)
            {
                case "openattributes":
                    EditAttributesAction(null);
                    break;
                case "copyattributes":
                    DataManager.CopySelectedAttributesToAll();
                    break;
                case "toggleinclude":
                    ToggleInclusionAction(null);
                    break;
                case "duplicate":
                    DataManager.DuplicateSelectedData(Data);
                    break;
                case "export": 
                    Exporter.Export(ExportType.Data, ExportDataSelection.SelectedData);
                    break;
                case "clearsolution":
                    break;
                case "delete":
                    int idx = DataManager.SelectedContentIndex;
                    DataManager.RemoveData2(idx);
                    RemoveData?.Invoke(this, idx);   // 2) then animate UI removal
                    break;
                default: break;
            }
        }

        private void AppDelegate_StartPrintOperation(object sender, EventArgs e)
        {
            if (StateManager.CurrentState != ProgramState.Load) return;

            GVC.Print();
        }

        private void StateManager_UpdateStateDependentUI(object sender, EventArgs e)
        {
            ShowLoadDataPrompt();
        }

        partial void LoadDataButtonClick(NSObject sender)
        {
            //LoadDataPrompt.Hidden = true;

            AppDelegate.LaunchOpenFileDialog();
        }

        partial void LoadLastFile(NSObject sender)
        {
            if (AppSettings.LastDocumentUrls != null)
            {
                DataReaders.DataReader.Read(AppSettings.LastDocumentUrls);
            }
            else DataReaders.DataReader.Read(AppSettings.LastDocumentUrl);
        }

        private void OnSelectionChanged(object sender, ExperimentData e) => UpdateGraph();
        private void OnDataChanged(object sender, ExperimentData e) => UpdateGraph();

        private void UpdateGraph()
        {
            GVC.Initialize(DataManager.Current);

            TitleLabel.StringValue = DataManager.Current?.Name ?? "No Data Selected";
            TitleLabel.TextColor = DataManager.Current != null ? NSColor.Label : NSColor.DisabledControlText;

            ExperimentMenuButton.Enabled = DataManager.Current != null;

            UpdateLabel();
        }

        void UpdateLabel()
        {
            if (Data == null) InfoLabel.StringValue = "";
            else
            {
                InfoLabel.AttributedStringValue = Utilities.MacStrings.FromMarkDownString(string.Join(Environment.NewLine, DataManager.Current.GetInfoString()), InfoLabel.Font);
            }
        }

        partial void ClearButtonClick(NSObject sender)
        {
            DataManager.Clear();
        }

        partial void ContinueClick(NSObject sender)
        {
            
        }

        partial void EditAttributesAction(NSObject sender)
        {
            ExperimentDetailsPopoverController.Data = DataManager.Current;

            PerformSegue("DetailsSegue", this);
        }

        partial void ToggleInclusionAction(NSObject sender)
        {
            if (Data == null) return;

            Data.ToggleInclude();

            DataManager.InvokeDataInclusionDidChange();
        }

        void RemoveSolution()
        {
            if (Data == null) return;

            Data.SetModel(null);
        }

        partial void DuplicateDataAction(NSObject sender)
        {
            DataManager.DuplicateSelectedData(Data);
        }

        async void ShowLoadDataPrompt()
        {
            LoadLastButton.Enabled = false;

            if (AppSettings.LastDocumentUrl != null)
            {
                var format = DataReaders.DataReader.GetFormat(AppSettings.LastDocumentUrl.Path);

                if (format != DataReaders.ITCDataFormat.Unknown) LoadLastButton.Enabled = true;

                LoadLastButton.ToolTip = $"Reload the last file ({Path.GetFileName(AppSettings.LastDocumentUrl.Path)}) [SPACE] ";
            }

            LoadDataPrompt.Hidden = DataManager.DataIsLoaded;
        }
    }
}
