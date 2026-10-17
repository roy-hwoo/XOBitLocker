using System;
using System.Collections.Generic;
using System.Management;

namespace BitLockerVault.Services
{
    /// <summary>
    /// 卷活动监测：读取各逻辑卷的「累计读写字节数」
    /// （WMI 类 Win32_PerfRawData_PerfDisk_LogicalDisk）。两次采样之间累计值没有变化，
    /// 就说明这段时间内该卷没有任何读写操作。用累计值而不是每秒速率，短暂的 I/O 也不会漏掉。
    /// WMI 的类名与属性名不受系统语言影响，比 PerformanceCounter 的本地化名称更可靠。
    /// </summary>
    internal sealed class VolumeActivityMonitor
    {
        private const string Wql =
            "SELECT Name, DiskReadBytesPerSec, DiskWriteBytesPerSec " +
            "FROM Win32_PerfRawData_PerfDisk_LogicalDisk";

        /// <summary>盘符字母（如 "Z"）-> 上次采样到的累计读写字节数。</summary>
        private readonly Dictionary<string, long> _lastTotals =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 采样一次。返回 盘符字母（如 "Z"）-> 自上次采样以来是否发生过读写；
        /// 首次采样的卷一律为 false（只建立基线）。WMI 不可用时返回 null。
        /// </summary>
        public Dictionary<string, bool> Poll()
        {
            try
            {
                var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                using (var searcher = new ManagementObjectSearcher(Wql))
                using (ManagementObjectCollection col = searcher.Get())
                {
                    foreach (ManagementBaseObject mo in col)
                    {
                        using (mo)
                        {
                            string letter = LetterOf(Convert.ToString(mo["Name"]));
                            if (letter == null) continue;
                            totals[letter] = ToInt64(mo["DiskReadBytesPerSec"]) + ToInt64(mo["DiskWriteBytesPerSec"]);
                        }
                    }
                }
                if (totals.Count == 0) return null;

                var activity = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, long> kv in totals)
                {
                    long last;
                    activity[kv.Key] = _lastTotals.TryGetValue(kv.Key, out last) && last != kv.Value;
                }

                _lastTotals.Clear();
                foreach (KeyValuePair<string, long> kv in totals) _lastTotals[kv.Key] = kv.Value;
                return activity;
            }
            catch (Exception ex)
            {
                LogService.Warn("读取卷 I/O 计数器失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>把 WMI 实例名（形如 "Z:"）转成盘符字母（"Z"）；非盘符实例返回 null。</summary>
        private static string LetterOf(string instanceName)
        {
            if (string.IsNullOrEmpty(instanceName)) return null;
            string n = instanceName.Trim();
            if (n.Length != 2 || n[1] != ':') return null;

            char c = char.ToUpperInvariant(n[0]);
            if (c < 'A' || c > 'Z') return null;
            return c.ToString();
        }

        private static long ToInt64(object value)
        {
            if (value == null) return 0L;
            try { return Convert.ToInt64(value); }
            catch { return 0L; }
        }
    }
}
