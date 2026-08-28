using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Shigure;

internal sealed record UiDropDownOption(object? Value, string Display)
{
    public override string ToString() => Display;
}

/// <summary>统一的深色下拉弹出层，供普通下拉控件和表格下拉单元格共用。</summary>
internal static class UiDropDownPopup
{
    public static ToolStripDropDown? Show(
        Control owner,
        Rectangle anchorBounds,
        IReadOnlyList<UiDropDownOption> items,
        object? selectedValue,
        Action<UiDropDownOption> applySelection,
        int preferredWidth = 0,
        int minimumWidth = 120,
        int maximumWidth = 420,
        int maximumVisibleItems = 9,
        Action? closed = null)
    {
        if (items.Count == 0 || owner.IsDisposed || !owner.IsHandleCreated)
        {
            return null;
        }

        var scale = Math.Max(1f, owner.DeviceDpi / 96f);
        var itemHeight = Math.Max(
            (int)Math.Round(32 * scale),
            owner.Font.Height + (int)Math.Round(12 * scale));
        var visibleItems = Math.Clamp(items.Count, 1, maximumVisibleItems);
        var measuredWidth = items.Max(item =>
            TextRenderer.MeasureText(DisplayText(item.Display), owner.Font).Width);
        var requestedWidth = Math.Max(
            anchorBounds.Width,
            Math.Max(
                preferredWidth > 0 ? UiTheme.Scale(owner, preferredWidth) : 0,
                measuredWidth + (int)Math.Round(40 * scale)));
        var workingArea = Screen.FromControl(owner).WorkingArea;
        var availableWidth = Math.Max(1, workingArea.Width - 20);
        // maximumWidth 只限制因长文本或 DropDownWidth 产生的额外扩展，不能把弹层
        // 压得比锚定的控件/表格单元格更窄。宽表格在高 DPI 下经常超过 420px。
        var anchorWidth = Math.Clamp(anchorBounds.Width, 1, availableWidth);
        var scaledMinimumWidth = Math.Max(
            anchorWidth,
            Math.Min(UiTheme.Scale(owner, minimumWidth), availableWidth));
        var scaledMaximumWidth = Math.Max(
            scaledMinimumWidth,
            Math.Min(UiTheme.Scale(owner, maximumWidth), availableWidth));
        var popupWidth = Math.Clamp(
            requestedWidth,
            scaledMinimumWidth,
            scaledMaximumWidth);
        var listWidth = Math.Max(1, popupWidth - 2);
        var listHeight = visibleItems * itemHeight + 2;

        var listBox = new ListBox
        {
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            IntegralHeight = false,
            ItemHeight = itemHeight,
            Font = owner.Font,
            Size = new Size(listWidth, listHeight)
        };
        listBox.Items.AddRange(items.Cast<object>().ToArray());
        listBox.DrawItem += DrawItem;
        listBox.MouseMove += (_, e) =>
        {
            var index = listBox.IndexFromPoint(e.Location);
            if (index >= 0 && index != listBox.SelectedIndex)
            {
                listBox.SelectedIndex = index;
            }
        };

        var selectedIndex = FindSelectedIndex(items, selectedValue);
        if (selectedIndex >= 0)
        {
            listBox.SelectedIndex = selectedIndex;
        }

        var host = new ToolStripControlHost(listBox)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = listBox.Size
        };
        var dropDown = new ToolStripDropDown
        {
            AutoSize = false,
            AutoClose = true,
            BackColor = UiTheme.Border,
            DropShadowEnabled = true,
            Margin = Padding.Empty,
            Padding = new Padding(1),
            Size = new Size(popupWidth, listHeight + 2)
        };
        dropDown.Items.Add(host);

