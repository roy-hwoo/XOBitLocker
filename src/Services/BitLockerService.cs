using System;
using BitLockerVault.Models;

namespace BitLockerVault.Services
{
    /// <summary>BitLocker 加密、解锁、锁定与状态查询。</summary>
    internal static class BitLockerService
    {
        /// <summary>启用 BitLocker 加密（仅加密已用空间）。</summary>
        public static void Enable(string mountPoint, string password)
        {
            string mp = PowerShellRunner.Quote(mountPoint);
            string script =
                "$mp = " + mp + "; " +
                "$pw = " + PowerShellRunner.SecureStringLiteral(password) + "; " +
                "Enable-BitLocker -MountPoint $mp -PasswordProtector -Password $pw -EncryptionMethod XtsAes256 -UsedSpaceOnly -SkipHardwareTest | Out-Null; " +
                "Write-Output 'OK'";

            ExecResult r = PowerShellRunner.Run(script, 300000);
            if (!r.Success)
                throw new InvalidOperationException("启用 BitLocker 加密失败：\r\n" + r.Combined);
        }

        /// <summary>使用密码解锁卷。</summary>
        public static void Unlock(string mountPoint, string password)
        {
            string script =
                "$mp = " + PowerShellRunner.Quote(mountPoint) + "; " +
                "$pw = " + PowerShellRunner.SecureStringLiteral(password) + "; " +
                "Unlock-BitLocker -MountPoint $mp -Password $pw | Out-Null; " +
                "Write-Output 'OK'";

            ExecResult r = PowerShellRunner.Run(script, 180000);
            if (!r.Success)
                throw new InvalidOperationException("解锁失败（密码可能不正确）：\r\n" + r.Combined);
        }

        /// <summary>锁定卷。forceDismount 为 true 时强制卸载占用中的卷。</summary>
        public static void Lock(string mountPoint, bool forceDismount)
        {
            string script =
                "$mp = " + PowerShellRunner.Quote(mountPoint) + "; " +
                (forceDismount
                    ? "Lock-BitLocker -MountPoint $mp -ForceDismount | Out-Null; "
                    : "Lock-BitLocker -MountPoint $mp | Out-Null; ") +
                "Write-Output 'OK'";

            ExecResult r = PowerShellRunner.Run(script, 180000);
            if (!r.Success)
                throw new InvalidOperationException("锁定失败" + (forceDismount ? "（可能有文件正在被占用）" : "") + "：\r\n" + r.Combined);
        }

        /// <summary>查询卷的 BitLocker 状态。</summary>
        public static VolumeStatus GetStatus(string mountPoint)
        {
            string script =
                "$mp = " + PowerShellRunner.Quote(mountPoint) + "; " +
                "$v = Get-BitLockerVolume -MountPoint $mp -ErrorAction SilentlyContinue; " +
                "if ($null -eq $v) { Write-Output 'NONE' } else { " +
                "  Write-Output ('PROT=' + $v.ProtectionStatus); " +
                "  Write-Output ('LOCK=' + $v.LockStatus); " +
                "  Write-Output ('VOL=' + $v.VolumeStatus); " +
                "  Write-Output ('PCT=' + $v.EncryptionPercentage) " +
                "}";

            ExecResult r = PowerShellRunner.Run(script, 30000);
            if (!r.Success || r.Output.IndexOf("NONE", StringComparison.OrdinalIgnoreCase) >= 0)
                return null;

            var st = new VolumeStatus { MountPoint = mountPoint };
            foreach (string line in r.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = line.Trim();
                if (t.StartsWith("PROT=")) st.ProtectionStatus = t.Substring(5).Trim();
                else if (t.StartsWith("LOCK=")) st.LockStatus = t.Substring(5).Trim();
                else if (t.StartsWith("VOL=")) st.VolumeStateText = t.Substring(4).Trim();
                else if (t.StartsWith("PCT="))
                {
                    int pct;
                    int.TryParse(t.Substring(4).Trim(), out pct);
                    st.EncryptionPercentage = pct;
                }
            }
            return st;
        }

