using System;
using Eto.Drawing;
using Eto.Forms;

namespace Eto.NodeGraph;

/// <summary>Common pan, zoom and fit controls for a node graph canvas.</summary>
public sealed class NodeGraphNavigationControls : Panel
{
    /// <summary>Creates navigation controls bound to the supplied canvas.</summary>
    public NodeGraphNavigationControls(NodeGraphView view)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));
        Size = new Size(230, 76);
        var pan = new Drawable
        {
            Size = new Size(218, 30),
            ToolTip = "Drag to pan the graph",
            Cursor = Cursors.Move
        };
        pan.Paint += (_, e) =>
        {
            e.Graphics.FillRectangle(SystemColors.ControlBackground, new RectangleF(0, 0, pan.Width, pan.Height));
            e.Graphics.DrawText(SystemFonts.Default(), SystemColors.ControlText, 10, 6, "↔  Drag to pan  ↕");
        };
        PointF? dragStart = null;
        PointF startOffset = default;
        pan.MouseDown += (_, e) =>
        {
            if ((e.Buttons & MouseButtons.Primary) == 0) return;
            dragStart = pan.PointToScreen(e.Location);
            startOffset = view.Offset;
            pan.CaptureMouse();
            e.Handled = true;
        };
        pan.MouseMove += (_, e) =>
        {
            if (dragStart is not PointF start) return;
            var current = pan.PointToScreen(e.Location);
            view.Offset = new PointF(startOffset.X + current.X - start.X, startOffset.Y + current.Y - start.Y);
            e.Handled = true;
        };
        pan.MouseUp += (_, e) =>
        {
            dragStart = null;
            pan.ReleaseMouseCapture();
            e.Handled = true;
        };
        Button ZoomButton(string text, string tip, Action action)
        {
            var button = new Button { Text = text, ToolTip = tip, Width = 48, Height = 28 };
            button.Click += (_, _) => action();
            return button;
        }
        void Zoom(float factor) => view.ZoomAt(new PointF(view.Width / 2f, view.Height / 2f), factor);
        Content = new StackLayout
        {
            Size = new Size(230, 76),
            BackgroundColor = SystemColors.ControlBackground,
            Padding = 6,
            Spacing = 4,
            Items =
            {
                pan,
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Items =
                    {
                        ZoomButton("−", "Zoom out", () => Zoom(1 / 1.2f)),
                        ZoomButton("+", "Zoom in", () => Zoom(1.2f)),
                        ZoomButton("1:1", "Actual size", () => Zoom(1 / view.Zoom)),
                        ZoomButton("Fit", "Fit selection, or all nodes when nothing is selected (F)", () => view.FrameSelection())
                    }
                }
            }
        };
    }
}
