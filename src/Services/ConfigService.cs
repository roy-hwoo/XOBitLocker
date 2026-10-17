using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using BitLockerVault.Models;

namespace BitLockerVault.Services
{
    /// <summary>配置读写：%AppData%\XOBitLocker\config.json。</summary>
    internal static class ConfigService
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XOBitLocker");

        private static readonly string FilePath = Path.Combine(Dir, "config.json");

        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static string ConfigDirectory { get { return Dir; } }

        public static string ConfigFilePath { get { return FilePath; } }

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppConfig();
                string json = File.ReadAllText(FilePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return new AppConfig();

                AppConfig cfg = Serializer.Deserialize<AppConfig>(json);
                if (cfg == null) cfg = new AppConfig();
                if (cfg.Vaults == null) cfg.Vaults = new List<VaultInfo>();
                if (cfg.MountDrive == null) cfg.MountDrive = "Z:";
                return cfg;
            }
            catch (Exception ex)
            {
                LogService.Error("读取配置失败，使用默认配置", ex);
                return new AppConfig();
            }
        }

        public static void Save(AppConfig config)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string json = Serializer.Serialize(config);
                File.WriteAllText(FilePath, json, new UTF8Encoding(false));
                LogService.Info("配置已保存：" + FilePath);
            }
            catch (Exception ex)
            {
                LogService.Error("保存配置失败", ex);
                throw;
            }
        }
    }
}
