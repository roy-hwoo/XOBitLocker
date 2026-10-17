using System;
using System.Security.Cryptography;
using System.Text;

namespace BitLockerVault.Services
{
    /// <summary>使用 Windows DPAPI（当前用户作用域）加密/解密密码。</summary>
    internal static class SecretService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BitLockerVault.DPAPI.v1");

        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            byte[] data = Encoding.UTF8.GetBytes(plainText);
            byte[] enc = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }

        public static string Unprotect(string protectedText)
        {
            if (string.IsNullOrEmpty(protectedText)) return "";
            try
            {
                byte[] enc = Convert.FromBase64String(protectedText);
                byte[] data = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception ex)
            {
                LogService.Error("密码解密失败（可能由其他用户账户生成）", ex);
                return "";
            }
        }
    }
}
