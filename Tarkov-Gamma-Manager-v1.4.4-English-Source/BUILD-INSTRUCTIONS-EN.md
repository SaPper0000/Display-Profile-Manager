# Build Instructions

1. Open `Tarkov-Gamma-Manager-v1.4.sln` in Visual Studio.
2. Select **Release | Any CPU**.
3. Confirm `Prefer32Bit=false`.
4. Run **Rebuild Solution**.
5. The application is built as a normal portable WinForms executable.
6. Do not use Visual Studio Publish / ClickOnce.

Target framework: **.NET Framework 4.7.2**

For v1.4.4, the English build is generated from the Korean source as the master implementation, with the language flag set to English. This keeps the AMD Saturation, NVIDIA Digital Vibrance, per-monitor handling, numeric value input, and bug fixes synchronized between builds.
