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
            return registered;
        }

        public void Unregister()
        {
            if (registered && windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(windowHandle, Id);
                registered = false;
            }
        }

        public bool ProcessMessage(ref Message m)
        {
            if (m.Msg != WM_HOTKEY || m.WParam.ToInt32() != Id) return false;
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
