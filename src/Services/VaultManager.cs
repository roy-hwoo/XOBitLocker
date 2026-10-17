using System;
using System.Collections.Generic;
using System.Linq;
using BitLockerVault.Models;

namespace BitLockerVault.Services
{
    /// <summary>用户输入的解锁密码，以及是否要记住它。</summary>
    internal sealed class PasswordInput
    {
        /// <summary>用户输入的密码。</summary>
        public string Password;

        /// <summary>是否记住该密码（下次挂载自动使用）。</summary>
        public bool Remember;
    }

    /// <summary>
    /// 挂载过程中需要用户输入密码时的回调。返回 null 表示用户取消。
    /// 回调可能在工作线程上被调用，实现方需自行切到 UI 线程弹窗。
    /// </summary>
    internal delegate PasswordInput PasswordPrompt(VaultInfo vault);

    /// <summary>业务编排层：配置文件 + VHD + BitLocker 的组合操作。</summary>
    internal sealed class VaultManager
    {
        private static readonly object Gate = new object();

        private AppConfig _config;

        public VaultManager()
        {
            _config = ConfigService.Load();
        }

        public AppConfig Config { get { return _config; } }

        public IList<VaultInfo> Vaults { get { return _config.Vaults; } }

        public void Reload()
        {
            lock (Gate) { _config = ConfigService.Load(); }
        }

        public void Save()
        {
            lock (Gate) { ConfigService.Save(_config); }
        }

        public VaultInfo FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _config.Vaults.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>创建加密盘：创建 VHDX → 分区格式化 → 启用 BitLocker → 记录配置。</summary>
        public VaultInfo CreateVault(CreateVaultRequest request, Action<string> progress)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (string.IsNullOrWhiteSpace(request.VhdPath)) throw new ArgumentException("请选择 VHDX 存放位置。");
            if (string.IsNullOrWhiteSpace(request.Password)) throw new ArgumentException("密码不能为空。");

            string letter = DriveHelper.Normalize(request.DriveLetter);
            if (letter.Length < 2)
                letter = DriveHelper.FindFreeDrive(_config.MountDrive);
            if (letter.Length < 2)
                throw new InvalidOperationException("没有可用的盘符，请先在设置中释放一个盘符。");
            if (!DriveHelper.IsDriveFree(letter))
                letter = DriveHelper.FindFreeDrive(_config.MountDrive);
            if (letter.Length < 2 || !DriveHelper.IsDriveFree(letter))
                throw new InvalidOperationException("盘符 " + letter + " 已被占用，请选择一个空闲盘符。");

            Report(progress, "正在创建虚拟硬盘 ...");
            VhdService.CreateDynamicVhd(request.VhdPath, request.SizeBytes, letter);

            try
            {
                Report(progress, "正在启用 BitLocker 加密 ...");
                BitLockerService.Enable(letter, request.Password);
            }
            catch
            {
                // 加密失败时回收刚创建的虚拟硬盘，避免残留文件导致重试时报「文件已存在」
                Report(progress, "加密失败，正在清理虚拟硬盘 ...");
                CleanupFailedVhd(request.VhdPath);
                throw;
            }

            var vault = new VaultInfo
            {
                Name = System.IO.Path.GetFileName(request.VhdPath),
                VhdPath = request.VhdPath,
                SizeBytes = request.SizeBytes,
                DriveLetter = letter,
                ProtectedPassword = SecretService.Protect(request.Password),
                AutoMount = false
            };

            _config.Vaults.Add(vault);
            Save();
            Report(progress, "创建完成：" + letter);
            LogService.Info("创建加密盘成功：" + request.VhdPath + " -> " + letter);
            return vault;
        }

        /// <summary>加密失败后卸载并删除刚创建的虚拟硬盘文件。</summary>
        private static void CleanupFailedVhd(string vhdPath)
        {
            try { VhdService.DismountVhd(vhdPath); }
            catch (Exception ex) { LogService.Warn("清理时卸载虚拟硬盘失败：" + ex.Message); }

            try
            {
                if (System.IO.File.Exists(vhdPath)) System.IO.File.Delete(vhdPath);
            }
            catch (Exception ex) { LogService.Warn("清理时删除虚拟硬盘文件失败：" + ex.Message); }
        }

        /// <summary>挂载并解锁加密盘，返回最终盘符。</summary>
        public string MountVault(VaultInfo vault, Action<string> progress)
        {
            return MountVault(vault, progress, true, null);
        }

