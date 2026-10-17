using System;

namespace BitLockerVault.Models
{
    /// <summary>创建加密盘的请求参数。</summary>
    internal sealed class CreateVaultRequest
    {
        /// <summary>VHDX 存放位置（完整文件路径）。</summary>
        public string VhdPath { get; set; }

        /// <summary>虚拟硬盘大小（字节）。</summary>
        public long SizeBytes { get; set; }

        /// <summary>加密密码（明文，仅内存中使用）。</summary>
        public string Password { get; set; }

        /// <summary>使用的盘符，例如 "Z:"。</summary>
        public string DriveLetter { get; set; }

        public CreateVaultRequest()
        {
            VhdPath = "";
            Password = "";
            DriveLetter = "";
        }
    }
}
