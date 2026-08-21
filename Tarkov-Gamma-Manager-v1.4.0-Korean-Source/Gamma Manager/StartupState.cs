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
            get { return Path.Combine(Application.StartupPath, "GammaManager.StartupBackup.json"); }
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
                        if (string.Equals(d.displayLink, saved.DisplayLink, StringComparison.Ordinal)) { display = d; break; }
                    if (display == null) { allRestored = false; continue; }

                    ushort[,] ramp = Expand(saved.GammaRamp);
                    if (ramp != null) Gamma.SetRawGammaRamp(display.displayLink, ramp);

                    if (saved.IsExternal && display.isExternal)
                    {
                        ExternalMonitor.SetBrightness(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorBrightness)));
                        if (saved.MonitorContrast >= 0)
                            ExternalMonitor.SetContrast(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorContrast)));
                    }
                    else if (!saved.IsExternal && !display.isExternal)
                    {
                        InternalMonitor.SetBrightness((byte)Math.Max(0, Math.Min(100, saved.MonitorBrightness)));
                    }
                }

                if (allRestored) { File.Delete(BackupPath); return true; }
                return false;
            }
            catch
            {
                // Leave the backup in place. A later launch can try the restore again.
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
            catch
            {
                return false;
            }
        }

        public static bool RestoreSaved(List<Display.DisplayInfo> displays)
        {
            if (!File.Exists(BackupPath)) return false;
            try
            {
                StartupState state;
                using (FileStream fs = File.OpenRead(BackupPath))
                    state = (StartupState)new DataContractJsonSerializer(typeof(StartupState)).ReadObject(fs);
                if (state == null || state.Displays == null) return false;
                foreach (StartupDisplayState saved in state.Displays)
                {
                    Display.DisplayInfo display = null;
                    foreach (Display.DisplayInfo d in displays)
                        if (string.Equals(d.displayLink, saved.DisplayLink, StringComparison.Ordinal)) { display = d; break; }
                    if (display == null) continue;
                    ushort[,] ramp = Expand(saved.GammaRamp);
                    if (ramp != null) Gamma.SetRawGammaRamp(display.displayLink, ramp);
                    if (saved.IsExternal && display.isExternal)
                    {
                        ExternalMonitor.SetBrightness(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorBrightness)));
                        if (saved.MonitorContrast >= 0) ExternalMonitor.SetContrast(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorContrast)));
                    }
                    else if (!saved.IsExternal && !display.isExternal)
                        InternalMonitor.SetBrightness((byte)Math.Max(0, Math.Min(100, saved.MonitorBrightness)));
                }
                return true;
            }
            catch { return false; }
        }

        public static void RestoreAndClear(List<Display.DisplayInfo> displays)
        {
            RestorePending(displays);
            // Do not erase an unrecovered backup; it is useful on the next launch.
        }
    }
}