        /// <summary>
        /// 挂载加密盘，返回最终盘符。unlock 为 false 时只挂载、保持锁定状态。
        /// 需要解锁但未记住密码时通过 askPassword 向用户索取；askPassword 为 null 则直接报错。
        /// </summary>
        public string MountVault(VaultInfo vault, Action<string> progress, bool unlock, PasswordPrompt askPassword)
        {
            if (vault == null) throw new ArgumentNullException("vault");

            string previousLetter = vault.DriveLetter;
            bool attachedBefore = VhdService.IsAttached(vault.VhdPath);

            Report(progress, "正在挂载虚拟硬盘 ...");
            string letter = VhdService.MountVhd(vault.VhdPath, string.IsNullOrEmpty(vault.DriveLetter) ? _config.MountDrive : vault.DriveLetter);
            if (string.IsNullOrEmpty(letter))
                throw new InvalidOperationException("挂载成功但未能取得盘符，请检查磁盘管理。");

            vault.DriveLetter = DriveHelper.Normalize(letter);
            Save();

            if (!unlock)
            {
                Report(progress, "已挂载（保持锁定）：" + letter);
                return letter;
            }

            VolumeStatus status = BitLockerService.GetStatus(letter);
            if (status == null || !string.Equals(status.LockStatus, "Locked", StringComparison.OrdinalIgnoreCase))
            {
                Report(progress, "已挂载：" + letter);
                return letter;
            }

            string pwd = SecretService.Unprotect(vault.ProtectedPassword);
            bool remember = false;
            if (string.IsNullOrEmpty(pwd))
            {
                if (askPassword == null)
                    throw new InvalidOperationException("该加密盘处于锁定状态且未记住密码，无法自动解锁。");

                Report(progress, "等待输入密码 ...");
                PasswordInput answer = askPassword(vault);
                if (answer == null || string.IsNullOrEmpty(answer.Password))
                {
                    // 用户取消：本次新挂载的盘回收掉，恢复到挂载前的状态
                    if (!attachedBefore)
                    {
                        try { VhdService.DismountVhd(vault.VhdPath); } catch { }
                        vault.DriveLetter = previousLetter;
                        Save();
                    }
                    throw new OperationCanceledException("已取消输入密码，未解锁该加密盘。");
                }
                pwd = answer.Password;
                remember = answer.Remember;
            }

            Report(progress, "正在解锁 ...");
            BitLockerService.Unlock(letter, pwd);

            // 解锁成功后（即密码正确）才记住，避免把错误密码写进配置
            if (remember) vault.ProtectedPassword = SecretService.Protect(pwd);

            Save();
            Report(progress, "已挂载：" + letter);
            return letter;
        }

