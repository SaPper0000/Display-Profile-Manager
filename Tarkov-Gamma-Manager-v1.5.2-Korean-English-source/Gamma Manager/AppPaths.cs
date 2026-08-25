using System;
using System.IO;

namespace Gamma_Manager
{
    internal static class AppPaths
    {
        private const string AppFolderName = "TarkovGammaManager";

        public static string Root
        {
            get
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppFolderName);
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string ConfigDirectory { get { return Ensure("config"); } }
        public static string StateDirectory { get { return Ensure("state"); } }
        public static string LogsDirectory { get { return Ensure("logs"); } }
        public static string BackupDirectory { get { return Ensure("backup"); } }

        public static string ConfigFile(string name) { return Path.Combine(ConfigDirectory, name); }
        public static string StateFile(string name) { return Path.Combine(StateDirectory, name); }

        private static string Ensure(string name)
        {
            string path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
