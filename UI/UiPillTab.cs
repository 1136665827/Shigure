using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Shigure;

/// <summary>
/// 编辑器分区标签：圆角 pill，选中为 AccentSoft 填充。
/// </summary>
internal sealed class UiPillTab : Control
{
    private bool _selected;
    private bool _hovered;
    private bool _pressed;

    public UiPillTab(string text)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true);
        Text = text;
        Dock = DockStyle.Fill;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        ForeColor = UiTheme.Muted;
        Margin = new Padding(3, 2, 3, 2);
        TabStop = true;
        AccessibleRole = AccessibleRole.PageTab;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_pressed)
        {
            _pressed = false;
            Invalidate();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var fill = _selected
            ? UiTheme.AccentSoft
            : _pressed
                ? UiTheme.Pressed
                : _hovered
                    ? UiTheme.Hover
                    : Color.Transparent;

        if (fill.A > 0)
        {
            using var path = UiTheme.CreateRoundedRectanglePath(bounds, UiTheme.Scale(this, UiTheme.ControlCornerRadius));
            using var brush = new SolidBrush(fill);
            graphics.FillPath(brush, path);
            if (_selected)
            {
                using var border = new Pen(Color.FromArgb(90, UiTheme.Accent));
                graphics.DrawPath(border, path);
            }
        }

        var textColor = _selected || _hovered ? UiTheme.Text : UiTheme.Muted;
        if (_selected)
        {
            textColor = UiTheme.Accent;
        }

        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            ClientRectangle,
            textColor,
            TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            var focusBounds = Rectangle.Inflate(bounds, -4, -4);
            ControlPaint.DrawFocusRectangle(graphics, focusBounds, UiTheme.Text, fill);
        }
    }
}
