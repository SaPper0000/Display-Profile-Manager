using System;
using System.Management;

namespace Gamma_Manager
{
    internal class InternalMonitor
    {
        // WMI 연결 객체를 매번 생성하지 않고 저장(캐싱)해두는 변수
        private static ManagementObject readInstance;
        private static ManagementObject writeInstance;
        private static bool isInitialized = false;

        private static void EnsureWmiInstances()
        {
            if (isInitialized) return;
            try
            {
                ManagementScope s = new ManagementScope("root\\WMI");

                // 읽기용 객체 1회만 연결 후 저장
                using (ManagementObjectSearcher mosRead = new ManagementObjectSearcher(s, new SelectQuery("WmiMonitorBrightness")))
                using (ManagementObjectCollection mocRead = mosRead.Get())
                {
                    foreach (ManagementObject o in mocRead) { readInstance = o; break; }
                }

                // 쓰기용 객체 1회만 연결 후 저장
                using (ManagementObjectSearcher mosWrite = new ManagementObjectSearcher(s, new SelectQuery("WmiMonitorBrightnessMethods")))
                using (ManagementObjectCollection mocWrite = mosWrite.Get())
                {
                    foreach (ManagementObject o in mocWrite) { writeInstance = o; break; }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("WMI Init failed: " + ex.Message);
            }
            isInitialized = true;
        }

        public static bool TryGetBrightness(out int value)
        {
            value = 0;
            EnsureWmiInstances();
            if (readInstance == null) return false;

            try
            {
                readInstance.Get(); // 최신값으로 갱신
                value = (byte)readInstance.GetPropertyValue("CurrentBrightness");
                return value >= 0 && value <= 100;
            }
            catch (Exception ex)
            {
                Logger.Warn("WMI monitor brightness read failed: " + ex.Message);
                return false;
            }
        }

        public static int GetBrightness()
        {
            int value;
            return TryGetBrightness(out value) ? value : -1;
        }

        public static void SetBrightness(byte targetBrightness)
        {
            EnsureWmiInstances();
            if (writeInstance == null)
            {
                Logger.Warn("WMI monitor brightness write found no instance.");
                return;
            }

            try
            {
                // 이미 연결된 객체를 재사용하여 즉시 밝기 변경
                writeInstance.InvokeMethod("WmiSetBrightness", new object[] { UInt32.MaxValue, targetBrightness });
            }
            catch (Exception ex)
            {
                Logger.Warn("WMI SetBrightness failed: " + ex.Message);
            }
        }

        // --- 메모리 누수 방지용 자원 정리 메서드 추가 ---
        public static void Cleanup()
        {
            if (readInstance != null)
            {
                readInstance.Dispose();
                readInstance = null;
            }
            if (writeInstance != null)
            {
                writeInstance.Dispose();
                writeInstance = null;
            }
            isInitialized = false;
        }
    }
}