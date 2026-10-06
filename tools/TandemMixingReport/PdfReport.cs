using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using SkiaSharp;

namespace TandemMixingReport
{
    sealed class ReportHeader
    {
        public string Commit { get; init; }
        public DateTime Generated { get; init; }
        public string DilutionMethod { get; init; }
    }

    static class PdfReport
    {
        const float PageWidth = 842;   // A4 landscape, points
        const float PageHeight = 595;
        const float Margin = 36;

        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        static readonly SKColor Surface = SKColor.Parse("#ffffff");
        static readonly SKColor TextPrimary = SKColor.Parse("#0b0b0b");
        static readonly SKColor TextSecondary = SKColor.Parse("#52514e");
        static readonly SKColor Grid = SKColor.Parse("#e6e5e0");
        static readonly SKColor Axis = SKColor.Parse("#8a8984");
        static readonly SKColor Context = SKColor.Parse("#b5b4ae");
        static readonly SKColor Band = SKColor.Parse("#f1efe8");
        static readonly SKColor Before = SKColor.Parse("#2a78d6");
        static readonly SKColor After = SKColor.Parse("#eb6834");
        static readonly SKColor Truth = SKColor.Parse("#1baf7a");

        static readonly SKTypeface Typeface = SKTypeface.FromFamilyName("Helvetica") ?? SKTypeface.Default;
        static readonly SKTypeface BoldTypeface = SKTypeface.FromFamilyName("Helvetica", SKFontStyle.Bold) ?? SKTypeface.Default;

        public static void Write(string path, ReportHeader header, IReadOnlyList<ReportCase> cases)
        {
            using var stream = File.Create(path);
            using var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
            {
                Title = "Model-free tandem mixing report",
                Creator = "FT-ITC TandemMixingReport",
                Creation = header.Generated,
            });

            WriteSummary(document, header, cases);
            foreach (var report in cases.Where(c => c.Failure == null))
                WriteCase(document, report);

            document.Close();
        }

        // ---------------------------------------------------------------- summary

        static void WriteSummary(SKDocument document, ReportHeader header, IReadOnlyList<ReportCase> cases)
        {
            var canvas = BeginPage(document);
            var y = Margin + 14;
            Text(canvas, "Model-free tandem mixing report", Margin, y, 16, TextPrimary, bold: true);
            y += 18;
            Text(canvas, $"Generated {header.Generated:yyyy-MM-dd HH:mm} · commit {header.Commit}", Margin, y, 9, TextSecondary);
            y += 18;

            var bias = TandemContinuityScanner.Bias;
            var method = new[]
            {
                "Each transition is solved in order. For a candidate mixing fraction f, the back-mixing bookkeeping recalculates the concentrations,",
                "and each included injection is placed at its midpoint molar ratio with its heat per mole of injectant.",
                $"Score = RSS of one monotonic {PolynomialName} through the included injections among the last {TandemContinuityScanner.PointsPerSide} before the transition and the first {TandemContinuityScanner.PointsPerSide} after it",
                $"(excluded injections are dropped, not replaced; at least {TandemContinuityScanner.MinimumIncludedPointsPerSide} included on each side), multiplied by 1 + {Number(bias.Strength)} (f − {Percent(bias.Center)})².",
                "Scan 0–100% in 2% steps, then neighbour refinement in 1% and 0.2% steps.",
                $"Back-mixing: dead volume {(ReportData.Settings().DeadVolume * 1e6).ToString("0", Invariant)} µL, titrated overflow removed, dilution method {header.DilutionMethod}.",
                "Synthetic data: one-site, n = 1, ΔH = −40 kJ/mol, offset −0.5 kJ/mol. Synthetic pages also show the true-fraction window (aqua).",
                $"Standard design: {ReportData.Standard.Describe()}.",
                $"Short-run design (mirrors real projects 061–112): {ReportData.ShortRun.Describe()}.",
                "Real data: each project's non-tandem experiments, in project order, are the runs; each is processed with its saved settings before the search.",
            };
            foreach (var line in method)
            {
                Text(canvas, line, Margin, y, 8.5f, TextPrimary);
                y += 11.5f;
            }

            y += 8;
            var columns = new[] { ("Group", 0f), ("Case", 52f), ("Runs", 300f), ("True", 335f), ("Chosen", 450f), ("Error (points)", 565f), ("Note", 680f) };
            void TableHeader()
            {
                foreach (var (label, offset) in columns) Text(canvas, label, Margin + offset, y, 8.5f, TextSecondary, bold: true);
                y += 4;
                Line(canvas, Margin, y, PageWidth - Margin, y, Axis, 0.5f);
                y += 11;
            }

            TableHeader();
            foreach (var report in cases)
            {
                if (y > PageHeight - Margin)
                {
                    document.EndPage();
                    canvas = BeginPage(document);
                    y = Margin + 14;
                    TableHeader();
                }

                var cells = new[]
                {
                    report.Group,
                    report.Title,
                    report.RunCount.ToString(Invariant),
                    report.TrueFractions == null ? "–" : string.Join(" / ", report.TrueFractions.Select(Percent)),
                    report.ChosenFractions == null ? "–" : string.Join(" / ", report.ChosenFractions.Select(Percent)),
                    ErrorText(report),
                    report.Failure ?? "",
                };
                for (var index = 0; index < columns.Length; index++)
                    Text(canvas, Truncate(cells[index], index == 1 ? 46 : index == 6 ? 40 : 30), Margin + columns[index].Item2, y, 8.5f, TextPrimary);
                y += 12.5f;
            }

            document.EndPage();
        }

