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

        public static bool Enabled => _enabled;
        private static string LogDirectory => AppPaths.LogsDirectory;

        private static string BaseLogName => "TarkovGammaManager-" + DateTime.Now.ToString("yyyy-MM-dd");

        public static string DirectoryPath => LogDirectory;

        public static void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        public static void Info(string message) => Write("INFO", message, null);
        public static void Warn(string message) => Write("WARN", message, null);
        public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private static string GetCurrentLogPath()
        {
            string primaryPath = Path.Combine(LogDirectory, $"{BaseLogName}.log");

            // 기본 파일이 최대 크기를 초과하지 않았다면 그대로 사용
            if (!File.Exists(primaryPath) || new FileInfo(primaryPath).Length < MaxFileSizeBytes)
            {
                return primaryPath;
            }

            // 용량 초과 시 인덱스 넘버링(_1, _2 ...) 파일 탐색
            int index = 1;
            while (true)
            {
                string indexedPath = Path.Combine(LogDirectory, $"{BaseLogName}_{index}.log");
                if (!File.Exists(indexedPath) || new FileInfo(indexedPath).Length < MaxFileSizeBytes)
                {
                    return indexedPath;
                }
                index++;
            }
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
                        sb.Append(" | StackTrace: ").Append(ex.StackTrace);
                    }
                    sb.AppendLine();

                    string targetPath = GetCurrentLogPath();
                    File.AppendAllText(targetPath, sb.ToString(), Encoding.UTF8);
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
            Info("Tarkov Gamma Manager started");
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