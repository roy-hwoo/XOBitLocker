using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BitLockerVault.Models;
using BitLockerVault.Services;

namespace BitLockerVault.Forms
{
    /// <summary>设置界面。</summary>
    internal sealed class SettingsForm : Form
    {
        private NumericUpDown _numAutoLock;
        private CheckBox _chkForceDismount;
        private CheckBox _chkAutoStart;
        private CheckedListBox _chkAutoMountList;
        private CheckBox _chkAutoMountUnlock;
        private CheckBox _chkAdminPassword;
        private Button _btnSetAdminPassword;
        private Button _btnOk;
        private Button _btnCancel;

        /// <summary>本次在设置界面里新输入的管理密码；null 表示未修改。</summary>
        private string _newAdminPassword;

        private readonly VaultManager _manager;

        public SettingsForm(VaultManager manager)
        {
            _manager = manager;
            InitializeUi();
            LoadValues();
        }

        private void InitializeUi()
        {
            Text = "设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 466);
            Font = new Font("Microsoft YaHei UI", 9F);

            const int labelX = 20;
            const int ctrlX = 160;
            const int ctrlW = 320;
            int y = 22;

            // 自动锁定时间
            Controls.Add(MakeLabel("自动锁定时间：", labelX, y + 3));
            _numAutoLock = new NumericUpDown
            {
                Left = ctrlX,
                Top = y,
                Width = 80,
                Minimum = 0,
                Maximum = 1440,
                Value = 0
            };
            Controls.Add(_numAutoLock);
            Controls.Add(MakeHint("分钟（0 表示不自动锁定）", ctrlX + 90, y + 3));
            Controls.Add(MakeHint("该加密盘无读写操作达到此时长即自动锁定", labelX, y + 26));
            y += 48;

            // 锁定时是否强制卸载
            Controls.Add(MakeLabel("强制卸载：", labelX, y + 3));
            _chkForceDismount = new CheckBox
            {
                Text = "自动锁定时强制卸载卷(盘被占用也强制卸)",
                Left = ctrlX,
                Top = y,
                AutoSize = true
            };
            Controls.Add(_chkForceDismount);
            Controls.Add(MakeHint("不勾选时，盘被占用会锁定失败并自动重试", labelX, y + 22));
            y += 70;

            // 开机启动
            Controls.Add(MakeLabel("开机启动：", labelX, y + 3));
            _chkAutoStart = new CheckBox
            {
                Text = "登录 Windows 时自动启动本程序",
                Left = ctrlX,
                Top = y,
                Width = ctrlW,
                AutoSize = true
            };
            Controls.Add(_chkAutoStart);
            y += 40;

            // 开机自动挂载（可多选）
            Controls.Add(MakeLabel("开机自动挂载：", labelX, y + 3));
            _chkAutoMountList = new CheckedListBox
            {
                Left = ctrlX,
                Top = y,
                Width = ctrlW,
                Height = 90,
                CheckOnClick = true,
                IntegralHeight = false
            };
            Controls.Add(_chkAutoMountList);
            y += 122;

            // 开机自动挂载时是否自动解密
            Controls.Add(MakeLabel("自动解密：", labelX, y + 3));
            _chkAutoMountUnlock = new CheckBox
            {
                Text = "开机自动挂载时自动解锁加密盘(只对记住密码盘有效)",
                Left = ctrlX,
                Top = y,
                Width = ctrlW,
                AutoSize = true
            };
            Controls.Add(_chkAutoMountUnlock);
            y += 42;

            // 管理密码
            Controls.Add(MakeLabel("管理密码：", labelX, y + 3));
            _chkAdminPassword = new CheckBox
            {
                Text = "启用管理密码（打开管理界面 / 设置需先输入密码）",
                Left = ctrlX,
                Top = y,
                Width = ctrlW,
                AutoSize = true
            };
            _chkAdminPassword.CheckedChanged += delegate { _btnSetAdminPassword.Enabled = _chkAdminPassword.Checked; };
            Controls.Add(_chkAdminPassword);

            _btnSetAdminPassword = new Button
            {
                Text = "设置管理密码...",
                Left = ctrlX,
                Top = y + 28,
                Width = 130,
                Height = 28
            };
            _btnSetAdminPassword.Click += OnSetAdminPassword;
            Controls.Add(_btnSetAdminPassword);
            y += 74;

            // 底部按钮
            _btnOk = new Button
            {
                Text = "确定",
                Left = ClientSize.Width - 200,
                Top = ClientSize.Height - 42,
                Width = 84,
                Height = 30
            };
            _btnOk.Click += OnOk;
            Controls.Add(_btnOk);

            _btnCancel = new Button
            {
                Text = "取消",
                Left = ClientSize.Width - 108,
                Top = ClientSize.Height - 42,
                Width = 84,
                Height = 30,
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            return new Label { Text = text, Left = x, Top = y, Width = 135, Height = 20 };
        }

        private static Label MakeHint(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Left = x,
                Top = y,
                Width = 260,
                Height = 20,
                ForeColor = Color.FromArgb(120, 120, 120)
            };
        }