        /// <summary>
        /// 挂载列表之外的 VHDX/VHD 文件：已在列表中则直接挂载；否则挂载、必要时用密码解锁，
        /// 并写入配置列表。password 为空时若该盘已锁定则报错。
        /// rememberPassword 为 true 时才把密码加密后保存（下次挂载自动解锁）。
        /// </summary>
        public VaultInfo AttachVhdFile(string vhdPath, string password, bool rememberPassword, Action<string> progress)
        {
            if (string.IsNullOrWhiteSpace(vhdPath)) throw new ArgumentException("未指定虚拟硬盘文件。");
            if (!System.IO.File.Exists(vhdPath))
                throw new System.IO.FileNotFoundException("找不到虚拟硬盘文件：\r\n" + vhdPath);

            VaultInfo existing = _config.Vaults.FirstOrDefault(
                v => string.Equals(v.VhdPath, vhdPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                MountVault(existing, progress);
                return existing;
            }

            Report(progress, "正在挂载虚拟硬盘 ...");
            string letter = VhdService.MountVhd(vhdPath, string.IsNullOrEmpty(_config.MountDrive) ? "Z:" : _config.MountDrive);
            if (string.IsNullOrEmpty(letter))
                throw new InvalidOperationException("挂载成功但未能取得盘符，请检查磁盘管理。");
            letter = DriveHelper.Normalize(letter);

            VolumeStatus status = BitLockerService.GetStatus(letter);
            if (status != null && string.Equals(status.LockStatus, "Locked", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(password))
                {
                    try { VhdService.DismountVhd(vhdPath); } catch { }
                    throw new InvalidOperationException("该加密盘当前处于锁定状态，请提供正确的密码后重试。");
                }
                try
                {
                    Report(progress, "正在解锁 ...");
                    BitLockerService.Unlock(letter, password);
                }
                catch
                {
                    try { VhdService.DismountVhd(vhdPath); } catch { }
                    throw;
                }
            }

            var vault = new VaultInfo
            {
                Name = System.IO.Path.GetFileName(vhdPath),
                VhdPath = vhdPath,
                DriveLetter = letter,
                ProtectedPassword = rememberPassword ? SecretService.Protect(password) : "",
                AutoMount = false
            };
            _config.Vaults.Add(vault);
            Save();
            Report(progress, "已挂载：" + letter);
            LogService.Info("挂载文件成功：" + vhdPath + " -> " + letter);
            return vault;
        }

        /// <summary>锁定加密盘（不卸载）。</summary>
        public void LockVault(VaultInfo vault, bool forceDismount)
        {
            if (vault == null) throw new ArgumentNullException("vault");
            if (string.IsNullOrEmpty(vault.DriveLetter))
                throw new InvalidOperationException("该加密盘没有可用的盘符。");
            if (DriveHelper.IsDriveFree(vault.DriveLetter))
                throw new InvalidOperationException("该加密盘当前未挂载，无法锁定。");
            BitLockerService.Lock(vault.DriveLetter, forceDismount);
            LogService.Info("已锁定：" + vault.Name + " (" + vault.DriveLetter + ")");
        }

        /// <summary>锁定所有已挂载的加密盘，返回成功锁定的数量。</summary>
        public int LockAll(bool forceDismount)
        {
            int count = 0;
            foreach (VaultInfo v in _config.Vaults.ToList())
            {
                try
                {
                    if (string.IsNullOrEmpty(v.DriveLetter)) continue;
                    if (DriveHelper.IsDriveFree(v.DriveLetter)) continue;
                    VolumeStatus st = BitLockerService.GetStatus(v.DriveLetter);
                    if (st == null) continue;
                    if (string.Equals(st.LockStatus, "Unlocked", StringComparison.OrdinalIgnoreCase))
                    {
                        BitLockerService.Lock(v.DriveLetter, forceDismount);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error("锁定失败：" + v.Name, ex);
                }
            }
            return count;
        }

        /// <summary>卸载加密盘（先锁定再卸载）。</summary>
        public void UnmountVault(VaultInfo vault, bool forceDismount)
        {
            if (vault == null) throw new ArgumentNullException("vault");
            try
            {
                if (!string.IsNullOrEmpty(vault.DriveLetter) && VhdService.IsAttached(vault.VhdPath))
                    BitLockerService.Lock(vault.DriveLetter, forceDismount);
            }
            catch (Exception ex)
            {
                LogService.Warn("卸载前锁定失败（继续卸载）：" + ex.Message);
            }
            VhdService.DismountVhd(vault.VhdPath);
            LogService.Info("已卸载：" + vault.Name);
        }

        /// <summary>查询加密盘当前状态。</summary>
        public VaultState GetVaultState(VaultInfo vault)
        {
            if (vault == null) return VaultState.Unknown;
            try
            {
                bool attached = VhdService.IsAttached(vault.VhdPath);
                if (!attached) return VaultState.NotMounted;
                if (string.IsNullOrEmpty(vault.DriveLetter)) return VaultState.Unknown;
                VolumeStatus st = BitLockerService.GetStatus(vault.DriveLetter);
                return BitLockerService.ResolveState(true, st);
            }
            catch (Exception ex)
            {
                LogService.Error("查询状态失败：" + vault.Name, ex);
                return VaultState.Unknown;
            }
        }

        /// <summary>状态文本（中文）。</summary>
        public static string StateText(VaultState state)
        {
            switch (state)
            {
                case VaultState.NotMounted: return "未挂载";
                case VaultState.Off: return "未加密";
                case VaultState.Locked: return "已锁定";
                case VaultState.Unlocked: return "已解锁";
                case VaultState.Encrypting: return "加密中";
                case VaultState.Decrypting: return "解密中";
                case VaultState.Suspended: return "已挂起";
                default: return "未知";
            }
        }

        /// <summary>修改加密盘的盘符（要求该盘已挂载）。</summary>
        public void ChangeDriveLetter(VaultInfo vault, string newLetter)
        {
            if (vault == null) throw new ArgumentNullException("vault");
            string letter = DriveHelper.Normalize(newLetter);
            if (letter.Length < 2) throw new ArgumentException("请选择有效的盘符。");
            if (string.Equals(letter, vault.DriveLetter, StringComparison.OrdinalIgnoreCase)) return;
            if (!DriveHelper.IsDriveFree(letter))
                throw new InvalidOperationException("盘符 " + letter + " 已被占用，请选择其它盘符。");

            if (!VhdService.IsAttached(vault.VhdPath))
                throw new InvalidOperationException("该加密盘当前未挂载，请先挂载后再修改盘符。");
            if (string.IsNullOrEmpty(vault.DriveLetter) || DriveHelper.IsDriveFree(vault.DriveLetter))
                throw new InvalidOperationException("该加密盘已挂载但未分配盘符，无法修改。");

            VhdService.ChangeDriveLetter(vault.DriveLetter, letter);
            vault.DriveLetter = letter;
            Save();
            LogService.Info("已修改盘符：" + vault.DisplayName + " -> " + letter);
        }

        /// <summary>修改密码（要求已挂载且已解锁；「未加密」时也可用，相当于重新设置密码并恢复保护）。新密码为空表示清空密码。</summary>
        public void ChangePassword(VaultInfo vault, string newPassword)
        {
            if (vault == null) throw new ArgumentNullException("vault");
            if (string.IsNullOrEmpty(vault.DriveLetter))
                throw new InvalidOperationException("该加密盘尚未挂载。");

            VaultState state = GetVaultState(vault);
            if (state != VaultState.Unlocked && state != VaultState.Off)
                throw new InvalidOperationException(
                    "修改密码需要加密盘处于「已解锁」或「未加密」状态，当前状态：" + StateText(state) + "。");

            if (string.IsNullOrEmpty(newPassword))
            {
                BitLockerService.RemovePassword(vault.DriveLetter);
                vault.ProtectedPassword = "";
                Save();
                LogService.Info("已清空密码：" + vault.DisplayName);
                return;
            }

            string old = SecretService.Unprotect(vault.ProtectedPassword);
            BitLockerService.ChangePassword(vault.DriveLetter, newPassword, old);
            vault.ProtectedPassword = SecretService.Protect(newPassword);
            Save();
            LogService.Info("已修改密码：" + vault.DisplayName);
        }

        /// <summary>忘记记住的密码：清除已保存的密码，下次挂载需要重新手动输入。</summary>
        public void ForgetPassword(VaultInfo vault)
        {
            if (vault == null) return;
            vault.ProtectedPassword = "";
            Save();
            LogService.Info("已清除记住的密码：" + vault.DisplayName);
        }

        /// <summary>清除列表中所有加密盘已记住的密码，返回被清除的数量。</summary>
        public int ForgetAllPasswords()
        {
            int count = 0;
            foreach (VaultInfo v in _config.Vaults)
            {
                if (v == null || string.IsNullOrEmpty(v.ProtectedPassword)) continue;
                v.ProtectedPassword = "";
                count++;
            }
            if (count > 0)
            {
                Save();
                LogService.Info("已清除全部记住的密码，共 " + count + " 个加密盘。");
            }
            return count;
        }

        /// <summary>是否已启用管理密码（打开管理界面/设置前需要验证）。</summary>
        public bool IsAdminPasswordEnabled
        {
            get { return !string.IsNullOrEmpty(_config.ProtectedAdminPassword); }
        }

        /// <summary>校验管理密码是否正确；未启用管理密码时恒为 true。</summary>
        public bool VerifyAdminPassword(string password)
        {
            if (!IsAdminPasswordEnabled) return true;
            string saved = SecretService.Unprotect(_config.ProtectedAdminPassword);
            return string.Equals(saved, password ?? "", StringComparison.Ordinal);
        }

        /// <summary>设置管理密码；传 null 或空字符串表示关闭管理密码。</summary>
        public void SetAdminPassword(string password)
        {
            _config.ProtectedAdminPassword = SecretService.Protect(password ?? "");
            Save();
            LogService.Info(IsAdminPasswordEnabled ? "已启用管理密码" : "已关闭管理密码");
        }

        /// <summary>
        /// 删除加密盘：先卸载（如已挂载），再移除配置记录；deleteFile 为 true 时同时删除 VHDX 文件。
        /// </summary>
        public void RemoveVault(VaultInfo vault, bool deleteFile)
        {
            if (vault == null) return;

            if (!string.IsNullOrEmpty(vault.VhdPath) && VhdService.IsAttached(vault.VhdPath))
            {
                try { UnmountVault(vault, true); }
                catch (Exception ex) { LogService.Warn("删除时卸载失败（继续删除）：" + ex.Message); }
            }

            _config.Vaults.Remove(vault);
            Save();

            if (deleteFile && !string.IsNullOrEmpty(vault.VhdPath))
            {
                try
                {
                    if (System.IO.File.Exists(vault.VhdPath)) System.IO.File.Delete(vault.VhdPath);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("已从列表中移除，但删除文件失败：\r\n" + ex.Message);
                }
            }

            LogService.Info("已删除加密盘：" + vault.DisplayName + (deleteFile ? "（含文件）" : "（保留文件）"));
        }

        /// <summary>需要开机自动挂载、且文件存在的加密盘。</summary>
        public IEnumerable<VaultInfo> GetAutoMountVaults()
        {
            return _config.Vaults.Where(v => v.AutoMount && !string.IsNullOrEmpty(v.VhdPath));
        }

        private static void Report(Action<string> progress, string message)
        {
            LogService.Info(message);
            if (progress != null) progress(message);
        }
    }
}
