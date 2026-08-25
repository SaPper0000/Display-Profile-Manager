using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

namespace Gamma_Manager
{
    [DataContract]
    internal sealed class StartupDisplayState
    {
        [DataMember] public string DisplayLink;
        [DataMember] public bool IsExternal;
        [DataMember] public int MonitorBrightness;
        [DataMember] public int MonitorContrast;
        [DataMember] public ushort[] GammaRamp;
    }

    [DataContract]
    internal sealed class StartupState
    {
        [DataMember] public List<StartupDisplayState> Displays = new List<StartupDisplayState>();
    }

    internal static class StartupStateManager
    {
        private static string BackupPath
        {
            get { return AppPaths.StateFile("GammaManager.StartupBackup.json"); }
        }

        private static ushort[] Flatten(ushort[,] ramp)
        {
            if (ramp == null) return null;
            ushort[] data = new ushort[768];
            int n = 0;
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 256; i++)
                    data[n++] = ramp[c, i];
            return data;
        }

        private static ushort[,] Expand(ushort[] data)
        {
            if (data == null || data.Length != 768) return null;
            ushort[,] ramp = new ushort[3, 256];
            int n = 0;
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 256; i++)
                    ramp[c, i] = data[n++];
            return ramp;
        }

        public static bool RestorePending(List<Display.DisplayInfo> displays)
        {
            if (!File.Exists(BackupPath)) return true;
            try
            {
                StartupState state;
                using (FileStream fs = File.OpenRead(BackupPath))
                {
                    state = (StartupState)new DataContractJsonSerializer(typeof(StartupState)).ReadObject(fs);
                }
                if (state == null || state.Displays == null) return false;

                bool allRestored = true;
                foreach (StartupDisplayState saved in state.Displays)
                {
                    Display.DisplayInfo display = null;
                    foreach (Display.DisplayInfo d in displays)
                        if (string.Equals(d.displayLink, saved.DisplayLink, StringComparison.OrdinalIgnoreCase)) { display = d; break; }
                    if (display == null) { allRestored = false; continue; }

                    ushort[,] ramp = Expand(saved.GammaRamp);
                    if (ramp != null)
                        Gamma.SetRawGammaRamp(display.displayLink, ramp);
                    else
                        Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f));

                    if (saved.IsExternal && display.isExternal)
                    {
                        if (Display.RefreshPhysicalMonitorHandle(display) &&
                            display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                        {
                            ExternalMonitor.SetBrightness(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorBrightness)));
                            if (saved.MonitorContrast >= 0)
                                ExternalMonitor.SetContrast(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorContrast)));
                        }
                    }
                    else if (!saved.IsExternal && !display.isExternal)
                    {
                        int targetBrightness = Math.Max(0, Math.Min(100, saved.MonitorBrightness));
                        InternalMonitor.SetBrightness((byte)targetBrightness);
                    }
                }

                if (allRestored) { ClearBackup(); return true; }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("Startup state restore failed; leaving backup in place for the next launch.", ex);
                return false;
            }
        }

        public static bool Capture(List<Display.DisplayInfo> displays)
        {
            try
            {
                StartupState state = new StartupState();
                foreach (Display.DisplayInfo display in displays)
                {
                    StartupDisplayState saved = new StartupDisplayState();
                    saved.DisplayLink = display.displayLink;
                    saved.IsExternal = display.isExternal;
                    int brightness;
                    int contrast;
                    if (display.isExternal)
                    {
                        saved.MonitorBrightness = ExternalMonitor.TryGetBrightness(display.PhysicalHandle, out brightness) ? brightness : display.monitorBrightness;
                        saved.MonitorContrast = ExternalMonitor.TryGetContrast(display.PhysicalHandle, out contrast) ? contrast : display.monitorContrast;
                    }
                    else
                    {
                        saved.MonitorBrightness = InternalMonitor.TryGetBrightness(out brightness) ? brightness : display.monitorBrightness;
                        saved.MonitorContrast = -1;
                    }
                    saved.GammaRamp = Flatten(Gamma.GetGammaRamp(display.displayLink));
                    state.Displays.Add(saved);
                }

                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(StartupState));
                using (FileStream fs = File.Create(BackupPath))
                    serializer.WriteObject(fs, state);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Startup state capture failed.", ex);
                return false;
            }
        }

        public static bool RestoreOriginalMonitor(Display.DisplayInfo display)
        {
            if (display == null || !File.Exists(BackupPath)) return false;
            try
            {
                StartupState state;
                using (FileStream fs = File.OpenRead(BackupPath))
                    state = (StartupState)new DataContractJsonSerializer(typeof(StartupState)).ReadObject(fs);
                if (state == null || state.Displays == null) return false;

                foreach (StartupDisplayState saved in state.Displays)
                {
                    if (!string.Equals(display.displayLink, saved.DisplayLink, StringComparison.OrdinalIgnoreCase)) continue;

                    ushort[,] ramp = Expand(saved.GammaRamp);
                    if (ramp != null)
                        Gamma.SetRawGammaRamp(display.displayLink, ramp);
                    else
                        Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f));

                    if (saved.IsExternal && display.isExternal)
                    {
                        Display.RefreshPhysicalMonitorHandle(display);
                        if (display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                        {
                            ExternalMonitor.SetBrightness(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorBrightness)));
                            if (saved.MonitorContrast >= 0)
                                ExternalMonitor.SetContrast(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorContrast)));
                        }
                        return true;
                    }
                    else if (!saved.IsExternal && !display.isExternal)
                    {
                        int target = Math.Max(0, Math.Min(100, saved.MonitorBrightness));
                        InternalMonitor.SetBrightness((byte)target);
                        return true;
                    }
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("Hard reset monitor restore failed.", ex);
                return false;
            }
        }

        public static bool RestoreOriginalMonitors(List<Display.DisplayInfo> displays)
        {
            if (displays == null || displays.Count == 0 || !File.Exists(BackupPath)) return false;
            try
            {
                StartupState state;
                using (FileStream fs = File.OpenRead(BackupPath))
                    state = (StartupState)new DataContractJsonSerializer(typeof(StartupState)).ReadObject(fs);
                if (state == null || state.Displays == null) return false;

                foreach (Display.DisplayInfo display in displays)
                {
                    if (display == null) continue;
                    foreach (StartupDisplayState saved in state.Displays)
                    {
                        if (!string.Equals(display.displayLink, saved.DisplayLink, StringComparison.OrdinalIgnoreCase)) continue;

                        ushort[,] ramp = Expand(saved.GammaRamp);
                        if (ramp != null)
                            Gamma.SetRawGammaRamp(display.displayLink, ramp);
                        else
                            Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f));

                        if (saved.IsExternal && display.isExternal)
                        {
                            Display.RefreshPhysicalMonitorHandle(display);
                            if (display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                            {
                                ExternalMonitor.SetBrightness(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorBrightness)));
                                if (saved.MonitorContrast >= 0)
                                    ExternalMonitor.SetContrast(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorContrast)));
                            }
                        }
                        else if (!saved.IsExternal && !display.isExternal)
                        {
                            int target = Math.Max(0, Math.Min(100, saved.MonitorBrightness));
                            InternalMonitor.SetBrightness((byte)target);
                        }
                        break;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Hard reset all monitors restore failed.", ex);
                return false;
            }
        }

        public static void ClearBackup()
        {
            try
            {
                if (File.Exists(BackupPath))
                    File.Delete(BackupPath);
            }
            catch (Exception ex)
            {
                Logger.Warn("ClearBackup failed: " + ex.Message);
            }
        }

        public static bool TryGetOriginalValues(string displayLink, out int brightness, out int contrast)
        {
            brightness = 50;
            contrast = 50;
            if (string.IsNullOrEmpty(displayLink) || !File.Exists(BackupPath)) return false;
            try
            {
                StartupState state;
                using (FileStream fs = File.OpenRead(BackupPath))
                    state = (StartupState)new DataContractJsonSerializer(typeof(StartupState)).ReadObject(fs);
                if (state == null || state.Displays == null) return false;

                foreach (var saved in state.Displays)
                {
                    if (string.Equals(saved.DisplayLink, displayLink, StringComparison.OrdinalIgnoreCase))
                    {
                        brightness = saved.MonitorBrightness;
                        contrast = saved.MonitorContrast;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }


        public static void RestoreAndClear(List<Display.DisplayInfo> displays)
        {
            RestorePending(displays);
        }
    }
}