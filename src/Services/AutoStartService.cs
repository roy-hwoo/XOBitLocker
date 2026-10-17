using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BitLockerVault.Services
{
    /// <summary>
    /// 开机自启 / 登录自动挂载 由「计划任务」实现。
    /// 使用计划任务而非 Run 注册表项，原因是程序需要管理员权限，
    /// 计划任务可以设置 RunLevel=Highest 从而在登录时静默提权运行，避免每次弹 UAC。
    /// </summary>
    internal static class AutoStartService
    {
        private const string TaskName = "XOBitLocker";

        public static string ExecutablePath
        {
            get
            {
                string path = System.Windows.Forms.Application.ExecutablePath;
                if (string.IsNullOrEmpty(path))
                    path = System.Reflection.Assembly.GetEntryAssembly().Location;
                return path;
            }
        }

        /// <summary>查询计划任务是否已存在。</summary>
        public static bool IsEnabled()
        {
            ExecResult r = RunSchtasks("/Query /TN \"" + TaskName + "\"");
            return r.Success;
        }

        /// <summary>创建（或覆盖）登录时触发的计划任务。</summary>
        public static void Enable()
        {
            string exe = ExecutablePath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                throw new InvalidOperationException("找不到程序文件，无法设置开机自启。");

            string args = "/Create /TN \"" + TaskName + "\" /TR \"\\\"" + exe + "\\\"\" " +
                          "/SC ONLOGON /RL HIGHEST /F";
            ExecResult r = RunSchtasks(args);
            if (!r.Success)
                throw new InvalidOperationException("设置开机自启失败：\r\n" + r.Combined);
            LogService.Info("已启用开机自启计划任务：" + exe);
        }

        /// <summary>删除计划任务。</summary>
        public static void Disable()
        {
            ExecResult r = RunSchtasks("/Delete /TN \"" + TaskName + "\" /F");
            if (!r.Success && IsEnabled())
                throw new InvalidOperationException("取消开机自启失败：\r\n" + r.Combined);
            LogService.Info("已取消开机自启计划任务。");
        }

        public static void SetEnabled(bool enabled)
        {
            if (enabled) Enable();
            else Disable();
        }

        private static ExecResult RunSchtasks(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default
            };

            var result = new ExecResult();
            try
            {
                using (var p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    result.Output = (stdout ?? "").Trim();
                    result.Error = (stderr ?? "").Trim();
                    result.ExitCode = p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.Error = ex.Message;
            }
            return result;
        }
    }
}
