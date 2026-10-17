using System;
using System.IO;
using System.Text;

namespace BitLockerVault.Services
{
    /// <summary>VHDX 虚拟硬盘的创建、挂载与卸载。</summary>
    internal static class VhdService
    {
        /// <summary>
        /// 创建动态扩展 VHDX，并完成分区、格式化与盘符分配。
        /// </summary>
        /// <param name="vhdPath">VHDX 文件完整路径</param>
        /// <param name="sizeBytes">最大容量（字节）</param>
        /// <param name="driveLetter">分配的盘符，例如 "Z:"</param>
        public static void CreateDynamicVhd(string vhdPath, long sizeBytes, string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(vhdPath)) throw new ArgumentException("VHDX 路径不能为空。");
            if (!vhdPath.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("虚拟硬盘文件必须以 .vhdx 结尾。");

            long sizeMb = sizeBytes / (1024L * 1024L);
            if (sizeMb < 100) sizeMb = 100;

            string dir = Path.GetDirectoryName(vhdPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(vhdPath)) throw new IOException("目标文件已存在：" + vhdPath);

            string letter = DriveHelper.Normalize(driveLetter);
            if (letter.Length < 2) throw new ArgumentException("未指定有效的盘符。");

            var sb = new StringBuilder();
            sb.AppendLine("create vdisk file=\"" + vhdPath + "\" maximum=" + sizeMb + " type=expandable");
            sb.AppendLine("select vdisk file=\"" + vhdPath + "\"");
            sb.AppendLine("attach vdisk");
            sb.AppendLine("create partition primary");
            sb.AppendLine("format fs=ntfs quick");
            sb.AppendLine("assign letter=" + letter.Substring(0, 1));
            sb.AppendLine("exit");

            ExecResult result = DiskPart.RunScript(sb.ToString());
            if (!result.Success)
            {
                throw new InvalidOperationException("创建虚拟硬盘失败：\r\n" + result.Combined);
            }

            // 卷标使用保存文件名的名称
            SetVolumeLabel(letter, MakeVolumeLabel(vhdPath));
        }

        /// <summary>由 VHDX 文件路径派生卷标名称（保存文件名，不含扩展名）。</summary>
        private static string MakeVolumeLabel(string vhdPath)
        {
            string label = Path.GetFileNameWithoutExtension(vhdPath) ?? "";
            label = label.Trim();
            // NTFS 卷标最多 32 个字符
            if (label.Length > 32) label = label.Substring(0, 32);
            return label;
        }

        /// <summary>设置已挂载卷的卷标。</summary>
        private static void SetVolumeLabel(string driveLetter, string label)
        {
            if (string.IsNullOrEmpty(label)) return;

            string letter = DriveHelper.Normalize(driveLetter);
            if (letter.Length < 1) return;

            string script =
                "$ErrorActionPreference = 'Stop'; " +
                "Set-Volume -DriveLetter " + PowerShellRunner.Quote(letter.Substring(0, 1)) +
                " -NewFileSystemLabel " + PowerShellRunner.Quote(label) + " | Out-Null; " +
                "Write-Output 'OK'";

            ExecResult result = PowerShellRunner.Run(script, 60000);
            if (!result.Success)
            {
                throw new InvalidOperationException("设置卷标失败：\r\n" + result.Combined);
            }
        }

        /// <summary>挂载已存在的 VHDX 并返回其盘符（可能为空字符串）。</summary>
        public static string MountVhd(string vhdPath, string preferredDrive)
        {
            string pref = DriveHelper.Normalize(preferredDrive);
            string prefLetter = pref.Length >= 1 ? pref.Substring(0, 1) : "";

            string script =
                "$ErrorActionPreference = 'Stop'; " +
                "$path = " + PowerShellRunner.Quote(vhdPath) + "; " +
                "$pref = " + PowerShellRunner.Quote(prefLetter) + "; " +
                "if (-not (Test-Path -LiteralPath $path)) { throw '虚拟硬盘文件不存在：' + $path }; " +
                "$img = Get-DiskImage -ImagePath $path; " +
                "if (-not $img.Attached) { $img = Mount-DiskImage -ImagePath $path -PassThru }; " +
                "$letter = ''; " +
                "for ($i = 0; $i -lt 12 -and -not $letter; $i++) { " +
                "  Start-Sleep -Milliseconds 500; " +
                "  $part = ($img | Get-Disk | Get-Partition | Where-Object { $_.DriveLetter } | Select-Object -First 1); " +
                "  if ($part) { $letter = [string]$part.DriveLetter } " +
                "} " +
                "if (-not $letter -and $pref) { " +
                "  $disk = $img | Get-Disk; " +
                "  $part = $disk | Get-Partition | Select-Object -First 1; " +
                "  if ($part) { Add-PartitionAccessPath -DiskNumber $disk.Number -PartitionNumber $part.PartitionNumber -AccessPath ($pref + ':') -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500; $letter = $pref } " +
                "} " +
                "Write-Output ('LETTER=' + $letter)";

            ExecResult result = PowerShellRunner.Run(script, 60000);
            if (!result.Success)
            {
                throw new InvalidOperationException("挂载虚拟硬盘失败：\r\n" + result.Combined);
            }
            return ParseValue(result.Output, "LETTER=");
        }

