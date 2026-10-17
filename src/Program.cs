using System;
using System.Threading;
using System.Windows.Forms;
using BitLockerVault.Services;

namespace BitLockerVault
{
    internal static class Program
    {
        private const string MutexName = "Global\\BitLockerVault.SingleInstance";

        private static Mutex _mutex;

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "XOBitLocker 程序已经在运行中。\r\n请在系统托盘区查看图标。",
                    "XOBitLocker",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Application.Run(new TrayApplicationContext());
            }
            catch (Exception ex)
            {
                LogService.Error("未处理的异常", ex);
                MessageBox.Show(ex.ToString(), "程序异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                GC.KeepAlive(_mutex);
            }
        }
    }
}
