using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal static class Program
    {
        private const string SingleInstanceName = "Tarkov-Gamma-Manager-SingleInstance";
        private static Mutex _singleInstanceMutex;

        [STAThread]
        static void Main(string[] args)
        {
            Logger.Initialize();
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            bool isRestart = args != null && Array.Exists(args, a =>
                string.Equals(a, "--restart", StringComparison.OrdinalIgnoreCase));

            bool createdNew = false;

            if (isRestart)
            {
                _singleInstanceMutex = new Mutex(false, SingleInstanceName);
                try
                {
                    _singleInstanceMutex.WaitOne();
                    createdNew = true;
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
                Logger.Info("Second launch detected; exiting because another instance is already running.");
                var iniFile = new IniFile(
                    System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "GammaManager.ini"));
                string savedLanguage = iniFile.Read("Language", "Settings");
                bool korean = !string.Equals(savedLanguage, "English", StringComparison.OrdinalIgnoreCase);

                MessageBox.Show(
                    korean ? "프로그램이 이미 실행 중입니다." : "The program is already running.",
                    korean ? "Gamma Manager" : "Gamma Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
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
                try { _singleInstanceMutex.ReleaseMutex(); } catch (Exception ex) { Logger.Warn("Failed to release single-instance mutex: " + ex.Message); }
                try { _singleInstanceMutex.Dispose(); } catch { }
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
