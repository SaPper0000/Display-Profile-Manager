using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Gamma_Manager
{
    internal class IniFile
    {
        private static readonly Lazy<IniFile> _shared = new Lazy<IniFile>(() => new IniFile());
        public static IniFile Shared => _shared.Value;

        private readonly string _filePath;
        private const string DefaultConfigFileName = "GammaManager.ini";
        private readonly string EXE = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;

        private readonly Dictionary<string, Dictionary<string, string>> cache =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private readonly object fileLock = new object();
        private readonly object _fileIOLock = new object();
        private bool isDirty = false;
        private CancellationTokenSource _saveCts = null;

        public IniFile(string IniPath = null)
        {
            if (string.IsNullOrEmpty(IniPath))
            {
                IniPath = AppPaths.ConfigFile(DefaultConfigFileName);
                MigrateLegacyIniIfNeeded(IniPath);
            }

            _filePath = new FileInfo(IniPath).FullName;
            string directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            LoadFromDisk();
        }

        public string FilePath => _filePath;

        private static void MigrateLegacyIniIfNeeded(string targetPath)
        {
            try
            {
                if (File.Exists(targetPath)) return;

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                string localDefault = Path.Combine(baseDir, DefaultConfigFileName);
                if (File.Exists(localDefault))
                {
                    File.Copy(localDefault, targetPath, true);
                    Logger.Info($"Migrated local INI [{localDefault}] to AppData [{targetPath}]");
                    return;
                }

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
            lock (_fileIOLock)
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
            Dictionary<string, List<KeyValuePair<string, string>>> snapshot;

            // 1. 메모리 락 구간: 최소한의 스냅샷 복제 후 즉시 락 해제
            lock (fileLock)
            {
                if (!isDirty) return;

                snapshot = new Dictionary<string, List<KeyValuePair<string, string>>>(cache.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var section in cache)
                {
                    snapshot[section.Key] = section.Value.ToList();
                }
                isDirty = false;
            }

            // 2. I/O 락 구간: 고유 임시 파일명을 통한 안전한 원자적 저장
            lock (_fileIOLock)
            {
                string dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string tempPath = Path.Combine(dir, $"{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
                string backupPath = _filePath + ".bak";

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                        {
                            foreach (var section in snapshot)
                            {
                                sw.WriteLine($"[{section.Key}]");
                                foreach (var kvp in section.Value)
                                {
                                    sw.WriteLine($"{kvp.Key}={kvp.Value}");
                                }
                                sw.WriteLine();
                            }
                            sw.Flush();
                            fs.Flush(true);
                        }

                        if (!File.Exists(_filePath))
                        {
                            File.Move(tempPath, _filePath);
                        }
                        else
                        {
                            File.Replace(tempPath, _filePath, backupPath, ignoreMetadataErrors: true);
                            if (File.Exists(backupPath))
                            {
                                try { File.Delete(backupPath); } catch { }
                            }
                        }

                        break;
                    }
                    catch (Exception ex)
                    {
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

                        if (attempt == 2)
                        {
                            Logger.Error("Failed to save INI file atomically to disk after 3 attempts.", ex);
                            lock (fileLock) { isDirty = true; }
                        }
                        else
                        {
                            Thread.Sleep(30);
                        }
                    }
                }
            }
        }

        private void QueueSave()
        {
            CancellationTokenSource cts;
            lock (fileLock)
            {
                if (_saveCts != null)
                {
                    try { _saveCts.Cancel(); } catch { }
                    try { _saveCts.Dispose(); } catch { }
                }
                _saveCts = new CancellationTokenSource();
                cts = _saveCts;
            }

            var token = cts.Token;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(60, token);
                    if (!token.IsCancellationRequested)
                    {
                        SaveToDisk();
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
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
                return string.Empty;
            }
        }

        public void Write(string Key, string Value, string Section = null)
        {
            if (Key == null) throw new ArgumentNullException(nameof(Key), "Key cannot be null.");
            if (Value == null) throw new ArgumentNullException(nameof(Value), "Value cannot be null.");

            lock (fileLock)
            {
                Section = Section ?? EXE;
                if (!cache.ContainsKey(Section))
                    cache[Section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!cache[Section].TryGetValue(Key, out string currentValue) || currentValue != Value)
                {
                    cache[Section][Key] = Value;
                    isDirty = true;
                    QueueSave();
                }
            }
        }

        public string[] GetSections()
        {
            lock (fileLock)
            {
                return cache.Keys.ToArray();
            }
        }

        public string[] GetKeys(string Section = null)
        {
            lock (fileLock)
            {
                Section = Section ?? EXE;
                if (cache.TryGetValue(Section, out var sectionDict))
                {
                    return sectionDict.Keys.ToArray();
                }
                return new string[0];
            }
        }

        public void DeleteKey(string Key, string Section = null)
        {
            if (Key == null) throw new ArgumentNullException(nameof(Key), "Key cannot be null.");

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
                if (string.IsNullOrEmpty(oldSection) || string.IsNullOrEmpty(newSection) ||
                    string.Equals(oldSection, newSection, StringComparison.OrdinalIgnoreCase)) return;

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
                if (_saveCts != null)
                {
                    try { _saveCts.Cancel(); } catch { }
                    try { _saveCts.Dispose(); } catch { }
                    _saveCts = null;
                }
            }
            SaveToDisk();
        }

        public void Reload()
        {
            Flush();
            LoadFromDisk();
        }
    }
}