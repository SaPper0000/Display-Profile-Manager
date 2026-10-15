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

        public static bool GetHdrBypass(IniFile iniFile)
        {
            if (iniFile == null) return true;
            string val = iniFile.Read("HdrBypass", SectionName);
            if (string.IsNullOrEmpty(val)) return true; // 기본값 ON (화이트아웃 방지 활성화)
            return val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static void SetHdrBypass(IniFile iniFile, bool value)
        {
            iniFile?.Write("HdrBypass", value ? "1" : "0", SectionName);
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
                        if (d != null && string.Equals(d.displayLink, targetScreen.DeviceName, StringComparison.OrdinalIgnoreCase))
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

                    bool hdrBypass = GetHdrBypass(iniFile);
                    bool anyHdrBypassed = false;
                    string displayLink = targetDisplay != null ? targetDisplay.displayLink : (targetScreen != null ? targetScreen.DeviceName : null);

                    if (target == 2)
                    {
                        // 전체 화면(VirtualScreen) 캡처 시 모니터별 영역에 맞는 프로필 필터와 HDR 우회를 각각 독립 적용
                        Rectangle fullRect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                        foreach (Screen scr in Screen.AllScreens)
                        {
                            Rectangle screenRegion = new Rectangle(
                                scr.Bounds.X - bounds.X,
                                scr.Bounds.Y - bounds.Y,
                                scr.Bounds.Width,
                                scr.Bounds.Height);
                            Rectangle clipRegion = Rectangle.Intersect(fullRect, screenRegion);
                            if (clipRegion.Width <= 0 || clipRegion.Height <= 0) continue;

                            Display.DisplayInfo scrDisplay = null;
                            if (displays != null)
                            {
                                foreach (var d in displays)
                                {
                                    if (d != null && string.Equals(d.displayLink, scr.DeviceName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        scrDisplay = d;
                                        break;
                                    }
                                }
                            }
                            if (scrDisplay == null) scrDisplay = targetDisplay;

                            bool scrHdr = HdrService.IsHdrEnabled(scr.DeviceName);
                            if (!scrHdr || !hdrBypass)
                            {
                                ApplyProfileFilter(bmp, scrDisplay, clipRegion);
                            }
                            else
                            {
                                anyHdrBypassed = true;
                                Logger.Info($"[Screenshot] HDR monitor detected ({scr.DeviceName}). Bypassed double-gamma filter for region.");
                            }
                        }
                    }
                    else
                    {
                        // 단일 모니터 캡처: HDR 모니터 화이트아웃 방지 (이중 감마 적용 차단)
                        bool isHdr = HdrService.IsHdrEnabled(displayLink);
                        if (!isHdr || !hdrBypass)
                        {
                            ApplyProfileFilter(bmp, targetDisplay);
                        }
                        else
                        {
                            anyHdrBypassed = true;
                            Logger.Info($"[Screenshot] HDR monitor detected ({displayLink}). Bypassed double-gamma filter to prevent whiteout.");
                        }
                    }

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
                            string msg = anyHdrBypassed
                                ? (LanguageManager.Korean ? "📸 스크린샷 캡처 완료 (HDR 최적화)" : "📸 Screenshot Captured (HDR)")
                                : (LanguageManager.Korean ? "📸 스크린샷 캡처 완료" : "📸 Screenshot Captured");
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

            // 이미 STA 스레드(메인 UI 스레드 등)인 경우 별도 스레드 생성 없이 안전하게 복사
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                try
                {
                    using (Bitmap copyBmp = new Bitmap(bmp))
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            try
                            {
                                Clipboard.SetDataObject(copyBmp, true, 3, 50);
                                break;
                            }
                            catch (ExternalException)
                            {
                                Thread.Sleep(40);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Clipboard.SetDataObject failed on current STA thread: " + ex.Message);
                }
                return;
            }

            // 비-STA 스레드에서 호출된 경우, 호출 스레드에서 먼저 독립 클론을 생성한 뒤 STA 스레드로 소유권 이전 (GDI+ 동시 접근 원천 차단)
            Bitmap ownedCopy = null;
            try
            {
                ownedCopy = new Bitmap(bmp);
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to clone bitmap for STA clipboard thread: " + ex.Message);
                return;
            }

            Thread staThread = new Thread(() =>
            {
                using (ownedCopy)
                {
                    try
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            try
                            {
                                Clipboard.SetDataObject(ownedCopy, true, 3, 50);
                                break;
                            }
                            catch (ExternalException)
                            {
                                Thread.Sleep(40);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Clipboard.SetDataObject failed on STA thread: " + ex.Message);
                    }
                }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join(1000);
        }

        private static void ApplyProfileFilter(Bitmap bmp, Display.DisplayInfo display, Rectangle? targetRegion = null)
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

            // 실제 하드웨어 SetDeviceGammaRamp에 적용되는 것과 동일한 정규화 및 안전 범위 클램핑이 반영된 램프 테이블 사용
            ushort[,] ramp = Gamma.CreateGammaRamp(
                rGamma, gGamma, bGamma,
                rContrast, gContrast, bContrast,
                rBright, gBright, bBright,
                sb, sbm, sbt, hg, peak, width);

            byte[] lutR = new byte[256];
            byte[] lutG = new byte[256];
            byte[] lutB = new byte[256];

            for (int i = 0; i < 256; i++)
            {
                lutR[i] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(ramp[0, i] / 257.0)));
                lutG[i] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(ramp[1, i] / 257.0)));
                lutB[i] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(ramp[2, i] / 257.0)));
            }

            Rectangle fullRect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            Rectangle region = targetRegion.HasValue ? Rectangle.Intersect(fullRect, targetRegion.Value) : fullRect;
            if (region.Width <= 0 || region.Height <= 0) return;

            BitmapData bData = bmp.LockBits(fullRect, ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
            try
            {
                float satFactor = (float)saturation / 100.0f;
                bool applySat = Math.Abs(satFactor - 1.0f) > 0.001f;

                int startX = region.X;
                int endX = region.Right;
                int startY = region.Y;
                int endY = region.Bottom;
                int stride = bData.Stride;

                unsafe
                {
                    IntPtr scan0Ptr = bData.Scan0;

                    System.Threading.Tasks.Parallel.For(startY, endY, y =>
                    {
                        byte* row = (byte*)scan0Ptr.ToPointer() + (y * stride);
                        for (int x = startX; x < endX; x++)
                        {
                            int idx = x * 4;
                            byte b = lutB[row[idx]];
                            byte g = lutG[row[idx + 1]];
                            byte r = lutR[row[idx + 2]];

                            if (applySat)
                            {
                                double gray = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                                double adjR = gray + (r - gray) * satFactor;
                                double adjG = gray + (g - gray) * satFactor;
                                double adjB = gray + (b - gray) * satFactor;

                                row[idx]     = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(adjB)));
                                row[idx + 1] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(adjG)));
                                row[idx + 2] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(adjR)));
                            }
                            else
                            {
                                row[idx]     = b;
                                row[idx + 1] = g;
                                row[idx + 2] = r;
                            }
                        }
                    });
                }
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