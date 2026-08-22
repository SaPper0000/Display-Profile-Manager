namespace Gamma_Manager
{
    internal static class LanguageManager
    {
        public static bool Korean { get; private set; } = true;

        public static void SetLanguage(bool korean)
        {
            Korean = korean;
        }
    }
}