        /// <summary>根据卷状态推导出加密盘运行状态。</summary>
        public static VaultState ResolveState(bool attached, VolumeStatus status)
        {
            if (!attached) return VaultState.NotMounted;
            if (status == null) return VaultState.Off;

            string vol = status.VolumeStateText ?? "";
            if (vol.IndexOf("EncryptionInProgress", StringComparison.OrdinalIgnoreCase) >= 0)
                return VaultState.Encrypting;
            if (vol.IndexOf("DecryptionInProgress", StringComparison.OrdinalIgnoreCase) >= 0)
                return VaultState.Decrypting;

            if (string.Equals(status.ProtectionStatus, "Off", StringComparison.OrdinalIgnoreCase))
                return VaultState.Off;
            if (string.Equals(status.ProtectionStatus, "Suspended", StringComparison.OrdinalIgnoreCase))
                return VaultState.Suspended;

            if (string.Equals(status.LockStatus, "Locked", StringComparison.OrdinalIgnoreCase))
                return VaultState.Locked;
            if (string.Equals(status.LockStatus, "Unlocked", StringComparison.OrdinalIgnoreCase))
                return VaultState.Unlocked;

            return VaultState.Unknown;
        }

        /// <summary>
        /// 修改密码。BitLocker 同一类型的密码保护器只允许存在一个，且删除最后一个保护器会
        /// 关闭整个卷的保护（状态变为 Off / 未加密）。
        /// 保护已开启时：先加临时恢复密码保护器顶住保护 → 删除旧密码保护器 → 添加新密码保护器
        /// → 确认保护已开启 → 删除临时恢复密码保护器；添加失败时用旧密码回滚。
        /// 保护已关闭（未加密）时：卷已加密的则添加密码保护器后用 manage-bde 重新开启保护，
        /// 从未加密的则直接启用 BitLocker 加密。
        /// </summary>
        public static void ChangePassword(string mountPoint, string newPassword, string oldPassword)
        {
            string script =
                "$mp = " + PowerShellRunner.Quote(mountPoint) + "; " +
                // manage-bde 只接受带冒号的盘符（如 "R:"），传 "R" 会报 Invalid Syntax 且被静默忽略
                "$vol = $mp; if ($vol -notmatch ':$') { $vol = $vol + ':' }; " +
                "$new = " + PowerShellRunner.SecureStringLiteral(newPassword) + "; " +
                "$old = " + PowerShellRunner.SecureStringLiteral(oldPassword ?? "") + "; " +
                "$v0 = Get-BitLockerVolume -MountPoint $mp; " +
                "$pwdIds = @($v0.KeyProtector | Where-Object { $_.KeyProtectorType -eq 'Password' } | ForEach-Object { [string]$_.KeyProtectorId }); " +
                "$protected = $v0.ProtectionStatus -ne 'Off'; " +
                // 1) 保护已开启且存在密码保护器时，先加一个临时恢复密码保护器顶住保护状态，
                //    否则删除最后一个密码保护器会把保护整体关掉
                "$tmpId = $null; " +
                "if ($protected -and $pwdIds.Count -gt 0) { try { $tmpId = [string](Add-BitLockerKeyProtector -MountPoint $mp -RecoveryPasswordProtector).KeyProtectorId } catch { } }; " +
                // 2) 删除现有密码保护器
                "foreach ($id in $pwdIds) { Remove-BitLockerKeyProtector -MountPoint $mp -KeyProtectorId $id | Out-Null }; " +
                "if (-not $protected) { " +
                // 3a) 保护已关闭（未加密）：先清掉残留的恢复密码保护器（否则以后锁定会要求输入 48 位恢复密钥）
                "  $rpIds = @((Get-BitLockerVolume -MountPoint $mp).KeyProtector | Where-Object { $_.KeyProtectorType -eq 'RecoveryPassword' } | ForEach-Object { [string]$_.KeyProtectorId }); " +
                "  foreach ($id in $rpIds) { Remove-BitLockerKeyProtector -MountPoint $mp -KeyProtectorId $id | Out-Null }; " +
                "  $enc = (Get-BitLockerVolume -MountPoint $mp).EncryptionStatus; " +
                "  if ($enc -eq 'FullyDecrypted') { " +
                // 从未加密：直接启用 BitLocker 加密（仅加密已用空间），与创建加密盘时同一条路径
                "    Enable-BitLocker -MountPoint $mp -PasswordProtector -Password $new -EncryptionMethod XtsAes256 -UsedSpaceOnly -SkipHardwareTest | Out-Null " +
                "  } else { " +
                // 卷已加密、只是保护被关闭：添加密码保护器后重新开启保护
                "    Add-BitLockerKeyProtector -MountPoint $mp -PasswordProtector -Password $new | Out-Null; " +
                "    manage-bde -protectors -enable $vol | Out-Null " +
                "  } " +
                "} else { " +
                // 3b) 添加新密码保护器，失败则用旧密码回滚
                "  try { " +
                "    Add-BitLockerKeyProtector -MountPoint $mp -PasswordProtector -Password $new | Out-Null " +
                "  } catch { " +
                "    if ($old.Length -gt 0) { try { Add-BitLockerKeyProtector -MountPoint $mp -PasswordProtector -Password $old | Out-Null } catch { } }; " +
                "    throw " +
                "  }; " +
                // 4) 确保保护处于开启状态
                "  $st = Get-BitLockerVolume -MountPoint $mp; " +
                "  if ($st.ProtectionStatus -eq 'Suspended') { Resume-BitLocker -MountPoint $mp | Out-Null } " +
                "  elseif ($st.ProtectionStatus -eq 'Off') { manage-bde -protectors -enable $vol | Out-Null } " +
                "}; " +
                // 5) 删除临时恢复密码保护器
                "if ($tmpId) { try { Remove-BitLockerKeyProtector -MountPoint $mp -KeyProtectorId $tmpId | Out-Null } catch { } }; " +
                // 保护开启不是瞬时生效，稍等片刻再取最终状态
                "for ($i = 0; $i -lt 10; $i++) { if ((Get-BitLockerVolume -MountPoint $mp).ProtectionStatus -ne 'Off') { break }; Start-Sleep -Milliseconds 500 }; " +
                "$fin = Get-BitLockerVolume -MountPoint $mp; " +
                "Write-Output ('PROT=' + $fin.ProtectionStatus); " +
                "Write-Output 'OK'";

            ExecResult r = PowerShellRunner.Run(script, 180000);
            if (!r.Success)
                throw new InvalidOperationException("修改密码失败：\r\n" + r.Combined);
            if (r.Output.IndexOf("PROT=Off", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidOperationException("修改密码后 BitLocker 保护未开启（状态 Off），请重试。\r\n" + r.Combined);
        }

        /// <summary>
        /// 清空密码：删除该卷上的所有密码保护器与恢复密码保护器。
        /// 注意：只删密码保护器时，若卷上残留「恢复密码（48 位）」保护器（例如之前「修改密码」时
        /// 添加的临时保护器没删干净），卷一旦被锁定就会要求输入 48 位恢复密钥。故一并删除。
        /// 删除全部保护器后该卷不再需要任何凭据即可挂载（保护状态变为 Off，卷内数据仍保持加密）。
        /// </summary>
        public static void RemovePassword(string mountPoint)
        {
            string script =
                "$mp = " + PowerShellRunner.Quote(mountPoint) + "; " +
                // manage-bde 只接受带冒号的盘符（如 "R:"）
                "$vol = $mp; if ($vol -notmatch ':$') { $vol = $vol + ':' }; " +
                "foreach ($t in @('Password','RecoveryPassword')) { " +
                "  $ids = @((Get-BitLockerVolume -MountPoint $mp).KeyProtector | Where-Object { $_.KeyProtectorType -eq $t } | ForEach-Object { [string]$_.KeyProtectorId }); " +
                "  foreach ($id in $ids) { try { Remove-BitLockerKeyProtector -MountPoint $mp -KeyProtectorId $id -ErrorAction Stop | Out-Null } catch { } }; " +
                // 个别情况下上面的 cmdlet 拒绝删除最后一个保护器，退回用 manage-bde 按 ID 删除
                "  $left = @((Get-BitLockerVolume -MountPoint $mp).KeyProtector | Where-Object { $_.KeyProtectorType -eq $t } | ForEach-Object { [string]$_.KeyProtectorId }); " +
                "  foreach ($id in $left) { manage-bde -protectors -delete $vol -id $id | Out-Null }; " +
                "} " +
                "$fin = Get-BitLockerVolume -MountPoint $mp; " +
                "$p = @($fin.KeyProtector | Where-Object { $_.KeyProtectorType -eq 'Password' }); " +
                "$rp = @($fin.KeyProtector | Where-Object { $_.KeyProtectorType -eq 'RecoveryPassword' }); " +
                "Write-Output ('LEFT=' + $p.Count); " +
                "Write-Output ('RLEFT=' + $rp.Count); " +
                "Write-Output ('PROT=' + $fin.ProtectionStatus); " +
                "Write-Output ('KP=' + (@($fin.KeyProtector | ForEach-Object { $_.KeyProtectorType }) -join ',')); " +
                "Write-Output 'OK'";

            ExecResult r = PowerShellRunner.Run(script, 180000);
            if (!r.Success)
                throw new InvalidOperationException("清空密码失败：\r\n" + r.Combined);
            if (r.Output.IndexOf("LEFT=0", StringComparison.OrdinalIgnoreCase) < 0 ||
                r.Output.IndexOf("RLEFT=0", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException(
                    "清空密码失败：该盘仍存在密码保护器或恢复密码保护器（否则下次锁定会要求输入 48 位恢复密钥），请重试。\r\n" + r.Combined);
        }
    }
}
