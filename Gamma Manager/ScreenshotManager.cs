using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Media;

namespace Gamma_Manager
{
    internal static class ScreenshotManager
    {
        private const string SectionName = "Screenshot";

        public static string GetHotkey(IniFile iniFile)
        {
            return iniFile?.Read("Hotkey", SectionName) ?? string.Empty;
        }

        public static void SetHotkey(IniFile iniFile, string value)
        {
            iniFile?.Write("Hotkey", value ?? string.Empty, SectionName);
        }

        public static int GetTarget(IniFile iniFile)
        {
            if (iniFile == null) return 0;
            string val = iniFile.Read("Target", SectionName);
            int res;
            return int.TryParse(val, out res) ? res : 0;
        }

        public static void SetTarget(IniFile iniFile, int value)
        {
            iniFile?.Write("Target", value.ToString(), SectionName);
        }

        public static bool GetClipboard(IniFile iniFile)
        {
            if (iniFile == null) return true;
            string val = iniFile.Read("Clipboard", SectionName);
            if (string.IsNullOrEmpty(val)) return true;
            return val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static void SetClipboard(IniFile iniFile, bool value)
        {
            iniFile?.Write("Clipboard", value ? "1" : "0", SectionName);
        }

        public static bool GetSaveFile(IniFile iniFile)
        {
            if (iniFile == null) return true;
            string val = iniFile.Read("SaveFile", SectionName);
            if (string.IsNullOrEmpty(val)) return true;
            return val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static void SetSaveFile(IniFile iniFile, bool value)
        {
            iniFile?.Write("SaveFile", value ? "1" : "0", SectionName);
        }

        public static string GetDefaultSaveDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DisplayProfileManager");
        }

        public static string GetSaveDir(IniFile iniFile)
        {
            string defaultDir = GetDefaultSaveDir();
            if (iniFile == null) return defaultDir;
            string val = iniFile.Read("SaveDir", SectionName);
            return string.IsNullOrEmpty(val) ? defaultDir : val;
        }

        public static void SetSaveDir(IniFile iniFile, string value)
        {
            iniFile?.Write("SaveDir", string.IsNullOrEmpty(value) ? GetDefaultSaveDir() : value, SectionName);
        }

        public static string GetFormat(IniFile iniFile)
        {
            if (iniFile == null) return "PNG";
            string val = iniFile.Read("Format", SectionName);
            return string.IsNullOrEmpty(val) ? "PNG" : val;
        }

        public static void SetFormat(IniFile iniFile, string value)
        {
            iniFile?.Write("Format", string.IsNullOrEmpty(value) ? "PNG" : value, SectionName);
        }

        public static bool GetPlaySound(IniFile iniFile)
        {
            if (iniFile == null) return true;
            string val = iniFile.Read("PlaySound", SectionName);
            if (string.IsNullOrEmpty(val)) return true;
            return val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static void SetPlaySound(IniFile iniFile, bool value)
        {
            iniFile?.Write("PlaySound", value ? "1" : "0", SectionName);
        }

        public static bool GetShowOSD(IniFile iniFile)
        {
            if (iniFile == null) return true;
            string val = iniFile.Read("ShowOSD", SectionName);
            if (string.IsNullOrEmpty(val)) return true;
            return val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static void SetShowOSD(IniFile iniFile, bool value)
        {
            iniFile?.Write("ShowOSD", value ? "1" : "0", SectionName);
        }

        public static void OpenSaveFolder(IniFile iniFile)
        {
            try
            {
                string dir = GetSaveDir(iniFile);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                System.Diagnostics.Process.Start(dir);
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to open screenshot save folder: " + ex.Message);
            }
        }

        public static bool Capture(Window mainWindow, IniFile iniFile, List<Display.DisplayInfo> displays, Display.DisplayInfo currDisplay)
        {
            try
            {
                int target = GetTarget(iniFile);
                Rectangle bounds;
                Screen targetScreen = null;

                if (target == 1)
                {
                    targetScreen = Screen.PrimaryScreen;
                    bounds = targetScreen.Bounds;
                }
                else if (target == 2)
                {
                    bounds = SystemInformation.VirtualScreen;
                }
                else
                {
                    targetScreen = Screen.FromPoint(Cursor.Position);
                    bounds = targetScreen.Bounds;
                }

                Display.DisplayInfo targetDisplay = null;
                if (targetScreen != null && displays != null)
                {
                    foreach (var d in displays)
                    {
                        if (string.Equals(d.displayLink, targetScreen.DeviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            targetDisplay = d;
                            break;
                        }
                    }
                }

                if (targetDisplay == null)
                {
                    targetDisplay = currDisplay ?? (displays != null && displays.Count > 0 ? displays[0] : null);
                }

                using (Bitmap bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                    }

                    ApplyProfileFilter(bmp, targetDisplay);

                    bool useClipboard = GetClipboard(iniFile);
                    bool saveFile = GetSaveFile(iniFile);
                    bool playSound = GetPlaySound(iniFile);
                    bool showOSD = GetShowOSD(iniFile);
                    string format = GetFormat(iniFile);

                    if (useClipboard)
                    {
                        SetClipboardImageSafe(bmp);
                    }

                    if (saveFile)
                    {
                        try
                        {
                            string dir = GetSaveDir(iniFile);
                            if (!Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }

                            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            bool isJpg = format != null && format.IndexOf("JPG", StringComparison.OrdinalIgnoreCase) >= 0;
                            string ext = isJpg ? "jpg" : "png";
                            string fileName = string.Format("Screenshot_{0}.{1}", timestamp, ext);
                            string filePath = Path.Combine(dir, fileName);

                            int counter = 1;
                            while (File.Exists(filePath))
                            {
                                fileName = string.Format("Screenshot_{0}_{1}.{2}", timestamp, counter, ext);
                                filePath = Path.Combine(dir, fileName);
                                counter++;
                            }

                            if (string.Equals(ext, "jpg", StringComparison.OrdinalIgnoreCase))
                            {
                                ImageCodecInfo jpgEncoder = GetEncoder(ImageFormat.Jpeg);
                                if (jpgEncoder != null)
                                {
                                    using (EncoderParameters encoderParams = new EncoderParameters(1))
                                    using (EncoderParameter qualityParam = new EncoderParameter(Encoder.Quality, 95L))
                                    {
                                        encoderParams.Param[0] = qualityParam;
                                        bmp.Save(filePath, jpgEncoder, encoderParams);
                                    }
                                }
                                else
                                {
                                    bmp.Save(filePath, ImageFormat.Jpeg);
                                }
                            }
                            else
                            {
                                bmp.Save(filePath, ImageFormat.Png);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn("Failed to save screenshot to file: " + ex.Message);
                        }
                    }

                    if (playSound)
                    {
                        PlayShutterSound();
                    }

                    if (showOSD)
                    {
                        try
                        {
                            string msg = LanguageManager.Korean ? "📸 스크린샷 캡처 완료" : "📸 Screenshot Captured";
                            string displayLink = targetDisplay != null ? targetDisplay.displayLink : (targetScreen != null ? targetScreen.DeviceName : null);
                            OSDForm.ShowMessage(displayLink, msg);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn("Failed to show OSD for screenshot: " + ex.Message);
                        }
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Screenshot Capture failed: " + ex.Message);
                return false;
            }
        }

        private static void PlayShutterSound()
        {
            try
            {
                // 1순위: Windows 탐색기 경쾌한 찰칵/딸깍 사운드
                string navSound = @"C:\Windows\Media\Windows Navigation Start.wav";
                if (File.Exists(navSound))
                {
                    using (SoundPlayer player = new SoundPlayer(navSound))
                    {
                        player.Play();
                        return;
                    }
                }

                // 2순위: 팝업 차단음 (짧은 틱 사운드)
                string popupSound = @"C:\Windows\Media\Windows Pop-up Blocked.wav";
                if (File.Exists(popupSound))
                {
                    using (SoundPlayer player = new SoundPlayer(popupSound))
                    {
                        player.Play();
                        return;
                    }
                }

                // 3순위: 기본 비프음 (짧고 조용한 톤 1200Hz, 35ms)
                Console.Beep(1200, 35);
            }
            catch
            {
                // Ignore sound failures
            }
        }

        private static void SetClipboardImageSafe(Bitmap bmp)
        {
            if (bmp == null) return;
            Thread staThread = new Thread(() =>
            {
                try
                {
                    // using 블록 밖에서 bmp가 Dispose되어도 안전하도록 독립된 클론 비트맵 생성 및 copy: true 설정
                    using (Bitmap copyBmp = new Bitmap(bmp))
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            try
                            {
                                Clipboard.SetDataObject(copyBmp, true, 5, 100);
                                break;
                            }
                            catch (ExternalException)
                            {
                                Thread.Sleep(50);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Clipboard.SetDataObject failed on STA thread: " + ex.Message);
                }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join(1000);
        }

        private static void ApplyProfileFilter(Bitmap bmp, Display.DisplayInfo display)
        {
            float rGamma = display != null ? display.rGamma : 1.0f;
            float gGamma = display != null ? display.gGamma : 1.0f;
            float bGamma = display != null ? display.bGamma : 1.0f;

            float rContrast = display != null ? display.rContrast : 1.0f;
            float gContrast = display != null ? display.gContrast : 1.0f;
            float bContrast = display != null ? display.bContrast : 1.0f;

            float rBright = display != null ? display.rBright : 0.0f;
            float gBright = display != null ? display.gBright : 0.0f;
            float bBright = display != null ? display.bBright : 0.0f;

            int sb = display != null ? display.shadowBoost : 0;
            int sbm = display != null ? display.shadowBoostMode : 0;
            int sbt = display != null ? display.shadowBoostTint : 0;

            int hg = display != null ? display.highlightGuard : 0;
            int peak = (display != null && display.shadowBoostCustomPeak > 0) ? display.shadowBoostCustomPeak : 25;
            int width = (display != null && display.shadowBoostCustomWidth > 0) ? display.shadowBoostCustomWidth : 3;

            int saturation = display != null ? display.saturation : 100;

            double rTint = 1.0;
            double gTint = 1.0;
            double bTint = 1.0;

            if (sbt == 1)
            {
                rTint = 1.15;
                gTint = 1.05;
                bTint = 0.85;
            }
            else if (sbt == 2)
            {
                rTint = 0.85;
                gTint = 1.05;
                bTint = 1.15;
            }

            byte[] lutR = new byte[256];
            byte[] lutG = new byte[256];
            byte[] lutB = new byte[256];

            for (int i = 0; i < 256; i++)
            {
                double inVal = (double)i / 255.0;
                double outR = Gamma.ApplyChannelCurve(inVal, rGamma, rContrast, rBright, sb, sbm, rTint, hg, peak, width);
                double outG = Gamma.ApplyChannelCurve(inVal, gGamma, gContrast, gBright, sb, sbm, gTint, hg, peak, width);
                double outB = Gamma.ApplyChannelCurve(inVal, bGamma, bContrast, bBright, sb, sbm, bTint, hg, peak, width);

                lutR[i] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(outR * 255.0)));
                lutG[i] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(outG * 255.0)));
                lutB[i] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(outB * 255.0)));
            }

            BitmapData bData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
            try
            {
                int byteCount = Math.Abs(bData.Stride) * bmp.Height;
                byte[] pixels = new byte[byteCount];
                Marshal.Copy(bData.Scan0, pixels, 0, byteCount);

                float satFactor = (float)saturation / 100.0f;
                bool applySat = Math.Abs(satFactor - 1.0f) > 0.001f;

                int height = bmp.Height;
                int widthVal = bmp.Width;
                int stride = bData.Stride;

                for (int y = 0; y < height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < widthVal; x++)
                    {
                        int idx = rowOffset + (x * 4);
                        byte b = lutB[pixels[idx]];
                        byte g = lutG[pixels[idx + 1]];
                        byte r = lutR[pixels[idx + 2]];

                        if (applySat)
                        {
                            double gray = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                            double adjR = gray + (r - gray) * satFactor;
                            double adjG = gray + (g - gray) * satFactor;
                            double adjB = gray + (b - gray) * satFactor;

                            pixels[idx] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(adjB)));
                            pixels[idx + 1] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(adjG)));
                            pixels[idx + 2] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(adjR)));
                        }
                        else
                        {
                            pixels[idx] = b;
                            pixels[idx + 1] = g;
                            pixels[idx + 2] = r;
                        }
                    }
                }

                Marshal.Copy(pixels, 0, bData.Scan0, byteCount);
            }
            finally
            {
                bmp.UnlockBits(bData);
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }
}