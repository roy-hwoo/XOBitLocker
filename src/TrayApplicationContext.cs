using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using BitLockerVault.Forms;
using BitLockerVault.Models;
using BitLockerVault.Services;

namespace BitLockerVault
{
    /// <summary>托盘应用上下文：托盘图标、右键菜单、自动锁定、开机自动挂载。</summary>
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly VaultManager _manager = new VaultManager();
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;

        private readonly ToolStripMenuItem _miAutoStart;
        private readonly ToolStripMenuItem _miAutoMount;
        private readonly ToolStripMenuItem _miLock;

        private readonly Timer _lockTimer;
        private readonly VolumeActivityMonitor _activity = new VolumeActivityMonitor();

        /// <summary>加密盘 Id -> 该盘最后一次有读写操作的时间。</summary>
        private readonly Dictionary<string, DateTime> _idleSince = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        /// <summary>自动锁定检查是否正在后台执行（0 = 空闲，1 = 执行中），防止重入。</summary>
        private int _autoLockBusy;

        /// <summary>自动锁定已被关闭的标记（1 = 需要清空计时状态）。</summary>
        private int _autoLockDisabled;

        /// <summary>本轮空闲期间已记录过「锁定失败」日志的加密盘 Id，避免每分钟重试时刷日志。</summary>
        private readonly HashSet<string> _lockFailLogged = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>UI 线程同步上下文，用于从后台线程安全地弹托盘气泡（首次 Tick 时捕获）。</summary>
        private System.Threading.SynchronizationContext _uiContext;

        private VaultListForm _mainForm;

        public TrayApplicationContext()
        {
            // 保留左侧图像边栏：ToolStripMenuItem 的勾选标记就画在这里，
            // 若设 ShowImageMargin = false，「开机自启」开启后不会显示 ✔
            _menu = new ContextMenuStrip();
            _menu.Opening += OnMenuOpening;

            _miAutoStart = new ToolStripMenuItem("开机自启", null, OnToggleAutoStart);
            _miAutoMount = new ToolStripMenuItem("开机自动挂载");
            _miLock = new ToolStripMenuItem("锁定");

            var miCreate = new ToolStripMenuItem("创建加密盘...", null, OnCreateVault);
            var miSettings = new ToolStripMenuItem("设置...", null, OnSettings);
            var miOpen = new ToolStripMenuItem("打开管理界面", null, OnOpenMain);
            var miExit = new ToolStripMenuItem("退出", null, OnExit);

            _menu.Items.Add(_miAutoStart);
            _menu.Items.Add(_miAutoMount);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(miCreate);
            _menu.Items.Add(_miLock);
            _menu.Items.Add(miSettings);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(miOpen);
            _menu.Items.Add(miExit);

            _tray = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = "XOBitLocker",
                Visible = true,
                ContextMenuStrip = _menu
            };
            _tray.DoubleClick += OnOpenMain;
            _tray.BalloonTipClicked += OnOpenMain;

            _lockTimer = new Timer { Interval = 5000 };
            _lockTimer.Tick += OnLockTimerTick;
            _lockTimer.Start();

            // 登录后执行一次「开机自动挂载」
            Task.Run(delegate { AutoMountAtStartup(); });

