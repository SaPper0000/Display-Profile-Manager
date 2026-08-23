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
        private readonly string _filePath;
        private readonly string EXE = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;

        // INI 데이터를 메모리에 보관하는 캐시: Dictionary<Section, Dictionary<Key, Value>>
        private readonly Dictionary<string, Dictionary<string, string>> cache = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        
        // 동시성 문제 방지용 락
        private readonly object fileLock = new object();
        private bool isDirty = false;

        public IniFile(string IniPath = null)
        {
            _filePath = new FileInfo(IniPath ?? EXE + ".ini").FullName;
            LoadFromDisk();
        }

        public string FilePath { get { return _filePath; } }

        // --- 메모리 로드 (최초 1회 실행) ---
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

        // --- 지연(비동기) 디스크 저장 ---
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
                    lines.Add(""); // 빈 줄 추가
                }

                try
                {
                    File.WriteAllLines(_filePath, lines, Encoding.UTF8);
                    isDirty = false;
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to save INI file to disk.", ex);
                }
            }
        }

        // 값 변경 시마다 백그라운드 스레드에서 즉시, 그러나 UI를 멈추지 않고 저장
        private void QueueSave()
        {
            Task.Run(() => SaveToDisk());
        }

        // --- 데이터 접근 메서드 (메모리 처리로 초고속) ---
        public string Read(string Key, string Section = null)
        {
            lock (fileLock)
            {
                Section = Section ?? EXE;
                if (cache.TryGetValue(Section, out var sectionDict))
                {
                    if (sectionDict.TryGetValue(Key, out string value))
                        return value;
                }
                return "";
            }
        }

        public void Write(string Key, string Value, string Section = null)
        {
            lock (fileLock)
            {
                Section = Section ?? EXE;
                
                // 삭제 명령 (Value가 null이거나 Key가 null인 경우)
                if (Key == null || Value == null)
                {
                    if (Key == null) 
                    {
                        DeleteSection(Section);
                        return;
                    }
                    else
                    {
                        if (cache.ContainsKey(Section))
                        {
                            if (cache[Section].Remove(Key))
                            {
                                isDirty = true;
                                QueueSave();
                            }
                        }
                        return;
                    }
                }

                if (!cache.ContainsKey(Section))
                    cache[Section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!cache[Section].ContainsKey(Key) || cache[Section][Key] != Value)
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

        public void DeleteKey(string Key, string Section = null)
        {
            Write(Key, null, Section);
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
                    string.Equals(oldSection, newSection, StringComparison.OrdinalIgnoreCase))
                    return;

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

        // 백업용 (외부 파일에서 로드)
        public void Reload()
        {
            LoadFromDisk();
        }
    }
}