        static string ErrorText(ReportCase report)
        {
            if (report.TrueFractions == null || report.ChosenFractions == null) return "–";
            return string.Join(" / ", report.ChosenFractions.Zip(report.TrueFractions, (chosen, truth) =>
                (100 * (chosen - truth)).ToString("+0.0;−0.0;0.0", Invariant)));
        }

        // ---------------------------------------------------------------- case pages

        static void WriteCase(SKDocument document, ReportCase report)
        {
            var canvas = BeginPage(document);
            Text(canvas, $"{report.Group}: {report.Title}", Margin, Margin + 12, 13, TextPrimary, bold: true);
            var subtitle = $"Chosen {string.Join(" / ", report.ChosenFractions.Select(Percent))}";
            if (report.TrueFractions != null)
                subtitle += $"   ·   true {string.Join(" / ", report.TrueFractions.Select(Percent))}   ·   error {ErrorText(report)} points";
            Text(canvas, subtitle, Margin, Margin + 28, 9, TextSecondary);

            var full = new SKRect(Margin + 34, Margin + 48, PageWidth - Margin - 150, 282);
            DrawFullTitration(canvas, report, full);
            DrawFullLegend(canvas, report, new SKPoint(full.Right + 16, full.Top + 6));

            var transitions = report.Chosen.Windows.Count;
            var top = 360f;
            var bottom = PageHeight - Margin - 18;
            var gap = 40f;
            var left = Margin + 34;
            var width = (PageWidth - Margin - left - gap * (transitions - 1)) / transitions;
            for (var transition = 0; transition < transitions; transition++)
            {
                var x = left + transition * (width + gap);
                DrawZoom(canvas, report, transition, new SKRect(x, top, x + width, bottom));
            }

            document.EndPage();
        }

        static void DrawFullTitration(SKCanvas canvas, ReportCase report, SKRect area)
        {
            var points = report.Chosen.Points;
            var plot = Plot.Create(area,
                points.Select(p => p.X), points.Select(p => Kj(p.Y)), padFraction: 0.04);

            // Fit windows first so the markers sit on top.
            for (var transition = 0; transition < report.Chosen.Windows.Count; transition++)
            {
                var window = report.Chosen.Windows[transition];
                var x0 = plot.MapX(window.All.Min(p => p.x));
                var x1 = plot.MapX(window.All.Max(p => p.x));
                Rect(canvas, new SKRect(x0 - 3, area.Top, x1 + 3, area.Bottom), Band);
                Text(canvas, $"T{transition + 1}", (x0 + x1) / 2, area.Top + 10, 8, TextSecondary, align: SKTextAlign.Center);
            }

            plot.DrawAxes(canvas, "Molar ratio", HeatLabel);
            foreach (var point in points)
            {
                var center = new SKPoint(plot.MapX(point.X), plot.MapY(Kj(point.Y)));
                if (!point.Included) Cross(canvas, center, 3, Context);
                else if (point.Run % 2 == 0) Dot(canvas, center, 2.6f, Before);
                else Ring(canvas, center, 2.6f, Before, 1.1f);
            }
        }

