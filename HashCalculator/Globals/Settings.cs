﻿using System;
using System.IO;
using System.Windows;
using System.Xml.Serialization;

namespace HashCalculator
{
    internal static class Settings
    {
        private static readonly XmlSerializer serializer =
            new XmlSerializer(typeof(SettingsViewModel));
        private static readonly string configBaseDataPath =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static readonly DirectoryInfo ConfigDir =
            new DirectoryInfo(Path.Combine(configBaseDataPath, "HashCalculator"));
        private static readonly string configFile = Path.Combine(ConfigDir.FullName, "settings.xml");
        public static readonly string libDir = Path.Combine(ConfigDir.FullName, "Library");

        public static string[] StartupArgs { get; set; }

        public static SettingsViewModel Current { get; private set; }
            = new SettingsViewModel();

        public static bool SaveSettings()
        {
            try
            {
                if (!ConfigDir.Exists)
                {
                    ConfigDir.Create();
                }
                using (FileStream fs = File.Create(configFile))
                {
                    serializer.Serialize(fs, Current);
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置保存失败：\n{ex.Message}", "错误");
                return false;
            }
        }

        /// <summary>
        /// 只有在窗口加载前调用才有效<br/>
        /// 因为部分窗口 xaml 内以 Static 方式绑定 Settings.Current
        /// </summary>
        public static bool LoadSettings()
        {
            bool executeResult = false;
            if (File.Exists(configFile))
            {
                try
                {
                    using (FileStream fs = File.OpenRead(configFile))
                    {
                        if (serializer.Deserialize(fs) is SettingsViewModel model)
                        {
                            Current = model;
                            executeResult = true;
                        }
                    }
                }
                catch (Exception) { }
            }
            return executeResult;
        }

        public static void SetProcessEnvVar()
        {
            Environment.SetEnvironmentVariable("PATH", libDir);
        }

        private static string ExtractFile(string fname, bool force, bool dll = true)
        {
            string userDllPath = Path.Combine(libDir, fname);
            if (force || !File.Exists(userDllPath))
            {
                try
                {
                    if (!Directory.Exists(libDir))
                    {
                        Directory.CreateDirectory(libDir);
                    }
                    string resPath;
                    if (!dll)
                    {
                        resPath = string.Format("HashCalculator.HashAlgos.AlgoDlls.{0}", fname);
                    }
                    else
                    {
                        resPath = string.Format(
                            "HashCalculator.HashAlgos.AlgoDlls.{0}_{1}.dll",
                            Path.GetFileNameWithoutExtension(fname),
                            Environment.Is64BitProcess ? "x64" : "x86");
                    }
                    if (AppLoading.ExecutingAsmb.GetManifestResourceStream(resPath) is Stream stream)
                    {
                        using (stream)
                        {
                            byte[] dllBuffer = new byte[stream.Length];
                            stream.Read(dllBuffer, 0, dllBuffer.Length);
                            using (FileStream fs = File.OpenWrite(userDllPath))
                            {
                                fs.Write(dllBuffer, 0, dllBuffer.Length);
                            }
                        }
                    }
                    else
                    {
                        return $"内嵌资源不存在： {resPath}";
                    }
                }
                catch (Exception exception) { return exception.Message; }
            }
            return string.Empty;
        }

        public static string ExtractBlake3Dll(bool force)
        {
            return ExtractFile(Embedded.Blake3, Current.PreviousVer != Info.Ver || force);
        }

        private static string ExtractBridgeDll(bool force)
        {
            return ExtractFile(Embedded.Hashes, Current.PreviousVer != Info.Ver || force);
        }

        public static string ExtractEmbeddedAlgoDlls(bool force)
        {
            string message = "\n".Join(
                ExtractFile(Embedded.Readme, Current.PreviousVer != Info.Ver || force, false),
                ExtractBlake3Dll(force),
                ExtractBridgeDll(force));
            if (string.IsNullOrEmpty(message))
            {
                Current.PreviousVer = Info.Ver;
            }
            return message;
        }
    }
}
