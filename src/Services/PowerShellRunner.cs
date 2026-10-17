using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace BitLockerVault.Services
{
    /// <summary>PowerShell 命令执行结果。</summary>
    internal sealed class ExecResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }

        public bool Success { get { return ExitCode == 0; } }

        public ExecResult()
        {
            Output = "";
            Error = "";
        }

        /// <summary>合并输出，便于向用户展示错误信息。</summary>
        public string Combined
        {
            get
            {
                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(Output)) sb.Append(Output.Trim());
                if (!string.IsNullOrEmpty(Error))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(Error.Trim());
                }
                return sb.ToString();
            }
        }
    }

    /// <summary>以管理员身份调用 PowerShell 脚本。</summary>
    internal static class PowerShellRunner
    {
        /// <summary>脚本内部用于标记异常消息的前缀。</summary>
        private const string ErrorMarker = "!!ERROR!! ";

        public static ExecResult Run(string script)
        {
            return Run(script, 0);
        }

        /// <summary>执行 PowerShell 脚本，timeoutMs <= 0 表示不限时。</summary>
        public static ExecResult Run(string script, int timeoutMs)
        {
            // 统一以 UTF-8 输出，避免中文乱码
            const string prelude =
                "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
                "$OutputEncoding = [System.Text.Encoding]::UTF8; " +
                "$ProgressPreference = 'SilentlyContinue'; " +
                "$ErrorActionPreference = 'Stop'; ";

            // 用 try/catch 把异常信息写入标准输出，避免 PowerShell 把错误序列化成 CLIXML
            string full = prelude +
                "try { " + script + " } catch { Write-Output (" + Quote(ErrorMarker) +
                " + $_.Exception.Message); exit 1 }";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var result = new ExecResult();
            var outSb = new StringBuilder();
            var errSb = new StringBuilder();

            try
            {
                using (var p = new Process())
                {
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) outSb.AppendLine(e.Data);
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) errSb.AppendLine(e.Data);
                    };

                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (timeoutMs > 0)
                    {
                        if (!p.WaitForExit(timeoutMs))
                        {
                            try { p.Kill(); } catch { }
                            result.ExitCode = -1;
                            result.Error = "命令执行超时。";
                            return result;
                        }
                    }
                    else
                    {
                        p.WaitForExit();
                    }

                    p.WaitForExit(); // 确保异步输出读取完成
                    result.ExitCode = p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                LogService.Error("PowerShell 执行失败", ex);
                result.ExitCode = -1;
                result.Error = ex.Message;
                return result;
            }

            result.Output = outSb.ToString().Trim();
            result.Error = DecodeClixml(errSb.ToString().Trim());
            // 失败时异常消息由脚本写入标准输出，剥离内部标记后作为错误信息展示
            int markIdx = result.Output.IndexOf(ErrorMarker, StringComparison.Ordinal);
            if (markIdx >= 0)
            {
                result.Error = result.Output.Substring(markIdx + ErrorMarker.Length).Trim();
                result.Output = result.Output.Substring(0, markIdx).Trim();
            }
            LogService.Info("PowerShell 执行完成，ExitCode=" + result.ExitCode);
            if (!result.Success)
            {
                LogService.Warn("PowerShell 输出：" + result.Combined);
            }
            return result;
        }

        /// <summary>把 PowerShell 写入 stderr 的 CLIXML 文本还原成可读的纯文本。</summary>
        private static string DecodeClixml(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("<Objs", StringComparison.Ordinal) < 0)
                return text;

            var sb = new StringBuilder();
            foreach (Match m in Regex.Matches(text, "<S S=\"[^\"]*\">(.*?)</S>", RegexOptions.Singleline))
                sb.AppendLine(m.Groups[1].Value);

            if (sb.Length == 0)
                sb.Append(Regex.Replace(text, "<[^>]+>", ""));

            return sb.ToString()
                     .Replace("_x000D_", "")
                     .Replace("_x000A_", "\r\n")
                     .Replace("#< CLIXML", "")
                     .Trim();
        }

        /// <summary>把字符串包装成 PowerShell 单引号字面量（已转义单引号）。</summary>
        public static string Quote(string value)
        {
            return "'" + (value ?? "").Replace("'", "''") + "'";
        }

        /// <summary>构造将明文密码转换为 SecureString 的脚本片段（空密码返回空 SecureString）。</summary>
        public static string SecureStringLiteral(string password)
        {
            // ConvertTo-SecureString 不接受空字符串（会报「参数 String 为空字符串」），故单独处理
            if (string.IsNullOrEmpty(password))
                return "(New-Object System.Security.SecureString)";
            return "ConvertTo-SecureString -String " + Quote(password) + " -AsPlainText -Force";
        }
    }
}
