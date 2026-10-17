using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using BitLockerVault.Models;
using BitLockerVault.Services;

namespace BitLockerVault.Forms
{
    /// <summary>加密盘管理窗口：查看状态、挂载/解锁、锁定、卸载。</summary>
    internal sealed class VaultListForm : Form
    {
        private ListView _list;
        private Button _btnMount;
        private Button _btnLock;
        private Button _btnUnmount;
        private Button _btnForgetAll;
        private Button _btnCreate;
        private Button _btnSettings;
        private Label _lblStatus;

        private ContextMenuStrip _listMenu;
        private ToolStripMenuItem _miForgetPassword;
        private ToolStripMenuItem _miChangeDrive;
        private ToolStripMenuItem _miChangePassword;
        private ToolStripMenuItem _miLock;
        private ToolStripMenuItem _miRemove;
        private ToolStripMenuItem _miUnmount;
        private ToolStripMenuItem _miDelete;
        private ToolStripMenuItem _miRefresh;

        private readonly VaultManager _manager;
        private bool _busy;

        public VaultListForm(VaultManager manager)
        {
            _manager = manager;
            InitializeUi();
        }

        private void InitializeUi()
        {
            Text = "XOBitLocker 加解锁密管理界面";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(866, 440);
            ClientSize = new Size(866, 460);
            MaximizeBox = false;   // 标题栏取消最大化按钮
            MinimizeBox = false;   // 标题栏取消最小化按钮
            Font = new Font("Microsoft YaHei UI", 9F);
            Icon = Icon.ExtractAssociatedIcon(AutoStartService.ExecutablePath);

            _list = new ListView
            {
                Left = 14,
                Top = 14,
                Width = ClientSize.Width - 28,
                Height = 340,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                MultiSelect = false,
                HideSelection = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _list.Columns.Add("名称", 180);
            _list.Columns.Add("盘符", 70);
            _list.Columns.Add("状态", 90);
            _list.Columns.Add("文件位置", 490);
            _list.DoubleClick += delegate { MountSelected(SelectedVault); };
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            _list.MouseDown += OnListMouseDown;
            Controls.Add(_list);

            // 列表右键菜单
            _listMenu = new ContextMenuStrip();
            _miForgetPassword = new ToolStripMenuItem("清除记住密码", null, OnForgetPassword);
            _miChangeDrive = new ToolStripMenuItem("修改盘符", null, OnChangeDrive);
            _miChangePassword = new ToolStripMenuItem("修改密码", null, OnChangePassword);
            _miLock = new ToolStripMenuItem("锁定", null, OnLock);
            _miRemove = new ToolStripMenuItem("移除列表", null, OnRemoveFromList);
            _miUnmount = new ToolStripMenuItem("卸载", null, OnUnmount);
            _miDelete = new ToolStripMenuItem("删除", null, OnDelete);
            _miRefresh = new ToolStripMenuItem("刷新", null, OnRefresh);
            _listMenu.Items.Add(_miForgetPassword);
            _listMenu.Items.Add(_miChangeDrive);
            _listMenu.Items.Add(_miChangePassword);
            _listMenu.Items.Add(new ToolStripSeparator());
            _listMenu.Items.Add(_miLock);
            _listMenu.Items.Add(_miUnmount);
            _listMenu.Items.Add(new ToolStripSeparator());
            _listMenu.Items.Add(_miRemove);
            _listMenu.Items.Add(_miDelete);
            _listMenu.Items.Add(new ToolStripSeparator());
            _listMenu.Items.Add(_miRefresh);
            _listMenu.Opening += OnListMenuOpening;
            _list.ContextMenuStrip = _listMenu;

            new ToolTip().SetToolTip(_list, "双击挂载并解锁该加密盘（已记住密码则直接解锁）。");

            int btnY = ClientSize.Height - 54;
            _btnMount = MakeButton("挂载", 14, btnY, 88, OnMount);
            _btnLock = MakeButton("锁定", 110, btnY, 64, OnLock);
            _btnUnmount = MakeButton("卸载", 182, btnY, 64, OnUnmount);
            _btnForgetAll = MakeButton("清除全部记住密码", 254, btnY, 140, OnForgetAllPasswords);

            // 「创建加密盘」与「设置」靠右排列（随窗口缩放保持贴右）
            int right = ClientSize.Width - 14;
            _btnSettings = MakeButton("设置", right - 64, btnY, 64, OnSettings);
            _btnSettings.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            _btnCreate = MakeButton("创建加密盘", right - 64 - 8 - 116, btnY, 116, OnCreate);
            _btnCreate.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnCreate.Image = LoadLockIcon16();
            // ImageBeforeText 让图标与文字紧邻，避免 MiddleLeft + MiddleRight 把两者顶到按钮两端
            _btnCreate.TextImageRelation = TextImageRelation.ImageBeforeText;
            _btnCreate.ImageAlign = ContentAlignment.MiddleLeft;
            _btnCreate.TextAlign = ContentAlignment.MiddleCenter;
            _btnCreate.Padding = new Padding(4, 0, 4, 0);

            new ToolTip().SetToolTip(_btnMount, "选中列表中的加密盘则挂载并解锁；\r\n未选中任何项时可选择 vhdx/vhd 文件挂载。");

            _lblStatus = new Label
            {
                Left = 14,
                Top = btnY - 26,
                Width = ClientSize.Width - 28,
                Height = 20,
                ForeColor = Color.FromArgb(90, 90, 90),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(_lblStatus);

            Shown += delegate { RefreshList(); };
        }

        private Button MakeButton(string text, int x, int y, int width, EventHandler handler)
        {
            var btn = new Button
            {
                Text = text,
                Left = x,
                Top = y,
                Width = width,
                Height = 30,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btn.Click += handler;
            Controls.Add(btn);
            return btn;
        }

        /// <summary>读取程序内嵌图标（lock.ico），缩放到 16x16（与标题栏图标同尺寸）后用作按钮图标。</summary>
        private static Image LoadLockIcon16()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(AutoStartService.ExecutablePath);
                if (icon == null) return null;

                // ExtractAssociatedIcon 返回的是系统大图标（32x32），
                // new Icon(icon, 16, 16) 对 HICON 来源的图标不起缩放作用，必须显式缩放到 16x16。
                using (Bitmap source = icon.ToBitmap())
                {
                    var small = new Bitmap(16, 16);
                    using (Graphics g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(source, 0, 0, 16, 16);
                    }
                    return small;
                }
            }
            catch (Exception ex)
            {
                LogService.Error("加载按钮图标失败", ex);
                return null;
            }
        }

        public async void RefreshList()
        {
            var vaults = new List<VaultInfo>(_manager.Vaults);

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (VaultInfo v in vaults)
                {
                    var item = new ListViewItem(v.DisplayName);
                    item.SubItems.Add(string.IsNullOrEmpty(v.DriveLetter) ? "-" : v.DriveLetter);
                    item.SubItems.Add("查询中 ...");
                    item.SubItems.Add(v.VhdPath ?? "");
                    item.Tag = v;
                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            SetBusy(true, "正在查询各加密盘状态 ...");
            VaultState[] states = null;
            try
            {
                states = await Task.Run(delegate
                {
                    var result = new VaultState[vaults.Count];
                    for (int i = 0; i < vaults.Count; i++)
                        result[i] = _manager.GetVaultState(vaults[i]);
                    return result;
                });
            }
            catch (Exception ex)
            {
                LogService.Error("查询状态失败", ex);
            }

            if (IsDisposed) return;
            if (states != null)
            {
                for (int i = 0; i < vaults.Count && i < _list.Items.Count; i++)
                    _list.Items[i].SubItems[2].Text = VaultManager.StateText(states[i]);
            }
            SetBusy(false, vaults.Count == 0 ? "暂无加密盘，可点击「创建加密盘」新建。" : "");
        }

        private VaultInfo SelectedVault
        {
            get
            {
                if (_list.SelectedItems.Count == 0) return null;
                return _list.SelectedItems[0].Tag as VaultInfo;
            }
        }

        /// <summary>当前选中行在列表中显示的状态文本（SubItems[2]）。</summary>
        private string SelectedStateText
        {
            get
            {
                if (_list.SelectedItems.Count == 0) return "";
                ListViewItem item = _list.SelectedItems[0];
                return item.SubItems.Count > 2 ? item.SubItems[2].Text : "";
            }
        }

        /// <summary>选中盘是否已挂载（状态非「未挂载」且盘符当前被占用）。</summary>
        private bool IsSelectedMounted
        {
            get
            {
                VaultInfo v = SelectedVault;
                if (v == null) return false;
                if (string.Equals(SelectedStateText, "未挂载", StringComparison.Ordinal)) return false;
                return !string.IsNullOrEmpty(v.DriveLetter) && !DriveHelper.IsDriveFree(v.DriveLetter);
            }
        }

        private void UpdateButtons()
        {
            VaultInfo v = SelectedVault;
            bool has = v != null;
            string state = SelectedStateText;
            bool mounted = has && IsSelectedMounted;
            bool locked = string.Equals(state, "已锁定", StringComparison.Ordinal);
            bool off = string.Equals(state, "未加密", StringComparison.Ordinal);

            _btnMount.Enabled = !_busy;
            _btnLock.Enabled = has && mounted && !locked && !off && !_busy;
            _btnUnmount.Enabled = has && mounted && !_busy;
            _btnForgetAll.Enabled = !_busy;
            _btnCreate.Enabled = !_busy;
            _btnSettings.Enabled = !_busy;
        }

        /// <summary>在列表空白处或某一行上按下右键时，先把光标所在行选中（ListView 默认不会）。</summary>
        private void OnListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;

            ListViewItem item = _list.GetItemAt(e.X, e.Y);
            if (item == null)
            {
                _list.SelectedItems.Clear();
                return;
            }
            item.Selected = true;
            item.Focused = true;
        }

        private void OnListMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            VaultInfo v = SelectedVault;
            bool has = v != null && !_busy;

            bool remembered = has && !string.IsNullOrEmpty(v.ProtectedPassword);
            _miForgetPassword.Enabled = remembered;
            _miForgetPassword.Text = remembered ? "清除记住密码" : "清除记住密码（未记住）";

            // 依据列表中的状态文本置灰（避免再次执行慢查询）
            string state = has ? SelectedStateText : "";
            bool mounted = has && IsSelectedMounted;                    // 已挂载：未挂载时为 false
            bool locked = string.Equals(state, "已锁定", StringComparison.Ordinal);
            bool off = string.Equals(state, "未加密", StringComparison.Ordinal);

            // 未挂载：「修改盘符/修改密码/锁定/卸载」全部不可用
            // 已锁定：「修改密码/锁定」不可用（修改盘符、卸载仍可用）
            // 未加密：「修改密码」可用（用于重新设置密码、恢复保护）
            _miChangeDrive.Enabled = has && mounted;
            _miChangePassword.Enabled = has && (string.Equals(state, "已解锁", StringComparison.Ordinal)
                || string.Equals(state, "未加密", StringComparison.Ordinal));
            _miLock.Enabled = has && mounted && !locked && !off;
            _miUnmount.Enabled = has && mounted;

            _miRemove.Enabled = has;
            _miDelete.Enabled = has;
            _miRefresh.Enabled = !_busy;   // 刷新与选中项无关，始终可用
        }