        private void LoadValues()
        {
            AppConfig cfg = _manager.Config;

            _numAutoLock.Value = cfg.AutoLockMinutes < 0 ? 0 : Math.Min(cfg.AutoLockMinutes, 1440);
            _chkForceDismount.Checked = cfg.AutoLockForceDismount;
            _chkAutoStart.Checked = cfg.AutoStart;
            _chkAutoMountUnlock.Checked = cfg.AutoUnlockOnAutoMount;

            _chkAdminPassword.Checked = _manager.IsAdminPasswordEnabled;
            _btnSetAdminPassword.Enabled = _chkAdminPassword.Checked;

            // 开机自动挂载（多选）
            _chkAutoMountList.Items.Clear();
            if (_manager.Vaults.Count == 0)
            {
                _chkAutoMountList.Items.Add("（暂无加密盘）");
            }
            else
            {
                foreach (VaultInfo v in _manager.Vaults)
                {
                    int i = _chkAutoMountList.Items.Add(v);
                    _chkAutoMountList.SetItemChecked(i, v.AutoMount);
                }
            }
        }

        private void OnSetAdminPassword(object sender, EventArgs e)
        {
            string pwd = AdminPasswordForm.PromptNew(this);
            if (pwd == null) return;

            _newAdminPassword = pwd;
            _chkAdminPassword.Checked = true;
            MessageBox.Show(this, "管理密码已设置，点击「确定」保存后生效。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnOk(object sender, EventArgs e)
        {
            AppConfig cfg = _manager.Config;

            // 勾选启用但既没有已保存的密码、也没有新设置密码：先让用户设置
            if (_chkAdminPassword.Checked && _newAdminPassword == null && !_manager.IsAdminPasswordEnabled)
            {
                MessageBox.Show(this, "请先点击「设置管理密码...」输入管理密码。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            cfg.AutoLockMinutes = (int)_numAutoLock.Value;
            cfg.AutoLockForceDismount = _chkForceDismount.Checked;
            cfg.AutoUnlockOnAutoMount = _chkAutoMountUnlock.Checked;

            // 开机自动挂载（多选，按勾选状态回写）
            var autoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in _chkAutoMountList.CheckedItems)
            {
                var v = item as VaultInfo;
                if (v != null) autoIds.Add(v.Id);
            }
            foreach (VaultInfo v in _manager.Vaults)
                v.AutoMount = autoIds.Contains(v.Id);

            // 开机自启（计划任务）
            bool wantAutoStart = _chkAutoStart.Checked;
            if (wantAutoStart != cfg.AutoStart || wantAutoStart != AutoStartService.IsEnabled())
            {
                try
                {
                    AutoStartService.SetEnabled(wantAutoStart);
                }
                catch (Exception ex)
                {
                    LogService.Error("设置开机自启失败", ex);
                    MessageBox.Show(this, ex.Message, "设置开机自启失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            cfg.AutoStart = wantAutoStart;

            // 管理密码：有新密码则更新，未勾选启用则清除
            if (_newAdminPassword != null)
                cfg.ProtectedAdminPassword = SecretService.Protect(_newAdminPassword);
            else if (!_chkAdminPassword.Checked)
                cfg.ProtectedAdminPassword = "";

            try
            {
                _manager.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存设置失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
