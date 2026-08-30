using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal static class Program
    {
        private const string SingleInstanceName = "Display-Profile-Manager-SingleInstance";
        private static Mutex _singleInstanceMutex;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        [STAThread]
        static void Main(string[] args)
        {
            // 전역 예외 처리 정책 고정
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            Logger.Initialize();

            bool isRestart = args != null && Array.Exists(args, a =>
                string.Equals(a, "--restart", StringComparison.OrdinalIgnoreCase));

            bool createdNew = false;

            if (isRestart)
            {
                _singleInstanceMutex = new Mutex(false, SingleInstanceName);
                try
                {
                    createdNew = _singleInstanceMutex.WaitOne(3000, false);
                    if (!createdNew)
                    {
                        Logger.Warn("Restart mutex timed out waiting for previous instance.");
                    }
                }
                catch (AbandonedMutexException)
                {
                    createdNew = true;
                    Logger.Warn("Restart mutex was abandoned; continuing with new instance.");
                }
            }
            else
            {
                _singleInstanceMutex = new Mutex(true, SingleInstanceName, out createdNew);
            }

            if (!createdNew)
            {
                Logger.Info("Second launch detected; bringing existing window to front.");

                // 현재 실행 중인 동일 프로세스를 탐색하여 메인 윈도우 핸들 취득 (버전 하드코딩 제거)
                System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess();
                System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName(current.ProcessName);
                IntPtr hWnd = IntPtr.Zero;

                foreach (System.Diagnostics.Process p in processes)
                {
                    if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                    {
                        hWnd = p.MainWindowHandle;
                        break;
                    }
                }

                if (hWnd != IntPtr.Zero)
                {
                    ShowWindow(hWnd, SW_RESTORE);
                    SetForegroundWindow(hWnd);
                }
                else
                {
                    var iniFile = new IniFile();
                    string savedLanguage = iniFile.Read("Language", "Settings");
                    bool korean = !string.Equals(savedLanguage, "English", StringComparison.OrdinalIgnoreCase);

                    MessageBox.Show(
                   korean ? "프로그램이 이미 실행 중입니다." : "The program is already running.",
                   "Display Profile Manager",
                   MessageBoxButtons.OK,
                   MessageBoxIcon.Information);
                }

                _singleInstanceMutex.Dispose();
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Logger.Info("Application.Run starting.");
                Application.Run(new Window());
                Logger.Info("Application.Run finished normally.");
            }
            catch (Exception ex)
            {
                Logger.Error("Fatal exception escaped the UI application loop.", ex);
                throw;
            }
            finally
            {
                if (createdNew && _singleInstanceMutex != null)
                {
                    try { _singleInstanceMutex.ReleaseMutex(); } catch (Exception ex) { Logger.Warn("Failed to release single-instance mutex: " + ex.Message); }
                    try { _singleInstanceMutex.Dispose(); } catch { }
                }
            }
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Logger.Error("Unhandled Windows Forms UI exception.", e.Exception);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Logger.Error("Unhandled AppDomain exception. IsTerminating=" + e.IsTerminating, e.ExceptionObject as Exception);
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error("Unobserved Task exception.", e.Exception);
            e.SetObserved();
        }
    }
}