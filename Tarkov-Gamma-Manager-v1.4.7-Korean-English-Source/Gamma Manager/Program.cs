using System;
using System.Threading;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal static class Program
    {
        // 프로그램이 여러 개 실행되지 않도록 하는 전역 Mutex
        private static Mutex _singleInstanceMutex;

        [STAThread]
        static void Main()
        {
            bool createdNew;
            _singleInstanceMutex = new Mutex(
                true,
                "Tarkov-Gamma-Manager-SingleInstance",
                out createdNew);

            // 이미 실행 중이면 저장된 UI 언어에 맞는 안내 메시지를 표시하고 새 프로세스는 종료
            if (!createdNew)
            {
                // 첫 번째 인스턴스가 아직 Window를 만들기 전이어도 설정 파일에서 언어를 읽습니다.
                var iniFile = new IniFile();
                string savedLanguage = iniFile.Read("Language", "Settings");
                bool korean = !string.Equals(savedLanguage, "English", StringComparison.OrdinalIgnoreCase);

                MessageBox.Show(
                    korean ? "프로그램이 이미 실행 중입니다." : "The program is already running.",
                    korean ? "Gamma Manager" : "Gamma Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Window());
            }
            finally
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
        }
    }
}