            // 首次运行默认开启开机自启：若计划任务不存在则补建
            Task.Run(delegate { EnsureAutoStartTask(); });
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                string exe = AutoStartService.ExecutablePath;
                Icon icon = Icon.ExtractAssociatedIcon(exe);
                if (icon != null) return icon;
            }
            catch (Exception ex)
            {
                LogService.Error("加载程序图标失败", ex);
            }
            return SystemIcons.Shield;
        }

        // ---------------- 托盘菜单 ----------------

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _miAutoStart.Checked = _manager.Config.AutoStart;

            // 开机自动挂载 ▸ 加密盘列表（可多选，各自独立勾选）
            _miAutoMount.DropDownItems.Clear();
            if (_manager.Vaults.Count == 0)
            {
                var empty = new ToolStripMenuItem("（暂无加密盘）") { Enabled = false };
                _miAutoMount.DropDownItems.Add(empty);
            }
            else
            {
                foreach (VaultInfo v in _manager.Vaults)
                {
                    VaultInfo captured = v;
                    var item = new ToolStripMenuItem(captured.DisplayName)
                    {
                        CheckOnClick = true,
                        Checked = captured.AutoMount
                    };
                    item.Click += delegate
                    {
                        captured.AutoMount = item.Checked;
                        SafeSave();
                    };
                    _miAutoMount.DropDownItems.Add(item);
                }
            }

            // 锁定 ▸
            _miLock.DropDownItems.Clear();
            var miAll = new ToolStripMenuItem("全部锁定", null, delegate { LockAll(true); });
            _miLock.DropDownItems.Add(miAll);
            _miLock.DropDownItems.Add(new ToolStripSeparator());

            bool any = false;
            foreach (VaultInfo v in _manager.Vaults)
            {
                if (string.IsNullOrEmpty(v.DriveLetter) || DriveHelper.IsDriveFree(v.DriveLetter)) continue;
                any = true;
                VaultInfo captured = v;
                string text = captured.DisplayName + "  (" + v.DriveLetter + ")";
                var item = new ToolStripMenuItem(text, null, delegate { LockOne(captured); });
                _miLock.DropDownItems.Add(item);
            }
            if (!any)
            {
                var empty = new ToolStripMenuItem("（没有已挂载的加密盘）") { Enabled = false };
                _miLock.DropDownItems.Add(empty);
            }
        }

        private void OnToggleAutoStart(object sender, EventArgs e)
        {
            bool target = !_manager.Config.AutoStart;
            try
            {
                AutoStartService.SetEnabled(target);
                _manager.Config.AutoStart = target;
                SafeSave();
                Notify(target ? "已开启开机自启。" : "已关闭开机自启。");
            }
            catch (Exception ex)
            {
                LogService.Error("切换开机自启失败", ex);
                MessageBox.Show(ex.Message, "设置开机自启失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnCreateVault(object sender, EventArgs e)
        {
            using (var f = new CreateVaultForm(_manager))
            {
                f.ShowDialog();
            }
        }

        private void OnSettings(object sender, EventArgs e)
        {
            // 已启用管理密码时，打开设置前先验证
            if (!AdminPasswordForm.Verify(_manager, null)) return;

            using (var f = new SettingsForm(_manager))
            {
                f.ShowDialog();
            }
        }

        private void OnOpenMain(object sender, EventArgs e)
        {
            // 已启用管理密码时，打开管理界面（托盘双击/气泡/菜单）前先验证
            if (!AdminPasswordForm.Verify(_manager, null)) return;

            ShowMainForm();
        }

        private void ShowMainForm()
        {
            if (_mainForm == null || _mainForm.IsDisposed)
                _mainForm = new VaultListForm(_manager);

            if (!_mainForm.Visible)
            {
                _mainForm.Show();
                _mainForm.RefreshList();
            }
            if (_mainForm.WindowState == FormWindowState.Minimized)
                _mainForm.WindowState = FormWindowState.Normal;

            _mainForm.Activate();
            _mainForm.BringToFront();
        }

        private void OnExit(object sender, EventArgs e)
        {
            _lockTimer.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            ExitThread();
        }

        // ---------------- 操作 ----------------

        private void LockOne(VaultInfo vault)
        {
            try
            {
                _manager.LockVault(vault, true);
                Notify("已锁定：" + vault.DisplayName);
            }
            catch (Exception ex)
            {
                LogService.Error("锁定失败", ex);
                MessageBox.Show(ex.Message, "锁定失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LockAll(bool notify)
        {
            try
            {
                int count = _manager.LockAll(true);
                if (notify)
                    Notify(count > 0 ? ("已锁定 " + count + " 个加密盘。") : "没有已解锁的加密盘。");
            }
            catch (Exception ex)
            {
                LogService.Error("全部锁定失败", ex);
            }
        }

        // ---------------- 自动锁定 ----------------

        private void OnLockTimerTick(object sender, EventArgs e)
        {
            if (_uiContext == null) _uiContext = System.Threading.SynchronizationContext.Current;

            int minutes = _manager.Config.AutoLockMinutes;
            if (minutes <= 0)
            {
                // 不在 UI 线程直接改集合，交给下一次后台检查清理
                System.Threading.Interlocked.Exchange(ref _autoLockDisabled, 1);
                return;
            }

            // 检查过程涉及 WMI 查询与 PowerShell 调用，放到后台线程，避免卡住界面
            if (System.Threading.Interlocked.Exchange(ref _autoLockBusy, 1) == 1) return;

            Task.Run(delegate
            {
                try
                {
                    AutoLockCheck(minutes);
                }
                catch (Exception ex)
                {
                    LogService.Error("自动锁定检查失败", ex);
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _autoLockBusy, 0);
                }
            });
        }

        /// <summary>
        /// 自动锁定检查：以「该加密盘本身有没有读写操作」为判据——某个盘连续
        /// AutoLockMinutes 分钟没有任何读写，就把它锁定（不影响仍在使用的其它盘）。
        /// 是否强制卸载由设置项 AutoLockForceDismount 决定。
        /// WMI 计数器不可用时，退回「系统键鼠无操作」判据。
        /// </summary>
        private void AutoLockCheck(int minutes)
        {
            // 自动锁定曾被设为 0：清空计时状态（只在本后台线程改集合，避免跨线程冲突）
            if (System.Threading.Interlocked.Exchange(ref _autoLockDisabled, 0) == 1)
            {
                _idleSince.Clear();
                _lockFailLogged.Clear();
                return;
            }

            DateTime now = DateTime.Now;
            TimeSpan limit = TimeSpan.FromMinutes(minutes);
            bool force = _manager.Config.AutoLockForceDismount;

            Dictionary<string, bool> io = _activity.Poll();
            TimeSpan inputIdle = GetIdleTime();

            foreach (VaultInfo v in new List<VaultInfo>(_manager.Vaults))
            {
                if (string.IsNullOrEmpty(v.DriveLetter)) continue;

                string letter = DriveHelper.Normalize(v.DriveLetter);
                if (letter.Length < 1) continue;

                // 未挂载（盘符不存在）的盘无需计时
                if (DriveHelper.IsDriveFree(letter.Substring(0, 1) + ":"))
                {
                    _idleSince.Remove(v.Id);
                    continue;
                }

                bool busy;
                if (io != null)
                {
                    bool hasIo;
                    busy = io.TryGetValue(letter.Substring(0, 1), out hasIo) && hasIo;
                }
                else
                {
                    busy = inputIdle < limit;
                }

                if (busy)
                {
                    _idleSince[v.Id] = now;
                    _lockFailLogged.Remove(v.Id);
                    continue;
                }

                DateTime since;
                if (!_idleSince.TryGetValue(v.Id, out since))
                {
                    // 第一次发现它空闲，从这里开始计时
                    _idleSince[v.Id] = now;
                    continue;
                }
                if (now - since < limit) continue;

                // 空闲时间已到：确认状态后锁定
                _idleSince.Remove(v.Id);
                try
                {
                    if (_manager.GetVaultState(v) != VaultState.Unlocked) continue;

                    _manager.LockVault(v, force);
                    _lockFailLogged.Remove(v.Id);
                    LogService.Info("自动锁定（" + minutes + " 分钟无操作）：" + v.DisplayName + " (" + letter + ")");
                    Notify("「" + v.DisplayName + "」已 " + minutes + " 分钟无操作，已自动锁定。");
                }
                catch (Exception ex)
                {
                    // 锁定失败不弹气泡（会反复打扰），只在本轮空闲期间记一次日志，之后静默重试
                    if (_lockFailLogged.Add(v.Id))
                    {
                        LogService.Error("自动锁定失败：" + v.DisplayName, ex);
                    }

                    // 把计时点回拨到「还差 1 分钟到期」，实现 1 分钟后重试而非 5 秒后重试
                    _idleSince[v.Id] = now.AddMinutes(1).Subtract(limit);
                }
            }
        }

        private static TimeSpan GetIdleTime()
        {
            try
            {
                LASTINPUTINFO info = new LASTINPUTINFO();
                info.cbSize = (uint)Marshal.SizeOf(info);
                if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
                uint tick = unchecked((uint)Environment.TickCount);
                uint elapsed = unchecked(tick - info.dwTime);
                return TimeSpan.FromMilliseconds(elapsed);
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        // ---------------- 开机自动挂载 ----------------

        private void AutoMountAtStartup()
        {
            // 等托盘初始化完成
            System.Threading.Thread.Sleep(1500);

            var targets = new List<VaultInfo>();
            foreach (VaultInfo v in _manager.GetAutoMountVaults())
            {
                if (System.IO.File.Exists(v.VhdPath)) targets.Add(v);
            }
            if (targets.Count == 0) return;

            // 是否自动解密由设置决定：不勾选时只挂载、保持「已锁定」状态
            bool unlock = _manager.Config.AutoUnlockOnAutoMount;

            foreach (VaultInfo target in targets)
            {
                try
                {
                    // 开机自动挂载不弹气泡提醒，仅写日志
                    string letter = _manager.MountVault(target, null, unlock, null);
                    LogService.Info("已自动挂载" + (unlock ? "并解锁：" : "（保持锁定）：")
                        + target.DisplayName + "  (" + letter + ")");
                }
                catch (Exception ex)
                {
                    LogService.Error("开机自动挂载失败：" + target.DisplayName, ex);
                }
            }
        }

        /// <summary>配置里开机自启为开启、但计划任务不存在时补建（首次运行默认开启）。</summary>
        private void EnsureAutoStartTask()
        {
            try
            {
                if (!_manager.Config.AutoStart) return;
                if (AutoStartService.IsEnabled()) return;
                AutoStartService.Enable();
            }
            catch (Exception ex)
            {
                LogService.Error("补建开机自启计划任务失败", ex);
            }
        }

        // ---------------- 辅助 ----------------

        private void SafeSave()
        {
            try { _manager.Save(); }
            catch (Exception ex) { LogService.Error("保存配置失败", ex); }
        }

        private void Notify(string message)
        {
            // 可能从后台线程调用（自动锁定），封送回 UI 线程再弹气泡
            if (_uiContext != null)
            {
                _uiContext.Post(delegate { ShowBalloon(message); }, null);
                return;
            }
            ShowBalloon(message);
        }

        private void ShowBalloon(string message)
        {
            try
            {
                _tray.BalloonTipTitle = "XOBitLocker";
                _tray.BalloonTipText = message;
                _tray.ShowBalloonTip(3000);
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_tray != null) _tray.Dispose();
                if (_menu != null) _menu.Dispose();
                if (_lockTimer != null) _lockTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---------------- Win32 ----------------

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    }
}
