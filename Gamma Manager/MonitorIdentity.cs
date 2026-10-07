using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace Gamma_Manager
{
    /// <summary>
    /// 물리 모니터를 안정적으로 식별하기 위한 EDID/PNP 기반 식별자 생성기.
    /// 같은 모델 두 대도 가능하면 EDID Serial로 분리하고, Serial이 없는 경우
    /// Windows PNP Device ID를 사용한다. 동일 ID가 현재 세션에서 중복되면
    /// DISPLAY 링크를 런타임 보조키로 붙여 충돌을 막는다.
    /// </summary>
    internal static class MonitorIdentity
    {
        internal sealed class IdentityInfo
        {
            public string Manufacturer = string.Empty;
            public string ProductCode = string.Empty;
            public string Serial = string.Empty;
            public string RawHardwareId = string.Empty;
        }

        public static IdentityInfo Read(string hardwareId)
        {
            IdentityInfo info = new IdentityInfo { RawHardwareId = hardwareId ?? string.Empty };
            if (string.IsNullOrWhiteSpace(hardwareId)) return info;

            try
            {
                // 1. 다양한 Windows 장치 ID 포맷(\ 및 # 구분자) 대응 분해
                string[] parts = hardwareId.Split(new[] { '\\', '#' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return info;

                // 2. 모델 코드 추출 (예: GSM5B15, SAM0B23 등)
                string modelCode = string.Empty;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Equals("DISPLAY", StringComparison.OrdinalIgnoreCase) ||
                        parts[i].Equals("MONITOR", StringComparison.OrdinalIgnoreCase) ||
                        parts[i].Equals("?", StringComparison.OrdinalIgnoreCase))
                        continue;
                    modelCode = parts[i];
                    break;
                }

                if (string.IsNullOrEmpty(modelCode)) return info;

                // 3. 해당 모델 하위의 인스턴스를 탐색하여 실제 EDID 데이터 추출
                using (RegistryKey modelKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + modelCode))
                {
                    if (modelKey != null)
                    {
                        string[] subKeyNames = modelKey.GetSubKeyNames();
                        if (subKeyNames != null && subKeyNames.Length > 0)
                        {
                            // 1순위: hardwareId 내에 subKeyName(인스턴스 ID)이 포함되어 있는 정확한 서브키 찾기
                            string matchedSubKey = null;
                            foreach (string subKeyName in subKeyNames)
                            {
                                if (!string.IsNullOrEmpty(subKeyName) && hardwareId.IndexOf(subKeyName, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    matchedSubKey = subKeyName;
                                    break;
                                }
                            }

                            // 매칭된 서브키가 있으면 해당 서브키 먼저 시도, 없으면 전체 서브키 순회
                            List<string> orderedSubKeys = new List<string>();
                            if (!string.IsNullOrEmpty(matchedSubKey))
                            {
                                orderedSubKeys.Add(matchedSubKey);
                            }
                            foreach (string subKeyName in subKeyNames)
                            {
                                if (!orderedSubKeys.Contains(subKeyName))
                                    orderedSubKeys.Add(subKeyName);
                            }

                            foreach (string subKeyName in orderedSubKeys)
                            {
                                using (RegistryKey devParams = modelKey.OpenSubKey(subKeyName + @"\Device Parameters"))
                                {
                                    if (devParams == null) continue;
                                    byte[] edid = devParams.GetValue("EDID") as byte[];
                                    if (edid != null && edid.Length >= 128)
                                    {
                                        info.Manufacturer = DecodeManufacturer(edid);
                                        info.ProductCode = edid.Length >= 12
                                            ? (edid[10] | (edid[11] << 8)).ToString("X4", CultureInfo.InvariantCulture)
                                            : string.Empty;
                                        info.Serial = DecodeSerial(edid);

                                        if (!string.IsNullOrEmpty(info.Serial) || !string.IsNullOrEmpty(info.Manufacturer))
                                            return info;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Info("EDID identity read failed for " + hardwareId + ": " + ex.Message);
            }

            return info;
        }

        public static string BuildBaseKey(IdentityInfo info, string displayLink)
        {
            if (info == null) return NormalizeFallback(displayLink);

            if (!string.IsNullOrWhiteSpace(info.Serial))
            {
                string manufacturer = NormalizeToken(info.Manufacturer);
                string product = NormalizeToken(info.ProductCode);
                string serial = NormalizeToken(info.Serial);
                string pnp = NormalizeToken(info.RawHardwareId);
                return "EDID|" + manufacturer + "|" + product + "|" + serial +
                       (string.IsNullOrEmpty(pnp) ? string.Empty : "|PNP=" + pnp);
            }

            if (!string.IsNullOrWhiteSpace(info.RawHardwareId))
                return "PNP|" + NormalizeToken(info.RawHardwareId);

            return NormalizeFallback(displayLink);
        }

        public static void EnsureUniqueKeys(List<Display.DisplayInfo> displays)
        {
            if (displays == null || displays.Count == 0) return;

            Dictionary<string, List<Display.DisplayInfo>> groups = new Dictionary<string, List<Display.DisplayInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (Display.DisplayInfo d in displays)
            {
                if (d == null) continue;
                string baseKey = !string.IsNullOrEmpty(d.monitorKey) ? d.monitorKey : BuildBaseKey(
                    new IdentityInfo
                    {
                        Manufacturer = d.edidManufacturer ?? string.Empty,
                        ProductCode = d.edidProductCode ?? string.Empty,
                        Serial = d.edidSerial ?? string.Empty,
                        RawHardwareId = d.hardwareId ?? string.Empty
                    }, d.displayLink);
                d.monitorKey = baseKey;

                if (!groups.TryGetValue(baseKey, out List<Display.DisplayInfo> list))
                {
                    list = new List<Display.DisplayInfo>();
                    groups[baseKey] = list;
                }
                list.Add(d);
            }

            foreach (KeyValuePair<string, List<Display.DisplayInfo>> group in groups)
            {
                if (group.Value.Count <= 1) continue;

                foreach (Display.DisplayInfo d in group.Value.OrderBy(x => x.displayLink, StringComparer.OrdinalIgnoreCase))
                {
                    d.monitorKey = group.Key + "|DISPLAY=" + NormalizeToken(d.displayLink);
                }
            }
        }

        private static string DecodeManufacturer(byte[] edid)
        {
            if (edid == null || edid.Length < 10) return string.Empty;
            int packed = (edid[8] << 8) | edid[9];
            char c1 = (char)('A' + ((packed >> 10) & 0x1F) - 1);
            char c2 = (char)('A' + ((packed >> 5) & 0x1F) - 1);
            char c3 = (char)('A' + (packed & 0x1F) - 1);
            return new string(new[] { c1, c2, c3 });
        }

        private static string DecodeSerial(byte[] edid)
        {
            // Base EDID numeric serial.
            if (edid.Length >= 16)
            {
                uint numeric = (uint)(edid[12] | (edid[13] << 8) | (edid[14] << 16) | (edid[15] << 24));
                if (numeric != 0 && numeric != 0xFFFFFFFF)
                    return numeric.ToString("X8", CultureInfo.InvariantCulture);
            }

            // EDID monitor descriptor type 0xFF (ASCII serial), preferred when numeric serial is zero.
            for (int offset = 54; offset + 18 <= edid.Length && offset < 126; offset += 18)
            {
                if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 2] == 0 && edid[offset + 3] == 0xFF)
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 5; i < 18; i++)
                    {
                        byte b = edid[offset + i];
                        if (b == 0x0A || b == 0x0D) break;
                        if (b >= 0x20 && b <= 0x7E) sb.Append((char)b);
                    }
                    string value = sb.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(value) && !value.All(c => c == '0' || c == '-' || c == ' '))
                        return value;
                }
            }

            return string.Empty;
        }

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            StringBuilder sb = new StringBuilder(value.Trim().ToUpperInvariant().Length);
            foreach (char c in value.Trim().ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '|' || c == '=' || c == '.')
                    sb.Append(c);
                else if (char.IsWhiteSpace(c))
                    sb.Append('_');
            }
            return sb.ToString();
        }


        internal static void AssignStableDisplayNames(List<Display.DisplayInfo> displays)
        {
            if (displays == null) return;

            foreach (Display.DisplayInfo d in displays)
            {
                if (d == null) continue;
                if (string.IsNullOrEmpty(d.baseDisplayName))
                    d.baseDisplayName = d.displayName ?? string.Empty;
                d.displayName = d.baseDisplayName;
            }

            var groups = displays
                .Where(d => d != null && !string.IsNullOrEmpty(d.baseDisplayName))
                .GroupBy(d => d.baseDisplayName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var group in groups)
            {
                List<Display.DisplayInfo> ordered = group
                    .OrderBy(d => d.monitorKey ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(d => d.hardwareId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(d => d.displayLink ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < ordered.Count; i++)
                    ordered[i].displayName = ordered[i].baseDisplayName + " (#" + (i + 1) + ")";
            }
        }

        internal static string GetStorageToken(string monitorKey)
        {
            if (string.IsNullOrWhiteSpace(monitorKey)) return string.Empty;

            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(monitorKey);
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();
            }
        }

        internal static string GetStableSpecialPresetName(string prefix, string monitorKey)
        {
            string token = GetStorageToken(monitorKey);
            return string.IsNullOrEmpty(token) ? string.Empty : prefix + token;
        }

        private static string NormalizeFallback(string displayLink)
        {
            string link = NormalizeToken(displayLink);
            return string.IsNullOrEmpty(link) ? string.Empty : "DISPLAY|" + link;
        }
    }
}
