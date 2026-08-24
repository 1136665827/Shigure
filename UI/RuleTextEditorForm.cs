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
        BackColor = UiTheme.Background;
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
            BackColor = UiTheme.Background,
            Padding = new Padding(14, 12, 14, 12),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        Controls.Add(root);

        root.Controls.Add(CreateLabel("注释"), 0, 0);
        ConfigureMultilineTextBox(_commentBox, wordWrap: true);
        _commentBox.Margin = new Padding(0, 0, 0, 8);
        root.Controls.Add(_commentBox, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = UiTheme.Background,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 0)
        };

        var saveButton = UiTheme.CreateButton("保存", UiTheme.Accent, Color.Black);
        saveButton.AutoSize = false;
        saveButton.Size = new Size(84, 36);
        saveButton.Margin = Padding.Empty;
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = UiTheme.CreateButton("取消", UiTheme.Field, UiTheme.Text);
        cancelButton.AutoSize = false;
        cancelButton.Size = new Size(84, 36);
        cancelButton.Margin = new Padding(0, 0, 10, 0);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        actions.Controls.Add(saveButton);
        actions.Controls.Add(cancelButton);
        root.Controls.Add(actions, 0, 2);

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