        static void DrawFullLegend(SKCanvas canvas, ReportCase report, SKPoint origin)
        {
            var y = origin.Y;
            Dot(canvas, new SKPoint(origin.X + 4, y - 3), 2.6f, Before);
            Text(canvas, "Runs 1, 3, 5", origin.X + 14, y, 8, TextPrimary);
            y += 13;
            Ring(canvas, new SKPoint(origin.X + 4, y - 3), 2.6f, Before, 1.1f);
            Text(canvas, "Runs 2, 4", origin.X + 14, y, 8, TextPrimary);
            y += 13;
            Cross(canvas, new SKPoint(origin.X + 4, y - 3), 3, Context);
            Text(canvas, "Excluded", origin.X + 14, y, 8, TextPrimary);
            y += 13;
            Rect(canvas, new SKRect(origin.X, y - 7, origin.X + 8, y + 1), Band);
            Text(canvas, "Fit window", origin.X + 14, y, 8, TextPrimary);
            y += 20;
            Text(canvas, "Drawn at the chosen", origin.X, y, 8, TextSecondary);
            y += 11;
            Text(canvas, "fractions.", origin.X, y, 8, TextSecondary);
            if (report.Chosen.Windows.Count > 0)
            {
                y += 20;
                Text(canvas, "Score at chosen f", origin.X, y, 8, TextSecondary, bold: true);
                for (var transition = 0; transition < report.Chosen.Windows.Count; transition++)
                {
                    y += 11;
                    var window = report.Chosen.Windows[transition];
                    Text(canvas, $"T{transition + 1}: {Number(window.Score)}", origin.X, y, 8, TextSecondary);
                }
            }
        }

        static void DrawZoom(SKCanvas canvas, ReportCase report, int transition, SKRect area)
        {
            var chosen = report.Chosen.Windows[transition];
            var truth = report.Truth?.Windows[transition];
            var windowPoints = chosen.All.Concat(truth?.All ?? Enumerable.Empty<(double x, double y)>()).ToList();
            var plot = Plot.Create(area, windowPoints.Select(p => p.x), windowPoints.Select(p => Kj(p.y)), padFraction: 0.12);

            plot.DrawAxes(canvas, "Molar ratio", transition == 0 ? HeatLabel : null, compact: true);
            canvas.Save();
            canvas.ClipRect(area);

            // Remaining injections of the two runs inside the zoom, for context.
            var inWindow = new HashSet<(double, double)>(chosen.All);
            foreach (var point in report.Chosen.Points.Where(p => p.Run == transition || p.Run == transition + 1))
            {
                if (inWindow.Contains((point.X, point.Y))) continue;
                var center = new SKPoint(plot.MapX(point.X), plot.MapY(Kj(point.Y)));
                if (point.Included) Dot(canvas, center, 1.8f, Context);
                else Cross(canvas, center, 2.5f, Context);
            }

            if (truth != null)
            {
                Curve(canvas, plot, truth.Fit, truth.All, Truth, dashed: true);
                foreach (var point in truth.All)
                    Ring(canvas, new SKPoint(plot.MapX(point.x), plot.MapY(Kj(point.y))), 4.4f, Truth, 1.2f);
            }

            Curve(canvas, plot, chosen.Fit, chosen.All, TextSecondary, dashed: false);
            foreach (var point in chosen.Pre)
                Dot(canvas, new SKPoint(plot.MapX(point.x), plot.MapY(Kj(point.y))), 2.8f, Before);
            foreach (var point in chosen.Post)
                Dot(canvas, new SKPoint(plot.MapX(point.x), plot.MapY(Kj(point.y))), 2.8f, After);
            canvas.Restore();

            // Legend above the panel.
            var y = area.Top - 22;
            Text(canvas, $"T{transition + 1}", area.Left, y, 9, TextPrimary, bold: true);
            Dot(canvas, new SKPoint(area.Left + 22, y - 3), 2.8f, Before);
            Text(canvas, $"run {transition + 1}", area.Left + 28, y, 8, TextPrimary);
            Dot(canvas, new SKPoint(area.Left + 66, y - 3), 2.8f, After);
            Text(canvas, $"run {transition + 2} at {Percent(chosen.Fraction)}", area.Left + 72, y, 8, TextPrimary);
            y += 11;
            Text(canvas, $"chosen score {Number(chosen.Score)}", area.Left + 22, y, 8, TextSecondary);
            if (truth != null)
            {
                Ring(canvas, new SKPoint(area.Right - 92, y - 3), 3.4f, Truth, 1.2f);
                Text(canvas, $"true {Percent(truth.Fraction)}: {Number(truth.Score)}", area.Right - 84, y, 8, TextSecondary);
            }
        }

