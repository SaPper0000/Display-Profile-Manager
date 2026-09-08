using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal static class Logger
    {
        private static readonly object Sync = new object();
        private static volatile bool _enabled;

        // 로그 보관 정책 설정
        private const int MaxLogRetentionDays = 7;      // 보관 기간: 7일
        private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 단일 파일 최대 크기: 10MB
        private const int MaxDailyLogFiles = 10;        // 👈 하루 최대 생성 파일 수 (10개 x 10MB = 최대 100MB)

        private static string _cachedDate;
        private static string _cachedLogPath;
        private static long _cachedFileLength;

        public static bool Enabled => _enabled;
        private static string LogDirectory => AppPaths.LogsDirectory;

        public static string DirectoryPath => LogDirectory;

        public static void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        public static void Info(string message) => Write("INFO", message, null);
        public static void Warn(string message) => Write("WARN", message, null);
        public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private static string GetCurrentLogPath(int appendBytes)
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_cachedDate == today && _cachedLogPath != null && (_cachedFileLength + appendBytes) < MaxFileSizeBytes)
            {
                _cachedFileLength += appendBytes;
                return _cachedLogPath;
            }

            _cachedDate = today;
            string baseName = "DisplayProfileManager-" + today;
            string primaryPath = Path.Combine(LogDirectory, $"{baseName}.log");

            string selectedPath = primaryPath;
            long fileLength = 0;

            if (File.Exists(primaryPath))
            {
                fileLength = new FileInfo(primaryPath).Length;
                if (fileLength >= MaxFileSizeBytes)
                {
                    selectedPath = Path.Combine(LogDirectory, $"{baseName}_{MaxDailyLogFiles - 1}.log");
                    for (int index = 1; index < MaxDailyLogFiles; index++)
                    {
                        string indexedPath = Path.Combine(LogDirectory, $"{baseName}_{index}.log");
                        if (!File.Exists(indexedPath))
                        {
                            selectedPath = indexedPath;
                            fileLength = 0;
                            break;
                        }
                        long len = new FileInfo(indexedPath).Length;
                        if (len < MaxFileSizeBytes)
                        {
                            selectedPath = indexedPath;
                            fileLength = len;
                            break;
                        }
                    }
                }
            }

            _cachedLogPath = selectedPath;
            _cachedFileLength = fileLength + appendBytes;
            return selectedPath;
        }

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
                        if (ex.InnerException != null)
                        {
                            sb.Append(" | InnerException: ").Append(ex.InnerException.Message);
                        }
                        sb.Append(" | StackTrace: ").Append(ex.StackTrace);
                    }
                    sb.AppendLine();

                    string logText = sb.ToString();
                    byte[] bytes = Encoding.UTF8.GetBytes(logText);
                    string targetPath = GetCurrentLogPath(bytes.Length);
                    File.AppendAllText(targetPath, logText, Encoding.UTF8);
                }
            }
            catch
            {
                // 로깅 실패가 본 프로그램의 동작을 중단시키지 않도록 예외 무시
            }
        }

        public static void Initialize()
        {
            try
            {
                string iniPath = AppPaths.ConfigFile("GammaManager.ini");
                string enabledText = ReadSetting(iniPath, "LogEnabled", "Settings");
                _enabled = string.Equals(enabledText, "True", StringComparison.OrdinalIgnoreCase) || enabledText == "1";
            }
            catch
            {
                _enabled = false;
            }

            // 로그 보관 정책에 따른 이전 파일 정리 (비동기 백그라운드 작업)
            CleanupOldLogsAsync();

            if (!_enabled) return;

            Info("============================================================");
            Info("Display Profile Manager started");
            Info("Version=" + Application.ProductVersion + ", OS=" + Environment.OSVersion +
                 ", 64BitOS=" + Environment.Is64BitOperatingSystem + ", 64BitProcess=" + Environment.Is64BitProcess +
                 ", CLR=" + Environment.Version);
            Info("LogDirectory=" + LogDirectory);
        }

        private static void CleanupOldLogsAsync()
        {
            Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(LogDirectory)) return;

                    var directoryInfo = new DirectoryInfo(LogDirectory);
                    DateTime thresholdDate = DateTime.Now.AddDays(-MaxLogRetentionDays);

                    // .log 확장자 파일 중 보관 기간(7일)이 지난 파일 삭제
                    var oldFiles = directoryInfo.GetFiles("*.log")
                                                .Where(f => f.LastWriteTime < thresholdDate);

                    foreach (var file in oldFiles)
                    {
                        try
                        {
                            file.Delete();
                        }
                        catch
                        {
                            // 다른 프로세스가 점유 중일 경우 건너뜀
                        }
                    }
                }
                catch
                {
                    // 정리 도중 발생하는 예외 안전 처리
                }
            });
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