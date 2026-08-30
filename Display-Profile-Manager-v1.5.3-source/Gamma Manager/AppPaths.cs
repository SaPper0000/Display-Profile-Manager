using System;
using System.IO;

namespace Gamma_Manager
{
    internal static class AppPaths
    {
        private const string AppFolderName = "DisplayProfileManager";
        private const string LegacyAppFolderName = "TarkovGammaManager";

        private static readonly object MigrationLock = new object();
        private static bool migrationChecked;

        public static string Root
        {
            get
            {
                string localAppData = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

                string newPath = Path.Combine(localAppData, AppFolderName);
                EnsureLegacyDataMigrated(localAppData, newPath);

                Directory.CreateDirectory(newPath);
                return newPath;
            }
        }

        public static string ConfigDirectory { get { return Ensure("config"); } }
        public static string StateDirectory { get { return Ensure("state"); } }
        public static string LogsDirectory { get { return Ensure("logs"); } }
        public static string BackupDirectory { get { return Ensure("backup"); } }
        public static string ImagesDirectory { get { return Ensure("images"); } }

        public static string ConfigFile(string name)
        {
            return Path.Combine(ConfigDirectory, name);
        }

        public static string StateFile(string name)
        {
            return Path.Combine(StateDirectory, name);
        }

        private static string Ensure(string name)
        {
            string path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void EnsureLegacyDataMigrated(
            string localAppData,
            string newPath)
        {
            lock (MigrationLock)
            {
                if (migrationChecked)
                    return;

                migrationChecked = true;

                try
                {
                    string legacyPath = Path.Combine(
                        localAppData,
                        LegacyAppFolderName);

                    if (!Directory.Exists(legacyPath))
                        return;

                    if (Directory.Exists(newPath))
                    {
                        string[] existingFiles = Directory.GetFiles(
                            newPath,
                            "*",
                            SearchOption.AllDirectories);

                        if (existingFiles.Length > 0)
                            return;
                    }

                    CopyDirectory(legacyPath, newPath);
                }
                catch
                {
                    // 설정 이전에 실패해도 프로그램 실행은 계속한다.
                    // 새 설정 폴더는 Root에서 생성된다.
                }
            }
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (string filePath in Directory.GetFiles(sourceDirectory))
            {
                string destinationFile = Path.Combine(
                    destinationDirectory,
                    Path.GetFileName(filePath));

                File.Copy(filePath, destinationFile, true);
            }

            foreach (string sourceSubDirectory in Directory.GetDirectories(sourceDirectory))
            {
                string destinationSubDirectory = Path.Combine(
                    destinationDirectory,
                    Path.GetFileName(sourceSubDirectory));

                CopyDirectory(sourceSubDirectory, destinationSubDirectory);
            }
        }
    }
}