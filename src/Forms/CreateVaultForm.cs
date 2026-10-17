using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using BitLockerVault.Models;
using BitLockerVault.Services;

namespace BitLockerVault.Forms
{
    /// <summary>创建加密盘界面。</summary>
    internal sealed class CreateVaultForm : Form
    {
        private TextBox _txtPath;
        private Button _btnBrowse;
        private NumericUpDown _numSize;
        private ComboBox _cboUnit;
        private ComboBox _cboFormat;
        private TextBox _txtPassword;
        private TextBox _txtConfirm;
        private CheckBox _chkShowPassword;
        private Button _btnCreate;
        private Button _btnCancel;
        private Label _lblStatus;
        private ProgressBar _progress;

        private readonly VaultManager _manager;
        private bool _busy;

        public CreateVaultForm(VaultManager manager)
        {
            _manager = manager;
            InitializeUi();
        }

        private void InitializeUi()
        {
            Text = "创建加密盘";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(540, 340);
            Font = new Font("Microsoft YaHei UI", 9F);

            const int labelX = 20;
            const int ctrlX = 130;
            const int ctrlW = 380;
            int y = 24;

            // 存放位置
            Controls.Add(MakeLabel("存放位置：", labelX, y + 3));
            _txtPath = new TextBox { Left = ctrlX, Top = y, Width = ctrlW - 90 };
            Controls.Add(_txtPath);
            _btnBrowse = new Button { Text = "浏览...", Left = ctrlX + ctrlW - 82, Top = y - 2, Width = 82, Height = 26 };
            _btnBrowse.Click += OnBrowse;
            Controls.Add(_btnBrowse);
            y += 44;

            // 虚拟硬盘大小
            Controls.Add(MakeLabel("虚拟硬盘大小：", labelX, y + 3));
            _numSize = new NumericUpDown
            {
                Left = ctrlX,
                Top = y,
                Width = 200,
                Minimum = 1,
                Maximum = 1024 * 1024,
                Value = 10,
                DecimalPlaces = 0
            };
            Controls.Add(_numSize);
            _cboUnit = new ComboBox
            {
                Left = ctrlX + 212,
                Top = y,
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cboUnit.Items.AddRange(new object[] { "MB", "GB", "TB" });
            _cboUnit.SelectedIndex = 1;
            Controls.Add(_cboUnit);
            y += 44;

            // 虚拟盘格式
            Controls.Add(MakeLabel("虚拟盘格式：", labelX, y + 3));
            _cboFormat = new ComboBox
            {
                Left = ctrlX,
                Top = y,
                Width = ctrlW,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cboFormat.Items.Add("VHDX 格式动态扩展盘");
            _cboFormat.SelectedIndex = 0;
            _cboFormat.Enabled = false;
            Controls.Add(_cboFormat);
            y += 44;

            // 密码
            Controls.Add(MakeLabel("密码：", labelX, y + 3));
            _txtPassword = new TextBox { Left = ctrlX, Top = y, Width = ctrlW, UseSystemPasswordChar = true };
            Controls.Add(_txtPassword);
            y += 32;

            Controls.Add(MakeLabel("确认密码：", labelX, y + 3));
            _txtConfirm = new TextBox { Left = ctrlX, Top = y, Width = ctrlW, UseSystemPasswordChar = true };
            Controls.Add(_txtConfirm);
            y += 34;

            _chkShowPassword = new CheckBox
            {
                Text = "显示密码",
                Left = ctrlX,
                Top = y,
                Width = 120,
                AutoSize = true
            };
            _chkShowPassword.CheckedChanged += delegate
            {
                _txtPassword.UseSystemPasswordChar = !_chkShowPassword.Checked;
                _txtConfirm.UseSystemPasswordChar = !_chkShowPassword.Checked;
            };
            Controls.Add(_chkShowPassword);
            y += 34;

            // 状态与进度
            _lblStatus = new Label
            {
                Left = labelX,
                Top = y,
                Width = ctrlW + (ctrlX - labelX),
                Height = 20,
                Text = "准备就绪。",
                ForeColor = Color.FromArgb(90, 90, 90)
            };
            Controls.Add(_lblStatus);
            y += 24;

            _progress = new ProgressBar
            {
                Left = labelX,
                Top = y,
                Width = ctrlX + ctrlW - labelX,
                Height = 8,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Visible = false
            };
            Controls.Add(_progress);

            // 按钮
            _btnCreate = new Button
            {
                Text = "创建",
                Left = ctrlX + ctrlW - 176,
                Top = ClientSize.Height - 44,
                Width = 82,
                Height = 30
            };
            _btnCreate.Click += OnCreate;
            Controls.Add(_btnCreate);

            _btnCancel = new Button
            {
                Text = "取消",
                Left = ctrlX + ctrlW - 86,
                Top = ClientSize.Height - 44,
                Width = 82,
                Height = 30,
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(_btnCancel);

            AcceptButton = _btnCreate;
            CancelButton = _btnCancel;
            FormClosing += OnFormClosing;
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            return new Label { Text = text, Left = x, Top = y, Width = 110, Height = 20, AutoSize = false };
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "选择虚拟硬盘存放位置";
                dlg.Filter = "VHDX 虚拟硬盘 (*.vhdx)|*.vhdx";
                dlg.DefaultExt = "vhdx";
                dlg.AddExtension = true;
                dlg.OverwritePrompt = true;
                dlg.FileName = "加密盘.vhdx";
                try
                {
                    dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
                catch { }

                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _txtPath.Text = dlg.FileName;
            }
        }

        private long ComputeSizeBytes()
        {
            long value = (long)_numSize.Value;
            string unit = _cboUnit.SelectedItem as string ?? "GB";
            long factor = 1024L * 1024L * 1024L;
            if (unit == "MB") factor = 1024L * 1024L;
            else if (unit == "TB") factor = 1024L * 1024L * 1024L * 1024L;
            return value * factor;
        }

        private async void OnCreate(object sender, EventArgs e)
        {
            if (_busy) return;

            string path = _txtPath.Text.Trim().Trim('"');
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show(this, "请选择虚拟硬盘的存放位置。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!path.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "存放位置必须以 .vhdx 结尾。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (File.Exists(path))
            {
                MessageBox.Show(this, "该位置已存在同名文件，请更换文件名。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_txtPassword.Text.Length < 8)
            {
                MessageBox.Show(this, "密码长度至少 8 位。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_txtPassword.Text != _txtConfirm.Text)
            {
                MessageBox.Show(this, "两次输入的密码不一致。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            long sizeBytes = ComputeSizeBytes();
            if (sizeBytes < 100L * 1024L * 1024L)
            {
                MessageBox.Show(this, "虚拟硬盘大小至少 100 MB。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var request = new CreateVaultRequest
            {
                VhdPath = path,
                SizeBytes = sizeBytes,
                Password = _txtPassword.Text,
                DriveLetter = ""
            };

            SetBusy(true, "开始创建 ...");
            try
            {
                VaultInfo created = await Task.Run(() => _manager.CreateVault(request, ReportProgress));
                SetBusy(false, "创建完成，已挂载到 " + created.DriveLetter);
                MessageBox.Show(this,
                    "加密盘创建成功！\r\n\r\n文件：" + created.VhdPath + "\r\n盘符：" + created.DriveLetter,
                    "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                LogService.Error("创建加密盘失败", ex);
                SetBusy(false, "创建失败。");
                MessageBox.Show(this, ex.Message, "创建失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ReportProgress(string message)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((Action)delegate
                {
                    if (!IsDisposed) _lblStatus.Text = message;
                });
            }
            catch { }
        }

        private void SetBusy(bool busy, string status)
        {
            _busy = busy;
            _txtPath.Enabled = !busy;
            _btnBrowse.Enabled = !busy;
            _numSize.Enabled = !busy;
            _cboUnit.Enabled = !busy;
            _txtPassword.Enabled = !busy;
            _txtConfirm.Enabled = !busy;
            _btnCreate.Enabled = !busy;
            _btnCancel.Enabled = !busy;
            _progress.Visible = busy;
            if (status != null) _lblStatus.Text = status;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                MessageBox.Show(this, "正在创建加密盘，请稍候 ...", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
