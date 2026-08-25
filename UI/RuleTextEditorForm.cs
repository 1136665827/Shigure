using System.Drawing;

namespace Shigure;

/// <summary>
/// 规则注释编辑器。
/// </summary>
internal sealed class RuleTextEditorForm : Form
{
    private readonly TextBox _commentBox = new();

    public string CommentText { get; private set; } = string.Empty;

    public RuleTextEditorForm(string? comment)
    {
        InitializeComponent();
        _commentBox.Text = comment ?? string.Empty;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyDarkTitleBar(this);
    }

    private void InitializeComponent()
    {
        Text = "编辑规则注释";
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        ClientSize = new Size(760, 380);
        MinimumSize = new Size(620, 320);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(UiTheme.CardPadding, 12, UiTheme.CardPadding, 12),
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        Controls.Add(root);

        ConfigureMultilineTextBox(_commentBox, wordWrap: true);
        _commentBox.Margin = new Padding(0);
        var editorCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(UiTheme.CardPadding),
            Margin = new Padding(0, 0, 0, UiTheme.PageGap),
            ColumnCount = 1,
            RowCount = 2
        };
        editorCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        editorCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editorCard.Controls.Add(UiTheme.CreateSectionTitle(Font, "规则注释"), 0, 0);
        editorCard.Controls.Add(_commentBox, 0, 1);
        root.Controls.Add(editorCard, 0, 0);

        var actionCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(UiTheme.CardPadding, 10, UiTheme.CardPadding, 10),
            ColumnCount = 2,
            RowCount = 1
        };
        actionCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184));
        actionCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };

        var saveButton = UiTheme.CreateButton("保存", UiTheme.ButtonKind.Primary);
        UiTheme.StyleActionButton(saveButton, 84);
        saveButton.Margin = new Padding(8, 0, 0, 0);
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = UiTheme.CreateButton("取消", UiTheme.ButtonKind.Secondary);
        UiTheme.StyleActionButton(cancelButton, 84);
        cancelButton.Margin = new Padding(8, 0, 0, 0);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        actions.Controls.Add(saveButton);
        actions.Controls.Add(cancelButton);
        actionCard.Controls.Add(actions, 1, 0);
        root.Controls.Add(actionCard, 0, 1);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private static Label CreateLabel(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };

    private static void ConfigureMultilineTextBox(TextBox box, bool wordWrap)
    {
        UiTheme.StyleTextBox(box);
        box.Dock = DockStyle.Fill;
        box.Multiline = true;
        box.AcceptsReturn = true;
        box.AcceptsTab = true;
        box.WordWrap = wordWrap;
        box.ScrollBars = wordWrap ? ScrollBars.Vertical : ScrollBars.Both;
    }

    private void SaveAndClose()
    {
        CommentText = _commentBox.Text.Trim();
        DialogResult = DialogResult.OK;
    }
}
