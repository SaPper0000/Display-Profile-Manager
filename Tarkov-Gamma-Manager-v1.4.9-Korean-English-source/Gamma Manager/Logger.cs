using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal static class Logger
    {
        private static readonly object Sync = new object();
        private static volatile bool _enabled;

        public static bool Enabled { get { return _enabled; } }
        private static string LogDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs"); }
        }

        private static string LogPath
        {
            get { return Path.Combine(LogDirectory, "TarkovGammaManager-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log"); }
        }

        public static string DirectoryPath { get { return LogDirectory; } }

        public static void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        public static void Info(string message) { Write("INFO", message, null); }
        public static void Warn(string message) { Write("WARN", message, null); }
        public static void Error(string message, Exception ex = null) { Write("ERROR", message, ex); }

        private static void Write(string level, string message, Exception ex)
        {
            if (!_enabled) return;
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    var sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    sb.Append(" [").Append(level).Append("] ");
                    sb.Append(message ?? string.Empty);
                    if (ex != null)
                    {
                        sb.Append(" | Exception: ").Append(ex.GetType().FullName);
                        sb.Append(" | Message: ").Append(ex.Message);
                        sb.Append(" | StackTrace: ").Append(ex.StackTrace);
                    }
                    sb.AppendLine();
                    File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never become a source of application failure.
            }
        }

        public static void Initialize()
        {
            // Logging is opt-in. Read the persisted setting before any startup log is written.
            try
            {
                string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GammaManager.ini");
                string enabledText = ReadSetting(iniPath, "LogEnabled", "Settings");
                _enabled = string.Equals(enabledText, "True", StringComparison.OrdinalIgnoreCase) || enabledText == "1";
            }
            catch
            {
                _enabled = false;
            }

            if (!_enabled) return;

            Info("============================================================");
            Info("Tarkov Gamma Manager started");
            Info("Version=" + Application.ProductVersion + ", OS=" + Environment.OSVersion +
                 ", 64BitOS=" + Environment.Is64BitOperatingSystem + ", 64BitProcess=" + Environment.Is64BitProcess +
                 ", CLR=" + Environment.Version);
            Info("LogDirectory=" + LogDirectory);
        }

        private static string ReadSetting(string iniPath, string key, string section)
        {
            var value = new StringBuilder(255);
            NativeGetPrivateProfileString(section, key, "", value, value.Capacity, iniPath);
            return value.ToString();
        }

        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int NativeGetPrivateProfileString(string section, string key, string defaultValue, StringBuilder returnedString, int size, string filePath);
    }
}
