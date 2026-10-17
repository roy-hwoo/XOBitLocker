using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BitLockerVault.Services
{
    /// <summary>简易日志：写入 %AppData%\XOBitLocker\logs\app-yyyyMMdd.log。</summary>
    internal static class LogService
    {
        private static readonly object Gate = new object();

        private static string LogDirectory
        {
            get { return Path.Combine(ConfigService.ConfigDirectory, "logs"); }
        }

        public static void Info(string message)
        {
            Write("INFO ", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN ", message, null);
        }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", message, ex);
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDirectory);
                    string file = Path.Combine(LogDirectory, "app-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    var sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                      .Append(" [").Append(level).Append("] ")
                      .Append(message);
                    if (ex != null)
                    {
                        sb.AppendLine();
                        sb.Append(ex);
                    }
                    sb.AppendLine();
                    File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
                    Debug.Write(sb.ToString());
                }
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }
    }
}
