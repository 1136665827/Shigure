using System.Drawing;

namespace Shigure;

public sealed class FormulaEditorForm : Form
{
    private readonly TextBox _formulaBox = new();

    public string FormulaText { get; private set; } = string.Empty;

    public FormulaEditorForm(string? formula)
    {
        FormulaText = FormulaEvaluator.NormalizeExpression(formula);
        InitializeComponent();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyDarkTitleBar(this);
    }

    private void InitializeComponent()
    {
        Text = "编辑公式";
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        ClientSize = new Size(760, 260);
        FormBorderStyle = FormBorderStyle.FixedDialog;
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

        _formulaBox.Multiline = true;
        _formulaBox.ScrollBars = ScrollBars.Vertical;
        _formulaBox.Text = FormulaText;
        _formulaBox.Dock = DockStyle.Fill;
        _formulaBox.Margin = new Padding(0);
        UiTheme.StyleTextBox(_formulaBox);
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
        editorCard.Controls.Add(UiTheme.CreateSectionTitle(Font, "公式表达式"), 0, 0);
        editorCard.Controls.Add(_formulaBox, 0, 1);
        root.Controls.Add(editorCard, 0, 0);

        root.Controls.Add(BuildActionRow(), 0, 1);
    }

    private Control BuildActionRow()
    {
        var row = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(UiTheme.CardPadding, 10, UiTheme.CardPadding, 10),
            Margin = new Padding(0),
            ColumnCount = 2,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        var okButton = UiTheme.CreateButton("确定", UiTheme.ButtonKind.Primary);
        UiTheme.StyleActionButton(okButton, 84);
        okButton.Margin = new Padding(8, 0, 0, 0);
        okButton.Click += (_, _) =>
        {
            FormulaText = _formulaBox.Text.Trim();
            DialogResult = DialogResult.OK;
        };

        var cancelButton = UiTheme.CreateButton("取消", UiTheme.ButtonKind.Secondary);
        UiTheme.StyleActionButton(cancelButton, 84);
        cancelButton.Margin = new Padding(8, 0, 0, 0);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        actions.Controls.Add(okButton);
        actions.Controls.Add(cancelButton);
        row.Controls.Add(actions, 1, 0);
        AcceptButton = okButton;
        CancelButton = cancelButton;
        return row;
    }
}
