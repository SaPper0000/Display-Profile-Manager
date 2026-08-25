using System;

namespace Gamma_Manager
{
    internal static class LanguageManager
    {
        public static bool Korean { get; private set; } = true;

        public static event Action OnLanguageChanged;

        public static void SetLanguage(bool korean)
        {
            if (Korean == korean) return;
            Korean = korean;
            OnLanguageChanged?.Invoke();
        }
    }
}