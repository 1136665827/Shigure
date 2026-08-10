using System.Drawing;
using System.Globalization;

namespace Shigure;

/// <summary>
/// 图形化编辑 Fuyutsui class/*.lua 的 ClassBlocks（states / auras / spells / group），
/// 并展示同文件中的 spellsList。
/// </summary>
public sealed class ClassConfigEditorControl : UserControl
{
    private readonly Func<string?> _resolveClassDirectory;
    private readonly Func<string, Task<string?>> _updateConfigAsync;

    private readonly ListBox _classList = new();
    private readonly ListBox _specList = new();
    private readonly Label _pathLabel = new();
    private readonly Label _statusLabel = new();
    private readonly ToolTip _toolTip = new();
    private readonly Button _reloadButton = null!;
    private readonly Button _saveButton = null!;

    private readonly DataGridView _statesGrid = new();
    private readonly DataGridViewComboBoxColumn _stateNameColumn = new();
    private readonly DataGridView _aurasGrid = new();
    private readonly ComboBox _auraBucketBox = new();
    private readonly DataGridView _spellsGrid = new();
    private readonly DataGridView _spellsListGrid = new();
    private readonly NumericUpDown _groupNumBox = new();
    private readonly NumericUpDown _groupHealthBox = new();
    private readonly NumericUpDown _groupRoleBox = new();
    private readonly NumericUpDown _groupDispelBox = new();
    private readonly CheckBox _groupEnabledBox = new();
    private readonly CheckBox _groupHasDispelBox = new();
    private readonly DataGridView _groupAurasGrid = new();

    private string? _classDirectory;
    private readonly Dictionary<int, ClassBlocksStore.ClassFileDocument> _documents = new();
    private ClassBlocksStore.ClassFileDocument? _currentDocument;
    private ClassBlocksStore.SpecBlocks? _currentSpec;
    private int? _currentClassId;
    private int? _currentSpecId;
    private bool _suppressUi;
    private bool _dirty;

    internal event Action<bool>? DirtyStateChanged;
    private string _selectedStateCategory = ClassStateCatalog.CategoryState;
    private string _lastStateCategory = ClassStateCatalog.CategoryState;
    private string _lastAuraBucket = "player";

    private static readonly string[] FixedStateNames = ["锚点", "职业", "专精"];

    private static readonly (string Key, string Text)[] AuraBuckets =
    [
        ("player", "玩家"),
        ("target.harmful", "目标·敌对"),
        ("target.helpful", "目标·友善"),
        ("focus.harmful", "焦点·敌对"),
        ("focus.helpful", "焦点·友善")
    ];

    public ClassConfigEditorControl(
        Func<string?> resolveClassDirectory,
        Func<string, Task<string?>> updateConfigAsync)
    {
        _resolveClassDirectory = resolveClassDirectory;
        _updateConfigAsync = updateConfigAsync;
        _reloadButton = UiTheme.CreateButton("刷新", UiTheme.ButtonKind.Secondary);
        _saveButton = UiTheme.CreateButton("保存", UiTheme.ButtonKind.Primary);
        InitializeComponent();
        ReloadFromAddon();
    }