        /// <summary>卸载 VHDX。已锁定 BitLocker 卷时 force 建议为 true。</summary>
        public static void DismountVhd(string vhdPath)
        {
            string script =
                "$path = " + PowerShellRunner.Quote(vhdPath) + "; " +
                "if (-not (Test-Path -LiteralPath $path)) { Write-Output 'OK'; return }; " +
                "$img = Get-DiskImage -ImagePath $path; " +
                "if ($img -and $img.Attached) { Dismount-DiskImage -ImagePath $path -ErrorAction SilentlyContinue }; " +
                "Write-Output 'OK'";

            ExecResult result = PowerShellRunner.Run(script, 60000);
            if (!result.Success)
            {
                throw new InvalidOperationException("卸载虚拟硬盘失败：\r\n" + result.Combined);
            }
        }

        /// <summary>修改已挂载卷的盘符。</summary>
        public static void ChangeDriveLetter(string oldLetter, string newLetter)
        {
            string oldL = DriveHelper.Normalize(oldLetter).Substring(0, 1);
            string newL = DriveHelper.Normalize(newLetter).Substring(0, 1);

            string script =
                "$old = " + PowerShellRunner.Quote(oldL) + "; " +
                "$new = " + PowerShellRunner.Quote(newL) + "; " +
                "$p = Get-Partition -DriveLetter $old; " +
                "Set-Partition -DiskNumber $p.DiskNumber -PartitionNumber $p.PartitionNumber -NewDriveLetter $new; " +
                "Write-Output 'OK'";

            ExecResult result = PowerShellRunner.Run(script, 60000);
            if (!result.Success)
            {
                throw new InvalidOperationException("修改盘符失败：\r\n" + result.Combined);
            }
        }

        /// <summary>判断 VHDX 当前是否已挂载。</summary>
        public static bool IsAttached(string vhdPath)
        {
            string script =
                "$path = " + PowerShellRunner.Quote(vhdPath) + "; " +
                "if (-not (Test-Path -LiteralPath $path)) { Write-Output 'ATTACHED=False'; return }; " +
                "$img = Get-DiskImage -ImagePath $path -ErrorAction SilentlyContinue; " +
                "Write-Output ('ATTACHED=' + [bool]($img -and $img.Attached))";

            ExecResult result = PowerShellRunner.Run(script, 30000);
            return string.Equals(ParseValue(result.Output, "ATTACHED="), "True", StringComparison.OrdinalIgnoreCase);
        }

        private static string ParseValue(string output, string prefix)
        {
            if (string.IsNullOrEmpty(output)) return "";
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = line.Trim();
                if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return t.Substring(prefix.Length).Trim();
            }
            return "";
        }
    }

    /// <summary>diskpart 脚本执行器。</summary>
    internal static class DiskPart
    {
        public static ExecResult RunScript(string scriptContent)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "blv-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(tempFile, scriptContent, Encoding.Default);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "diskpart.exe",
                    Arguments = "/s \"" + tempFile + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.Default,
                    StandardErrorEncoding = Encoding.Default
                };

                var result = new ExecResult();
                using (var p = new System.Diagnostics.Process())
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

                // diskpart 有时即使失败也返回 0，需要检查输出关键字
                string combined = result.Output + "\n" + result.Error;
                if (combined.IndexOf("Virtual Disk Service error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    combined.IndexOf("DiskPart has encountered an error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    combined.IndexOf("The disk you specified is not valid", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.ExitCode = 1;
                }

                LogService.Info("diskpart 执行完成，ExitCode=" + result.ExitCode + "\r\n" + combined);
                return result;
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }
    }
}
