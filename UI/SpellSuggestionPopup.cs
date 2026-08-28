namespace Shigure;

/// <summary>在输入框下方显示带图标的 spellId 候选，不抢占文本框焦点。</summary>
internal static class SpellSuggestionPopup
{
    public static ToolStripDropDown? Show(
        Control owner,
        Rectangle anchorBounds,
        IReadOnlyList<SpellSuggestion> items,
        Action<SpellSuggestion> applySelection,
        Action? closed = null)
    {
        if (items.Count == 0 || owner.IsDisposed || !owner.IsHandleCreated)
        {
            return null;
        }

        var rowHeight = Math.Max(UiTheme.Scale(owner, 44), owner.Font.Height + UiTheme.Scale(owner, 16));
        var workingArea = Screen.FromControl(owner).WorkingArea;
        var availableWidth = Math.Max(1, workingArea.Width - UiTheme.Scale(owner, 20));
        var popupWidth = Math.Clamp(anchorBounds.Width, 1, availableWidth);
        var listSize = new Size(Math.Max(1, popupWidth - 2), rowHeight * items.Count);

        ToolStripDropDown? dropDown = null;
        var list = new SpellSuggestionList(
            items,
            rowHeight,
            suggestion =>
            {
                applySelection(suggestion);
                Dismiss(dropDown, ToolStripDropDownCloseReason.ItemClicked);
            })
        {
            Size = listSize,
            Font = owner.Font
        };
        var host = new ToolStripControlHost(list)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = listSize
        };
        dropDown = new NonActivatingToolStripDropDown
        {
            AutoSize = false,
            AutoClose = false,
            BackColor = UiTheme.Border,
            DropShadowEnabled = true,
            Margin = Padding.Empty,
            Padding = new Padding(1),
            Size = new Size(popupWidth, listSize.Height + 2)
        };
        dropDown.Items.Add(host);
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
        return dropDown;
    }

    public static void Dismiss(
        ToolStripDropDown? dropDown,
        ToolStripDropDownCloseReason reason = ToolStripDropDownCloseReason.AppClicked)
    {
        if (dropDown is null || dropDown.IsDisposed)
        {
            return;
        }

        if (dropDown is NonActivatingToolStripDropDown nonActivating)
        {
            nonActivating.Dismiss(reason);
            return;
        }

        dropDown.Close(reason);
        if (dropDown.Visible)
        {
            dropDown.Hide();
        }
    }

    private sealed class NonActivatingToolStripDropDown : ToolStripDropDown, IMessageFilter
    {
        private const int WsExNoActivate = 0x08000000;
        private const int WmMouseActivate = 0x0021;
        private const int WmActivateApp = 0x001C;
        private const int WmLeftButtonDown = 0x0201;
        private const int WmRightButtonDown = 0x0204;
        private const int WmMiddleButtonDown = 0x0207;
        private const int WmNonClientLeftButtonDown = 0x00A1;
        private static readonly IntPtr MaNoActivate = new(3);
        private bool _messageFilterInstalled;

        protected override CreateParams CreateParams
        {
            get
            {
                var createParams = base.CreateParams;
                createParams.ExStyle |= WsExNoActivate;
                return createParams;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmMouseActivate)
            {
                message.Result = MaNoActivate;
                return;
            }

            base.WndProc(ref message);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (!_messageFilterInstalled)
            {
                Application.AddMessageFilter(this);
                _messageFilterInstalled = true;
            }
        }

        protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
        {
            RemoveMessageFilter();
            base.OnClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                RemoveMessageFilter();
            }

            base.Dispose(disposing);
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg == WmActivateApp && message.WParam == IntPtr.Zero)
            {
                Dismiss(ToolStripDropDownCloseReason.AppFocusChange);
            }
            else if (message.Msg is WmLeftButtonDown or WmRightButtonDown
                     or WmMiddleButtonDown or WmNonClientLeftButtonDown
                     && !Bounds.Contains(Control.MousePosition))
            {
                Dismiss(ToolStripDropDownCloseReason.AppClicked);
            }

            return false;
        }

        public void Dismiss(ToolStripDropDownCloseReason reason)
        {
            if (IsDisposed)
            {
                return;
            }

            RemoveMessageFilter();
            AutoClose = true;
            Close(reason);
            if (Visible)
            {
                Hide();
            }
        }

        private void RemoveMessageFilter()
        {
            if (!_messageFilterInstalled)
            {
                return;
            }

            Application.RemoveMessageFilter(this);
            _messageFilterInstalled = false;
        }
    }

    private sealed class SpellSuggestionList : Control
    {
        private readonly IReadOnlyList<SpellSuggestion> _items;
        private readonly int _rowHeight;
        private readonly Action<SpellSuggestion> _applySelection;
        private int _hoveredIndex = -1;

        public SpellSuggestionList(
            IReadOnlyList<SpellSuggestion> items,
            int rowHeight,
            Action<SpellSuggestion> applySelection)
        {
            _items = items;
            _rowHeight = rowHeight;
            _applySelection = applySelection;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = UiTheme.Surface;
            ForeColor = UiTheme.Text;
            Cursor = Cursors.Hand;
            TabStop = false;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "技能候选";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(UiTheme.Surface);
            var iconSize = Math.Min(UiTheme.Scale(this, 32), _rowHeight - UiTheme.Scale(this, 8));
            var horizontalPadding = UiTheme.Scale(this, 10);
            var iconGap = UiTheme.Scale(this, 10);
            var idWidth = UiTheme.Scale(this, 104);

            using var separator = new Pen(UiTheme.Border);
            using var hoveredBrush = new SolidBrush(UiTheme.AccentSoft);
            for (var index = 0; index < _items.Count; index++)
            {
                var rowBounds = new Rectangle(0, index * _rowHeight, ClientSize.Width, _rowHeight);
                if (index == _hoveredIndex)
                {
                    e.Graphics.FillRectangle(hoveredBrush, rowBounds);
                }

                var iconBounds = new Rectangle(
                    horizontalPadding,
                    rowBounds.Top + (rowBounds.Height - iconSize) / 2,
                    iconSize,
                    iconSize);
                var suggestion = _items[index];
                var icon = SpellIconCatalog.Get(suggestion.SpellId);
                if (icon is not null)
                {
                    e.Graphics.DrawImage(icon, iconBounds);
                }
                else
                {
                    e.Graphics.DrawRectangle(separator, iconBounds);
                }

                var idBounds = new Rectangle(
                    Math.Max(iconBounds.Right + iconGap, rowBounds.Right - idWidth - horizontalPadding),
                    rowBounds.Top,
                    idWidth,
                    rowBounds.Height);
                var nameBounds = new Rectangle(
                    iconBounds.Right + iconGap,
                    rowBounds.Top,
                    Math.Max(0, idBounds.Left - iconBounds.Right - iconGap),
                    rowBounds.Height);
                var textColor = index == _hoveredIndex ? UiTheme.Accent : ForeColor;
                TextRenderer.DrawText(
                    e.Graphics,
                    suggestion.Name,
                    Font,
                    nameBounds,
                    textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(
                    e.Graphics,
                    suggestion.SpellId.ToString(),
                    Font,
                    idBounds,
                    index == _hoveredIndex ? UiTheme.Accent : UiTheme.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                    | TextFormatFlags.NoPrefix);

                if (index < _items.Count - 1)
                {
                    e.Graphics.DrawLine(separator, rowBounds.Left, rowBounds.Bottom - 1, rowBounds.Right, rowBounds.Bottom - 1);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var index = e.Y >= 0 ? e.Y / _rowHeight : -1;
            index = index >= 0 && index < _items.Count ? index : -1;
            if (_hoveredIndex != index)
            {
                _hoveredIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoveredIndex >= 0)
            {
                _hoveredIndex = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            var index = e.Y >= 0 ? e.Y / _rowHeight : -1;
            if (index >= 0 && index < _items.Count)
            {
                _applySelection(_items[index]);
            }
        }
    }
}
