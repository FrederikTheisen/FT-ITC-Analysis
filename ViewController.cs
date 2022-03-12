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
        public ViewController(IntPtr handle) : base(handle)
        {
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            // Do any additional setup after loading the view.

            DataManager.Init();

            DataManager.SelectionDidChange += OnSelectionChanged;
            DataManager.DataDidChange += OnDataChanged;

            Test();
        }

        void Test()
        {
            var data = new double[10] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var y = new DenseVector(data);
            var lambda = 10000000.0;
            var L = y.Count; //len(y)
            var D = Diff(new DiagonalMatrix(L, L, 1)); //sparse.csc_matrix(np.diff(np.eye(L), 2))
            var w = new DenseVector(L).Add(1);
            
            var z = new DenseVector(L);

            for (int i = 0; i < 15; i++)
            {
                var W = SparseMatrix.CreateDiagonal(L, L, (o => w[o]));
                var a = D * D.Transpose();
                var Z = W + lambda * a;
                
                
                var mul = w.PointwiseMultiply(y);

                var monitor = new Iterator<double>(
                new IterationCountStopCriterion<double>(1000),
                new ResidualStopCriterion<double>(0.00001));

                var solver = new MathNet.Numerics.LinearAlgebra.Double.Solvers.TFQMR();

                var nZ = Z.Solve(mul);
                z = new DenseVector(nZ.Storage.ToArray());
                w = Select(z.ToList(), y.ToList());
            }

            var Baseline = z.Select(o => (float)o).ToList();
        }

        SparseMatrix Diff(DiagonalMatrix m)
        {
            var dense = new SparseMatrix(m.RowCount, m.RowCount - 2);

            var rows = m.EnumerateRows().ToList();

            for (int i = 0; i < rows.Count(); i++)
            {
                var row = rows[i];
                double[] newrow = new double[row.Count() - 2];

                for (int j = 0; j < row.Count() - 2; j++)
                {
                    if (i == j) newrow[j] = 1;
                    else if (i == j + 1) newrow[j] = -2;
                    else if (i == j + 2) newrow[j] = 1;

                    //newrow[j] = (row[j + 2] - row[j + 1]) - (row[j + 1] - row[j]);
                }

                dense.SetRow(i, newrow);
            }


            return dense;
        }

        DenseVector Select(List<double> z, List<double> y)
        {
            var w = new DenseVector(z.Count);

            for (int i = 0; i < z.Count(); i++)
            {
                if (z[i] < y[i]) w[i] = 0.96;
                else w[i] = (1 - 0.96);
            }

            return w;
        }

        private void OnSelectionChanged(object sender, ExperimentData e)
        {
            GVC.Initialize(DataManager.Current());
        }

        private void OnDataChanged(object sender, ExperimentData e)
        {
            ClearDataButton.Enabled = DataManager.DataIsLoaded;
            ContinueButton.Enabled = DataManager.DataIsLoaded;

            GVC.Initialize(e);
        }

        partial void ButtonClick(NSButton sender)
        {
            var dlg = NSOpenPanel.OpenPanel;
            dlg.CanChooseFiles = true;
            dlg.AllowsMultipleSelection = true;
            dlg.CanChooseDirectories = true;
            dlg.AllowedFileTypes = DataReaders.ITCFormatAttribute.GetAllExtensions();

            if (dlg.RunModal() == 1)
            {
                // Nab the first file
                var urls = new List<string>();

                foreach (var url in dlg.Urls)
                {
                    Console.WriteLine(url.Path);
                    urls.Add(url.Path);
                }


                DataReaders.DataReader.Read(urls);
            }
        }

        partial void ClearButtonClick(NSObject sender)
        {
            DataManager.Clear();
        }

        partial void ContinueClick(NSObject sender)
        {
            DataManager.SetMode(1);
        }

        public override NSObject RepresentedObject
        {
            get
            {
                return base.RepresentedObject;
            }
            set
            {
                base.RepresentedObject = value;
                // Update the view, if already loaded.
            }
        }
    }
}
