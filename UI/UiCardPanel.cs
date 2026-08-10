using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Shigure;

internal sealed class UiCardPanel : TableLayoutPanel
{
    private Color _fillColor = UiTheme.SurfaceRaised;
    private int _cornerRadius = 10;

    public UiCardPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor
    {
        get => _fillColor;
        set
        {
            if (_fillColor == value)
            {
                return;
            }

            _fillColor = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            var next = Math.Max(0, value);
            if (_cornerRadius == next)
            {
                return;
            }

            _cornerRadius = next;
            Invalidate();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (ClientSize.Width <= 1 || ClientSize.Height <= 1)
        {
            return;
        }

        using var path = UiTheme.CreateRoundedRectanglePath(ClientRectangle, UiTheme.Scale(this, CornerRadius));
        using var fill = new SolidBrush(FillColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(fill, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width <= 1 || ClientSize.Height <= 1)
        {
            return;
        }

        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        using var path = UiTheme.CreateRoundedRectanglePath(bounds, UiTheme.Scale(this, CornerRadius));
        using var outline = new Pen(UiTheme.Border);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(outline, path);
    }
}
