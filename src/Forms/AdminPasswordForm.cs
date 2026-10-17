using System;
using System.Drawing;
using System.Windows.Forms;
using BitLockerVault.Services;

namespace BitLockerVault.Forms
{
    /// <summary>管理密码对话框：校验已设置的管理密码，以及设置新的管理密码。</summary>
    internal sealed class AdminPasswordForm : Form
    {
        private readonly VaultManager _manager;
        private TextBox _txt;
        private Label _lblError;

        private AdminPasswordForm(VaultManager manager)
        {
            _manager = manager;
            InitializeUi();
        }

        private void InitializeUi()
        {
            Text = "管理密码";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(360, 158);
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(new Label { Text = "请输入管理密码：", Left = 16, Top = 18, Width = 328, Height = 20 });

            _txt = new TextBox { Left = 16, Top = 46, Width = 328, UseSystemPasswordChar = true };
            Controls.Add(_txt);

            _lblError = new Label
            {
                Text = "",
                Left = 16,
                Top = 74,
                Width = 328,
                Height = 20,
                ForeColor = Color.FromArgb(200, 60, 60)
            };
            Controls.Add(_lblError);

            var ok = new Button { Text = "确定", Left = 172, Top = 108, Width = 80, Height = 30 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 264, Top = 108, Width = 80, Height = 30 };
            ok.Click += OnOk;
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (_manager.VerifyAdminPassword(_txt.Text))
            {
                DialogResult = DialogResult.OK;
                return;
            }
            _lblError.Text = "密码错误，请重新输入。";
            _txt.SelectAll();
            _txt.Focus();
        }

        /// <summary>
        /// 若已启用管理密码则弹出校验框：通过返回 true，取消或密码错误后放弃返回 false。
        /// 未启用管理密码时直接返回 true。
        /// </summary>
        internal static bool Verify(VaultManager manager, IWin32Window owner)
        {
            if (manager == null || !manager.IsAdminPasswordEnabled) return true;

            using (var f = new AdminPasswordForm(manager))
            {
                return f.ShowDialog(owner) == DialogResult.OK;
            }
        }

        /// <summary>输入新的管理密码（两次确认）。取消返回 null。</summary>
        internal static string PromptNew(IWin32Window owner)
        {
            using (var f = new Form())
            {
                f.Text = "设置管理密码";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ClientSize = new Size(340, 190);
                f.Font = new Font("Microsoft YaHei UI", 9F);

                f.Controls.Add(new Label { Text = "新密码", Left = 16, Top = 48, Width = 60, Height = 20 });
                var txtNew = new TextBox { Left = 80, Top = 45, Width = 244, UseSystemPasswordChar = true };
                f.Controls.Add(txtNew);

                f.Controls.Add(new Label { Text = "确认密码", Left = 16, Top = 82, Width = 60, Height = 20 });
                var txtConfirm = new TextBox { Left = 80, Top = 79, Width = 244, UseSystemPasswordChar = true };
                f.Controls.Add(txtConfirm);

                f.Controls.Add(new Label
                {
                    Text = "密码长度至少 6 位。",
                    Left = 16,
                    Top = 110,
                    Width = 308,
                    Height = 20,
                    ForeColor = Color.FromArgb(120, 120, 120)
                });

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
                    if (pwd.Length < 6)
                    {
                        MessageBox.Show(f, "密码长度至少 6 位。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    if (!string.Equals(pwd, txtConfirm.Text, StringComparison.Ordinal))
                    {
                        MessageBox.Show(f, "两次输入的密码不一致。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    return pwd;
                }
            }
        }
    }
}
