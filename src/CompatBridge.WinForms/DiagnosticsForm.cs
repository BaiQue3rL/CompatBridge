using CompatBridge.Core;
using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace CompatBridge
{
    internal sealed class DiagnosticsForm : Form
    {
        private readonly TransactionService _service;
        private readonly TextBox _details;

        public DiagnosticsForm(TransactionService service)
        {
            _service = service;
            Text = "CompatBridge 诊断与恢复";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(760, 540);
            MinimumSize = new Size(650, 450);
            Font = new Font("Microsoft YaHei UI", 9F);
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(10, 9, 0, 0)
            };
            actions.Controls.Add(CreateButton("刷新", RefreshDetails));
            actions.Controls.Add(CreateButton("撤销上次变更", Undo));
            actions.Controls.Add(CreateButton("恢复初始状态", RestoreBaseline));

            _details = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Color.White,
                Font = new Font("Consolas", 9.5F)
            };
            Controls.Add(_details);
            Controls.Add(actions);
            Shown += delegate { RefreshDetails(null, EventArgs.Empty); };
        }

        private static Button CreateButton(
            string text,
            EventHandler handler)
        {
            Button button = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 8, 0)
            };
            button.Click += handler;
            return button;
        }

        private void RefreshDetails(object sender, EventArgs e)
        {
            try
            {
                EnvironmentStatus status = _service.GetEnvironmentStatus();
                StringBuilder text = new StringBuilder();
                text.AppendLine("CompatBridge " + AppInfo.Version);
                text.AppendLine(new string('=', 66));
                text.AppendLine("Windows：      " + status.WindowsVersion);
                text.AppendLine("管理员权限：   " +
                    (status.IsAdministrator ? "是" : "否"));
                text.AppendLine("Edge：         " +
                    (status.EdgeInstalled ? status.EdgeVersion : "未检测到"));
                text.AppendLine("当前站点数：   " + _service.GetSites().Count);
                text.AppendLine("列表版本：     " + status.ManagedVersion);
                text.AppendLine("策略冲突：     " +
                    (status.HasBlockingConflict ? "有" : "无"));
                if (status.SupportIssues.Count > 0)
                {
                    text.AppendLine();
                    text.AppendLine("环境问题：");
                    foreach (string issue in status.SupportIssues)
                    {
                        text.AppendLine("  - " + issue);
                    }
                }
                if (status.Conflicts.Count > 0)
                {
                    text.AppendLine();
                    text.AppendLine("冲突：");
                    foreach (string conflict in status.Conflicts)
                    {
                        text.AppendLine("  - " + conflict);
                    }
                }
                text.AppendLine();
                text.AppendLine("数据目录：");
                text.AppendLine("  " + _service.Paths.Root);
                text.AppendLine();
                text.AppendLine("每次添加、删除和撤销都会使用更高的列表版本，");
                text.AppendLine("并在写入前自动保存注册表、XML 和状态快照。");
                _details.Text = text.ToString();
            }
            catch (Exception error)
            {
                _details.Text = error.ToString();
            }
        }

        private void Undo(object sender, EventArgs e)
        {
            if (MessageBox.Show(
                    "确定撤销上一次站点变更吗？",
                    "撤销上次变更",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            RunOperation(delegate { return _service.UndoLastChange(); });
        }

        private void RestoreBaseline(object sender, EventArgs e)
        {
            if (MessageBox.Show(
                    "确定恢复 CompatBridge 首次运行前的策略状态吗？\r\n" +
                    "备份和日志会继续保留。",
                    "恢复初始状态",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            RunOperation(delegate { return _service.RestoreBaseline(); });
        }

        private void RunOperation(Func<OperationResult> operation)
        {
            try
            {
                OperationResult result = operation();
                RefreshDetails(null, EventArgs.Empty);
                MessageBox.Show(
                    result.Message ?? "操作完成。请重新启动 Edge 使更改生效。",
                    "CompatBridge",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    error.Message,
                    "操作失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

    }
}
