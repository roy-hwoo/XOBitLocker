using System;

namespace BitLockerVault.Models
{
    /// <summary>一个加密盘的描述信息。</summary>
    internal sealed class VaultInfo
    {
        public VaultInfo()
        {
            Id = Guid.NewGuid().ToString("N");
        }

        /// <summary>唯一标识。</summary>
        public string Id { get; set; }

        /// <summary>名称（即保存的 VHDX 文件名）。</summary>
        public string Name { get; set; }

        /// <summary>VHDX 文件完整路径。</summary>
        public string VhdPath { get; set; }

        /// <summary>界面显示名称：优先展示保存的 VHDX 文件名（含扩展名）。</summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(VhdPath))
                {
                    try
                    {
                        string file = System.IO.Path.GetFileName(VhdPath);
                        if (!string.IsNullOrEmpty(file)) return file;
                    }
                    catch { }
                }
                return Name ?? "";
            }
        }

        /// <summary>下拉框等控件默认显示文件名，而不是类名。</summary>
        public override string ToString()
        {
            return DisplayName;
        }

        /// <summary>虚拟硬盘最大容量（字节）。</summary>
        public long SizeBytes { get; set; }

        /// <summary>该盘使用的盘符，例如 "Z:"。</summary>
        public string DriveLetter { get; set; }

        /// <summary>DPAPI 加密后的密码（Base64）。</summary>
        public string ProtectedPassword { get; set; }

        /// <summary>是否随开机自动挂载。</summary>
        public bool AutoMount { get; set; }
    }
}