        static void Curve(SKCanvas canvas, Plot plot, TandemContinuityScanner.WindowFit fit, IEnumerable<(double x, double y)> points, SKColor color, bool dashed)
        {
            if (fit == null) return;
            var list = points.ToList();
            var x0 = list.Min(p => p.x);
            var x1 = list.Max(p => p.x);
            using var path = new SKPath();
            const int steps = 60;
            for (var step = 0; step <= steps; step++)
            {
                var x = x0 + (x1 - x0) * step / steps;
                var point = new SKPoint(plot.MapX(x), plot.MapY(Kj(fit.Evaluate(x))));
                if (step == 0) path.MoveTo(point);
                else path.LineTo(point);
            }

            using var paint = Stroke(color, 1.4f);
            if (dashed) paint.PathEffect = SKPathEffect.CreateDash(new[] { 4f, 3f }, 0);
            canvas.DrawPath(path, paint);
        }

        // ---------------------------------------------------------------- plot frame

        sealed class Plot
        {
            public SKRect Area { get; private init; }
            double xMin, xMax, yMin, yMax;

            public static Plot Create(SKRect area, IEnumerable<double> xs, IEnumerable<double> ys, double padFraction)
            {
                var xList = xs.Where(double.IsFinite).ToList();
                var yList = ys.Where(double.IsFinite).ToList();
                var (x0, x1) = Padded(xList.Min(), xList.Max(), padFraction);
                var (y0, y1) = Padded(yList.Min(), yList.Max(), padFraction);
                return new Plot { Area = area, xMin = x0, xMax = x1, yMin = y0, yMax = y1 };
            }

            static (double, double) Padded(double min, double max, double fraction)
            {
                var span = max - min;
                if (!(span > 0)) span = Math.Max(Math.Abs(min), 1) * 0.1;
                return (min - fraction * span, max + fraction * span);
            }

            public float MapX(double x) => (float)(Area.Left + (x - xMin) / (xMax - xMin) * Area.Width);
            public float MapY(double y) => (float)(Area.Bottom - (y - yMin) / (yMax - yMin) * Area.Height);

