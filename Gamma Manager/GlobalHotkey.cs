using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class GlobalHotkey : IDisposable
    {
        [Flags]
        internal enum Modifiers : uint
        {
            None = 0x0000,
            Alt = 0x0001,
            Control = 0x0002,
            Shift = 0x0004,
            Win = 0x0008
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("imm32.dll")]
        private static extern uint ImmGetVirtualKey(IntPtr hWnd);

        private const uint MAPVK_VSC_TO_VK = 0;

        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12; // Alt
        public const int VK_PAUSE = 0x13;
        public const int VK_ESCAPE = 0x1B;
        public const int VK_SPACE = 0x20;
        public const int VK_PRIOR = 0x21; // PageUp
        public const int VK_NEXT = 0x22;  // PageDown
        public const int VK_END = 0x23;
        public const int VK_HOME = 0x24;
        public const int VK_LEFT = 0x25;
        public const int VK_UP = 0x26;
        public const int VK_RIGHT = 0x27;
        public const int VK_DOWN = 0x28;
        public const int VK_SNAPSHOT = 0x2C; // PrintScreen
        public const int VK_INSERT = 0x2D;
        public const int VK_DELETE = 0x2E;
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;
        public const int VK_SCROLL = 0x91;
        public const int VK_LSHIFT = 0xA0;
        public const int VK_RSHIFT = 0xA1;
        public const int VK_LCONTROL = 0xA2;
        public const int VK_RCONTROL = 0xA3;
        public const int VK_LMENU = 0xA4;
        public const int VK_RMENU = 0xA5;

        public static readonly int[] CandidateVirtualKeys = new int[]
        {
            // Function keys (F1 - F24) - 최우선 순위
            0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x7B, // F1-F12
            0x7C, 0x7D, 0x7E, 0x7F, 0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, // F13-F24
            // Alpha keys (A - Z)
            0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D,
            0x4E, 0x4F, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A,
            // Digits (0 - 9)
            0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
            // Numpad (0 - 9, operations)
            0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
            0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F,
            // Navigation & editing
            0x21, 0x22, 0x23, 0x24, // PgUp, PgDn, End, Home
            0x25, 0x26, 0x27, 0x28, // Left, Up, Right, Down
            0x2C, 0x2D, 0x2E, // PrintScreen, Insert, Delete
            0x13, 0x91, // Pause, ScrollLock
            0x08, // Backspace
            0x20, // Space (조합키 필요)
            // OEM keys
            0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0,
            0xDB, 0xDC, 0xDD, 0xDE, 0xE2
        };

        public static bool IsCandidateKey(Keys key)
        {
            int vk = (int)(key & Keys.KeyCode);
            for (int i = 0; i < CandidateVirtualKeys.Length; i++)
            {
                if (CandidateVirtualKeys[i] == vk)
                    return true;
            }
            return false;
        }

        public static string FormatHotkey(Keys key, Modifiers modifiers, string noneText = null)
        {
            if (key == Keys.None)
            {
                return noneText ?? (LanguageManager.Korean ? "없음 (지정)" : "None (Set)");
            }

            string text = "";
            if ((modifiers & Modifiers.Control) != 0) text += "Ctrl + ";
            if ((modifiers & Modifiers.Alt) != 0) text += "Alt + ";
            if ((modifiers & Modifiers.Shift) != 0) text += "Shift + ";
            if ((modifiers & Modifiers.Win) != 0) text += "Win + ";
            return text + key.ToString();
        }

        public static bool TryParseHotkey(string value, out Keys key, out Modifiers modifiers)
        {
            key = Keys.None;
            modifiers = Modifiers.None;
            if (string.IsNullOrWhiteSpace(value)) return false;

            string[] parts = value.Split('+');
            string keyText = parts[parts.Length - 1].Trim();
            if (!Enum.TryParse(keyText, true, out key) || key == Keys.None) return false;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= Modifiers.Control;
                        break;
                    case "alt":
                        modifiers |= Modifiers.Alt;
                        break;
                    case "shift":
                        modifiers |= Modifiers.Shift;
                        break;
                    case "win":
                    case "windows":
                        modifiers |= Modifiers.Win;
                        break;
                    default:
                        return false;
                }
            }
            return true;
        }

        public static Modifiers GetCurrentModifiers()
        {
            return GetModifiersFromKeyData(Control.ModifierKeys, false);
        }

        public static Modifiers GetModifiersFromKeyData(Keys keyData, bool winKeyHeld = false)
        {
            Modifiers mods = Modifiers.None;
            Keys combined = Control.ModifierKeys | keyData;

            if ((combined & Keys.Control) == Keys.Control)
                mods |= Modifiers.Control;
            if ((combined & Keys.Alt) == Keys.Alt)
                mods |= Modifiers.Alt;
            if ((combined & Keys.Shift) == Keys.Shift)
                mods |= Modifiers.Shift;
            if (winKeyHeld)
                mods |= Modifiers.Win;

            return mods;
        }

        /// <summary>
        /// 한글 IME 활성 상태에서 알파벳 키 입력 시 발생하는 VK_PROCESSKEY(0xE5)를
        /// GetAsyncKeyState 폴링 없이 메시지 스캔코드(MapVirtualKey) 및 ImmGetVirtualKey로 안전하게 복원합니다.
        /// </summary>
        public static Keys ResolveKeyCode(ref Message msg, Keys keyData)
        {
            Keys keyCode = keyData & Keys.KeyCode;
            if (keyCode == Keys.ProcessKey)
            {
                uint scanCode = (uint)((msg.LParam.ToInt64() >> 16) & 0xFF);
                if (scanCode != 0)
                {
                    uint mappedVk = MapVirtualKey(scanCode, MAPVK_VSC_TO_VK);
                    if (mappedVk != 0 && mappedVk != 0xE5)
                    {
                        keyCode = (Keys)mappedVk;
                    }
                }

                if (keyCode == Keys.ProcessKey && msg.HWnd != IntPtr.Zero)
                {
                    try
                    {
                        uint immVk = ImmGetVirtualKey(msg.HWnd);
                        if (immVk != 0 && immVk != 0xE5)
                        {
                            keyCode = (Keys)immVk;
                        }
                    }
                    catch { }
                }
            }
            return keyCode;
        }

        /// <summary>
        /// 설정 창 포커스 상태에서 윈도우 메시지(ProcessCmdKey)만으로 안전하게 핫키 입력을 판정합니다.
        /// (GetAsyncKeyState 루프 폴링을 사용하지 않아 안티치트 오탐 여지를 원천 차단합니다.)
        /// </summary>
        public static bool TryExtractHotkeyFromCmdKey(
            ref Message msg,
            Keys keyData,
            ref bool winKeyHeld,
            out Keys key,
            out Modifiers modifiers)
        {
            key = Keys.None;
            modifiers = Modifiers.None;

            Keys keyCode = ResolveKeyCode(ref msg, keyData);

            // 1. ESC = 해제 / 취소
            if (keyCode == Keys.Escape)
            {
                winKeyHeld = false;
                key = Keys.Escape;
                return true;
            }

            // 2. Windows 조합키 눌림 상태 추적
            if (keyCode == Keys.LWin || keyCode == Keys.RWin)
            {
                winKeyHeld = true;
                return false;
            }

            // 3. 조합키 단독 입력 시에는 일반 키 입력을 계속 대기
            bool isModifierOnly =
                keyCode == Keys.ControlKey || keyCode == Keys.LControlKey || keyCode == Keys.RControlKey ||
                keyCode == Keys.Menu || keyCode == Keys.LMenu || keyCode == Keys.RMenu ||
                keyCode == Keys.ShiftKey || keyCode == Keys.LShiftKey || keyCode == Keys.RShiftKey;
            if (isModifierOnly || keyCode == Keys.None)
            {
                return false;
            }

            Modifiers currentMods = GetModifiersFromKeyData(keyData, winKeyHeld);

            // 4. Space 키는 단독 지정 방지 (조합키 필수)
            if (keyCode == Keys.Space && currentMods == Modifiers.None)
            {
                return false;
            }

            // 5. 유효한 핫키 후보 키인지 검증 (Enter, Tab, 한/영, 한자, CapsLock 등 오입력 차단)
            if (!IsCandidateKey(keyCode))
            {
                return false;
            }

            winKeyHeld = false;
            key = keyCode;
            modifiers = currentMods;
            return true;
        }

        private const int WM_HOTKEY = 0x0312;
        private readonly IntPtr windowHandle;
        private bool registered;
        private bool disposed;

        public int Id { get; private set; }
        public Modifiers ModifierKeys { get; private set; }
        public Keys Key { get; private set; }
        public event EventHandler Pressed;

        public GlobalHotkey(IntPtr windowHandle, int id, Keys key, Modifiers modifiers)
        {
            this.windowHandle = windowHandle;
            Id = id;
            Key = key;
            ModifierKeys = modifiers;
        }

        public bool Register()
        {
            if (disposed || windowHandle == IntPtr.Zero) return false;
            registered = RegisterHotKey(windowHandle, Id, (uint)ModifierKeys, (uint)Key);
            if (!registered)
                Logger.Warn("RegisterHotKey failed. Id=" + Id + ", Modifiers=" + ModifierKeys + ", Key=" + Key + ", Win32Error=" + Marshal.GetLastWin32Error());
            else
                Logger.Info("Global hotkey registered. Id=" + Id + ", Modifiers=" + ModifierKeys + ", Key=" + Key);
            return registered;
        }

        public void Unregister()
        {
            if (registered && windowHandle != IntPtr.Zero)
            {
                if (!UnregisterHotKey(windowHandle, Id))
                    Logger.Warn("UnregisterHotKey failed. Id=" + Id + ", Win32Error=" + Marshal.GetLastWin32Error());
                registered = false;
            }
        }

        public static bool IsHotkeyAvailableInOS(IntPtr hWnd, Keys key, Modifiers modifiers, out int win32Error)
        {
            win32Error = 0;
            if (key == Keys.None || hWnd == IntPtr.Zero) return true;

            const int testId = 0x7FFE;
            bool success = RegisterHotKey(hWnd, testId, (uint)modifiers, (uint)key);
            if (success)
            {
                try
                {
                    return true;
                }
                finally
                {
                    UnregisterHotKey(hWnd, testId);
                }
            }
            win32Error = Marshal.GetLastWin32Error();
            return false;
        }

        private long lastTimestamp = 0;
        private static readonly long MinIntervalTicks = (System.Diagnostics.Stopwatch.Frequency * 180) / 1000; // 180ms 초고속 반복 입력(Auto-repeat) 방지

        public bool ProcessMessage(ref Message m)
        {
            if (m.Msg != WM_HOTKEY || m.WParam.ToInt32() != Id) return false;

            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (lastTimestamp != 0 && (now - lastTimestamp) < MinIntervalTicks && now >= lastTimestamp) return true;
            lastTimestamp = now;

            EventHandler handler = Pressed;
            if (handler != null) handler(this, EventArgs.Empty);
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Unregister();
        }
    }
}
