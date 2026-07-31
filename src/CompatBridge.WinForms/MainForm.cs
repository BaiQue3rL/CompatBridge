using CompatBridge.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CompatBridge
{
    internal sealed class MainForm : Form
    {
        private readonly TransactionService _service;
        private readonly Color _accent = Color.FromArgb(0, 103, 184);
        private readonly Color _success = Color.FromArgb(16, 124, 16);
        private readonly Color _danger = Color.FromArgb(196, 43, 28);

        private TextBox _addressInput;
        private TextBox _searchInput;
        private Button _addButton;
        private Button _bulkAddButton;
        private DataGridView _siteGrid;
        private Label _environmentLabel;
        private Label _siteCountLabel;
        private Label _resultLabel;
        private List<ManagedSite> _sites = new List<ManagedSite>();

        public MainForm()
        {
            _service = new TransactionService();
            InitializeWindow();
            BuildInterface();
            Load += delegate { RefreshAll(); };
        }

        private void InitializeWindow()
        {
            Text = "CompatBridge（兼容桥）";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 570);
            Size = new Size(920, 650);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.White;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }
        }

        private void BuildInterface()
        {
            Panel header = BuildHeader();
            Panel footer = BuildFooter();

            TableLayoutPanel content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.White
            };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Controls.Add(BuildAddCard(), 0, 0);
            content.Controls.Add(BuildListHeader(), 0, 1);

            _siteGrid = BuildSiteGrid();
            content.Controls.Add(_siteGrid, 0, 2);

            Controls.Add(content);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private Panel BuildHeader()
        {
            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = Color.FromArgb(245, 248, 252)
            };
            Label title = new Label
            {
                AutoSize = true,
                Text = "兼容桥  CompatBridge",
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(32, 32, 32),
                Location = new Point(82, 14)
            };
            Label subtitle = new Label
            {
                AutoSize = true,
                Text = "需要 IE 模式的网址，加进来就可以",
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(85, 54)
            };
            PictureBox logo = new PictureBox
            {
                Location = new Point(24, 22),
                Size = new Size(44, 44),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            if (Icon != null)
            {
                logo.Image = Icon.ToBitmap();
            }
            _environmentLabel = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(590, 24),
                Size = new Size(300, 36),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            header.Controls.Add(logo);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(_environmentLabel);
            header.Resize += delegate
            {
                _environmentLabel.Left =
                    header.ClientSize.Width - _environmentLabel.Width - 24;
            };
            return header;
        }

        private Control BuildAddCard()
        {
            Panel card = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(24, 18, 24, 8),
                Padding = new Padding(18),
                BackColor = Color.FromArgb(250, 252, 255),
                BorderStyle = BorderStyle.FixedSingle
            };
            Label title = new Label
            {
                AutoSize = true,
                Text = "添加网址",
                Font = new Font(Font, FontStyle.Bold),
                Location = new Point(18, 15)
            };
            _addressInput = new TextBox
            {
                Location = new Point(18, 48),
                Height = 31,
                Font = new Font("Microsoft YaHei UI", 11F),
                Anchor = AnchorStyles.Top | AnchorStyles.Left |
                    AnchorStyles.Right
            };
            _addressInput.KeyDown += AddressInputKeyDown;
            _addButton = CreatePrimaryButton("添加", AddAddress);
            _addButton.Location = new Point(700, 46);
            _addButton.Size = new Size(100, 34);
            _addButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _bulkAddButton = CreateSecondaryButton(
                "批量添加",
                ShowBulkAdd);
            _bulkAddButton.Location = new Point(580, 46);
            _bulkAddButton.Size = new Size(110, 34);
            _bulkAddButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Label hint = new Label
            {
                AutoSize = true,
                Text = "例如：www.example.com 或 http://192.168.1.10:8080/oa",
                ForeColor = Color.FromArgb(100, 100, 100),
                Location = new Point(20, 91)
            };
            card.Controls.Add(title);
            card.Controls.Add(_addressInput);
            card.Controls.Add(_bulkAddButton);
            card.Controls.Add(_addButton);
            card.Controls.Add(hint);
            card.Resize += delegate
            {
                _addButton.Left = card.ClientSize.Width -
                    _addButton.Width - 18;
                _bulkAddButton.Left =
                    _addButton.Left - _bulkAddButton.Width - 10;
                _addressInput.Width = Math.Max(
                    180,
                    _bulkAddButton.Left - _addressInput.Left - 12);
            };
            return card;
        }

        private Control BuildListHeader()
        {
            Panel panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 10, 24, 8)
            };
            _siteCountLabel = new Label
            {
                AutoSize = true,
                Text = "已添加的网址",
                Font = new Font(Font, FontStyle.Bold),
                Location = new Point(24, 18)
            };
            _searchInput = new TextBox
            {
                Width = 235,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(650, 13)
            };
            _searchInput.TextChanged += delegate { PopulateSites(); };
            Label searchLabel = new Label
            {
                AutoSize = true,
                Text = "查找：",
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(602, 17)
            };
            panel.Controls.Add(_siteCountLabel);
            panel.Controls.Add(searchLabel);
            panel.Controls.Add(_searchInput);
            panel.Resize += delegate
            {
                _searchInput.Left =
                    panel.ClientSize.Width - _searchInput.Width - 24;
                searchLabel.Left = _searchInput.Left - searchLabel.Width - 6;
            };
            return panel;
        }

        private DataGridView BuildSiteGrid()
        {
            DataGridView grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(24, 0, 24, 16),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                ReadOnly = true,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                RowTemplate = { Height = 38 }
            };
            grid.ColumnHeadersHeight = 36;
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "网址",
                Name = "Url",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "打开方式",
                Name = "Mode",
                Width = 110
            });
            grid.Columns.Add(new DataGridViewButtonColumn
            {
                HeaderText = string.Empty,
                Name = "Delete",
                Text = "删除",
                UseColumnTextForButtonValue = true,
                Width = 86,
                FlatStyle = FlatStyle.Flat
            });
            grid.CellContentClick += DeleteSiteCellClick;
            return grid;
        }

        private Panel BuildFooter()
        {
            Panel footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = Color.FromArgb(247, 247, 247)
            };
            _resultLabel = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(24, 7),
                Size = new Size(610, 36),
                Anchor = AnchorStyles.Left | AnchorStyles.Top |
                    AnchorStyles.Right,
                ForeColor = Color.FromArgb(70, 70, 70)
            };
            Button advanced = new Button
            {
                Text = "诊断与恢复",
                Size = new Size(112, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(770, 10),
                FlatStyle = FlatStyle.Flat
            };
            advanced.FlatAppearance.BorderColor = Color.FromArgb(175, 175, 175);
            advanced.Click += ShowDiagnostics;
            footer.Controls.Add(_resultLabel);
            footer.Controls.Add(advanced);
            footer.Resize += delegate
            {
                advanced.Left = footer.ClientSize.Width -
                    advanced.Width - 24;
                _resultLabel.Width = Math.Max(
                    200,
                    advanced.Left - _resultLabel.Left - 14);
            };
            return footer;
        }

        private Button CreatePrimaryButton(string text, EventHandler handler)
        {
            Button button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = _accent,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = _accent;
            button.Click += handler;
            return button;
        }

        private static Button CreateSecondaryButton(
            string text,
            EventHandler handler)
        {
            Button button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(175, 175, 175);
            button.Click += handler;
            return button;
        }

        private void AddressInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                AddAddress(sender, EventArgs.Empty);
            }
        }

        private void AddAddress(object sender, EventArgs e)
        {
            string input = _addressInput.Text.Trim();
            if (input.Length == 0)
            {
                SetResult("请输入需要使用 IE 模式的网址。", false);
                _addressInput.Focus();
                return;
            }

            RunUi(delegate
            {
                List<PreviewItem> preview = _service.PreviewAdd(
                    new[] { input },
                    "Default",
                    false);
                PreviewItem invalid = preview.FirstOrDefault(
                    delegate(PreviewItem item)
                    {
                        return item.Classification ==
                            PreviewClassification.Invalid;
                    });
                if (invalid != null)
                {
                    throw new InvalidOperationException(
                        "网址格式不正确：" + invalid.Raw + "\r\n" +
                        invalid.Error);
                }

                int newCount = preview.Count(
                    delegate(PreviewItem item)
                    {
                        return item.Classification ==
                            PreviewClassification.Ready;
                    });
                OperationResult result = _service.AddSites(
                    new[] { input },
                    "Default",
                    false);
                _addressInput.Clear();
                RefreshAll();
                SetResult(
                    newCount > 0
                        ? "添加完成。"
                        : "网址已经存在，已重新发布列表。",
                    true);
                OfferEdgeRestart(result);
            });
        }

        private void DeleteSiteCellClick(
            object sender,
            DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 ||
                e.ColumnIndex != _siteGrid.Columns["Delete"].Index)
            {
                return;
            }
            string url = Convert.ToString(
                _siteGrid.Rows[e.RowIndex].Cells["Url"].Value);
            RunUi(delegate
            {
                OperationResult result =
                    _service.RemoveSites(new[] { url });
                RefreshAll();
                SetResult(
                    string.IsNullOrWhiteSpace(result.Message)
                        ? "已删除 " + url + "。"
                        : result.Message,
                    true);
                OfferEdgeRestart(result);
            });
        }

        private void ShowBulkAdd(object sender, EventArgs e)
        {
            using (BulkAddForm dialog = new BulkAddForm(_service))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                RefreshAll();
                SetResult(
                    "批量添加完成，共添加 " +
                    dialog.AddedCount +
                    " 个网址。",
                    true);
                OfferEdgeRestart(dialog.AppliedResult);
            }
        }

        private void RefreshAll()
        {
            RunUi(delegate
            {
                EnvironmentStatus environment =
                    _service.GetEnvironmentStatus();
                _sites = _service.GetSites();
                PopulateSites();
                UpdateEnvironment(environment);
            }, false);
        }

        private void PopulateSites()
        {
            if (_siteGrid == null)
            {
                return;
            }
            string filter = _searchInput == null
                ? string.Empty
                : _searchInput.Text.Trim();
            IEnumerable<ManagedSite> visible = _sites;
            if (filter.Length > 0)
            {
                visible = visible.Where(
                    delegate(ManagedSite site)
                    {
                        return site.Url.IndexOf(
                            filter,
                            StringComparison.OrdinalIgnoreCase) >= 0;
                    });
            }

            _siteGrid.Rows.Clear();
            foreach (ManagedSite site in visible)
            {
                _siteGrid.Rows.Add(site.Url, "IE 模式", "删除");
            }
            _siteCountLabel.Text = "已添加的网址（" + _sites.Count + "）";
        }

        private void UpdateEnvironment(EnvironmentStatus environment)
        {
            bool available =
                environment.IsSupported && !environment.HasBlockingConflict;
            _addButton.Enabled = available;
            _bulkAddButton.Enabled = available;
            if (!environment.IsSupported)
            {
                _environmentLabel.Text = "当前环境不支持";
                _environmentLabel.ForeColor = _danger;
            }
            else if (environment.HasBlockingConflict)
            {
                _environmentLabel.Text = "检测到策略冲突";
                _environmentLabel.ForeColor = _danger;
                SetResult(
                    environment.Conflicts.Count > 0
                        ? environment.Conflicts[0]
                        : "检测到策略冲突，请打开诊断。",
                    false);
            }
            else
            {
                _environmentLabel.Text = "环境正常";
                _environmentLabel.ForeColor = _success;
            }
        }

        private void OfferEdgeRestart(OperationResult result)
        {
            if (result == null || !result.Changed ||
                !result.RequiresEdgeRestart)
            {
                return;
            }
            if (Process.GetProcessesByName("msedge").Length == 0)
            {
                SetResult(_resultLabel.Text + " 下次打开 Edge 后生效。", true);
                return;
            }

            DialogResult restart = MessageBox.Show(
                "操作已经完成。\r\n\r\n" +
                "要让更改生效，必须重启 Edge。" +
                "是否现在重启？\r\n\r\n" +
                "已经打开的 IE 模式标签页不会原地切换回 Chromium。" +
                "Edge 通常会恢复原有窗口和标签页，请先确认网页中的内容已经保存。",
                "让更改生效",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);
            if (restart == DialogResult.Yes)
            {
                EnvironmentStatus status = _service.GetEnvironmentStatus();
                Process.Start(new ProcessStartInfo
                {
                    FileName = status.EdgePath,
                    Arguments = "edge://restart",
                    UseShellExecute = false
                });
                SetResult(
                    "正在重新启动 Edge；重启后请用新标签页访问该网址。",
                    true);
            }
            else
            {
                SetResult("操作完成；请稍后手动关闭并重新打开 Edge。", true);
            }
        }

        private void ShowDiagnostics(object sender, EventArgs e)
        {
            using (DiagnosticsForm dialog = new DiagnosticsForm(_service))
            {
                dialog.ShowDialog(this);
            }
            RefreshAll();
        }

        private void SetResult(string text, bool success)
        {
            _resultLabel.Text = text;
            _resultLabel.ForeColor = success
                ? _success
                : _danger;
        }

        private void RunUi(Action action)
        {
            RunUi(action, true);
        }

        private void RunUi(Action action, bool showErrors)
        {
            Cursor previous = Cursor;
            try
            {
                Cursor = Cursors.WaitCursor;
                action();
            }
            catch (Exception error)
            {
                SetResult(error.Message, false);
                if (showErrors)
                {
                    MessageBox.Show(
                        error.Message,
                        "CompatBridge 操作失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                Cursor = previous;
            }
        }
    }
}
