using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Gamma_Manager
{
    /// <summary>
    /// Vendor-native saturation control.
    /// NVIDIA: Digital Vibrance through NVAPI.
    /// AMD: Saturation through ADL (with robust fallback for display matching).
    /// </summary>
    internal static class Saturation
    {
        private const int ADL_DISPLAY_COLOR_SATURATION = 0x00000004;
        private const uint NVAPI_OK = 0;
        private const uint NVAPI_DVC_GET_INFO = 0x4085DE45;
        private const uint NVAPI_DVC_SET_LEVEL = 0x172409B4;
        private const uint NVAPI_INITIALIZE = 0x0150E828;

        #region Structs & Delegates
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
        #endregion

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint flags);
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
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

        private static volatile bool isShuttingDown = false;

        private sealed class Binding
        {
            public WinApi.DisplayAdapterVendor Vendor;
            public IntPtr NvDisplayHandle;
            public int AdlAdapterIndex = -1;
            public int AdlDisplayIndex = -1;
        }

        private static readonly object _globalInitLock = new object();

        private static readonly ConcurrentDictionary<string, object> _adapterLocks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, Binding> bindings = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> originalValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> usedAmdTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static object GetLockForDisplay(Display.DisplayInfo display)
        {
            if (display == null) return _globalInitLock;
            string key = !string.IsNullOrEmpty(display.displayLink) ? display.displayLink : display.adapterVendor.ToString();
            return _adapterLocks.GetOrAdd(key, _ => new object());
        }

        public static bool Prepare(Display.DisplayInfo display)
        {
            if (display == null || isShuttingDown) return false;
            string linkKey = display.displayLink ?? string.Empty;

            object adapterLock = GetLockForDisplay(display);
            lock (adapterLock)
            {
                if (isShuttingDown) return false;

                try
                {
                    Binding b;
                    lock (_globalInitLock)
                    {
                        if (bindings.TryGetValue(linkKey, out b))
                        {
                            return true;
                        }
                    }

                    if (display.adapterVendor == WinApi.DisplayAdapterVendor.Nvidia)
                    {
                        if (PrepareNvidia(display, out b))
                        {
                            lock (_globalInitLock) { bindings[linkKey] = b; }
                            int current, def, min, max, step;
                            if (GetNvidia(b, out current, out def, out min, out max, out step))
                            {
                                SetDisplayRange(display, current, def, min, max, step);
                                lock (_globalInitLock)
                                {
                                    if (!originalValues.ContainsKey(linkKey))
                                        originalValues[linkKey] = current;
                                }
                                return true;
                            }
                        }
                    }
                    else if (display.adapterVendor == WinApi.DisplayAdapterVendor.Amd)
                    {
                        if (PrepareAmd(display, out b))
                        {
                            lock (_globalInitLock) { bindings[linkKey] = b; }
                            int current, def, min, max, step;
                            if (GetAmd(b, out current, out def, out min, out max, out step))
                            {
                                SetDisplayRange(display, current, def, min, max, step);
                                lock (_globalInitLock)
                                {
                                    if (!originalValues.ContainsKey(linkKey))
                                        originalValues[linkKey] = current;
                                }
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
            if (display == null || isShuttingDown) return false;
            if (!Prepare(display)) return false;

            value = Math.Max(display.saturationMin, Math.Min(display.saturationMax, value));
            string linkKey = display.displayLink ?? string.Empty;

            object adapterLock = GetLockForDisplay(display);
            lock (adapterLock)
            {
                if (isShuttingDown) return false;

                Binding b;
                lock (_globalInitLock)
                {
                    if (!bindings.TryGetValue(linkKey, out b)) return false;
                }

                try
                {
                    bool ok = false;
                    if (b.Vendor == WinApi.DisplayAdapterVendor.Nvidia && nvSetDvcLevel != null)
                        ok = nvSetDvcLevel(b.NvDisplayHandle, 0, (uint)value) == NVAPI_OK;
                    else if (b.Vendor == WinApi.DisplayAdapterVendor.Amd && adlColorSet != null)
                        ok = adlColorSet(b.AdlAdapterIndex, b.AdlDisplayIndex, ADL_DISPLAY_COLOR_SATURATION, value) == 0;

                    if (ok) display.saturation = value;
                    else Logger.Warn("Saturation apply failed for " + display.displayName + ". Vendor=" + b.Vendor + ", Value=" + value);

                    return ok;
                }
                catch (Exception ex)
                {
                    Logger.Error("Saturation apply threw an exception for " + display.displayName, ex);
                    return false;
                }
            }
        }

        public static void SetOriginalValue(string displayLink, int originalValue)
        {
            if (string.IsNullOrEmpty(displayLink)) return;
            lock (_globalInitLock)
            {
                originalValues[displayLink] = originalValue;
            }
        }

        public static void Reset()
        {
            lock (_globalInitLock)
            {
                if (isShuttingDown) return;

                foreach (KeyValuePair<string, int> pair in originalValues)
                {
                    if (!bindings.TryGetValue(pair.Key, out Binding b)) continue;
                    try
                    {
                        if (b.Vendor == WinApi.DisplayAdapterVendor.Nvidia && nvSetDvcLevel != null)
                            nvSetDvcLevel(b.NvDisplayHandle, 0, (uint)pair.Value);
                        else if (b.Vendor == WinApi.DisplayAdapterVendor.Amd && adlColorSet != null)
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
                _adapterLocks.Clear();
            }
        }

        public static void Shutdown()
        {
            lock (_globalInitLock)
            {
                isShuttingDown = true;
                try
                {
                    if (adlReady && adlDestroy != null)
                    {
                        try { adlDestroy(); } catch (Exception ex) { Logger.Warn("ADL shutdown failed: " + ex.Message); }
                    }
                }
                finally
                {
                    adlReady = false;
                    adlAlloc = null;
                    adlCreate = null;
                    adlDestroy = null;
                    adlAdapterCount = null;
                    adlDisplayInfoGet = null;
                    adlColorGet = null;
                    adlColorSet = null;
                    if (adl != IntPtr.Zero)
                    {
                        try { FreeLibrary(adl); } catch { }
                        adl = IntPtr.Zero;
                    }

                    nvReady = false;
                    nvQuery = null;
                    nvInitialize = null;
                    nvGetAssociatedDisplayHandle = null;
                    nvGetDvcInfo = null;
                    nvSetDvcLevel = null;
                    if (nvapi != IntPtr.Zero)
                    {
                        try { FreeLibrary(nvapi); } catch { }
                        nvapi = IntPtr.Zero;
                    }

                    bindings.Clear();
                    originalValues.Clear();
                    usedAmdTargets.Clear();
                    _adapterLocks.Clear();
                }
            }
        }

        private static bool PrepareNvidia(Display.DisplayInfo display, out Binding binding)
        {
            binding = null;
            if (string.IsNullOrEmpty(display.displayLink) || !EnsureNvidia()) return false;

            IntPtr handle;
            if (nvGetAssociatedDisplayHandle(display.displayLink, out handle) != NVAPI_OK || handle == IntPtr.Zero) return false;

            binding = new Binding { Vendor = WinApi.DisplayAdapterVendor.Nvidia, NvDisplayHandle = handle };
            return true;
        }

        private static bool GetNvidia(Binding b, out int current, out int def, out int min, out int max, out int step)
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
                step = 1;
                return true;
            }
            finally { Marshal.FreeHGlobal(mem); }
        }

        private static bool EnsureNvidia()
        {
            if (nvReady) return true;
            lock (_globalInitLock)
            {
                if (nvReady || isShuttingDown) return nvReady;
                try
                {
                    nvapi = LoadLibraryEx("nvapi64.dll", IntPtr.Zero, LOAD_LIBRARY_SEARCH_SYSTEM32);
                    if (nvapi == IntPtr.Zero)
                        nvapi = LoadLibraryEx("nvapi.dll", IntPtr.Zero, LOAD_LIBRARY_SEARCH_SYSTEM32);
                    if (nvapi == IntPtr.Zero) return false;

                    IntPtr p = GetProcAddress(nvapi, "nvapi_QueryInterface");
                    if (p == IntPtr.Zero) { CleanupNvidiaLoadFailure(); return false; }

                    nvQuery = (NvQueryInterfaceDelegate)Marshal.GetDelegateForFunctionPointer(p, typeof(NvQueryInterfaceDelegate));
                    nvInitialize = DelegateFromQuery<NvInitializeDelegate>(NVAPI_INITIALIZE);
                    nvGetAssociatedDisplayHandle = DelegateFromQuery<NvGetAssociatedDisplayHandleDelegate>(0x35C29134);
                    nvGetDvcInfo = DelegateFromQuery<NvGetDvcInfoDelegate>(NVAPI_DVC_GET_INFO);
                    nvSetDvcLevel = DelegateFromQuery<NvSetDvcLevelDelegate>(NVAPI_DVC_SET_LEVEL);

                    if (nvInitialize == null || nvGetAssociatedDisplayHandle == null || nvGetDvcInfo == null || nvSetDvcLevel == null)
                    {
                        CleanupNvidiaLoadFailure();
                        return false;
                    }

                    if (nvInitialize() != NVAPI_OK)
                    {
                        CleanupNvidiaLoadFailure();
                        return false;
                    }

                    nvReady = true;
                    return true;
                }
                catch
                {
                    CleanupNvidiaLoadFailure();
                    return false;
                }
            }
        }

        private static void CleanupNvidiaLoadFailure()
        {
            nvReady = false;
            nvQuery = null;
            nvInitialize = null;
            nvGetAssociatedDisplayHandle = null;
            nvGetDvcInfo = null;
            nvSetDvcLevel = null;
            if (nvapi != IntPtr.Zero)
            {
                try { FreeLibrary(nvapi); } catch { }
                nvapi = IntPtr.Zero;
            }
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

            Binding fallbackCandidate = null;

            for (int adapter = 0; adapter < count; adapter++)
            {
                int n = 0;
                IntPtr ptr = IntPtr.Zero;
                try
                {
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

                        if ((info.DisplayInfoValue & 0x00000001) == 0) continue;

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
                        if (!GetAmd(candidate, out current, out def, out min, out max, out step))
                            continue;

                        string targetKey = adapter.ToString() + ":" + info.DisplayID.LogicalIndex.ToString();

                        lock (_globalInitLock)
                        {
                            if (usedAmdTargets.Contains(targetKey)) continue;

                            if (nameMatch || (makerMatch && logicalMatch) || logicalMatch)
                            {
                                binding = candidate;
                                usedAmdTargets.Add(targetKey);
                                return true;
                            }

                            if (fallbackCandidate == null)
                            {
                                fallbackCandidate = candidate;
                            }
                        }
                    }
                }
                finally
                {
                    if (ptr != IntPtr.Zero)
                    {
                        try { Marshal.FreeHGlobal(ptr); } catch { }
                    }
                }
            }

            lock (_globalInitLock)
            {
                if (fallbackCandidate != null)
                {
                    binding = fallbackCandidate;
                    string targetKey = fallbackCandidate.AdlAdapterIndex.ToString() + ":" + fallbackCandidate.AdlDisplayIndex.ToString();
                    usedAmdTargets.Add(targetKey);
                    return true;
                }
            }

            return false;
        }

        private static bool GetAmd(Binding b, out int current, out int def, out int min, out int max, out int step)
        {
            current = def = min = max = step = 0;
            int rc = adlColorGet(b.AdlAdapterIndex, b.AdlDisplayIndex, ADL_DISPLAY_COLOR_SATURATION, out current, out def, out min, out max, out step);
            if (rc != 0 || max < min) return false;
            return true;
        }

        private static bool EnsureAmd()
        {
            if (adlReady) return true;
            lock (_globalInitLock)
            {
                if (adlReady || isShuttingDown) return adlReady;
                try
                {
                    adl = LoadLibraryEx("atiadlxx.dll", IntPtr.Zero, LOAD_LIBRARY_SEARCH_SYSTEM32);
                    if (adl == IntPtr.Zero) adl = LoadLibraryEx("atiadlxy.dll", IntPtr.Zero, LOAD_LIBRARY_SEARCH_SYSTEM32);
                    if (adl == IntPtr.Zero) return false;

                    adlAlloc = delegate (int size) { return Marshal.AllocHGlobal(size); };
                    adlCreate = GetAdlDelegate<AdlMainControlCreateDelegate>("ADL_Main_Control_Create");
                    adlDestroy = GetAdlDelegate<AdlMainControlDestroyDelegate>("ADL_Main_Control_Destroy");
                    adlAdapterCount = GetAdlDelegate<AdlAdapterNumberOfAdaptersGetDelegate>("ADL_Adapter_NumberOfAdapters_Get");
                    adlDisplayInfoGet = GetAdlDelegate<AdlDisplayDisplayInfoGetDelegate>("ADL_Display_DisplayInfo_Get");
                    adlColorGet = GetAdlDelegate<AdlDisplayColorGetDelegate>("ADL_Display_Color_Get");
                    adlColorSet = GetAdlDelegate<AdlDisplayColorSetDelegate>("ADL_Display_Color_Set");

                    if (adlCreate == null || adlAdapterCount == null || adlDisplayInfoGet == null || adlColorGet == null || adlColorSet == null)
                    {
                        CleanupAmdLoadFailure();
                        return false;
                    }

                    if (adlCreate(adlAlloc, 1) != 0)
                    {
                        CleanupAmdLoadFailure();
                        return false;
                    }

                    adlReady = true;
                    return true;
                }
                catch
                {
                    CleanupAmdLoadFailure();
                    return false;
                }
            }
        }

        private static void CleanupAmdLoadFailure()
        {
            try { if (adlReady && adlDestroy != null) adlDestroy(); } catch { }
            adlReady = false;
            adlAlloc = null;
            adlCreate = null;
            adlDestroy = null;
            adlAdapterCount = null;
            adlDisplayInfoGet = null;
            adlColorGet = null;
            adlColorSet = null;
            if (adl != IntPtr.Zero)
            {
                try { FreeLibrary(adl); } catch { }
                adl = IntPtr.Zero;
            }
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
            if (end == start) return -1;
            return int.TryParse(name.Substring(start, end - start), out int n) ? n - 1 : -1;
        }

        public static void ClearBindings()
        {
            lock (_globalInitLock)
            {
                bindings.Clear();
                originalValues.Clear();
                usedAmdTargets.Clear();
                _adapterLocks.Clear();
            }
        }
    }
}