            public void DrawAxes(SKCanvas canvas, string xLabel, string yLabel, bool compact = false)
            {
                var size = compact ? 7f : 8f;
                foreach (var (tick, label) in Ticks(xMin, xMax, compact ? 4 : 8))
                {
                    var x = MapX(tick);
                    Line(canvas, x, Area.Top, x, Area.Bottom, Grid, 0.5f);
                    Text(canvas, label, x, Area.Bottom + size + 3, size, TextSecondary, align: SKTextAlign.Center);
                }

                foreach (var (tick, label) in Ticks(yMin, yMax, compact ? 5 : 6))
                {
                    var y = MapY(tick);
                    Line(canvas, Area.Left, y, Area.Right, y, Grid, 0.5f);
                    Text(canvas, label, Area.Left - 4, y + size / 3, size, TextSecondary, align: SKTextAlign.Right);
                }

                Line(canvas, Area.Left, Area.Bottom, Area.Right, Area.Bottom, Axis, 0.8f);
                Line(canvas, Area.Left, Area.Top, Area.Left, Area.Bottom, Axis, 0.8f);
                Text(canvas, xLabel, Area.MidX, Area.Bottom + 2 * size + 8, size, TextSecondary, align: SKTextAlign.Center);

                if (yLabel != null)
                {
                    canvas.Save();
                    canvas.RotateDegrees(-90, Area.Left - 30, Area.MidY);
                    Text(canvas, yLabel, Area.Left - 30, Area.MidY, size, TextSecondary, align: SKTextAlign.Center);
                    canvas.Restore();
                }
            }

            static IEnumerable<(double tick, string label)> Ticks(double min, double max, int target)
            {
                var raw = (max - min) / target;
                var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
                var normalized = raw / magnitude;
                var step = (normalized < 1.5 ? 1 : normalized < 3 ? 2 : normalized < 7 ? 5 : 10) * magnitude;
                var decimals = Math.Max(0, -(int)Math.Floor(Math.Log10(step) + 1e-9));
                for (var tick = Math.Ceiling(min / step) * step; tick <= max + 1e-9 * step; tick += step)
                {
                    var rounded = Math.Round(tick, decimals);
                    yield return (rounded, (rounded == 0 ? 0 : rounded).ToString("F" + decimals, Invariant).Replace('-', '−'));
                }
            }
        }

        // ---------------------------------------------------------------- drawing primitives

        static SKCanvas BeginPage(SKDocument document)
        {
            var canvas = document.BeginPage(PageWidth, PageHeight);
            canvas.Clear(Surface);
            return canvas;
        }

        static void Text(SKCanvas canvas, string text, float x, float y, float size, SKColor color, bool bold = false, SKTextAlign align = SKTextAlign.Left)
        {
            using var font = new SKFont(bold ? BoldTypeface : Typeface, size);
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawText(text, x, y, align, font, paint);
        }

        static void Line(SKCanvas canvas, float x0, float y0, float x1, float y1, SKColor color, float width)
        {
            using var paint = Stroke(color, width);
            canvas.DrawLine(x0, y0, x1, y1, paint);
        }

        static void Rect(SKCanvas canvas, SKRect rect, SKColor color)
        {
            using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
            canvas.DrawRect(rect, paint);
        }

        static void Dot(SKCanvas canvas, SKPoint center, float radius, SKColor color)
        {
            using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawCircle(center, radius, paint);
        }

        static void Ring(SKCanvas canvas, SKPoint center, float radius, SKColor color, float width)
        {
            using var paint = Stroke(color, width);
            canvas.DrawCircle(center, radius, paint);
        }

        static void Cross(SKCanvas canvas, SKPoint center, float half, SKColor color)
        {
            using var paint = Stroke(color, 1f);
            canvas.DrawLine(center.X - half, center.Y - half, center.X + half, center.Y + half, paint);
            canvas.DrawLine(center.X - half, center.Y + half, center.X + half, center.Y - half, paint);
        }

        static SKPaint Stroke(SKColor color, float width) =>
            new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };

        // ---------------------------------------------------------------- formatting

        static readonly string HeatLabel = $"ΔH ({EnergyUnit.KiloJoule.GetUnit()}/mol)";

        static double Kj(double joulesPerMole) => Energy.ConvertFromJoule(joulesPerMole, EnergyUnit.KiloJoule);

        static string PolynomialName => TandemContinuityScanner.PolynomialOrder switch
        {
            2 => "quadratic",
            3 => "cubic",
            var order => $"polynomial of order {order}",
        };

        static string Percent(double fraction) => $"{(100 * fraction).ToString("0.0", Invariant)}%";

        static string Number(double value) => value.ToString("G3", Invariant);

        static string Truncate(string text, int length) => text.Length <= length ? text : text.Substring(0, length - 1) + "…";
    }
}
