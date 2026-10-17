using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace BitLockerVault.Services
{
    /// <summary>盘符相关的辅助方法。</summary>
    internal static class DriveHelper
    {
        /// <summary>返回当前已被占用的盘符集合（形如 "C:"），含本地磁盘与网络映射盘。</summary>
        public static HashSet<string> GetUsedDrives()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 本地磁盘、光驱、可移动盘等
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    used.Add(d.Name.Substring(0, 2));
                }
                catch
                {
                    // 忽略异常盘符
                }
            }

            // 网络映射盘符：本程序以管理员身份运行，提权后的登录会话看不到当前用户映射的网络驱动器，
            // DriveInfo 会漏掉它们，导致创建/修改盘符时与网络盘冲突。
            // 改从当前用户的注册表 HKCU\Network 读取映射记录（每个子键名即一个盘符字母）。
            AddMappedNetworkDrives(used);

            return used;
        }

        /// <summary>把当前用户已映射的网络驱动器盘符并入 used 集合。</summary>
        private static void AddMappedNetworkDrives(HashSet<string> used)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Network"))
                {
                    if (key == null) return;
                    foreach (string name in key.GetSubKeyNames())
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        char c = char.ToUpperInvariant(name[0]);
                        if (c >= 'A' && c <= 'Z') used.Add(c + ":");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("读取网络映射盘符失败：" + ex.Message);
            }
        }

        public static bool IsDriveFree(string letter)
        {
            if (string.IsNullOrEmpty(letter) || letter.Length < 2) return false;
            return !GetUsedDrives().Contains(letter.Substring(0, 2));
        }

        /// <summary>从 preferred 开始向后查找一个空闲盘符（Z → A）。</summary>
        public static string FindFreeDrive(string preferred)
        {
            var used = GetUsedDrives();
            char start = 'Z';
            if (!string.IsNullOrEmpty(preferred) && preferred.Length >= 1)
            {
                char c = char.ToUpperInvariant(preferred[0]);
                if (c >= 'D' && c <= 'Z') start = c;
            }

            for (char c = start; c >= 'D'; c--)
            {
                string letter = c + ":";
                if (!used.Contains(letter)) return letter;
            }
            // 从 Z 再扫一遍（避免起点后移时漏掉更靠后的盘符）
            for (char c = 'Z'; c >= 'D'; c--)
            {
                string letter = c + ":";
                if (!used.Contains(letter)) return letter;
            }
            return "";
        }

        /// <summary>返回所有可用作挂载点的候选盘符（D: ~ Z:）。</summary>
        public static List<string> GetCandidateDrives()
        {
            var used = GetUsedDrives();
            var list = new List<string>();
            for (char c = 'Z'; c >= 'D'; c--)
            {
                string letter = c + ":";
                if (!used.Contains(letter)) list.Add(letter);
            }
            return list;
        }

        /// <summary>把盘符显示文本（可能带 "  (已占用)" 后缀）还原为纯盘符。</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string t = text.Trim();
            if (t.Length >= 2) return t.Substring(0, 2).ToUpperInvariant();
            return t.ToUpperInvariant();
        }
    }
}
