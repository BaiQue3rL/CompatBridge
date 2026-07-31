using System;
using System.Threading;
using System.Windows.Forms;

namespace CompatBridge
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null &&
                Array.Exists(
                    args,
                    delegate(string value)
                    {
                        return string.Equals(
                            value,
                            "--smoke-test",
                            StringComparison.OrdinalIgnoreCase);
                    }))
            {
                RunSmokeTest();
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Global\CompatBridge-876b4d2b-76b9-4be2-98c7-1a2096becc78", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "CompatBridge 已经在运行。",
                        "兼容桥",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }

        private static void RunSmokeTest()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (MainForm form = new MainForm())
                {
                    form.CreateControl();
                }
                using (BulkAddForm form = new BulkAddForm(
                    new CompatBridge.Core.TransactionService()))
                {
                    form.CreateControl();
                }
                Environment.ExitCode = 0;
            }
            catch
            {
                Environment.ExitCode = 1;
            }
        }
    }
}
