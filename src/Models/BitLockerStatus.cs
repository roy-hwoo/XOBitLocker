namespace BitLockerVault.Models
{
    /// <summary>加密盘的运行状态。</summary>
    internal enum VaultState
    {
        /// <summary>状态未知 / 查询失败。</summary>
        Unknown,
        /// <summary>VHDX 未挂载。</summary>
        NotMounted,
        /// <summary>已挂载，但尚未启用 BitLocker。</summary>
        Off,
        /// <summary>已挂载且已锁定。</summary>
        Locked,
        /// <summary>已挂载且已解锁，可正常读写。</summary>
        Unlocked,
        /// <summary>正在加密。</summary>
        Encrypting,
        /// <summary>正在解密。</summary>
        Decrypting,
        /// <summary>保护已挂起。</summary>
        Suspended
    }

    /// <summary>BitLocker 卷原始状态快照。</summary>
    internal sealed class VolumeStatus
    {
        public string MountPoint { get; set; }
        public string ProtectionStatus { get; set; }
        public string LockStatus { get; set; }
        /// <summary>对应 Get-BitLockerVolume 的 VolumeStatus 字段。</summary>
        public string VolumeStateText { get; set; }
        public int EncryptionPercentage { get; set; }

        public VolumeStatus()
        {
            MountPoint = "";
            ProtectionStatus = "";
            LockStatus = "";
            VolumeStateText = "";
            EncryptionPercentage = 0;
        }
    }
}