        /// <summary>清除记住密码：清除该加密盘已记住的密码，下次挂载需要重新手动输入。</summary>
        private void OnForgetPassword(object sender, EventArgs e)
        {
            VaultInfo v = SelectedVault;
            if (v == null || _busy) return;

            if (MessageBox.Show(this,
                    "确定要清除「" + v.DisplayName + "」已记住的密码吗？\r\n\r\n" +
                    "清除后，下次挂载该加密盘需要手动输入密码。",
                    "清除记住密码", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                _manager.ForgetPassword(v);
                SetStatus("已清除「" + v.DisplayName + "」记住的密码。");
            }
            catch (Exception ex)
            {
                LogService.Error("清除记住的密码失败", ex);
                MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshList();
        }

        /// <summary>清除全部记住密码：清除列表中所有加密盘已记住的密码。</summary>
        private void OnForgetAllPasswords(object sender, EventArgs e)
        {
            if (_busy) return;

            if (MessageBox.Show(this,
                    "确定要清除列表中所有加密盘已记住的密码吗？\r\n\r\n" +
                    "清除后，下次挂载这些加密盘都需要手动输入密码。",
                    "清除全部记住密码", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                int count = _manager.ForgetAllPasswords();
                SetStatus(count > 0
                    ? "已清除 " + count + " 个加密盘记住的密码。"
                    : "没有已记住密码的加密盘。");
            }
            catch (Exception ex)
            {
                LogService.Error("清除全部记住的密码失败", ex);
                MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshList();
        }

        /// <summary>移除列表：只移除列表记录并卸载，保留加密盘文件。</summary>
        private async void OnRemoveFromList(object sender, EventArgs e)
        {
            VaultInfo v = SelectedVault;
            if (v == null || _busy) return;

            if (MessageBox.Show(this,
                    "确定要把「" + v.DisplayName + "」从列表移除吗？\r\n\r\n" +
                    "加密盘文件会保留，可稍后重新挂载；若该盘当前已挂载会先卸载。",
                    "移除列表", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SetBusy(true, "正在移除 ...");
            try
            {
                await Task.Run(() => _manager.RemoveVault(v, false));
                SetBusy(false, "已从列表移除。");
            }
            catch (Exception ex)
            {
                LogService.Error("移除列表失败", ex);
                SetBusy(false, "移除失败。");
                MessageBox.Show(this, ex.Message, "移除失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshList();
        }

        private void OnCreate(object sender, EventArgs e)
        {
            using (var f = new CreateVaultForm(_manager))
            {
                if (f.ShowDialog(this) == DialogResult.OK) RefreshList();
            }
        }

        private void OnSettings(object sender, EventArgs e)
        {
            // 已启用管理密码时，打开设置前先验证
            if (!AdminPasswordForm.Verify(_manager, this)) return;

            using (var f = new SettingsForm(_manager))
            {
                f.ShowDialog(this);
            }
        }

        private void OnRefresh(object sender, EventArgs e)
        {
            _manager.Reload();
            RefreshList();
        }

        /// <summary>从文件选择器挂载列表之外的 vhdx/vhd 文件。</summary>
        private async void AttachFile()
        {
            if (_busy) return;

            string path;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择要挂载的虚拟硬盘文件";
                dlg.Filter = "虚拟硬盘文件 (*.vhd;*.vhdx)|*.vhd;*.vhdx|所有文件 (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }

            // 已在列表中的盘走双击那条路径（可复用已记住的密码、并支持记住新密码）
            VaultInfo known = _manager.Vaults.FirstOrDefault(
                v => string.Equals(v.VhdPath, path, StringComparison.OrdinalIgnoreCase));
            if (known != null)
            {
                MountSelected(known);
                return;
            }

            PasswordInput answer = PromptPasswordDialog(this, System.IO.Path.GetFileName(path));
            if (answer == null) return;

            SetBusy(true, "正在挂载 ...");
            try
            {
                await Task.Run(() => _manager.AttachVhdFile(path, answer.Password, answer.Remember, SetStatus));
                SetBusy(false, "挂载完成。");
            }
            catch (Exception ex)
            {
                LogService.Error("挂载文件失败", ex);
                SetBusy(false, "挂载失败。");
                MessageBox.Show(this, ex.Message, "挂载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshList();
        }

        /// <summary>输入解锁密码的对话框；「记住密码」勾选状态下返回 Remember=true。取消返回 null。</summary>
        private static PasswordInput PromptPasswordDialog(IWin32Window owner, string fileName)
        {
            using (var f = new Form())
            {
                f.Text = "输入解锁密码";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ClientSize = new Size(380, 190);
                f.Font = new Font("Microsoft YaHei UI", 9F);

                var lbl = new Label
                {
                    Text = "为「" + fileName + "」输入解锁密码：",
                    Left = 16,
                    Top = 18,
                    Width = 348,
                    Height = 20
                };
                f.Controls.Add(lbl);

                var txt = new TextBox { Left = 16, Top = 46, Width = 348, UseSystemPasswordChar = true };
                f.Controls.Add(txt);

                var hint = new Label
                {
                    Text = "若该盘未加密或已解锁，可留空。",
                    Left = 16,
                    Top = 74,
                    Width = 348,
                    Height = 20,
                    ForeColor = Color.FromArgb(120, 120, 120)
                };
                f.Controls.Add(hint);

                var chk = new CheckBox
                {
                    Text = "记住密码（下次双击直接解锁）",
                    Left = 16,
                    Top = 102,
                    Width = 348,
                    Height = 22,
                    Checked = true
                };
                f.Controls.Add(chk);

                var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 192, Top = 140, Width = 80, Height = 30 };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 284, Top = 140, Width = 80, Height = 30 };
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;

                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                return new PasswordInput { Password = txt.Text ?? "", Remember = chk.Checked };
            }
        }

        /// <summary>
        /// 「挂载」按钮：列表中选中了加密盘就挂载并解锁该项；未选中任何项时
        /// 弹出文件选择器，挂载指定的 vhdx/vhd 文件。
        /// </summary>
        private void OnMount(object sender, EventArgs e)
        {
            if (_busy) return;

            VaultInfo v = SelectedVault;
            if (v != null) MountSelected(v);
            else AttachFile();
        }

        /// <summary>
        /// 双击一行 / 选中后点「挂载」：挂载并解锁该加密盘。
        /// 已记住密码则直接解锁；否则弹出密码框（可勾选记住密码）。
        /// </summary>
        private async void MountSelected(VaultInfo v)
        {
            if (v == null || _busy) return;

            VaultState state = _manager.GetVaultState(v);
            if (state == VaultState.Unlocked)
            {
                MessageBox.Show(this, "该加密盘已处于解锁状态。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SetBusy(true, "正在挂载并解锁 ...");
            try
            {
                string letter = await Task.Run(() => _manager.MountVault(v, SetStatus, true, AskPassword));
                SetBusy(false, "已挂载到 " + letter);
            }
            catch (OperationCanceledException)
            {
                SetBusy(false, "已取消挂载。");
            }
            catch (Exception ex)
            {
                LogService.Error("挂载失败", ex);
                SetBusy(false, "挂载失败。");
                MessageBox.Show(this, ex.Message, "挂载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshList();
        }

        /// <summary>供 MountVault 在工作线程上调用：切回 UI 线程弹出密码框。</summary>
        private PasswordInput AskPassword(VaultInfo vault)
        {
            if (!IsHandleCreated) return null;

            PasswordInput answer = null;
            Invoke((Action)delegate { answer = PromptPasswordDialog(this, vault.DisplayName); });
            return answer;
        }

        private async void OnLock(object sender, EventArgs e)
        {
            VaultInfo v = SelectedVault;
            if (v == null || _busy) return;

            if (MessageBox.Show(this, "确定要锁定「" + v.DisplayName + "」吗？\r\n正在使用的文件会被强制关闭。", "确认锁定",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SetBusy(true, "正在锁定 ...");
            try
            {
                await Task.Run(() => _manager.LockVault(v, true));
                SetBusy(false, "已锁定。");
            }
            catch (Exception ex)
            {
                LogService.Error("锁定失败", ex);
                SetBusy(false, "锁定失败。");
                MessageBox.Show(this, ex.Message, "锁定失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshList();
        }

        private async void OnUnmount(object sender, EventArgs e)
        {
            VaultInfo v = SelectedVault;
            if (v == null || _busy) return;

            if (MessageBox.Show(this, "确定要卸载「" + v.DisplayName + "」吗？", "确认卸载",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SetBusy(true, "正在卸载 ...");
            try
            {
                await Task.Run(() => _manager.UnmountVault(v, true));
                SetBusy(false, "已卸载。");
            }
            catch (Exception ex)
            {
                LogService.Error("卸载失败", ex);
                SetBusy(false, "卸载失败。");
                MessageBox.Show(this, ex.Message, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshList();
        }

        private async void OnDelete(object sender, EventArgs e)
        {
            VaultInfo v = SelectedVault;
            if (v == null || _busy) return;

            DialogResult r = MessageBox.Show(this,
                "确定要删除「" + v.DisplayName + "」吗？\r\n\r\n" +
                "「是」：卸载并从列表移除，同时删除加密盘文件（不可恢复）\r\n" +
                "「否」：仅从列表移除，保留加密盘文件\r\n" +
                "「取消」：放弃操作",
                "删除加密盘", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (r == DialogResult.Cancel) return;

            bool deleteFile = (r == DialogResult.Yes);

            SetBusy(true, "正在删除 ...");
            try
            {
                await Task.Run(() => _manager.RemoveVault(v, deleteFile));
                SetBusy(false, deleteFile ? "已删除。" : "已从列表移除。");
            }
            catch (Exception ex)
            {
                LogService.Error("删除失败", ex);
                SetBusy(false, "删除失败。");
                MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshList();
        }

        private async void OnChangeDrive(object sender, EventArgs e)
        {
            VaultInfo v = SelectedVault;
            if (v == null || _busy) return;

            string current = v.DriveLetter ?? "";
            string letter = PromptDriveLetter(this, v.DisplayName, current);
            if (letter == null) return;

            SetBusy(true, "正在修改盘符 ...");
            try
            {
                await Task.Run(() => _manager.ChangeDriveLetter(v, letter));
                SetBusy(false, "盘符已修改为 " + letter);
            }
            catch (Exception ex)
            {
                LogService.Error("修改盘符失败", ex);
                SetBusy(false, "修改盘符失败。");
                MessageBox.Show(this, ex.Message, "修改盘符失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshList();
        }

        private async void OnChangePassword(object sender, EventArgs e)
        {
            VaultInfo v = SelectedVault;
            if (v == null || _busy) return;

            string newPassword = PromptNewPassword(this, v.DisplayName);
            if (newPassword == null) return;

            bool clearing = newPassword.Length == 0;
            SetBusy(true, clearing ? "正在清空密码 ..." : "正在修改密码 ...");
            try
            {
                await Task.Run(() => _manager.ChangePassword(v, newPassword));
                if (clearing)
                {
                    SetBusy(false, "密码已清空。");
                    MessageBox.Show(this, "密码已清空，之后挂载该盘无需输入密码。",
                        "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    SetBusy(false, "密码已修改。");
                    MessageBox.Show(this, "密码修改成功。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                LogService.Error(clearing ? "清空密码失败" : "修改密码失败", ex);
                SetBusy(false, clearing ? "清空密码失败。" : "修改密码失败。");
                MessageBox.Show(this, ex.Message, clearing ? "清空密码失败" : "修改密码失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshList();
        }

        private static string PromptDriveLetter(IWin32Window owner, string vaultName, string currentLetter)
        {
            using (var f = new Form())
            {
                f.Text = "修改盘符";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ClientSize = new Size(320, 150);
                f.Font = new Font("Microsoft YaHei UI", 9F);

                var lbl = new Label
                {
                    Text = "为「" + vaultName + "」选择新的盘符：",
                    Left = 16,
                    Top = 18,
                    Width = 288,
                    Height = 20
                };
                f.Controls.Add(lbl);

                var cbo = new ComboBox
                {
                    Left = 16,
                    Top = 46,
                    Width = 288,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                cbo.Items.AddRange(DriveHelper.GetCandidateDrives().ToArray());
                string cur = DriveHelper.Normalize(currentLetter);
                int idx = cbo.FindStringExact(cur);
                if (idx >= 0) cbo.SelectedIndex = idx;
                else if (cbo.Items.Count > 0) cbo.SelectedIndex = 0;
                f.Controls.Add(cbo);

                var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 132, Top = 100, Width = 80, Height = 30 };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 224, Top = 100, Width = 80, Height = 30 };
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;

                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                if (cbo.SelectedItem == null) return null;

                string picked = DriveHelper.Normalize(cbo.SelectedItem.ToString());
                if (string.Equals(picked, cur, StringComparison.OrdinalIgnoreCase))
                    return null;
                return picked;
            }
        }

        private static string PromptNewPassword(IWin32Window owner, string vaultName)
        {
            using (var f = new Form())
            {
                f.Text = "修改密码";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ClientSize = new Size(340, 190);
                f.Font = new Font("Microsoft YaHei UI", 9F);

                var lblInfo = new Label
                {
                    Text = "为「" + vaultName + "」设置新密码：",
                    Left = 16,
                    Top = 16,
                    Width = 308,
                    Height = 20
                };
                f.Controls.Add(lblInfo);

                var lblNew = new Label { Text = "新密码", Left = 16, Top = 48, Width = 60, Height = 20 };
                var txtNew = new TextBox { Left = 80, Top = 45, Width = 244, UseSystemPasswordChar = true };
                var lblConfirm = new Label { Text = "确认密码", Left = 16, Top = 82, Width = 60, Height = 20 };
                var txtConfirm = new TextBox { Left = 80, Top = 79, Width = 244, UseSystemPasswordChar = true };
                var lblHint = new Label
                {
                    Text = "密码长度至少 8 位；留空表示清空密码",
                    Left = 16,
                    Top = 110,
                    Width = 308,
                    Height = 20,
                    ForeColor = Color.FromArgb(120, 120, 120)
                };
                f.Controls.Add(lblNew);
                f.Controls.Add(txtNew);
                f.Controls.Add(lblConfirm);
                f.Controls.Add(txtConfirm);
                f.Controls.Add(lblHint);

                var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 152, Top = 140, Width = 80, Height = 30 };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 244, Top = 140, Width = 80, Height = 30 };
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;

                while (true)
                {
                    if (f.ShowDialog(owner) != DialogResult.OK) return null;

                    string pwd = txtNew.Text;
                    string confirm = txtConfirm.Text;
                    if (pwd.Length == 0)
                    {
                        if (MessageBox.Show(f,
                                "新密码留空将清除「" + vaultName + "」的密码保护。\r\n\r\n" +
                                "清除后，挂载该盘不再需要输入密码（盘内数据仍保持加密，可随时重新设置密码）。\r\n\r\n" +
                                "确定要清空密码吗？",
                                "清空密码", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                            continue;
                        return "";
                    }
                    if (pwd.Length < 8)
                    {
                        MessageBox.Show(f, "密码长度至少 8 位。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    if (!string.Equals(pwd, confirm, StringComparison.Ordinal))
                    {
                        MessageBox.Show(f, "两次输入的密码不一致。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    return pwd;
                }
            }
        }

        private void SetStatus(string message)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((Action)delegate { if (!IsDisposed) _lblStatus.Text = message; });
            }
            catch { }
        }

        private void SetBusy(bool busy, string status)
        {
            _busy = busy;
            _lblStatus.Text = status ?? "";
            UseWaitCursor = busy;
            UpdateButtons();
        }
    }
}