    private void InitializeComponent()
    {
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildSidebar(), 0, 0);
        root.Controls.Add(BuildSpecSidebar(), 1, 0);
        root.Controls.Add(BuildEditor(), 2, 0);
    }

    private Control BuildSidebar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 12, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = "职业",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 0);

        _classList.Dock = DockStyle.Fill;
        UiTheme.StyleClassIconListBox(
            _classList,
            item => (item as ClassListItem)?.ClassId,
            iconSize: 40);
        _classList.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressUi)
            {
                return;
            }

            SelectClassFromList();
        };
        panel.Controls.Add(_classList, 0, 1);
        return panel;
    }

    private Control BuildSpecSidebar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 12, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = "专精",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 0);

        _specList.Dock = DockStyle.Fill;
        UiTheme.StyleSpecIconListBox(
            _specList,
            item => item is SpecOption spec ? (spec.ClassId, spec.Id) : null,
            iconSize: 40);
        _specList.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressUi)
            {
                return;
            }

            SelectSpec(_specList.SelectedItem as SpecOption);
        };
        panel.Controls.Add(_specList, 0, 1);
        return panel;
    }

    private static void StyleActionButton(Button button)
    {
        button.AutoSize = false;
        button.Size = new Size(110, 36);
        button.Margin = new Padding(0, 0, 0, 8);
        button.TextAlign = ContentAlignment.MiddleCenter;
    }

    private Control BuildEditor()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(14, 12, 14, 12),
            Margin = new Padding(0, 0, 0, 8)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        header.Controls.Add(CreateFieldCaption("路径"), 0, 0);
        ConfigureInfoLabel(_pathLabel, UiTheme.Text);
        _pathLabel.Text = "未加载";
        _pathLabel.TextChanged += (_, _) => _toolTip.SetToolTip(_pathLabel, _pathLabel.Text);
        _toolTip.SetToolTip(_pathLabel, _pathLabel.Text);
        header.Controls.Add(_pathLabel, 1, 0);

        header.Controls.Add(CreateFieldCaption("状态"), 0, 1);
        ConfigureInfoLabel(_statusLabel, UiTheme.Muted);
        _statusLabel.Text = "点击刷新以加载项目 Fuyutsui\\class";
        _statusLabel.TextChanged += (_, _) => _toolTip.SetToolTip(_statusLabel, _statusLabel.Text);
        _toolTip.SetToolTip(_statusLabel, _statusLabel.Text);
        header.Controls.Add(_statusLabel, 1, 1);
        root.Controls.Add(header, 0, 0);

        root.Controls.Add(BuildSectionTabs(), 0, 1);

        var actionRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0, 8, 12, 12)
        };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 228));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };
        StyleActionButton(_reloadButton);
        StyleActionButton(_saveButton);
        _reloadButton.Margin = new Padding(0, 0, 8, 0);
        _saveButton.Margin = new Padding(0);
        _reloadButton.Click += (_, _) => ReloadFromAddon();
        _saveButton.Click += async (_, _) => await SaveAndUpdateAsync();
        actions.Controls.Add(_reloadButton);
        actions.Controls.Add(_saveButton);
        actionRow.Controls.Add(actions, 1, 0);
        root.Controls.Add(actionRow, 0, 2);
        return root;
    }

    private Control BuildSectionTabs()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var tabBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Border,
            ColumnCount = 5,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        for (var i = 0; i < 5; i++)
        {
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        }

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0),
            Padding = new Padding(12)
        };

        var pages = new Control[]
        {
            BuildStatesPage(),
            BuildAurasPage(),
            BuildSpellsPage(),
            BuildGroupPage(),
            BuildSpellsListPage()
        };
        foreach (var page in pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            contentHost.Controls.Add(page);
        }

        var labels = new Label[5];
        var selectedIndex = -1;
        void SelectTab(int index)
        {
            if (selectedIndex == index)
            {
                return;
            }

            selectedIndex = index;
            for (var i = 0; i < labels.Length; i++)
            {
                var selected = i == index;
                labels[i].BackColor = selected ? UiTheme.Field : UiTheme.Surface;
                labels[i].ForeColor = selected ? UiTheme.Text : UiTheme.Muted;
                labels[i].Invalidate();
                pages[i].Visible = selected;
                if (selected)
                {
                    pages[i].BringToFront();
                }
            }
        }

        var titles = new[] { "状态", "光环", "法术", "队伍", "技能列表" };
        for (var i = 0; i < titles.Length; i++)
        {
            var index = i;
            var label = new Label
            {
                Text = titles[i],
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Muted,
                Cursor = Cursors.Hand,
                Margin = new Padding(i == 0 ? 0 : 1, 0, 0, 0)
            };
            label.Click += (_, _) => SelectTab(index);
            label.MouseEnter += (_, _) =>
            {
                if (selectedIndex != index)
                {
                    label.BackColor = UiTheme.Hover;
                }
            };
            label.MouseLeave += (_, _) =>
            {
                if (selectedIndex != index)
                {
                    label.BackColor = UiTheme.Surface;
                }
            };
            label.Paint += (_, e) =>
            {
                if (selectedIndex != index)
                {
                    return;
                }

                using var accent = new SolidBrush(UiTheme.Accent);
                e.Graphics.FillRectangle(accent, 8, label.Height - 3, Math.Max(0, label.Width - 16), 2);
            };
            label.SizeChanged += (_, _) => label.Invalidate();
            labels[i] = label;
            tabBar.Controls.Add(label, i, 0);
        }

        root.Controls.Add(tabBar, 0, 0);
        root.Controls.Add(contentHost, 0, 1);
        SelectTab(0);
        return root;
    }

    private Control BuildStatesPage()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.SurfaceRaised
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        panel.Controls.Add(BuildStateCategoryTabs(), 0, 0);

        ConfigureGrid(_statesGrid);
        _stateNameColumn.Name = "Name";
        _stateNameColumn.HeaderText = "状态名";
        _stateNameColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _stateNameColumn.DisplayMember = nameof(ClassStateCatalog.StateOption.Display);
        _stateNameColumn.ValueMember = nameof(ClassStateCatalog.StateOption.Name);
        _stateNameColumn.FlatStyle = FlatStyle.Flat;
        _stateNameColumn.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
        _statesGrid.Columns.Add(_stateNameColumn);
        _statesGrid.Columns.Add(CreateDeleteColumn());
        _statesGrid.CellContentClick += HandleDeleteClick;
        _statesGrid.CellValueChanged += (_, _) => MarkDirty();
        _statesGrid.UserAddedRow += (_, _) => MarkDirty();
        _statesGrid.DataError += (_, e) => e.ThrowException = false;
        _statesGrid.EditingControlShowing += HandleStateEditingControlShowing;
        panel.Controls.Add(_statesGrid, 0, 1);
        panel.Controls.Add(BuildMoveButtons(_statesGrid), 0, 2);
        return panel;
    }

    private Control BuildStateCategoryTabs()
    {
        var categories = ClassStateCatalog.TopCategories;
        var tabBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Border,
            ColumnCount = categories.Length,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        foreach (var _ in categories)
        {
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / categories.Length));
        }

        var labels = new Label[categories.Length];
        void ApplySelection()
        {
            for (var i = 0; i < labels.Length; i++)
            {
                var selected = string.Equals(categories[i], _selectedStateCategory, StringComparison.Ordinal);
                labels[i].BackColor = selected ? UiTheme.Field : UiTheme.Surface;
                labels[i].ForeColor = selected ? UiTheme.Text : UiTheme.Muted;
                labels[i].Invalidate();
            }
        }

        void SelectCategory(string category)
        {
            if (_suppressUi
                || string.Equals(category, _selectedStateCategory, StringComparison.Ordinal))
            {
                return;
            }

            _statesGrid.EndEdit();
            WriteBackStatesCategory(_lastStateCategory);
            _selectedStateCategory = category;
            _lastStateCategory = category;
            ApplySelection();
            ReloadStatesGrid();
        }

        for (var i = 0; i < categories.Length; i++)
        {
            var category = categories[i];
            var label = new Label
            {
                Text = category,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Muted,
                Cursor = Cursors.Hand,
                Margin = new Padding(i == 0 ? 0 : 1, 0, 0, 0)
            };
            label.Click += (_, _) => SelectCategory(category);
            label.MouseEnter += (_, _) =>
            {
                if (!string.Equals(category, _selectedStateCategory, StringComparison.Ordinal))
                {
                    label.BackColor = UiTheme.Hover;
                }
            };
            label.MouseLeave += (_, _) =>
            {
                if (!string.Equals(category, _selectedStateCategory, StringComparison.Ordinal))
                {
                    label.BackColor = UiTheme.Surface;
                }
            };
            label.Paint += (_, e) =>
            {
                if (!string.Equals(category, _selectedStateCategory, StringComparison.Ordinal))
                {
                    return;
                }

                using var accent = new SolidBrush(UiTheme.Accent);
                e.Graphics.FillRectangle(accent, 8, label.Height - 3, Math.Max(0, label.Width - 16), 2);
            };
            label.SizeChanged += (_, _) => label.Invalidate();
            labels[i] = label;
            tabBar.Controls.Add(label, i, 0);
        }

        ApplySelection();
        return tabBar;
    }

    private Control BuildAurasPage()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.SurfaceRaised
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        top.Controls.Add(CreateMutedLabel("单位"));
        UiTheme.StyleComboBox(_auraBucketBox);
        _auraBucketBox.Width = 180;
        foreach (var bucket in AuraBuckets)
        {
            _auraBucketBox.Items.Add(new BucketOption(bucket.Key, bucket.Text));
        }

        _auraBucketBox.SelectedIndex = 0;
        _auraBucketBox.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressUi)
            {
                return;
            }

            WriteBackAuras(_lastAuraBucket);
            _lastAuraBucket = (_auraBucketBox.SelectedItem as BucketOption)?.Key ?? "player";
            FillAurasGrid();
        };
        top.Controls.Add(_auraBucketBox);
        panel.Controls.Add(top, 0, 0);

        ConfigureGrid(_aurasGrid);
        _aurasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", Width = 160 });
        _aurasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SpellId", HeaderText = "spellId", Width = 110 });
        _aurasGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SpellIds",
            HeaderText = "spellIds（逗号分隔）",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _aurasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "MaxApps", HeaderText = "maxApps", Width = 135 });
        _aurasGrid.Columns.Add(CreateDeleteColumn());
        _aurasGrid.CellContentClick += HandleDeleteClick;
        _aurasGrid.CellValueChanged += (_, _) => MarkDirty();
        _aurasGrid.UserAddedRow += (_, _) => MarkDirty();
        panel.Controls.Add(_aurasGrid, 0, 1);
        panel.Controls.Add(BuildMoveButtons(_aurasGrid), 0, 2);
        return panel;
    }

    private Control BuildSpellsPage()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.SurfaceRaised
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var textureOrderHint = CreateFieldCaption(
            "纹理按行排列；充能法术连续占 2 格：冷却 → 充能冷却。最大充能、施法次数只生成横向计数条。");
        textureOrderHint.Padding = new Padding(8, 0, 0, 0);
        panel.Controls.Add(textureOrderHint, 0, 0);

        ConfigureGrid(_spellsGrid);
        _spellsGrid.Columns.Add(CreateSpellTextColumn("Name", "名称", 24, 160));
        _spellsGrid.Columns.Add(CreateSpellTextColumn("SpellId", "法术 ID", 14, 120));
        _spellsGrid.Columns.Add(CreateSpellCheckColumn("Charge", "充能", 10, 80));
        _spellsGrid.Columns.Add(CreateSpellTextColumn("MaxCharge", "最大充能", 14, 110));
        _spellsGrid.Columns.Add(CreateSpellTextColumn("CastCount", "施法次数", 14, 110));
        _spellsGrid.Columns.Add(CreateSpellCheckColumn("ForcedKnown", "强制已学", 14, 110));
        _spellsGrid.Columns.Add(CreateSpellCheckColumn("InSpellBook", "法术书中", 14, 110));
        _spellsGrid.Columns.Add(CreateDeleteColumn());
        _spellsGrid.CellContentClick += HandleDeleteClick;
        _spellsGrid.CellValueChanged += (_, _) => MarkDirty();
        _spellsGrid.UserAddedRow += (_, _) => MarkDirty();
        _spellsGrid.DataError += (_, e) => e.ThrowException = false;
        panel.Controls.Add(_spellsGrid, 0, 1);
        panel.Controls.Add(BuildMoveButtons(_spellsGrid), 0, 2);
        return panel;
    }

    private Control BuildSpellsListPage()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.SurfaceRaised
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var hint = CreateFieldCaption("来自当前职业 Lua 的 Fuyutsui.spellsList，仅显示索引 1–100（只读）。");
        hint.Padding = new Padding(8, 0, 0, 0);
        panel.Controls.Add(hint, 0, 0);

        ConfigureGrid(_spellsListGrid);
        _spellsListGrid.AllowUserToAddRows = false;
        _spellsListGrid.ReadOnly = true;
        _spellsListGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
        _spellsListGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SpellId",
            HeaderText = "法术 ID",
            Width = 150,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _spellsListGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Index",
            HeaderText = "索引",
            Width = 120,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _spellsListGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "名称",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        panel.Controls.Add(_spellsListGrid, 0, 1);
        return panel;
    }

    private Control BuildGroupPage()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.SurfaceRaised
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = UiTheme.SurfaceRaised
        };
        _groupEnabledBox.Text = "启用 group";
        _groupEnabledBox.ForeColor = UiTheme.Text;
        _groupEnabledBox.AutoSize = true;
        _groupEnabledBox.CheckedChanged += (_, _) =>
        {
            if (!_suppressUi)
            {
                MarkDirty();
                UpdateGroupEditorsEnabled();
            }
        };
        fields.Controls.Add(_groupEnabledBox);
        fields.Controls.Add(CreateField("num", _groupNumBox, 1, 40, 5));
        fields.Controls.Add(CreateField("healthPercent", _groupHealthBox, 0, 40, 1));
        fields.Controls.Add(CreateField("role", _groupRoleBox, 0, 40, 2));
        _groupHasDispelBox.Text = "dispel";
        _groupHasDispelBox.ForeColor = UiTheme.Text;
        _groupHasDispelBox.AutoSize = true;
        _groupHasDispelBox.CheckedChanged += (_, _) =>
        {
            if (!_suppressUi)
            {
                MarkDirty();
                _groupDispelBox.Enabled = _groupEnabledBox.Checked && _groupHasDispelBox.Checked;
            }
        };
        fields.Controls.Add(_groupHasDispelBox);
        fields.Controls.Add(CreateField("", _groupDispelBox, 0, 40, 3));
        panel.Controls.Add(fields, 0, 0);

        ConfigureGrid(_groupAurasGrid);
        _groupAurasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Offset", HeaderText = "偏移", Width = 70 });
        _groupAurasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", Width = 160 });
        _groupAurasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SpellId", HeaderText = "spellId", Width = 110 });
        _groupAurasGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SpellIds",
            HeaderText = "spellIds（逗号分隔）",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _groupAurasGrid.Columns.Add(CreateDeleteColumn());
        _groupAurasGrid.CellContentClick += HandleDeleteClick;
        _groupAurasGrid.CellValueChanged += (_, _) => MarkDirty();
        _groupAurasGrid.UserAddedRow += (_, _) => MarkDirty();
        panel.Controls.Add(_groupAurasGrid, 0, 1);
        panel.Controls.Add(BuildMoveButtons(_groupAurasGrid), 0, 2);
        return panel;
    }

    private Control CreateField(string label, NumericUpDown box, decimal min, decimal max, decimal value)
    {
        var host = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(12, 0, 0, 0)
        };
        if (!string.IsNullOrEmpty(label))
        {
            host.Controls.Add(CreateMutedLabel(label));
        }

        UiTheme.StyleNumericUpDown(box);
        box.Minimum = min;
        box.Maximum = max;
        box.Value = value;
        box.Width = 70;
        box.ValueChanged += (_, _) =>
        {
            if (!_suppressUi)
            {
                MarkDirty();
            }
        };
        host.Controls.Add(box);
        return host;
    }

    private Control BuildMoveButtons(DataGridView grid)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiTheme.SurfaceRaised
        };
        var up = UiTheme.CreateButton("▲", UiTheme.Field, UiTheme.Text);
        var down = UiTheme.CreateButton("▼", UiTheme.Field, UiTheme.Text);
        up.AutoSize = false;
        down.AutoSize = false;
        up.Size = new Size(48, 32);
        down.Size = new Size(48, 32);
        up.Click += (_, _) => MoveSelectedRow(grid, -1);
        down.Click += (_, _) => MoveSelectedRow(grid, 1);
        bar.Controls.Add(up);
        bar.Controls.Add(down);
        return bar;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        UiTheme.StyleDataGridView(grid);
        grid.AllowUserToAddRows = true;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.EditMode = DataGridViewEditMode.EditOnEnter;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    }

    private static DataGridViewButtonColumn CreateDeleteColumn()
        => new()
        {
            Name = "Delete",
            HeaderText = "",
            Text = "×",
            UseColumnTextForButtonValue = true,
            Width = 44
        };

    private static DataGridViewTextBoxColumn CreateSpellTextColumn(
        string name,
        string headerText,
        float fillWeight,
        int minimumWidth)
        => new()
        {
            Name = name,
            HeaderText = headerText,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = fillWeight,
            MinimumWidth = minimumWidth,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

    private static DataGridViewCheckBoxColumn CreateSpellCheckColumn(
        string name,
        string headerText,
        float fillWeight,
        int minimumWidth)
        => new()
        {
            Name = name,
            HeaderText = headerText,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = fillWeight,
            MinimumWidth = minimumWidth,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            TrueValue = true,
            FalseValue = false,
            IndeterminateValue = false,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                NullValue = false
            }
        };

    private Label CreateMutedLabel(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 8, 8, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };

    private static Label CreateFieldCaption(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            AutoEllipsis = true
        };

    private static void ConfigureInfoLabel(Label label, Color foreColor)
    {
        label.Dock = DockStyle.Fill;
        label.AutoSize = false;
        label.ForeColor = foreColor;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.AutoEllipsis = true;
        label.Margin = new Padding(0);
    }

    public void ReloadFromAddon()
    {
        if (_dirty && !ConfirmDiscard())
        {
            return;
        }

        _classDirectory = _resolveClassDirectory();
        _documents.Clear();
        _currentDocument = null;
        _currentSpec = null;
        _currentClassId = null;
        _currentSpecId = null;
        SetDirty(false);

        _suppressUi = true;
        try
        {
            _classList.Items.Clear();
            ClearSpecList();
            ClearGrids();

            if (string.IsNullOrWhiteSpace(_classDirectory) || !Directory.Exists(_classDirectory))
            {
                _pathLabel.Text = "未找到 Fuyutsui\\class";
                _statusLabel.Text = "请确认程序目录中包含 Fuyutsui\\class 后点击刷新。";
                return;
            }

            _pathLabel.Text = _classDirectory;
            foreach (var (classId, className) in ClassNames.GetClasses())
            {
                var fileName = ClassNames.GetConfigFileName(classId);
                var path = Path.Combine(_classDirectory, $"{fileName}.lua");
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    var doc = ClassBlocksStore.Load(path);
                    _documents[classId] = doc;
                    _classList.Items.Add(new ClassListItem(classId, className, fileName, doc.IsModernFormat));
                }
                catch (Exception ex)
                {
                    _classList.Items.Add(new ClassListItem(classId, className, fileName, false, ex.Message));
                }
            }

            _statusLabel.Text = $"已加载 {_documents.Count} 个职业文件";
            if (_classList.Items.Count > 0)
            {
                _classList.SelectedIndex = 0;
            }
        }
        finally
        {
            _suppressUi = false;
        }

        SelectClassFromList();
    }

    private void SelectClassFromList()
    {
        if (_classList.SelectedItem is not ClassListItem item)
        {
            return;
        }

        if (_dirty && _currentClassId != item.ClassId && !ConfirmDiscard())
        {
            _suppressUi = true;
            try
            {
                SelectClassInList(_currentClassId);
            }
            finally
            {
                _suppressUi = false;
            }

            return;
        }

        var discarding = _dirty && _currentClassId != item.ClassId;
        if (_dirty && _currentClassId == item.ClassId)
        {
            return;
        }

        if (discarding && _currentClassId is { } previousClassId && _documents.ContainsKey(previousClassId))
        {
            try
            {
                _documents[previousClassId] = ClassBlocksStore.Load(_documents[previousClassId].FilePath);
            }
            catch
            {
                // 丢弃失败时保留内存副本，避免阻断切换。
            }
        }

        SetDirty(false);
        _currentClassId = item.ClassId;
        _documents.TryGetValue(item.ClassId, out _currentDocument);
        _pathLabel.Text = _currentDocument?.FilePath ?? Path.Combine(_classDirectory ?? "", $"{item.FileName}.lua");

        if (_currentDocument is null)
        {
            _statusLabel.Text = string.IsNullOrWhiteSpace(item.Error)
                ? "无法加载该职业文件"
                : item.Error!;
            _currentSpec = null;
            _currentSpecId = null;
            _suppressUi = true;
            try
            {
                ClearSpecList();
                ClearGrids();
            }
            finally
            {
                _suppressUi = false;
            }

            return;
        }

        if (!_currentDocument.IsModernFormat)
        {
            _statusLabel.Text = "此文件仍是旧版稀疏索引格式，请先迁移到 states/auras/spells/group 后再编辑。";
        }
        else
        {
            _statusLabel.Text = _dirty ? "已修改（未保存）" : "可编辑";
        }

        _suppressUi = true;
        try
        {
            var options = new List<SpecOption>();
            foreach (var spec in ClassNames.GetSpecs(item.ClassId))
            {
                if (_currentDocument.Specs.ContainsKey(spec.Id))
                {
                    options.Add(new SpecOption(item.ClassId, spec.Id, spec.Name));
                }
            }

            // 也显示文件中有但 ClassNames 未登记的专精。
            foreach (var specId in _currentDocument.Specs.Keys.OrderBy(x => x))
            {
                if (options.Any(x => x.Id == specId))
                {
                    continue;
                }

                options.Add(new SpecOption(item.ClassId, specId, $"专精{specId}"));
            }

            RebuildSpecList(options);
            _currentSpecId = null;
            _currentSpec = null;
        }
        finally
        {
            _suppressUi = false;
        }

        SelectSpec(_specList.SelectedItem as SpecOption);
    }

    private void RebuildSpecList(IReadOnlyList<SpecOption> options)
    {
        _specList.Items.Clear();
        foreach (var option in options)
        {
            _specList.Items.Add(option);
        }

        if (_specList.Items.Count > 0)
        {
            _specList.SelectedIndex = 0;
        }
    }

    private void ClearSpecList()
    {
        _specList.Items.Clear();
    }

    private void SelectSpec(SpecOption? spec)
    {
        if (_currentDocument is null || spec is null)
        {
            _currentSpec = null;
            _currentSpecId = null;
            _specList.Invalidate();
            ClearGrids();
            return;
        }

        if (_dirty && _currentSpecId is not null && _currentSpecId != spec.Id)
        {
            CommitCurrentSpecFromUi();
        }

        _currentSpecId = spec.Id;
        if (!_currentDocument.Specs.TryGetValue(spec.Id, out var blocks))
        {
            blocks = new ClassBlocksStore.SpecBlocks();
            _currentDocument.Specs[spec.Id] = blocks;
        }

        _currentSpec = blocks;
        _specList.Invalidate();
        FillAllEditors();
    }

    private void FillAllEditors()
    {
        _suppressUi = true;
        try
        {
            _lastStateCategory = _selectedStateCategory;
            _lastAuraBucket = (_auraBucketBox.SelectedItem as BucketOption)?.Key ?? "player";
            FillStatesGrid();
            FillAurasGrid();
            FillSpellsGrid();
            FillGroupEditors();
            FillSpellsListGrid();
        }
        finally
        {
            _suppressUi = false;
        }
    }

    private void FillStatesGrid()
    {
        _statesGrid.Rows.Clear();
        if (_currentSpec is null)
        {
            return;
        }

        var category = _selectedStateCategory;
        BindStateNameColumn(ClassStateCatalog.GetAllOptions(category));
        var storageCategory = ClassStateCatalog.GetStorageCategory(category);
        IEnumerable<string> names = _currentSpec.NestedStates
            ? _currentSpec.CategorizedStates.GetValueOrDefault(storageCategory) ?? []
            : _currentSpec.FlatStates;
        names = names.Where(name =>
            ClassStateCatalog.IsInCategory(name, category)
            && !IsHiddenStateName(name));

        foreach (var name in names)
        {
            EnsureStateOptionAvailable(category, name);
            _statesGrid.Rows.Add(name, "×");
        }
    }

    private void ReloadStatesGrid()
    {
        _suppressUi = true;
        try
        {
            FillStatesGrid();
        }
        finally
        {
            _suppressUi = false;
        }
    }

    private void BindStateNameColumn(IReadOnlyList<ClassStateCatalog.StateOption> options)
    {
        _stateNameColumn.DataSource = null;
        _stateNameColumn.DataSource = options.ToList();
        _stateNameColumn.DisplayMember = nameof(ClassStateCatalog.StateOption.Display);
        _stateNameColumn.ValueMember = nameof(ClassStateCatalog.StateOption.Name);
    }

    private void EnsureStateOptionAvailable(string category, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (_stateNameColumn.DataSource is IEnumerable<ClassStateCatalog.StateOption> current
            && current.Any(o => string.Equals(o.Name, name, StringComparison.Ordinal)))
        {
            return;
        }

        var options = ClassStateCatalog.GetAllOptions(category).ToList();
        if (!options.Any(o => string.Equals(o.Name, name, StringComparison.Ordinal)))
        {
            var optionCategory = ClassStateCatalog.FindCategory(name) ?? "未识别";
            options.Add(new ClassStateCatalog.StateOption(optionCategory, name));
        }

        BindStateNameColumn(options);
    }

    private void HandleStateEditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (_statesGrid.CurrentCell?.OwningColumn?.Name != "Name")
        {
            return;
        }

        if (e.Control is not ComboBox combo)
        {
            return;
        }

        var category = _selectedStateCategory;
        var current = _statesGrid.CurrentCell.Value?.ToString()?.Trim();
        var currentRowIndex = _statesGrid.CurrentCell.RowIndex;
        var usedNames = GetUsedStateNames(category, currentRowIndex);
        var options = ClassStateCatalog.GetOptions(category)
            .Where(option => !usedNames.Contains(option.Name) && !IsHiddenStateName(option.Name))
            .ToList();
        if (!string.IsNullOrWhiteSpace(current)
            && !options.Any(o => string.Equals(o.Name, current, StringComparison.Ordinal)))
        {
            var optionCategory = ClassStateCatalog.FindCategory(current) ?? "未识别";
            options.Insert(0, new ClassStateCatalog.StateOption(optionCategory, current));
        }

        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = UiTheme.Field;
        combo.ForeColor = UiTheme.Text;
        combo.DataSource = null;
        combo.DisplayMember = nameof(ClassStateCatalog.StateOption.Display);
        combo.ValueMember = nameof(ClassStateCatalog.StateOption.Name);
        combo.DataSource = options;
        if (!string.IsNullOrWhiteSpace(current))
        {
            combo.SelectedValue = current;
        }
    }

    private HashSet<string> GetUsedStateNames(string category, int excludedRowIndex)
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        if (_currentSpec is not null)
        {
            IEnumerable<string> storedNames;
            if (_currentSpec.NestedStates)
            {
                var storageCategory = ClassStateCatalog.GetStorageCategory(category);
                storedNames = _currentSpec.CategorizedStates.GetValueOrDefault(storageCategory) ?? [];
            }
            else
            {
                storedNames = _currentSpec.FlatStates;
            }

            foreach (var name in storedNames)
            {
                // 当前分类以表格中的未保存内容为准，其它分类仍以专精数据为准。
                if (!ClassStateCatalog.IsInCategory(name, category))
                {
                    usedNames.Add(name);
                }
            }
        }

        foreach (DataGridViewRow row in _statesGrid.Rows)
        {
            if (row.IsNewRow || row.Index == excludedRowIndex)
            {
                continue;
            }

            var name = row.Cells["Name"].Value?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                usedNames.Add(name);
            }
        }

        return usedNames;
    }

    private void FillAurasGrid()
    {
        _aurasGrid.Rows.Clear();
        if (_currentSpec is null)
        {
            return;
        }

        foreach (var aura in GetCurrentAuraList())
        {
            _aurasGrid.Rows.Add(
                aura.Name,
                aura.SpellId?.ToString(CultureInfo.InvariantCulture) ?? "",
                string.Join(", ", aura.SpellIds),
                aura.MaxApps?.ToString(CultureInfo.InvariantCulture) ?? "",
                "×");
        }
    }

    private void FillSpellsGrid()
    {
        _spellsGrid.Rows.Clear();
        if (_currentSpec is null)
        {
            return;
        }

        foreach (var spell in _currentSpec.Spells)
        {
            _spellsGrid.Rows.Add(
                spell.Name,
                spell.SpellId.ToString(CultureInfo.InvariantCulture),
                spell.Charge,
                spell.MaxCharge?.ToString(CultureInfo.InvariantCulture) ?? "",
                spell.CastCount?.ToString(CultureInfo.InvariantCulture) ?? "",
                spell.ForcedKnown,
                spell.InSpellBook,
                "×");
        }
    }

    private void FillSpellsListGrid()
    {
        _spellsListGrid.Rows.Clear();
        if (_currentDocument is null)
        {
            return;
        }

        foreach (var spell in _currentDocument.SpellsList.Where(spell => spell.Index is >= 1 and <= 100))
        {
            _spellsListGrid.Rows.Add(
                spell.SpellId.ToString(CultureInfo.InvariantCulture),
                spell.Index.ToString(CultureInfo.InvariantCulture),
                spell.Name);
        }
    }

    private void FillGroupEditors()
    {
        _groupAurasGrid.Rows.Clear();
        if (_currentSpec?.Group is { } group)
        {
            _groupEnabledBox.Checked = true;
            _groupNumBox.Value = Clamp(_groupNumBox, group.Num);
            _groupHealthBox.Value = Clamp(_groupHealthBox, group.HealthPercent ?? 1);
            _groupRoleBox.Value = Clamp(_groupRoleBox, group.Role ?? 2);
            _groupHasDispelBox.Checked = group.Dispel is not null;
            _groupDispelBox.Value = Clamp(_groupDispelBox, group.Dispel ?? 3);
            foreach (var aura in group.Auras)
            {
                _groupAurasGrid.Rows.Add(
                    aura.Offset.ToString(CultureInfo.InvariantCulture),
                    aura.Name,
                    aura.SpellId?.ToString(CultureInfo.InvariantCulture) ?? "",
                    string.Join(", ", aura.SpellIds),
                    "×");
            }
        }
        else
        {
            _groupEnabledBox.Checked = false;
            _groupHasDispelBox.Checked = false;
            _groupNumBox.Value = 5;
            _groupHealthBox.Value = 1;
            _groupRoleBox.Value = 2;
            _groupDispelBox.Value = 3;
        }

        UpdateGroupEditorsEnabled();
    }

    private void UpdateGroupEditorsEnabled()
    {
        var enabled = _groupEnabledBox.Checked;
        _groupNumBox.Enabled = enabled;
        _groupHealthBox.Enabled = enabled;
        _groupRoleBox.Enabled = enabled;
        _groupHasDispelBox.Enabled = enabled;
        _groupDispelBox.Enabled = enabled && _groupHasDispelBox.Checked;
        _groupAurasGrid.Enabled = enabled;
        _groupAurasGrid.ReadOnly = !enabled;
    }

    private List<ClassBlocksStore.AuraEntry> GetCurrentAuraList()
        => ResolveAuraList((_auraBucketBox.SelectedItem as BucketOption)?.Key ?? "player");

    private List<ClassBlocksStore.AuraEntry> ResolveAuraList(string key)
    {
        if (_currentSpec is null)
        {
            return [];
        }

        return key switch
        {
            "target.harmful" => _currentSpec.TargetHarmfulAuras,
            "target.helpful" => _currentSpec.TargetHelpfulAuras,
            "focus.harmful" => _currentSpec.FocusHarmfulAuras,
            "focus.helpful" => _currentSpec.FocusHelpfulAuras,
            _ => _currentSpec.PlayerAuras
        };
    }

    private void CommitCurrentSpecFromUi()
    {
        if (_currentSpec is null || _currentDocument is null || !_currentDocument.IsModernFormat)
        {
            return;
        }

        NormalizeFixedStateNames(_currentSpec);
        WriteBackStatesCategory(_lastStateCategory);

        WriteBackAuras(_lastAuraBucket);
        WriteBackSpells();
        WriteBackGroup();
    }

    private void WriteBackStatesCategory(string category)
    {
        if (_currentSpec is null)
        {
            return;
        }

        var storageCategory = ClassStateCatalog.GetStorageCategory(category);
        List<string> list;
        if (_currentSpec.NestedStates)
        {
            if (!_currentSpec.CategorizedStates.TryGetValue(storageCategory, out list!))
            {
                list = new List<string>();
                _currentSpec.CategorizedStates[storageCategory] = list;
            }
        }
        else
        {
            list = _currentSpec.FlatStates;
        }

        var editedNames = new List<string>();
        foreach (DataGridViewRow row in _statesGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var name = row.Cells["Name"].Value?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                editedNames.Add(name);
            }
        }

        var insertIndex = list.FindIndex(name =>
            ClassStateCatalog.IsInCategory(name, category)
            && !IsHiddenStateName(name));
        if (insertIndex < 0)
        {
            var anchorIndex = string.Equals(category, ClassStateCatalog.CategoryState, StringComparison.Ordinal)
                ? list.FindLastIndex(IsHiddenStateName)
                : -1;
            insertIndex = anchorIndex >= 0 ? anchorIndex + 1 : list.Count;
        }

        list.RemoveAll(name =>
            ClassStateCatalog.IsInCategory(name, category)
            && !IsHiddenStateName(name));
        list.InsertRange(Math.Min(insertIndex, list.Count), editedNames);
    }

    private void WriteBackAuras(string bucketKey)
    {
        if (_currentSpec is null)
        {
            return;
        }

        var list = ResolveAuraList(bucketKey);
        list.Clear();
        foreach (DataGridViewRow row in _aurasGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var name = row.Cells["Name"].Value?.ToString()?.Trim() ?? "";
            var spellIdsText = row.Cells["SpellIds"].Value?.ToString()?.Trim() ?? "";
            var spellIdText = row.Cells["SpellId"].Value?.ToString()?.Trim() ?? "";
            var maxAppsText = row.Cells["MaxApps"].Value?.ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(spellIdText) && string.IsNullOrWhiteSpace(spellIdsText))
            {
                continue;
            }

            var entry = new ClassBlocksStore.AuraEntry { Name = name };
            foreach (var id in ParseIdList(spellIdsText))
            {
                entry.SpellIds.Add(id);
            }

            if (long.TryParse(spellIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid))
            {
                entry.SpellId = sid;
            }

            if (int.TryParse(maxAppsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxApps))
            {
                entry.MaxApps = maxApps;
            }

            list.Add(entry);
        }
    }

    private void WriteBackSpells()
    {
        if (_currentSpec is null)
        {
            return;
        }

        _currentSpec.Spells.Clear();
        foreach (DataGridViewRow row in _spellsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var spellIdText = row.Cells["SpellId"].Value?.ToString()?.Trim() ?? "";
            if (!long.TryParse(spellIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var spellId))
            {
                continue;
            }

            var entry = new ClassBlocksStore.SpellEntry
            {
                SpellId = spellId,
                Name = row.Cells["Name"].Value?.ToString()?.Trim() ?? "",
                Charge = row.Cells["Charge"].Value is true,
                ForcedKnown = row.Cells["ForcedKnown"].Value is true,
                InSpellBook = row.Cells["InSpellBook"].Value is true
            };
            if (int.TryParse(row.Cells["MaxCharge"].Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxCharge))
            {
                entry.MaxCharge = maxCharge;
            }

            if (int.TryParse(row.Cells["CastCount"].Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var castCount))
            {
                entry.CastCount = castCount;
            }

            _currentSpec.Spells.Add(entry);
        }
    }

    private void WriteBackGroup()
    {
        if (_currentSpec is null)
        {
            return;
        }

        if (!_groupEnabledBox.Checked)
        {
            _currentSpec.Group = null;
            return;
        }

        var group = new ClassBlocksStore.GroupBlocks
        {
            Num = (int)_groupNumBox.Value,
            HealthPercent = (int)_groupHealthBox.Value,
            Role = (int)_groupRoleBox.Value,
            Dispel = _groupHasDispelBox.Checked ? (int)_groupDispelBox.Value : null
        };

        foreach (DataGridViewRow row in _groupAurasGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            if (!int.TryParse(row.Cells["Offset"].Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset))
            {
                continue;
            }

            var entry = new ClassBlocksStore.GroupAuraEntry
            {
                Offset = offset,
                Name = row.Cells["Name"].Value?.ToString()?.Trim() ?? ""
            };
            var spellIdsText = row.Cells["SpellIds"].Value?.ToString()?.Trim() ?? "";
            foreach (var id in ParseIdList(spellIdsText))
            {
                entry.SpellIds.Add(id);
            }

            if (long.TryParse(row.Cells["SpellId"].Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid))
            {
                entry.SpellId = sid;
            }

            group.Auras.Add(entry);
        }

        _currentSpec.Group = group;
    }

    private async Task SaveAndUpdateAsync()
    {
        if (_currentDocument is null || _currentClassId is null)
        {
            MessageBox.Show("请先选择一个职业文件。", "配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_currentDocument.IsModernFormat)
        {
            MessageBox.Show("旧版稀疏索引格式暂不支持保存。", "配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var localSaved = false;
        try
        {
            // 切换分类前把当前状态表写回。
            CommitCurrentSpecFromUi();
            ClassBlocksStore.Save(_currentDocument);
            localSaved = true;
            SetDirty(false);
            _statusLabel.Text = "本地 Lua 已保存，正在更新配置并同步游戏…";
            var syncIssue = await _updateConfigAsync(_currentDocument.FilePath);
            if (IsDisposed)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(syncIssue))
            {
                _statusLabel.Text = "本地已保存并更新配置，但游戏同步失败";
                MessageBox.Show(
                    $"本地 Lua 已保存，config/keymap 已更新，但游戏插件同步未完成：\n{syncIssue}",
                    "游戏同步失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _statusLabel.Text = "已保存并更新配置";
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                var title = localSaved ? "保存后的更新失败" : "保存失败";
                var message = localSaved
                    ? $"本地 Lua 已保存，但后续更新失败：\n{ex.Message}"
                    : ex.Message;
                MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                _statusLabel.Text = localSaved
                    ? $"本地已保存，后续更新失败: {ex.Message}"
                    : $"保存失败: {ex.Message}";
            }
        }
    }

    private void MarkDirty()
    {
        if (_suppressUi)
        {
            return;
        }

        SetDirty(true);
        if (_statusLabel.Text != "已修改（未保存）")
        {
            _statusLabel.Text = "已修改（未保存）";
        }
    }

    private void SetDirty(bool dirty)
    {
        if (_dirty == dirty)
        {
            _saveButton.Enabled = dirty;
            return;
        }

        _dirty = dirty;
        _saveButton.Enabled = dirty;
        DirtyStateChanged?.Invoke(dirty);
    }

    private bool ConfirmDiscard()
        => MessageBox.Show("当前修改尚未保存，确定丢弃吗？", "配置", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
           == DialogResult.Yes;

    private void SelectClassInList(int? classId)
    {
        for (var i = 0; i < _classList.Items.Count; i++)
        {
            if (_classList.Items[i] is ClassListItem item && item.ClassId == classId)
            {
                _classList.SelectedIndex = i;
                return;
            }
        }
    }

    private void ClearGrids()
    {
        _statesGrid.Rows.Clear();
        _aurasGrid.Rows.Clear();
        _spellsGrid.Rows.Clear();
        _spellsListGrid.Rows.Clear();
        _groupAurasGrid.Rows.Clear();
    }

    private void HandleDeleteClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (grid.Columns[e.ColumnIndex].Name != "Delete")
        {
            return;
        }

        if (grid.Rows[e.RowIndex].IsNewRow)
        {
            return;
        }

        grid.Rows.RemoveAt(e.RowIndex);
        MarkDirty();
    }

    private void MoveSelectedRow(DataGridView grid, int delta)
    {
        if (grid.CurrentRow is null || grid.CurrentRow.IsNewRow)
        {
            return;
        }

        var index = grid.CurrentRow.Index;
        var target = index + delta;
        if (target < 0 || target >= grid.Rows.Count || grid.Rows[target].IsNewRow)
        {
            return;
        }

        var values = new object[grid.Columns.Count];
        for (var i = 0; i < grid.Columns.Count; i++)
        {
            values[i] = grid.Rows[index].Cells[i].Value ?? DBNull.Value;
        }

        grid.Rows.RemoveAt(index);
        grid.Rows.Insert(target, values);
        grid.ClearSelection();
        grid.Rows[target].Selected = true;
        grid.CurrentCell = grid.Rows[target].Cells[0];
        MarkDirty();
    }

    private static IEnumerable<long> ParseIdList(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var part in text.Split([',', ' ', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                yield return id;
            }
        }
    }

    private static decimal Clamp(NumericUpDown box, int value)
        => Math.Min(box.Maximum, Math.Max(box.Minimum, value));

    private static void NormalizeFixedStateNames(ClassBlocksStore.SpecBlocks spec)
    {
        var states = spec.NestedStates
            ? spec.CategorizedStates[ClassStateCatalog.CategoryState]
            : spec.FlatStates;
        states.RemoveAll(IsHiddenStateName);
        states.InsertRange(0, FixedStateNames);
    }

    private static bool IsHiddenStateName(string? name)
        => name is not null && FixedStateNames.Contains(name, StringComparer.Ordinal);

    private sealed record ClassListItem(int ClassId, string Name, string FileName, bool IsModern, string? Error = null)
    {
        public override string ToString()
            => Error is not null ? $"{Name}（错误）" : IsModern ? Name : $"{Name}（旧格式）";
    }

    private sealed record SpecOption(int ClassId, int Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record BucketOption(string Key, string Text)
    {
        public override string ToString() => Text;
    }
}
