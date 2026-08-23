using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Gamma_Manager
{
    /// <summary>
    /// Vendor-native saturation control.
    /// NVIDIA: Digital Vibrance through NVAPI.
    /// AMD: Saturation through ADL.
    /// No Magnification/full-desktop color effect is used, so values are applied
    /// to the selected physical display rather than globally.
    /// </summary>
    internal static class Saturation
    {
        private const int ADL_DISPLAY_COLOR_SATURATION = 0x00000004;
        private const uint NVAPI_OK = 0;
        private const uint NVAPI_DVC_GET_INFO = 0x4085DE45;
        private const uint NVAPI_DVC_SET_LEVEL = 0x172409B4;
        private const uint NVAPI_DISP_GET_DISPLAY_ID_BY_NAME = 0xAE457190;
        private const uint NVAPI_INITIALIZE = 0x0150E828;

        [StructLayout(LayoutKind.Sequential)]
        private struct NvDvcInfo
        {
            public uint Version;
            public uint CurrentLevel;
            public uint MinLevel;
            public uint MaxLevel;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr NvQueryInterfaceDelegate(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NvInitializeDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NvGetAssociatedDisplayHandleDelegate([MarshalAs(UnmanagedType.LPStr)] string displayName, out IntPtr displayHandle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NvGetDvcInfoDelegate(IntPtr displayHandle, uint outputId, IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NvSetDvcLevelDelegate(IntPtr displayHandle, uint outputId, uint level);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AdlMemoryAllocDelegate(int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AdlMainControlCreateDelegate(AdlMemoryAllocDelegate callback, int enumerateConnectedAdapters);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AdlMainControlDestroyDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AdlAdapterNumberOfAdaptersGetDelegate(out int numAdapters);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AdlDisplayDisplayInfoGetDelegate(int adapterIndex, ref int numDisplays, out IntPtr displayInfo, int forceDetect);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AdlDisplayColorGetDelegate(int adapterIndex, int displayIndex, int colorType, out int current, out int @default, out int min, out int max, out int step);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AdlDisplayColorSetDelegate(int adapterIndex, int displayIndex, int colorType, int value);

        [StructLayout(LayoutKind.Sequential)]
        private struct AdlDisplayId
        {
            public int LogicalIndex;
            public int PhysicalIndex;
            public int LogicalAdapterIndex;
            public int PhysicalAdapterIndex;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct AdlDisplayInfo
        {
            public AdlDisplayId DisplayID;
            public int DisplayControllerIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DisplayManufacturerName;
            public int DisplayType;
            public int DisplayOutputType;
            public int DisplayConnector;
            public int DisplayInfoMask;
            public int DisplayInfoValue;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private static IntPtr nvapi;
        private static NvQueryInterfaceDelegate nvQuery;
        private static NvInitializeDelegate nvInitialize;
        private static NvGetAssociatedDisplayHandleDelegate nvGetAssociatedDisplayHandle;
        private static NvGetDvcInfoDelegate nvGetDvcInfo;
        private static NvSetDvcLevelDelegate nvSetDvcLevel;
        private static bool nvReady;

        private static IntPtr adl;
        private static AdlMemoryAllocDelegate adlAlloc;
        private static AdlMainControlCreateDelegate adlCreate;
        private static AdlMainControlDestroyDelegate adlDestroy;
        private static AdlAdapterNumberOfAdaptersGetDelegate adlAdapterCount;
        private static AdlDisplayDisplayInfoGetDelegate adlDisplayInfoGet;
        private static AdlDisplayColorGetDelegate adlColorGet;
        private static AdlDisplayColorSetDelegate adlColorSet;
        private static bool adlReady;

        private sealed class Binding
        {
            public WinApi.DisplayAdapterVendor Vendor;
            public IntPtr NvDisplayHandle;
            public int AdlAdapterIndex = -1;
            public int AdlDisplayIndex = -1;
        }

        private static readonly Dictionary<string, Binding> bindings = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> originalValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Prevent AMD ADL fallback from binding multiple Windows monitors to the same ADL display.
        private static readonly HashSet<string> usedAmdTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool Prepare(Display.DisplayInfo display)
        {
            if (display == null) return false;
            try
            {
                Binding b;
                if (bindings.TryGetValue(display.displayLink ?? string.Empty, out b))
                {
                    return true;
                }

                if (display.adapterVendor == WinApi.DisplayAdapterVendor.Nvidia)
                {
                    if (PrepareNvidia(display, out b))
                    {
                        bindings[display.displayLink] = b;
                        int current, def, min, max, step;
                        if (GetNvidia(display, b, out current, out def, out min, out max, out step))
                        {
                            SetDisplayRange(display, current, def, min, max, step);
                            if (!originalValues.ContainsKey(display.displayLink)) originalValues[display.displayLink] = current;
                            return true;
                        }
                    }
                }
                else if (display.adapterVendor == WinApi.DisplayAdapterVendor.Amd)
                {
                    if (PrepareAmd(display, out b))
                    {
                        bindings[display.displayLink] = b;
                        int current, def, min, max, step;
                        if (GetAmd(display, b, out current, out def, out min, out max, out step))
                        {
                            SetDisplayRange(display, current, def, min, max, step);
                            if (!originalValues.ContainsKey(display.displayLink)) originalValues[display.displayLink] = current;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Saturation preparation failed for " + display.displayName, ex);
            }

            display.saturationSupported = false;
            return false;
        }

        private static void SetDisplayRange(Display.DisplayInfo display, int current, int def, int min, int max, int step)
        {
            display.saturationSupported = true;
            display.saturation = current;
            display.saturationDefault = def;
            display.saturationMin = min;
            display.saturationMax = max;
            display.saturationStep = Math.Max(1, step);
        }

        public static bool Apply(Display.DisplayInfo display, int value)
        {
            if (display == null) return false;
            if (!Prepare(display)) return false;
            value = Math.Max(display.saturationMin, Math.Min(display.saturationMax, value));
            Binding b = bindings[display.displayLink];
            try
            {
                bool ok;
                if (b.Vendor == WinApi.DisplayAdapterVendor.Nvidia)
                    ok = nvSetDvcLevel(b.NvDisplayHandle, 0, (uint)value) == NVAPI_OK;
                else if (b.Vendor == WinApi.DisplayAdapterVendor.Amd)
                    ok = adlColorSet(b.AdlAdapterIndex, b.AdlDisplayIndex, ADL_DISPLAY_COLOR_SATURATION, value) == 0;
                else
                    ok = false;

                if (ok) display.saturation = value;
                if (!ok) Logger.Warn("Saturation apply failed for " + display.displayName + ". Vendor=" + b.Vendor + ", Value=" + value);
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Error("Saturation apply threw an exception for " + display.displayName, ex);
                return false;
            }
        }

        public static void Reset()
        {
            foreach (KeyValuePair<string, int> pair in originalValues)
            {
                Binding b;
                if (!bindings.TryGetValue(pair.Key, out b)) continue;
                try
                {
                    if (b.Vendor == WinApi.DisplayAdapterVendor.Nvidia)
                        nvSetDvcLevel(b.NvDisplayHandle, 0, (uint)pair.Value);
                    else if (b.Vendor == WinApi.DisplayAdapterVendor.Amd)
                        adlColorSet(b.AdlAdapterIndex, b.AdlDisplayIndex, ADL_DISPLAY_COLOR_SATURATION, pair.Value);
                }
                catch (Exception ex)
                {
                    Logger.Warn("Saturation reset failed for display link " + pair.Key + ": " + ex.Message);
                }
            }
            originalValues.Clear();
            usedAmdTargets.Clear();
            bindings.Clear();
        }

        private static bool PrepareNvidia(Display.DisplayInfo display, out Binding binding)
        {
            binding = null;
            if (!EnsureNvidia()) return false;
            IntPtr handle;
            if (nvGetAssociatedDisplayHandle(display.displayLink, out handle) != NVAPI_OK || handle == IntPtr.Zero) return false;
            binding = new Binding { Vendor = WinApi.DisplayAdapterVendor.Nvidia, NvDisplayHandle = handle };
            return true;
        }

        private static bool GetNvidia(Display.DisplayInfo display, Binding b, out int current, out int def, out int min, out int max, out int step)
        {
            current = def = min = max = 0; step = 1;
            int size = Marshal.SizeOf(typeof(NvDvcInfo));
            IntPtr mem = Marshal.AllocHGlobal(size);
            try
            {
                NvDvcInfo info = new NvDvcInfo { Version = (uint)(size | (1 << 16)) };
                Marshal.StructureToPtr(info, mem, false);
                if (nvGetDvcInfo(b.NvDisplayHandle, 0, mem) != NVAPI_OK) return false;
                info = (NvDvcInfo)Marshal.PtrToStructure(mem, typeof(NvDvcInfo));
                current = (int)info.CurrentLevel;
                def = current;
                min = (int)info.MinLevel;
                max = (int)info.MaxLevel;
                if (max < min || max <= 0) return false;
                step = Math.Max(1, (max - min) >= 100 ? 1 : 1);
                return true;
            }
            finally { Marshal.FreeHGlobal(mem); }
        }

        private static bool EnsureNvidia()
        {
            if (nvReady) return true;
            try
            {
                nvapi = LoadLibrary("nvapi64.dll");
                if (nvapi == IntPtr.Zero) return false;
                IntPtr p = GetProcAddress(nvapi, "nvapi_QueryInterface");
                if (p == IntPtr.Zero) return false;
                nvQuery = (NvQueryInterfaceDelegate)Marshal.GetDelegateForFunctionPointer(p, typeof(NvQueryInterfaceDelegate));
                nvInitialize = DelegateFromQuery<NvInitializeDelegate>(NVAPI_INITIALIZE);
                nvGetAssociatedDisplayHandle = DelegateFromQuery<NvGetAssociatedDisplayHandleDelegate>(0x35C29134);
                nvGetDvcInfo = DelegateFromQuery<NvGetDvcInfoDelegate>(NVAPI_DVC_GET_INFO);
                nvSetDvcLevel = DelegateFromQuery<NvSetDvcLevelDelegate>(NVAPI_DVC_SET_LEVEL);
                if (nvInitialize == null || nvGetAssociatedDisplayHandle == null || nvGetDvcInfo == null || nvSetDvcLevel == null) return false;
                if (nvInitialize() != NVAPI_OK) return false;
                nvReady = true;
                return true;
            }
            catch { return false; }
        }

        private static T DelegateFromQuery<T>(uint id) where T : class
        {
            IntPtr ptr = nvQuery(id);
            if (ptr == IntPtr.Zero) return null;
            return (T)(object)Marshal.GetDelegateForFunctionPointer(ptr, typeof(T));
        }

        private static bool PrepareAmd(Display.DisplayInfo display, out Binding binding)
        {
            binding = null;
            if (!EnsureAmd()) return false;

            int count;
            if (adlAdapterCount(out count) != 0 || count <= 0) return false;

            int windowsNumber = ParseDisplayNumber(display.displayLink);
            string friendly = display.monitorFriendlyName ?? string.Empty;
            string manufacturer = display.monitorManufacturerName ?? string.Empty;

            // ADL's display index is not guaranteed to match DISPLAY1/DISPLAY2 on
            // hybrid, MST, or multi-GPU systems.  First try an exact match, then
            // fall back to every connected ADL display and accept the first one
            // for which the color API reports a valid saturation range.
            Binding bestFallback = null;

            for (int adapter = 0; adapter < count; adapter++)
            {
                int n = 0;
                IntPtr ptr = IntPtr.Zero;
                int rc = adlDisplayInfoGet(adapter, ref n, out ptr, 1);
                if (rc != 0 || ptr == IntPtr.Zero || n <= 0) continue;

                int size = Marshal.SizeOf(typeof(AdlDisplayInfo));

                for (int i = 0; i < n; i++)
                {
                    AdlDisplayInfo info;
                    try
                    {
                        info = (AdlDisplayInfo)Marshal.PtrToStructure(
                            IntPtr.Add(ptr, i * size), typeof(AdlDisplayInfo));
                    }
                    catch
                    {
                        continue;
                    }

                    // Only use connected displays.  Some driver versions return
                    // inactive targets in ADL_Display_DisplayInfo_Get().
                    if ((info.DisplayInfoValue & 0x00000001) == 0)
                        continue;

                    string name = (info.DisplayName ?? string.Empty).Trim();
                    string maker = (info.DisplayManufacturerName ?? string.Empty).Trim();

                    bool nameMatch = !string.IsNullOrWhiteSpace(friendly) &&
                        !string.IsNullOrWhiteSpace(name) &&
                        (friendly.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf(friendly, StringComparison.OrdinalIgnoreCase) >= 0);

                    bool makerMatch = !string.IsNullOrWhiteSpace(manufacturer) &&
                        !string.IsNullOrWhiteSpace(maker) &&
                        (manufacturer.IndexOf(maker, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         maker.IndexOf(manufacturer, StringComparison.OrdinalIgnoreCase) >= 0);

                    bool logicalMatch = windowsNumber >= 0 &&
                        (info.DisplayID.LogicalIndex == windowsNumber ||
                         info.DisplayID.PhysicalIndex == windowsNumber);

                    Binding candidate = new Binding
                    {
                        Vendor = WinApi.DisplayAdapterVendor.Amd,
                        AdlAdapterIndex = adapter,
                        AdlDisplayIndex = info.DisplayID.LogicalIndex
                    };

                    int current, def, min, max, step;
                    if (!GetAmd(display, candidate, out current, out def, out min, out max, out step))
                        continue;

                    // Prefer a display that can be identified from Windows/EDID.
                    string targetKey = adapter.ToString() + ":" + info.DisplayID.LogicalIndex.ToString();

                    if (usedAmdTargets.Contains(targetKey))
                        continue;

                    if (nameMatch || (makerMatch && logicalMatch) || logicalMatch)
                    {
                        binding = candidate;
                        usedAmdTargets.Add(targetKey);
                        return true;
                    }

                    // If the Windows<->ADL naming differs, keep a valid ADL
                    // saturation target as a last resort, but never reuse one
                    // already assigned to another Windows monitor.
                    if (bestFallback == null)
                        bestFallback = candidate;
                }
            }

            if (bestFallback != null)
            {
                int current, def, min, max, step;
                if (GetAmd(display, bestFallback, out current, out def, out min, out max, out step))
                {
                    binding = bestFallback;
                    usedAmdTargets.Add(bestFallback.AdlAdapterIndex.ToString() + ":" +
                        bestFallback.AdlDisplayIndex.ToString());
                    return true;
                }
            }

            return false;
        }

        private static bool GetAmd(Display.DisplayInfo display, Binding b, out int current, out int def, out int min, out int max, out int step)
        {
            current = def = min = max = step = 0;
            int rc = adlColorGet(b.AdlAdapterIndex, b.AdlDisplayIndex, ADL_DISPLAY_COLOR_SATURATION, out current, out def, out min, out max, out step);
            if (rc != 0 || max < min) return false;
            return true;
        }

        private static bool EnsureAmd()
        {
            if (adlReady) return true;
            try
            {
                adl = LoadLibrary("atiadlxx.dll");
                if (adl == IntPtr.Zero) adl = LoadLibrary("atiadlxy.dll");
                if (adl == IntPtr.Zero) return false;
                adlAlloc = delegate (int size) { return Marshal.AllocHGlobal(size); };
                adlCreate = GetAdlDelegate<AdlMainControlCreateDelegate>("ADL_Main_Control_Create");
                adlDestroy = GetAdlDelegate<AdlMainControlDestroyDelegate>("ADL_Main_Control_Destroy");
                adlAdapterCount = GetAdlDelegate<AdlAdapterNumberOfAdaptersGetDelegate>("ADL_Adapter_NumberOfAdapters_Get");
                adlDisplayInfoGet = GetAdlDelegate<AdlDisplayDisplayInfoGetDelegate>("ADL_Display_DisplayInfo_Get");
                adlColorGet = GetAdlDelegate<AdlDisplayColorGetDelegate>("ADL_Display_Color_Get");
                adlColorSet = GetAdlDelegate<AdlDisplayColorSetDelegate>("ADL_Display_Color_Set");
                if (adlCreate == null || adlAdapterCount == null || adlDisplayInfoGet == null || adlColorGet == null || adlColorSet == null) return false;
                if (adlCreate(adlAlloc, 1) != 0) return false;
                adlReady = true;
                return true;
            }
            catch { return false; }
        }

        private static T GetAdlDelegate<T>(string name) where T : class
        {
            IntPtr ptr = GetProcAddress(adl, name);
            if (ptr == IntPtr.Zero) return null;
            return (T)(object)Marshal.GetDelegateForFunctionPointer(ptr, typeof(T));
        }

        private static int ParseDisplayNumber(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            int p = name.LastIndexOf("DISPLAY", StringComparison.OrdinalIgnoreCase);
            if (p < 0) return -1;
            int start = p + 7;
            int end = start;
            while (end < name.Length && char.IsDigit(name[end])) end++;
            int n;
            return int.TryParse(name.Substring(start, end - start), out n) ? n - 1 : -1;
        }
    }
}
