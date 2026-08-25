using System;
using System.Management;

namespace Gamma_Manager
{
    internal class InternalMonitor
    {
        private static readonly object Sync = new object();
        private static ManagementObject readInstance;
        private static ManagementObject writeInstance;

        private static bool EnsureWmiInstances()
        {
            lock (Sync)
            {
                if (readInstance != null && writeInstance != null) return true;
                ResetInstancesLocked();
                try
                {
                    ManagementScope scope = new ManagementScope(@"root\WMI");
                    using (ManagementObjectSearcher readSearcher = new ManagementObjectSearcher(scope, new SelectQuery("WmiMonitorBrightness")))
                    using (ManagementObjectCollection reads = readSearcher.Get())
                    {
                        foreach (ManagementObject o in reads)
                        {
                            readInstance = new ManagementObject(o.Path);
                            o.Dispose(); // 사용 후 즉시 해제
                            break;
                        }
                    }
                    using (ManagementObjectSearcher writeSearcher = new ManagementObjectSearcher(scope, new SelectQuery("WmiMonitorBrightnessMethods")))
                    using (ManagementObjectCollection writes = writeSearcher.Get())
                    {
                        foreach (ManagementObject o in writes)
                        {
                            writeInstance = new ManagementObject(o.Path);
                            o.Dispose(); // 사용 후 즉시 해제
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("WMI monitor reconnect failed: " + ex.Message);
                    ResetInstancesLocked();
                }
                return readInstance != null || writeInstance != null;
            }
        }

        private static void ResetInstancesLocked()
        {
            if (readInstance != null) { readInstance.Dispose(); readInstance = null; }
            if (writeInstance != null) { writeInstance.Dispose(); writeInstance = null; }
        }

        private static void Reconnect()
        {
            lock (Sync) ResetInstancesLocked();
            EnsureWmiInstances();
        }

        public static bool TryGetBrightness(out int value)
        {
            value = 0;
            lock (Sync)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    EnsureWmiInstances();
                    try
                    {
                        if (readInstance == null) return false;
                        readInstance.Get();
                        value = Convert.ToInt32(readInstance.GetPropertyValue("CurrentBrightness"));
                        return value >= 0 && value <= 100;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("WMI brightness read failed; reconnecting: " + ex.Message);
                        ResetInstancesLocked();
                    }
                }
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
            lock (Sync)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    EnsureWmiInstances();
                    try
                    {
                        if (writeInstance == null) { Logger.Warn("WMI brightness write found no instance."); return; }
                        writeInstance.InvokeMethod("WmiSetBrightness", new object[] { UInt32.MaxValue, targetBrightness });
                        return;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("WMI brightness write failed; reconnecting: " + ex.Message);
                        ResetInstancesLocked();
                    }
                }
            }
        }

        public static void Cleanup()
        {
            lock (Sync) ResetInstancesLocked();
        }
    }
}
