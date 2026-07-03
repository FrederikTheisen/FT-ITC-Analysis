using System;
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

namespace AnalysisITC.UI.MacOS.Drawing
{
    public static class CoreGraphicsExtensions
    {
        public static CGRect WithMargin(this CGRect box, CGEdgeMargin margin, float mod = 1)
        {
            return margin.BoxWithMargin(box, mod);
        }

        public static CGRect WithMargin(this CGRect box, CGEdgeMargin margin)
        {
            return margin.BoxWithMargin(box);
        }

        public static CGSize ScaleBy(this CGSize box, float value)
        {
            return new CGSize(box.Width * value, box.Height * value);
        }

        public static CGSize AbsoluteValueSize(this CGSize box)
        {
            return new CGSize(Math.Abs(box.Width), Math.Abs(box.Height));
        }

        public static CGPoint Add(this CGPoint p, float x, float y) => new CGPoint(p.X + x, p.Y + y);

        public static CGPoint Add(this CGPoint p1, CGPoint p2) => new CGPoint(p1.X + p2.X, p1.Y + p2.Y);

        public static CGPoint Subtract(this CGPoint p, float x, float y) => new CGPoint(p.X - x, p.Y - y);

        public static CGPoint Subtract(this CGPoint p1, CGPoint p2) => new CGPoint(p1.X - p2.X, p1.Y - p2.Y);
    }

    public struct CGEdgeMargin
    {
        public nfloat Left { get; private set; }
        public nfloat Right { get; private set; }
        public nfloat Top { get; private set; }
        public nfloat Bottom { get; private set; }

        public nfloat Width => Left + Right;
        public nfloat Height => Top + Bottom;

        public CGEdgeMargin(float left, float right, float top, float bottom)
        {
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }

        public CGRect BoxWithMargin(CGRect box, float mod = 1)
        {
            return new CGRect(box.X - Left * mod, box.Y - Bottom * mod, box.Width + Width * mod, box.Height + Height * mod);
        }

        public CGRect BoxWithMargin(CGRect box)
        {
            return new CGRect(box.X - Left, box.Y - Bottom, box.Width + Width, box.Height + Height);
        }
    }
}
