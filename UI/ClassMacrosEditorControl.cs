using System.Drawing;
using System.Globalization;

namespace Shigure;

/// <summary>
/// 图形化编辑 Fuyutsui core/classmacros.lua 的 ClassMacros
/// （dynamicSpells / staticSpells / specialSpells）。
/// </summary>
public sealed class ClassMacrosEditorControl : UserControl
{
    private readonly Func<string?> _resolveClassMacrosPath;
    private readonly Func<Task> _updateConfigAsync;

    private readonly ListBox _classList = new();
    private readonly Label _pathLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _offsetLabel = new();
    private readonly Button _reloadButton;
    private readonly Button _saveButton;

    private readonly ListBox _dynamicSpecList = new();
    private readonly DataGridView _dynamicGrid = new();
    private readonly DataGridView _staticGrid = new();
    private readonly DataGridView _specialGrid = new();

    private ClassMacrosStore.MacrosDocument? _document;
    private ClassMacrosStore.ClassMacros? _currentMacros;
    private string? _currentClassFile;
    private int? _currentClassId;
    private int? _currentDynamicSpecIndex;
    private bool _suppressUi;
    private bool _updatingDerivedColumns;
    private bool _dirty;

    public ClassMacrosEditorControl(Func<string?> resolveClassMacrosPath, Func<Task> updateConfigAsync)
    {
        _resolveClassMacrosPath = resolveClassMacrosPath;
        _updateConfigAsync = updateConfigAsync;
        _reloadButton = UiTheme.CreateButton("刷新", UiTheme.Field, UiTheme.Text);
        _saveButton = UiTheme.CreateButton("保存", UiTheme.Accent, Color.Black);
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
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildSidebar(), 0, 0);
        root.Controls.Add(BuildEditor(), 1, 0);
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
            item => (item as ClassListItem)?.ClassId);
        _classList.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppressUi)
            {
                SelectClassFromList();
            }
        };
        panel.Controls.Add(_classList, 0, 1);
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
            RowCount = 4,
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
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
        header.Controls.Add(_pathLabel, 1, 0);

        header.Controls.Add(CreateFieldCaption("状态"), 0, 1);
        ConfigureInfoLabel(_statusLabel, UiTheme.Muted);
        _statusLabel.Text = "点击刷新以从游戏窗口定位 core\\classmacros.lua";
        header.Controls.Add(_statusLabel, 1, 1);
        root.Controls.Add(header, 0, 0);

        _offsetLabel.Dock = DockStyle.Fill;
        _offsetLabel.AutoSize = false;
        _offsetLabel.ForeColor = UiTheme.Muted;
        _offsetLabel.TextAlign = ContentAlignment.MiddleLeft;
        _offsetLabel.Padding = new Padding(14, 0, 14, 0);
        _offsetLabel.Margin = new Padding(0);
        _offsetLabel.AutoEllipsis = true;
        _offsetLabel.Text = "创建顺序：动态宏（每项 30 槽）→ 静态宏 → 特殊宏；空字符串保留槽位";
        root.Controls.Add(_offsetLabel, 0, 1);

        root.Controls.Add(BuildSectionTabs(), 0, 2);

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
        root.Controls.Add(actionRow, 0, 3);
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
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0)
        };
        for (var i = 0; i < 3; i++)
        {
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
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
            BuildDynamicPage(),
            BuildArrayPage(_staticGrid, "完整宏", "注释", showParsedMacro: true),
            BuildArrayPage(_specialGrid, "完整宏", "注释", showParsedMacro: true)
        };
        foreach (var page in pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            contentHost.Controls.Add(page);
        }

        var labels = new Label[3];
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

        var titles = new[] { "动态宏", "静态宏", "特殊宏" };
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

    private Control BuildDynamicPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildDynamicSpecSidebar(), 0, 0);

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.SurfaceRaised,
            Margin = new Padding(0)
        };
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        ConfigureGrid(_dynamicGrid);
        _dynamicGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "法术名（每项占 30 个团队点名槽）",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _dynamicGrid.Columns.Add(CreateDeleteColumn());
        WireGrid(_dynamicGrid);
        editor.Controls.Add(_dynamicGrid, 0, 0);
        editor.Controls.Add(BuildMoveButtons(_dynamicGrid), 0, 1);
        root.Controls.Add(editor, 1, 0);
        return root;
    }

    private Control BuildDynamicSpecSidebar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 0, 12, 0),
            Margin = new Padding(0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = "专精",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Margin = new Padding(0)
        }, 0, 0);

        _dynamicSpecList.Dock = DockStyle.Fill;
        UiTheme.StyleListBox(
            _dynamicSpecList,
            Font,
            index => index >= 0 && index < _dynamicSpecList.Items.Count
                && _dynamicSpecList.Items[index] is DynamicSpecOption { ClassId: { } classId } option
                    ? (classId, option.SpecIndex)
                    : (null, null),
            showClassIconWithSpec: false);
        _dynamicSpecList.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppressUi)
            {
                SelectDynamicSpecFromList();
            }
        };
        panel.Controls.Add(_dynamicSpecList, 0, 1);
        return panel;
    }

    private Control BuildArrayPage(
        DataGridView grid,
        string textHeader,
        string commentHeader,
        bool showParsedMacro)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.SurfaceRaised
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        ConfigureGrid(grid);
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Index",
            HeaderText = "顺序",
            Width = 72,
            ReadOnly = true
        });
        if (showParsedMacro)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Unit",
                HeaderText = "单位",
                Width = 72,
                ReadOnly = true
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Condition",
                HeaderText = "条件",
                Width = 180,
                ReadOnly = true
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Spell",
                HeaderText = "技能",
                Width = 180,
                ReadOnly = true
            });
        }

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Text",
            HeaderText = textHeader,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Comment", HeaderText = commentHeader, Width = 180 });
        grid.Columns.Add(CreateDeleteColumn());
        WireGrid(grid);
        panel.Controls.Add(grid, 0, 0);
        panel.Controls.Add(BuildMoveButtons(grid), 0, 1);
        return panel;
    }

    private void WireGrid(DataGridView grid)
    {
        grid.CellContentClick += HandleDeleteClick;
        grid.CellFormatting += (_, e) => FormatMacroLineBreaks(grid, e);
        grid.CellParsing += (_, e) => ParseMacroLineBreaks(grid, e);
        grid.CellValueChanged += (_, e) =>
        {
            if (_updatingDerivedColumns)
            {
                return;
            }

            if ((grid == _staticGrid || grid == _specialGrid)
                && e.RowIndex >= 0
                && e.ColumnIndex >= 0
                && grid.Columns[e.ColumnIndex].Name is "Text" or "Comment")
            {
                UpdateMacroDisplay(grid, grid.Rows[e.RowIndex]);
            }

            MarkDirty();
            UpdateOffsetHint();
        };
        grid.UserAddedRow += (_, _) =>
        {
            RenumberArrayRows(grid);
            MarkDirty();
            UpdateOffsetHint();
        };
        grid.RowsRemoved += (_, _) =>
        {
            if (!_suppressUi)
            {
                RenumberArrayRows(grid);
                MarkDirty();
                UpdateOffsetHint();
            }
        };
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

    // 宏文本底层保留真实换行（供解析及保存），表格中以字面量 \n 单行显示。
    private void FormatMacroLineBreaks(DataGridView grid, DataGridViewCellFormattingEventArgs e)
    {
        if ((grid != _staticGrid && grid != _specialGrid)
            || e.RowIndex < 0
            || e.ColumnIndex < 0
            || grid.Columns[e.ColumnIndex].Name != "Text"
            || e.Value is not string text)
        {
            return;
        }

        e.Value = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "\\n", StringComparison.Ordinal);
        e.FormattingApplied = true;
    }

    private void ParseMacroLineBreaks(DataGridView grid, DataGridViewCellParsingEventArgs e)
    {
        if ((grid != _staticGrid && grid != _specialGrid)
            || e.RowIndex < 0
            || e.ColumnIndex < 0
            || grid.Columns[e.ColumnIndex].Name != "Text"
            || e.Value is not string text)
        {
            return;
        }

        e.Value = text.Replace("\\n", "\n", StringComparison.Ordinal);
        e.ParsingApplied = true;
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

        _document = null;
        _currentMacros = null;
        _currentClassFile = null;
        _currentClassId = null;
        _currentDynamicSpecIndex = null;
        _dirty = false;

        _suppressUi = true;
        try
        {
            _classList.Items.Clear();
            ClearGrids();

            var path = _resolveClassMacrosPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _pathLabel.Text = "未找到 core\\classmacros.lua";
                _statusLabel.Text = "请先打开游戏窗口，或确认已安装 Fuyutsui 插件后点击刷新。";
                UpdateOffsetHint();
                return;
            }

            try
            {
                _document = ClassMacrosStore.Load(path);
            }
            catch (Exception ex)
            {
                _pathLabel.Text = path;
                _statusLabel.Text = $"加载失败: {ex.Message}";
                return;
            }

            _pathLabel.Text = path;
            foreach (var (classId, className) in ClassNames.GetClasses())
            {
                var classFile = ClassMacrosStore.ToClassFileKey(classId);
                var has = _document.Classes.ContainsKey(classFile);
                _classList.Items.Add(new ClassListItem(classId, className, classFile, has));
            }

            // 文件中有但 ClassNames 未覆盖的键
            foreach (var classFile in _document.ClassOrder)
            {
                if (_classList.Items.Cast<ClassListItem>().Any(x =>
                        x.ClassFile.Equals(classFile, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                _classList.Items.Add(new ClassListItem(0, classFile, classFile, true));
            }

            _statusLabel.Text = $"已加载 {_document.Classes.Count} 个职业宏表";
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

        if (_dirty && _currentClassFile is not null
            && !_currentClassFile.Equals(item.ClassFile, StringComparison.OrdinalIgnoreCase)
            && !ConfirmDiscard())
        {
            _suppressUi = true;
            try
            {
                SelectClassInList(_currentClassFile);
            }
            finally
            {
                _suppressUi = false;
            }

            return;
        }

        var switching = _currentClassFile is not null
            && !_currentClassFile.Equals(item.ClassFile, StringComparison.OrdinalIgnoreCase);
        if (_dirty && switching)
        {
            // 丢弃：从源文本重新解析当前职业
            if (_document is not null)
            {
                try
                {
                    var reloaded = ClassMacrosStore.Load(_document.FilePath);
                    _document = reloaded;
                }
                catch
                {
                    // ignore
                }
            }

            _dirty = false;
        }
        else if (switching)
        {
            CommitCurrentFromUi();
        }

        _currentClassFile = item.ClassFile;
        _currentClassId = item.ClassId > 0 ? item.ClassId : null;
        if (_document is null)
        {
            _currentMacros = null;
            ClearGrids();
            UpdateOffsetHint();
            return;
        }

        if (!_document.Classes.TryGetValue(item.ClassFile, out var macros))
        {
            macros = new ClassMacrosStore.ClassMacros();
            _document.Classes[item.ClassFile] = macros;
            if (!_document.ClassOrder.Contains(item.ClassFile, StringComparer.OrdinalIgnoreCase))
            {
                _document.ClassOrder.Add(item.ClassFile);
            }
        }

        _currentMacros = macros;
        _statusLabel.Text = _dirty ? "已修改（未保存）" : (item.HasData ? "可编辑" : "新建空表（保存后写入）");
        FillAllEditors();
    }

    private void FillAllEditors()
    {
        _suppressUi = true;
        try
        {
            _dynamicGrid.Rows.Clear();
            _staticGrid.Rows.Clear();
            _specialGrid.Rows.Clear();
            _dynamicSpecList.Items.Clear();
            _currentDynamicSpecIndex = null;
            if (_currentMacros is null)
            {
                return;
            }

            RebuildDynamicSpecList();
            AddArrayRows(_staticGrid, _currentMacros.StaticSpells);
            AddArrayRows(_specialGrid, _currentMacros.SpecialSpells);
            if (_dynamicSpecList.Items.Count > 0)
            {
                _dynamicSpecList.SelectedIndex = 0;
            }

            FillDynamicEditor();
        }
        finally
        {
            _suppressUi = false;
        }

        UpdateOffsetHint();
    }

    private void RebuildDynamicSpecList()
    {
        _dynamicSpecList.Items.Clear();
        _dynamicSpecList.Items.Add(new DynamicSpecOption(_currentClassId, null, "通用"));

        var knownSpecIndexes = new HashSet<int>();
        if (_currentClassId is { } classId)
        {
            foreach (var spec in ClassNames.GetSpecs(classId))
            {
                knownSpecIndexes.Add(spec.Id);
                _dynamicSpecList.Items.Add(new DynamicSpecOption(classId, spec.Id, spec.Name));
            }
        }

        if (_currentMacros is null)
        {
            return;
        }

        foreach (var specIndex in _currentMacros.DynamicBySpec.Keys.OrderBy(index => index))
        {
            if (knownSpecIndexes.Add(specIndex))
            {
                _dynamicSpecList.Items.Add(
                    new DynamicSpecOption(_currentClassId, specIndex, $"专精{specIndex}"));
            }
        }
    }

    private void SelectDynamicSpecFromList()
    {
        if (_currentMacros is null || _dynamicSpecList.SelectedItem is not DynamicSpecOption option)
        {
            return;
        }

        CommitCurrentDynamicFromUi();
        _currentDynamicSpecIndex = option.SpecIndex;
        FillDynamicEditor();
        UpdateOffsetHint();
    }

    private void FillDynamicEditor()
    {
        var wasSuppressing = _suppressUi;
        _suppressUi = true;
        try
        {
            _dynamicGrid.Rows.Clear();
            if (_currentMacros is null)
            {
                return;
            }

            IReadOnlyList<string> spells = _currentDynamicSpecIndex is { } specIndex
                ? _currentMacros.DynamicBySpec.GetValueOrDefault(specIndex) ?? []
                : _currentMacros.DynamicCommon;
            foreach (var name in spells)
            {
                _dynamicGrid.Rows.Add(name, "×");
            }
        }
        finally
        {
            _suppressUi = wasSuppressing;
        }
    }

    private void UpdateOffsetHint()
    {
        var commonCount = 0;
        var specCount = 0;
        var staticCount = 0;
        var specialCount = 0;
        if (_currentMacros is not null)
        {
            // 数组中的空字符串同样占槽，所以按实际行数计算。
            staticCount = _staticGrid.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow);
            specialCount = _specialGrid.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow);
            var visibleDynamicCount = _dynamicGrid.Rows.Cast<DataGridViewRow>().Count(row => !row.IsNewRow);
            if (_currentDynamicSpecIndex is { } specIndex)
            {
                commonCount = _currentMacros.DynamicCommon.Count;
                specCount = visibleDynamicCount;
            }
            else
            {
                commonCount = visibleDynamicCount;
            }
        }

        var dynamicCount = commonCount + specCount;
        var dynamicSlots = dynamicCount * 30;
        var totalSlots = dynamicSlots + staticCount + specialCount;
        var scopeText = _currentDynamicSpecIndex is null
            ? $"通用 {commonCount} 项"
            : $"{GetCurrentDynamicSpecName()}：通用 {commonCount} + 专精 {specCount}，共 {dynamicCount} 项";
        _offsetLabel.Text =
            $"{scopeText}；动态宏 {dynamicSlots} 个（{dynamicCount} 项 × 30）；静态宏 {staticCount} 个；特殊宏 {specialCount} 个；" +
            $"共 {totalSlots} 个；最多 {FuyutsuiKeymapConverter.MacroSlotCapacity} 个";
    }

    private string GetCurrentDynamicSpecName()
        => _dynamicSpecList.SelectedItem is DynamicSpecOption option ? option.Name : "当前专精";

    private void CommitCurrentFromUi()
    {
        if (_currentMacros is null)
        {
            return;
        }

        CommitCurrentDynamicFromUi();
        WriteArrayGrid(_staticGrid, _currentMacros.StaticSpells);
        WriteArrayGrid(_specialGrid, _currentMacros.SpecialSpells);
    }

    private void CommitCurrentDynamicFromUi()
    {
        if (_currentMacros is null)
        {
            return;
        }

        var values = new List<string>();
        foreach (DataGridViewRow row in _dynamicGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            values.Add(row.Cells["Name"].Value?.ToString()?.Trim() ?? string.Empty);
        }

        if (_currentDynamicSpecIndex is not { } specIndex)
        {
            _currentMacros.DynamicCommon.Clear();
            _currentMacros.DynamicCommon.AddRange(values);
            return;
        }

        if (values.Count > 0 || _currentMacros.DynamicBySpec.ContainsKey(specIndex))
        {
            _currentMacros.UsesSpecDynamicSpells = true;
            _currentMacros.DynamicBySpec[specIndex] = values;
        }
    }

    private static void WriteArrayGrid(DataGridView grid, List<ClassMacrosStore.ArrayEntry> target)
    {
        target.Clear();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var text = row.Cells["Text"].Value?.ToString() ?? "";
            var comment = row.Cells["Comment"].Value?.ToString()?.Trim();
            target.Add(new ClassMacrosStore.ArrayEntry
            {
                Text = text.Replace("\r\n", "\n", StringComparison.Ordinal),
                Comment = string.IsNullOrWhiteSpace(comment) ? null : comment
            });
        }
    }

    private async Task SaveAndUpdateAsync()
    {
        if (_document is null)
        {
            MessageBox.Show("请先刷新并加载 classmacros.lua。", "宏", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            CommitCurrentFromUi();
            ClassMacrosStore.Save(_document);
            _dirty = false;
            _statusLabel.Text = "已写入 Lua，正在更新配置…";
            await _updateConfigAsync();
            if (IsDisposed)
            {
                return;
            }

            _statusLabel.Text = "已保存并更新配置";
            UpdateOffsetHint();
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                MessageBox.Show(ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _statusLabel.Text = $"保存失败: {ex.Message}";
            }
        }
    }

    private void MarkDirty()
    {
        if (_suppressUi)
        {
            return;
        }

        _dirty = true;
        if (_statusLabel.Text != "已修改（未保存）")
        {
            _statusLabel.Text = "已修改（未保存）";
        }
    }

    private bool ConfirmDiscard()
        => MessageBox.Show("当前修改尚未保存，确定丢弃吗？", "宏", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
           == DialogResult.Yes;

    private void SelectClassInList(string? classFile)
    {
        for (var i = 0; i < _classList.Items.Count; i++)
        {
            if (_classList.Items[i] is ClassListItem item
                && item.ClassFile.Equals(classFile, StringComparison.OrdinalIgnoreCase))
            {
                _classList.SelectedIndex = i;
                return;
            }
        }
    }

    private void ClearGrids()
    {
        _dynamicSpecList.Items.Clear();
        _dynamicGrid.Rows.Clear();
        _staticGrid.Rows.Clear();
        _specialGrid.Rows.Clear();
        _currentDynamicSpecIndex = null;
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
        RenumberArrayRows(grid);
        MarkDirty();
        UpdateOffsetHint();
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
        RenumberArrayRows(grid);
        MarkDirty();
        UpdateOffsetHint();
    }

    private void AddArrayRows(DataGridView grid, List<ClassMacrosStore.ArrayEntry> entries)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var rowIndex = grid.Rows.Add();
            var row = grid.Rows[rowIndex];
            row.Cells["Index"].Value = (i + 1).ToString(CultureInfo.InvariantCulture);
            row.Cells["Text"].Value = entry.Text;
            row.Cells["Comment"].Value = entry.Comment ?? "";
            row.Cells["Delete"].Value = "×";
            if (grid == _staticGrid || grid == _specialGrid)
            {
                UpdateMacroDisplay(grid, row);
            }
        }
    }

    private void UpdateMacroDisplay(DataGridView grid, DataGridViewRow row)
    {
        if (!grid.Columns.Contains("Unit")
            || !grid.Columns.Contains("Spell")
            || !grid.Columns.Contains("Condition")
            || row.IsNewRow)
        {
            return;
        }

        var text = row.Cells["Text"].Value?.ToString() ?? "";
        var comment = row.Cells["Comment"].Value?.ToString();
        var parsed = grid == _specialGrid
            ? FuyutsuiKeymapConverter.ParseSpecialMacro(text, comment)
            : FuyutsuiKeymapConverter.ParseStaticMacro(text, comment);

        _updatingDerivedColumns = true;
        try
        {
            row.Cells["Unit"].Value = grid == _specialGrid
                ? ReservedUnit.ToDisplayText(parsed.Unit)
                : parsed.Unit.ToString(CultureInfo.InvariantCulture);
            row.Cells["Spell"].Value = parsed.Spell;
            row.Cells["Condition"].Value = MacroConditionText.ToDisplayText(parsed.Condition);
        }
        finally
        {
            _updatingDerivedColumns = false;
        }
    }

    private static void RenumberArrayRows(DataGridView grid)
    {
        if (!grid.Columns.Contains("Index"))
        {
            return;
        }

        var index = 1;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!row.IsNewRow)
            {
                row.Cells["Index"].Value = index++.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    private sealed record DynamicSpecOption(int? ClassId, int? SpecIndex, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record ClassListItem(int ClassId, string Name, string ClassFile, bool HasData)
    {
        public override string ToString()
            => HasData ? Name : $"{Name}（无）";
    }
}
