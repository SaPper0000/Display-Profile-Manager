using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gamma_Manager
{
    internal class IniFile
    {
        // 프로그램 전체에서 공유하는 단일 인스턴스 (Lazy Thread-Safe Singleton)
        private static readonly Lazy<IniFile> _shared = new Lazy<IniFile>(() => new IniFile());
        public static IniFile Shared
        {
            get { return _shared.Value; }
        }

        private readonly string _filePath;
        private const string DefaultConfigFileName = "GammaManager.ini";
        private readonly string EXE = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;

        private readonly Dictionary<string, Dictionary<string, string>> cache = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly object fileLock = new object();
        private bool isDirty = false;
        private bool isSaveQueued = false;

        public IniFile(string IniPath = null)
        {
            if (string.IsNullOrEmpty(IniPath))
            {
                IniPath = AppPaths.ConfigFile(DefaultConfigFileName);
                MigrateLegacyIniIfNeeded(IniPath);
            }

            _filePath = new FileInfo(IniPath).FullName;
            string directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            LoadFromDisk();
        }

        public string FilePath { get { return _filePath; } }

        /// <summary>
        /// %LOCALAPPDATA%의 GammaManager.ini가 없는 경우, 
        /// 실행 폴더의 로컬 INI나 구버전 버전별 INI(Tarkov-Gamma-Manager-*.ini)를 찾아 자동으로 마이그레이션합니다.
        /// </summary>
        private static void MigrateLegacyIniIfNeeded(string targetPath)
        {
            try
            {
                if (File.Exists(targetPath)) return;

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                // 1순위: 로컬 실행 폴더의 GammaManager.ini
                string localDefault = Path.Combine(baseDir, DefaultConfigFileName);
                if (File.Exists(localDefault))
                {
                    File.Copy(localDefault, targetPath, true);
                    Logger.Info($"Migrated local INI [{localDefault}] to AppData [{targetPath}]");
                    return;
                }

                // 2순위: 구버전 버전 명칭 INI 파일들 (Tarkov-Gamma-Manager-*.ini)
                if (Directory.Exists(baseDir))
                {
                    var iniFiles = Directory.GetFiles(baseDir, "*.ini", SearchOption.TopDirectoryOnly);
                    var legacyCandidates = iniFiles
                        .Where(f => Path.GetFileName(f).StartsWith("Tarkov-Gamma-Manager-", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                        .ToList();

                    if (legacyCandidates.Count > 0)
                    {
                        string newestLegacy = legacyCandidates[0];
                        File.Copy(newestLegacy, targetPath, true);
                        Logger.Info($"Migrated versioned legacy INI [{newestLegacy}] to AppData [{targetPath}]");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to migrate legacy INI file: " + ex.Message);
            }
        }

        private void LoadFromDisk()
        {
            lock (fileLock)
            {
                cache.Clear();
                if (!File.Exists(_filePath)) return;

                string currentSection = EXE;
                string[] lines = File.ReadAllLines(_filePath, Encoding.UTF8);

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentSection = line.Substring(1, line.Length - 2).Trim();
                        if (!cache.ContainsKey(currentSection))
                            cache[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        continue;
                    }

                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex > 0)
                    {
                        string key = line.Substring(0, separatorIndex).Trim();
                        string value = line.Substring(separatorIndex + 1).Trim();

                        if (!cache.ContainsKey(currentSection))
                            cache[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        cache[currentSection][key] = value;
                    }
                }
                isDirty = false;
            }
        }

        public void ReorderSections(IEnumerable<string> orderedSections)
        {
            lock (fileLock)
            {
                var newCache = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var sec in orderedSections)
                {
                    if (cache.TryGetValue(sec, out var data)) newCache[sec] = data;
                }
                foreach (var kvp in cache)
                {
                    if (!newCache.ContainsKey(kvp.Key)) newCache[kvp.Key] = kvp.Value;
                }
                cache.Clear();
                foreach (var kvp in newCache) cache[kvp.Key] = kvp.Value;

                isDirty = true;
                QueueSave();
            }
        }

        private void SaveToDisk()
        {
            lock (fileLock)
            {
                if (!isDirty) return;

                List<string> lines = new List<string>();
                foreach (var section in cache)
                {
                    lines.Add($"[{section.Key}]");
                    foreach (var kvp in section.Value)
                    {
                        lines.Add($"{kvp.Key}={kvp.Value}");
                    }
                    lines.Add("");
                }

                string tempPath = _filePath + ".tmp";
                string backupPath = _filePath + ".bak";

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        File.WriteAllLines(tempPath, lines, Encoding.UTF8);

                        if (File.Exists(_filePath))
                        {
                            if (File.Exists(backupPath)) File.Delete(backupPath);
                            File.Replace(tempPath, _filePath, backupPath);
                        }
                        else
                        {
                            File.Move(tempPath, _filePath);
                        }
                        isDirty = false;
                        break; // 저장 성공 시 루프 탈출
                    }
                    catch (Exception ex)
                    {
                        if (attempt == 2)
                        {
                            Logger.Error("Failed to atomically save INI file to disk after 3 attempts.", ex);
                        }
                        else
                        {
                            System.Threading.Thread.Sleep(30); // 백신/OS 잠금 해제 대기
                        }
                    }
                }
            }
        }

        private void QueueSave()
        {
            lock (fileLock)
            {
                if (isSaveQueued) return;
                isSaveQueued = true;
            }

            Task.Run(async () =>
            {
                await Task.Delay(50);
                lock (fileLock)
                {
                    isSaveQueued = false;
                }
                SaveToDisk();
            });
        }

        public string Read(string Key, string Section = null)
        {
            lock (fileLock)
            {
                Section = Section ?? EXE;
                if (cache.TryGetValue(Section, out var sectionDict))
                {
                    if (sectionDict.TryGetValue(Key, out string value)) return value;
                }
                return "";
            }
        }

        /// <summary>
        /// 지정된 키와 값을 INI 메모리 캐시에 작성하고 비동기 저장 큐에 등록합니다.
        /// Fail-Fast 원칙에 따라 Key나 Value가 null일 경우 ArgumentNullException을 발생시킵니다.
        /// </summary>
        public void Write(string Key, string Value, string Section = null)
        {
            if (Key == null) throw new ArgumentNullException(nameof(Key), "Key cannot be null in Write method.");
            if (Value == null) throw new ArgumentNullException(nameof(Value), "Value cannot be null in Write method. Use DeleteKey to remove a key.");

            lock (fileLock)
            {
                Section = Section ?? EXE;
                if (!cache.ContainsKey(Section)) cache[Section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!cache[Section].TryGetValue(Key, out string currentValue) || currentValue != Value)
                {
                    cache[Section][Key] = Value;
                    isDirty = true;
                    QueueSave();
                }
            }
        }

        public string[] GetSections() { lock (fileLock) { return cache.Keys.ToArray(); } }

        /// <summary>
        /// 지정된 키를 명시적으로 삭제합니다.
        /// </summary>
        public void DeleteKey(string Key, string Section = null)
        {
            if (Key == null) throw new ArgumentNullException(nameof(Key), "Key cannot be null in DeleteKey method.");

            lock (fileLock)
            {
                Section = Section ?? EXE;
                if (cache.TryGetValue(Section, out var sectionDict))
                {
                    if (sectionDict.Remove(Key))
                    {
                        isDirty = true;
                        QueueSave();
                    }
                }
            }
        }

        /// <summary>
        /// 지정된 섹션 전체를 명시적으로 삭제합니다.
        /// </summary>
        public void DeleteSection(string Section = null)
        {
            lock (fileLock)
            {
                Section = Section ?? EXE;
                if (cache.Remove(Section))
                {
                    isDirty = true;
                    QueueSave();
                }
            }
        }

        public void RenameSection(string oldSection, string newSection)
        {
            lock (fileLock)
            {
                if (string.IsNullOrEmpty(oldSection) || string.IsNullOrEmpty(newSection) || string.Equals(oldSection, newSection, StringComparison.OrdinalIgnoreCase)) return;
                if (cache.TryGetValue(oldSection, out var oldDict))
                {
                    cache[newSection] = new Dictionary<string, string>(oldDict, StringComparer.OrdinalIgnoreCase);
                    cache.Remove(oldSection);
                    isDirty = true;
                    QueueSave();
                }
            }
        }

        public bool KeyExists(string Key, string Section = null)
        {
            lock (fileLock)
            {
                Section = Section ?? EXE;
                return cache.ContainsKey(Section) && cache[Section].ContainsKey(Key);
            }
        }

        public void Flush()
        {
            lock (fileLock)
            {
                isSaveQueued = false;
            }
            SaveToDisk();
        }

        public void Reload() { LoadFromDisk(); }
    }
}