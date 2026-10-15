using System;
using System.Collections.Concurrent;
using System.Management;

namespace Gamma_Manager
{
    internal class InternalMonitor
    {
        private static readonly object Sync = new object();

        // 모니터 식별 키(hardwareId / InstanceName)별 독립 WMI 인스턴스 캐시
        private static readonly ConcurrentDictionary<string, ManagementObject> readInstances =
            new ConcurrentDictionary<string, ManagementObject>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, ManagementObject> writeInstances =
            new ConcurrentDictionary<string, ManagementObject>(StringComparer.OrdinalIgnoreCase);

        private static string NormalizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            string s = id.Trim();
            if (s.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(4);
            int guidIdx = s.IndexOf("#{", StringComparison.OrdinalIgnoreCase);
            if (guidIdx > 0)
                s = s.Substring(0, guidIdx);
            if (s.EndsWith("_0", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 2);
            return s.Replace("\\", "_").Replace("#", "_").Trim().ToUpperInvariant();
        }

        private static string ExtractModelCode(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            string[] parts = id.Split(new[] { '\\', '#' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Equals("DISPLAY", StringComparison.OrdinalIgnoreCase) ||
                    parts[i].Equals("MONITOR", StringComparison.OrdinalIgnoreCase) ||
                    parts[i].Equals("?", StringComparison.OrdinalIgnoreCase))
                    continue;
                return parts[i].ToUpperInvariant();
            }
            return string.Empty;
        }

        private static bool MatchInstance(string instanceName, string targetHardwareId)
        {
            if (string.IsNullOrEmpty(instanceName)) return false;
            if (string.IsNullOrEmpty(targetHardwareId)) return true; // 타겟 ID가 없으면 첫 번째 인스턴스 수용

            string normInstance = NormalizeId(instanceName);
            string normTarget = NormalizeId(targetHardwareId);

            if (normInstance.Contains(normTarget) || normTarget.Contains(normInstance))
                return true;

            string instModel = ExtractModelCode(instanceName);
            string targetModel = ExtractModelCode(targetHardwareId);
            return !string.IsNullOrEmpty(instModel) &&
                   string.Equals(instModel, targetModel, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureWmiInstancesForMonitorLocked(string hardwareId, out ManagementObject readObj, out ManagementObject writeObj)
        {
            string key = hardwareId ?? string.Empty;

            readInstances.TryGetValue(key, out readObj);
            writeInstances.TryGetValue(key, out writeObj);

            if (readObj != null && writeObj != null) return;

            try
            {
                ManagementScope scope = new ManagementScope(@"root\WMI");

                // 1. Read 인스턴스 검색 (hardwareId 매칭)
                if (readObj == null)
                {
                    using (ManagementObjectSearcher readSearcher = new ManagementObjectSearcher(scope, new SelectQuery("WmiMonitorBrightness")))
                    using (ManagementObjectCollection reads = readSearcher.Get())
                    {
                        ManagementObject fallbackRead = null;
                        int count = 0;
                        foreach (ManagementObject o in reads)
                        {
                            count++;
                            try
                            {
                                string instName = o.GetPropertyValue("InstanceName") as string;
                                if (MatchInstance(instName, hardwareId))
                                {
                                    if (fallbackRead != null && fallbackRead != o)
                                    {
                                        fallbackRead.Dispose();
                                        fallbackRead = null;
                                    }
                                    readObj = o;
                                    break;
                                }

                                if (fallbackRead == null)
                                {
                                    fallbackRead = o;
                                }
                                else
                                {
                                    o.Dispose();
                                }
                            }
                            catch
                            {
                                o.Dispose();
                            }
                        }

                        if (readObj == null && (string.IsNullOrEmpty(hardwareId) || count == 1))
                            readObj = fallbackRead;
                        else if (readObj != fallbackRead && fallbackRead != null)
                            fallbackRead.Dispose();

                        if (readObj != null) readInstances[key] = readObj;
                    }
                }

                // 2. Write 인스턴스 검색 (hardwareId 매칭)
                if (writeObj == null)
                {
                    using (ManagementObjectSearcher writeSearcher = new ManagementObjectSearcher(scope, new SelectQuery("WmiMonitorBrightnessMethods")))
                    using (ManagementObjectCollection writes = writeSearcher.Get())
                    {
                        ManagementObject fallbackWrite = null;
                        int count = 0;
                        foreach (ManagementObject o in writes)
                        {
                            count++;
                            try
                            {
                                string instName = o.GetPropertyValue("InstanceName") as string;
                                if (MatchInstance(instName, hardwareId))
                                {
                                    if (fallbackWrite != null && fallbackWrite != o)
                                    {
                                        fallbackWrite.Dispose();
                                        fallbackWrite = null;
                                    }
                                    writeObj = o;
                                    break;
                                }

                                if (fallbackWrite == null)
                                {
                                    fallbackWrite = o;
                                }
                                else
                                {
                                    o.Dispose();
                                }
                            }
                            catch
                            {
                                o.Dispose();
                            }
                        }

                        if (writeObj == null && (string.IsNullOrEmpty(hardwareId) || count == 1))
                            writeObj = fallbackWrite;
                        else if (writeObj != fallbackWrite && fallbackWrite != null)
                            fallbackWrite.Dispose();

                        if (writeObj != null) writeInstances[key] = writeObj;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("WMI monitor instance search failed for " + hardwareId + ": " + ex.Message);
            }
        }

        public static bool TryGetBrightness(string hardwareId, out int value)
        {
            value = 0;
            lock (Sync)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    EnsureWmiInstancesForMonitorLocked(hardwareId, out ManagementObject readInstance, out _);

                    try
                    {
                        if (readInstance == null) return false;
                        readInstance.Get();
                        value = Convert.ToInt32(readInstance.GetPropertyValue("CurrentBrightness"));
                        return value >= 0 && value <= 100;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"WMI brightness read failed (attempt {attempt + 1}): {ex.Message}");

                        // 1차 실패 즉시 캐시를 제거하여 2차 재시도 시 신규 WMI 인스턴스를 검색·생성
                        ResetMonitorInstancesLocked(hardwareId);
                    }
                }
                return false;
            }
        }

        public static bool TryGetBrightness(out int value)
        {
            return TryGetBrightness(string.Empty, out value);
        }

        public static int GetBrightness(string hardwareId)
        {
            return TryGetBrightness(hardwareId, out int value) ? value : -1;
        }

        public static int GetBrightness()
        {
            return TryGetBrightness(string.Empty, out int value) ? value : -1;
        }

        public static bool SetBrightness(string hardwareId, byte targetBrightness)
        {
            lock (Sync)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    EnsureWmiInstancesForMonitorLocked(hardwareId, out _, out ManagementObject writeInstance);

                    try
                    {
                        if (writeInstance == null)
                        {
                            Logger.Warn("WMI brightness write found no instance for " + hardwareId);
                            return false;
                        }

                        writeInstance.InvokeMethod("WmiSetBrightness", new object[] { UInt32.MaxValue, targetBrightness });
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"WMI brightness write failed (attempt {attempt + 1}): {ex.Message}");

                        // 1차 실패 즉시 캐시를 제거하여 2차 재시도 시 신규 WMI 인스턴스를 검색·생성
                        ResetMonitorInstancesLocked(hardwareId);
                    }
                }
                return false;
            }
        }

        public static bool SetBrightness(byte targetBrightness)
        {
            return SetBrightness(string.Empty, targetBrightness);
        }

        private static void ResetMonitorInstancesLocked(string hardwareId)
        {
            string key = hardwareId ?? string.Empty;
            if (readInstances.TryRemove(key, out ManagementObject r))
            {
                try { r.Dispose(); } catch { }
            }
            if (writeInstances.TryRemove(key, out ManagementObject w))
            {
                try { w.Dispose(); } catch { }
            }
        }

        public static void Cleanup()
        {
            lock (Sync)
            {
                foreach (var r in readInstances.Values)
                {
                    try { r.Dispose(); } catch { }
                }
                readInstances.Clear();

                foreach (var w in writeInstances.Values)
                {
                    try { w.Dispose(); } catch { }
                }
                writeInstances.Clear();
            }
        }
    }
}