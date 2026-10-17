using System;
using System.Collections.Generic;

namespace BitLockerVault.Models
{
    /// <summary>应用全局配置，保存在 %AppData%\XOBitLocker\config.json。</summary>
    internal sealed class AppConfig
    {
        /// <summary>默认挂载盘符，例如 "Z:"。</summary>
        public string MountDrive { get; set; }

        /// <summary>自动锁定时间（分钟），0 表示不自动锁定。</summary>
        public int AutoLockMinutes { get; set; }

        /// <summary>自动锁定时是否强制卸载卷（即使有程序占用该盘也能锁上）。</summary>
        public bool AutoLockForceDismount { get; set; }

        /// <summary>是否开机自启（登录时以最高权限计划任务启动）。</summary>
        public bool AutoStart { get; set; }

        /// <summary>开机自动挂载时是否用已记住的密码自动解密；false 表示只挂载、保持锁定状态。</summary>
        public bool AutoUnlockOnAutoMount { get; set; }

        /// <summary>管理密码（DPAPI 加密）。为空表示未启用：打开管理界面/设置无需输入密码。</summary>
        public string ProtectedAdminPassword { get; set; }

        /// <summary>已创建的加密盘列表。</summary>
        public List<VaultInfo> Vaults { get; set; }

        public AppConfig()
        {
            MountDrive = "Z:";
            AutoLockMinutes = 0;
            AutoLockForceDismount = false; // 默认不强制卸载
            AutoStart = true;              // 默认开启开机自启
            AutoUnlockOnAutoMount = false; // 默认不自动解密
            ProtectedAdminPassword = "";
            Vaults = new List<VaultInfo>();
        }
    }
}