        void ApplySelectedValue()
        {
            if (listBox.SelectedItem is not UiDropDownOption selected)
            {
                return;
            }

            applySelection(selected);
            dropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        listBox.Click += (_, _) => ApplySelectedValue();
        listBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                ApplySelectedValue();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                dropDown.Close(ToolStripDropDownCloseReason.Keyboard);
            }
        };
        dropDown.Closed += (_, _) => closed?.Invoke();

        var screenAnchor = owner.RectangleToScreen(anchorBounds);
        var screenLocation = new Point(screenAnchor.Left, screenAnchor.Bottom);
        if (screenLocation.X + dropDown.Width > workingArea.Right)
        {
            screenLocation.X = Math.Max(workingArea.Left, workingArea.Right - dropDown.Width);
        }

        if (screenLocation.Y + dropDown.Height > workingArea.Bottom)
        {
            screenLocation.Y = Math.Max(workingArea.Top, screenAnchor.Top - dropDown.Height);
        }

        dropDown.Show(owner, owner.PointToClient(screenLocation), ToolStripDropDownDirection.BelowRight);
        listBox.Focus();
        return dropDown;
    }

    private static int FindSelectedIndex(IReadOnlyList<UiDropDownOption> items, object? selectedValue)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (Equals(items[i].Value, selectedValue))
            {
                return i;
            }
        }

        return -1;
    }

    private static string DisplayText(string value)
        => string.IsNullOrEmpty(value) ? "（留空）" : value;

    private static void DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ListBox listBox
            || e.Index < 0
            || e.Index >= listBox.Items.Count
            || listBox.Items[e.Index] is not UiDropDownOption item)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var background = new SolidBrush(selected ? UiTheme.AccentSoft : UiTheme.Surface))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }

        var textBounds = new Rectangle(
            e.Bounds.Left + 10,
            e.Bounds.Top,
            Math.Max(0, e.Bounds.Width - 20),
            e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            DisplayText(item.Display),
            listBox.Font,
            textBounds,
            selected ? UiTheme.Accent : UiTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>不依赖 Windows 原生 ComboBox 按钮的主题下拉控件。</summary>
internal sealed class UiDropDown : Control
{
    private readonly UiDropDownItemCollection _items;
    private ToolStripDropDown? _dropDown;
    private int _selectedIndex = -1;
    private int _updateCount;
    private bool _hovered;

    public UiDropDown()
    {
        _items = new UiDropDownItemCollection(this);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.Selectable
            | ControlStyles.UserPaint,
            true);
        BackColor = UiTheme.Field;
        ForeColor = UiTheme.Text;
        Cursor = Cursors.Hand;
        Margin = Padding.Empty;
        MinimumSize = new Size(40, 24);
        Size = new Size(160, 32);
        TabStop = true;
        AccessibleRole = AccessibleRole.ComboBox;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public UiDropDownItemCollection Items => _items;

    [DefaultValue(0)]
    public int DropDownWidth { get; set; }

    [DefaultValue(-1)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= Items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_selectedIndex == value)
            {
                return;
            }

            _selectedIndex = value;
            base.Text = SelectedItem?.ToString() ?? string.Empty;
            InvalidateWhenReady();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? SelectedItem => SelectedIndex >= 0 ? Items[SelectedIndex] : null;

    public event EventHandler? SelectedIndexChanged;

    public void BeginUpdate() => _updateCount++;

    public void EndUpdate()
    {
        if (_updateCount > 0)
        {
            _updateCount--;
        }

        InvalidateWhenReady();
    }

    internal void HandleItemsCleared()
    {
        if (_selectedIndex != -1)
        {
            _selectedIndex = -1;
            base.Text = string.Empty;
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateWhenReady();
    }

    internal void HandleItemsChanged()
    {
        if (_selectedIndex >= Items.Count)
        {
            SelectedIndex = -1;
            return;
        }

        base.Text = SelectedItem?.ToString() ?? string.Empty;
        InvalidateWhenReady();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var bounds = new Rectangle(0, 0, Math.Max(0, ClientSize.Width - 1), Math.Max(0, ClientSize.Height - 1));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var active = Focused || _dropDown is not null;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = UiTheme.CreateRoundedRectanglePath(bounds, UiTheme.Scale(this, UiTheme.ControlCornerRadius)))
        using (var background = new SolidBrush(Enabled ? UiTheme.Field : UiTheme.SurfaceRaised))
        using (var border = new Pen(active ? UiTheme.Accent : UiTheme.Border))
        {
            e.Graphics.FillPath(background, path);
            e.Graphics.DrawPath(border, path);
        }

        var buttonBounds = UiTheme.GetDropDownButtonBounds(this, bounds);
        var textBounds = new Rectangle(
            UiTheme.Scale(this, 10),
            0,
            Math.Max(0, buttonBounds.Left - UiTheme.Scale(this, 16)),
            ClientSize.Height);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textBounds,
            Enabled ? ForeColor : UiTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        UiTheme.PaintDropDownButton(e.Graphics, this, buttonBounds, active, Enabled, _hovered);
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
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Enabled)
        {
            Focus();
            ToggleDropDown();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled)
        {
            return;
        }

        if (e.KeyCode is Keys.Enter or Keys.Space or Keys.F4 || (e.Alt && e.KeyCode == Keys.Down))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ToggleDropDown();
            return;
        }

        if (e.KeyCode == Keys.Escape && _dropDown is not null)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            CloseDropDown();
            return;
        }

        if (e.KeyCode is Keys.Up or Keys.Down && Items.Count > 0)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            var delta = e.KeyCode == Keys.Up ? -1 : 1;
            SelectedIndex = Math.Clamp(SelectedIndex < 0 ? 0 : SelectedIndex + delta, 0, Items.Count - 1);
        }
    }

    protected override bool IsInputKey(Keys keyData)
    {
        var keyCode = keyData & Keys.KeyCode;
        return keyCode is Keys.Up or Keys.Down or Keys.Enter or Keys.Space or Keys.F4
            || base.IsInputKey(keyData);
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

    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled)
        {
            CloseDropDown();
        }

        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        if (!Visible)
        {
            CloseDropDown();
        }

        base.OnVisibleChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseDropDown();
        }

        base.Dispose(disposing);
    }

    private void ToggleDropDown()
    {
        if (_dropDown is not null)
        {
            CloseDropDown();
            return;
        }

        var options = Items.Select(item => new UiDropDownOption(item, item?.ToString() ?? string.Empty)).ToList();
        _dropDown = UiDropDownPopup.Show(
            this,
            ClientRectangle,
            options,
            SelectedItem,
            selected => SelectedIndex = Items.IndexOf(selected.Value),
            preferredWidth: DropDownWidth,
            closed: () =>
            {
                _dropDown = null;
                if (!IsDisposed && IsHandleCreated)
                {
                    Invalidate();
                }
            });
        Invalidate();
    }

    private void CloseDropDown()
    {
        var dropDown = _dropDown;
        _dropDown = null;
        dropDown?.Close(ToolStripDropDownCloseReason.AppClicked);
        InvalidateWhenReady();
    }

    private void InvalidateWhenReady()
    {
        if (_updateCount == 0)
        {
            Invalidate();
        }
    }
}

internal sealed class UiDropDownItemCollection : IEnumerable<object?>
{
    private readonly UiDropDown _owner;
    private readonly List<object?> _items = new();

    public UiDropDownItemCollection(UiDropDown owner)
    {
        _owner = owner;
    }

    public int Count => _items.Count;

    public object? this[int index] => _items[index];

    public int Add(object? item)
    {
        _items.Add(item);
        _owner.HandleItemsChanged();
        return _items.Count - 1;
    }

    public void AddRange(params object?[] items)
    {
        _items.AddRange(items);
        _owner.HandleItemsChanged();
    }

    public void Clear()
    {
        _items.Clear();
        _owner.HandleItemsCleared();
    }

    public bool Contains(object? item) => _items.Contains(item);

    public int IndexOf(object? item) => _items.IndexOf(item);

    public IEnumerator<object?> GetEnumerator() => _items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
