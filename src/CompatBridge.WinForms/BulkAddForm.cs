using CompatBridge.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CompatBridge
{
    internal sealed class BulkAddForm : Form
    {
        private readonly TransactionService _service;
        private readonly Color _accent = Color.FromArgb(0, 103, 184);
        private readonly Color _success = Color.FromArgb(16, 124, 16);
        private readonly Color _danger = Color.FromArgb(196, 43, 28);
        private readonly TextBox _input;
        private readonly DataGridView _previewGrid;
        private readonly Label _summary;
        private readonly Button _previewButton;
        private readonly Button _applyButton;
        private List<PreviewItem> _preview = new List<PreviewItem>();
        private string _previewSource = string.Empty;

        public BulkAddForm(TransactionService service)
        {
            _service = service;
            Text = "CompatBridge 批量添加";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(900, 650);
            MinimumSize = new Size(720, 540);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.White;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            Panel inputPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 210,
                Padding = new Padding(20, 16, 20, 12),
                BackColor = Color.FromArgb(250, 252, 255)
            };
            Label title = new Label
            {
                AutoSize = true,
                Text = "粘贴需要使用 IE 模式的网址",
                Font = new Font(Font, FontStyle.Bold),
                Location = new Point(20, 16)
            };
            Label hint = new Label
            {
                AutoSize = true,
                Text = "支持多行、从 Excel 复制一列，或导入 TXT/CSV 文件。",
                ForeColor = Color.FromArgb(95, 95, 95),
                Location = new Point(20, 43)
            };
            _input = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                AcceptsTab = false,
                Location = new Point(20, 70),
                Size = new Size(840, 82),
                Anchor = AnchorStyles.Top | AnchorStyles.Left |
                    AnchorStyles.Right
            };
            _input.TextChanged += InputChanged;

            Button importButton = CreateSecondaryButton(
                "导入 TXT/CSV",
                ImportFile);
            importButton.Location = new Point(20, 164);
            importButton.Size = new Size(120, 32);
            _previewButton = CreateSecondaryButton("预览", PreviewClicked);
            _previewButton.Location = new Point(150, 164);
            _previewButton.Size = new Size(88, 32);
            inputPanel.Controls.Add(title);
            inputPanel.Controls.Add(hint);
            inputPanel.Controls.Add(_input);
            inputPanel.Controls.Add(importButton);
            inputPanel.Controls.Add(_previewButton);

            _previewGrid = BuildPreviewGrid();

            Panel footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 64,
                Padding = new Padding(20, 12, 20, 12),
                BackColor = Color.FromArgb(247, 247, 247)
            };
            _summary = new Label
            {
                AutoSize = false,
                Text = "请粘贴或导入网址。",
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(20, 12),
                Size = new Size(550, 38),
                Anchor = AnchorStyles.Left | AnchorStyles.Top |
                    AnchorStyles.Right
            };
            Button cancelButton = CreateSecondaryButton(
                "取消",
                delegate { Close(); });
            cancelButton.Location = new Point(674, 15);
            cancelButton.Size = new Size(82, 34);
            cancelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _applyButton = CreatePrimaryButton(
                "添加有效网址",
                ApplyClicked);
            _applyButton.Location = new Point(766, 15);
            _applyButton.Size = new Size(114, 34);
            _applyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _applyButton.Enabled = false;
            footer.Controls.Add(_summary);
            footer.Controls.Add(cancelButton);
            footer.Controls.Add(_applyButton);
            footer.Resize += delegate
            {
                _applyButton.Left =
                    footer.ClientSize.Width - _applyButton.Width - 20;
                cancelButton.Left =
                    _applyButton.Left - cancelButton.Width - 10;
                _summary.Width = Math.Max(
                    220,
                    cancelButton.Left - _summary.Left - 14);
            };

            Controls.Add(_previewGrid);
            Controls.Add(footer);
            Controls.Add(inputPanel);
            Shown += delegate { _input.Focus(); };
        }

        public OperationResult AppliedResult { get; private set; }

        public int AddedCount { get; private set; }

        private DataGridView BuildPreviewGrid()
        {
            DataGridView grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
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
                RowTemplate = { Height = 34 }
            };
            grid.ColumnHeadersHeight = 36;
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "原始输入",
                Name = "Raw",
                FillWeight = 32,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "规范化网址",
                Name = "Url",
                FillWeight = 32,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "结果",
                Name = "Status",
                Width = 90
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "说明",
                Name = "Details",
                FillWeight = 36,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
            return grid;
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

        private void InputChanged(object sender, EventArgs e)
        {
            _previewSource = string.Empty;
            _applyButton.Enabled = _input.Text.Trim().Length > 0;
            if (_input.Text.Trim().Length == 0)
            {
                _preview.Clear();
                _previewGrid.Rows.Clear();
                _summary.Text = "请粘贴或导入网址。";
                _summary.ForeColor = Color.FromArgb(70, 70, 70);
            }
            else
            {
                _summary.Text = "内容已更改，点击“预览”查看分类；也可直接添加有效网址。";
                _summary.ForeColor = Color.FromArgb(70, 70, 70);
            }
        }

        private void ImportFile(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "导入网址";
                dialog.Filter =
                    "网址文件 (*.txt;*.csv)|*.txt;*.csv|" +
                    "文本文件 (*.txt)|*.txt|CSV 文件 (*.csv)|*.csv";
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    List<string> entries =
                        SiteInputFileReader.ReadEntries(dialog.FileName);
                    if (entries.Count == 0)
                    {
                        MessageBox.Show(
                            "文件中没有找到可预览的网址。",
                            "导入网址",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }
                    string imported = string.Join(
                        Environment.NewLine,
                        entries.ToArray());
                    if (_input.Text.Trim().Length > 0)
                    {
                        _input.AppendText(Environment.NewLine);
                    }
                    _input.AppendText(imported);
                    PreviewInput();
                }
                catch (Exception error)
                {
                    MessageBox.Show(
                        error.Message,
                        "导入失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void PreviewClicked(object sender, EventArgs e)
        {
            RunUi(PreviewInput);
        }

        private void PreviewInput()
        {
            string source = _input.Text;
            if (source.Trim().Length == 0)
            {
                _preview.Clear();
                _previewGrid.Rows.Clear();
                _applyButton.Enabled = false;
                _summary.Text = "请先粘贴或导入网址。";
                _summary.ForeColor = _danger;
                return;
            }

            _preview = _service.PreviewAdd(
                new[] { source },
                "Default",
                false);
            _previewSource = source;
            PopulatePreview();
        }

        private void PopulatePreview()
        {
            _previewGrid.Rows.Clear();
            foreach (PreviewItem item in _preview)
            {
                string details = item.Error;
                if (string.IsNullOrWhiteSpace(details))
                {
                    details = item.WarningText;
                }
                int rowIndex = _previewGrid.Rows.Add(
                    item.Raw,
                    item.Url ?? string.Empty,
                    item.ClassificationText,
                    details ?? string.Empty);
                DataGridViewRow row = _previewGrid.Rows[rowIndex];
                if (item.Classification == PreviewClassification.Ready)
                {
                    row.DefaultCellStyle.ForeColor = _success;
                }
                else if (item.Classification == PreviewClassification.Invalid ||
                    item.Classification == PreviewClassification.ConflictSettings)
                {
                    row.DefaultCellStyle.ForeColor = _danger;
                }
            }

            int ready = Count(PreviewClassification.Ready);
            int existing = Count(PreviewClassification.AlreadyExists);
            int duplicate = Count(PreviewClassification.DuplicateInput);
            int invalid = Count(PreviewClassification.Invalid);
            int conflict = Count(PreviewClassification.ConflictSettings);
            _summary.Text = string.Format(
                "可添加 {0}；已存在 {1}；重复 {2}；格式错误 {3}；冲突 {4}。",
                ready,
                existing,
                duplicate,
                invalid,
                conflict);
            _summary.ForeColor = ready > 0 ? _success : _danger;
            _applyButton.Enabled = ready > 0;
        }

        private int Count(PreviewClassification classification)
        {
            return _preview.Count(
                delegate(PreviewItem item)
                {
                    return item.Classification == classification;
                });
        }

        private void ApplyClicked(object sender, EventArgs e)
        {
            RunUi(delegate
            {
                if (!string.Equals(
                    _previewSource,
                    _input.Text,
                    StringComparison.Ordinal))
                {
                    PreviewInput();
                }
                string[] ready = _preview.Where(
                    delegate(PreviewItem item)
                    {
                        return item.Classification ==
                            PreviewClassification.Ready;
                    }).Select(
                    delegate(PreviewItem item)
                    {
                        return item.Url;
                    }).ToArray();
                if (ready.Length == 0)
                {
                    MessageBox.Show(
                        "没有可添加的新网址。请根据预览结果修改输入。",
                        "批量添加",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                AppliedResult = _service.AddSites(
                    ready,
                    "Default",
                    false);
                AddedCount = ready.Length;
                DialogResult = DialogResult.OK;
                Close();
            });
        }

        private void RunUi(Action action)
        {
            Cursor previous = Cursor;
            try
            {
                Cursor = Cursors.WaitCursor;
                action();
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    error.Message,
                    "CompatBridge 操作失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = previous;
            }
        }
    }
}
