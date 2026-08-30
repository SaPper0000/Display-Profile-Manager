using System;
using System.Collections.Concurrent;
using System.Management;

namespace Gamma_Manager
{
    internal class InternalMonitor
    {
        private static readonly object Sync = new object();

        // 👈 모니터 식별 키(hardwareId / InstanceName)별 독립 WMI 인스턴스 캐시
        private static readonly ConcurrentDictionary<string, ManagementObject> readInstances =
            new ConcurrentDictionary<string, ManagementObject>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, ManagementObject> writeInstances =
            new ConcurrentDictionary<string, ManagementObject>(StringComparer.OrdinalIgnoreCase);

        private static string NormalizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            return id.Replace("\\", "_").ToUpperInvariant();
        }

        private static bool MatchInstance(string instanceName, string targetHardwareId)
        {
            if (string.IsNullOrEmpty(instanceName)) return false;
            if (string.IsNullOrEmpty(targetHardwareId)) return true; // 타겟 ID가 없으면 첫 번째 인스턴스 수용

            string normInstance = NormalizeId(instanceName);
            string normTarget = NormalizeId(targetHardwareId);

            return normInstance.Contains(normTarget) || normTarget.Contains(normInstance);
        }

        private static void EnsureWmiInstancesForMonitor(string hardwareId, out ManagementObject readObj, out ManagementObject writeObj)
        {
            string key = hardwareId ?? string.Empty;

            readInstances.TryGetValue(key, out readObj);
            writeInstances.TryGetValue(key, out writeObj);

            if (readObj != null && writeObj != null) return;

            lock (Sync)
            {
                if (readInstances.TryGetValue(key, out readObj) && writeInstances.TryGetValue(key, out writeObj))
                    return;

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
                            foreach (ManagementObject o in reads)
                            {
                                string instName = o.GetPropertyValue("InstanceName") as string;
                                if (MatchInstance(instName, hardwareId))
                                {
                                    if (fallbackRead != null && fallbackRead != o)
                                    {
                                        fallbackRead.Dispose();
                                        fallbackRead = null; // 👈 null 초기화로 중복 해제 방지
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
                            if (readObj == null && string.IsNullOrEmpty(hardwareId))
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
                            foreach (ManagementObject o in writes)
                            {
                                string instName = o.GetPropertyValue("InstanceName") as string;
                                if (MatchInstance(instName, hardwareId))
                                {
                                    if (fallbackWrite != null && fallbackWrite != o)
                                    {
                                        fallbackWrite.Dispose();
                                        fallbackWrite = null; // 👈 null 초기화로 중복 해제 방지
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
                            if (writeObj == null && string.IsNullOrEmpty(hardwareId))
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
        }

        public static bool TryGetBrightness(string hardwareId, out int value)
        {
            value = 0;
            lock (Sync)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    EnsureWmiInstancesForMonitor(hardwareId, out ManagementObject readInstance, out _);

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
                        // 👈 2회 연속 실패(attempt == 1) 시에만 인스턴스 재생성
                        if (attempt == 1)
                        {
                            ResetMonitorInstancesLocked(hardwareId);
                        }
                    }
                }
                return false;
            }
        }

        // 하위 호환용 오버로드
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
                    EnsureWmiInstancesForMonitor(hardwareId, out _, out ManagementObject writeInstance);

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
                        // 👈 2회 연속 실패(attempt == 1) 시에만 인스턴스 재생성
                        if (attempt == 1)
                        {
                            ResetMonitorInstancesLocked(hardwareId);
                        }
                    }
                }
                return false;
            }
        }

        // 하위 호환용 오버로드
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