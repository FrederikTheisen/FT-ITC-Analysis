using System;
using System.Collections.Generic;
using System.Globalization;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using AnalysisITC.UI.MacOS.Drawing;

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

namespace AnalysisITC
{
    [Register("GraphView")]
    public class GraphView : NSGraph
    {
        public new ProgramState State = ProgramState.Load;

        public GraphView(IntPtr handle) : base(handle)
        {

        }

        public void Initialize(ExperimentData experiment)
        {
            if (experiment != null && experiment.HasThermogram)
            {
                Graph = new FileInfoGraph(experiment, this);
                Graph.YAxis.Buffer = .05f;
            }
            else Graph = null;

            Invalidate();
        }

        public override void SetFrameSize(CGSize newSize)
        {
            base.SetFrameSize(newSize);

            Invalidate();
        }

        public override void MouseEntered(NSEvent theEvent)
        {
            if (Graph == null) return;

            base.MouseEntered(theEvent);
        }

        public override void MouseExited(NSEvent theEvent)
        {
            if (Graph == null) return;

            base.MouseExited(theEvent);

            Invalidate();
        }

        public override void MouseMoved(NSEvent theEvent)
        {
            if (Graph == null) return;

            base.MouseMoved(theEvent);

            var update = (Graph as FileInfoGraph).SetCursorInfo(CursorPositionInView);

            if (update)
            {
                NSCursor.CrosshairCursor.Set();

                Invalidate();
            }
            else NSCursor.ArrowCursor.Set();
        }
    }
}
