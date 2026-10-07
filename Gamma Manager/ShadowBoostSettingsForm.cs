using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class ShadowBoostSettingsForm : Form
    {
        private sealed class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint, true);
                UpdateStyles();
            }
        }

        private class MonitorBackup
        {
            public int shadowBoost;
            public int shadowBoostMode;
            public int shadowBoostTint;
            public int highlightGuard;
            public int shadowBoostCustomPeak;
            public int shadowBoostCustomWidth;
        }

        private Display.DisplayInfo _display;
        private readonly List<Display.DisplayInfo> _displays;
        private readonly DisplayService _displayService;
        private readonly Action _onSettingsChanged;
        private readonly Action<Display.DisplayInfo> _onMonitorSwitched;
        private readonly Dictionary<Display.DisplayInfo, MonitorBackup> _initialBackups = new Dictionary<Display.DisplayInfo, MonitorBackup>();
        private readonly bool _ko;

        private ComboBox _comboMonitors;
        private CheckBox _chkEnabled;
        private ComboBox _comboMode;
        private TrackBar _trackBoost;
        private NumericUpDown _numBoost;
        private Label _lblValuePercent;
        private Button _btnPreset0;
        private Button _btnPreset25;
        private Button _btnPreset50;
        private Button _btnPreset75;

        // 고도화 기능 컨트롤
        private CheckBox _chkHighlightGuard;
        private TrackBar _trackHighlightGuard;
        private NumericUpDown _numHighlightGuard;
        private Label _lblHighlightGuardPct;
        private Panel _panelCustom;
        private TrackBar _trackCustomPeak;
        private NumericUpDown _numCustomPeak;
        private TrackBar _trackCustomWidth;
        private Label _lblCustomWidthValue;
        private Button _btnFullScreenTest;

        private Label _lblGuideDesc;
        private DoubleBufferedPanel _curvePanel;
        private CheckBox _chkTestBar;
        private DoubleBufferedPanel _testBarPanel;
        private Label _lblTestTip;
        private Button _btnReset;
        private Button _btnCancel;
        private Button _btnOk;

        private bool _isUpdatingUi;

        public ShadowBoostSettingsForm(
            Display.DisplayInfo display,
            List<Display.DisplayInfo> displays,
            DisplayService displayService,
            Action onSettingsChanged,
            Action<Display.DisplayInfo> onMonitorSwitched = null)
        {
            _display = display ?? throw new ArgumentNullException(nameof(display));
            _displays = displays ?? new List<Display.DisplayInfo> { display };
            _displayService = displayService;
            _onSettingsChanged = onSettingsChanged;
            _onMonitorSwitched = onMonitorSwitched;
            _ko = LanguageManager.Korean;

            if (_displays != null)
            {
                foreach (var d in _displays)
                {
                    if (d != null && !_initialBackups.ContainsKey(d))
                    {
                        _initialBackups[d] = new MonitorBackup
                        {
                            shadowBoost = d.shadowBoost,
                            shadowBoostMode = d.shadowBoostMode,
                            shadowBoostTint = d.shadowBoostTint,
                            highlightGuard = d.highlightGuard,
                            shadowBoostCustomPeak = d.shadowBoostCustomPeak,
                            shadowBoostCustomWidth = d.shadowBoostCustomWidth
                        };
                    }
                }
            }

            if (!_initialBackups.ContainsKey(_display))
            {
                _initialBackups[_display] = new MonitorBackup
                {
                    shadowBoost = _display.shadowBoost,
                    shadowBoostMode = _display.shadowBoostMode,
                    shadowBoostTint = _display.shadowBoostTint,
                    highlightGuard = _display.highlightGuard,
                    shadowBoostCustomPeak = _display.shadowBoostCustomPeak,
                    shadowBoostCustomWidth = _display.shadowBoostCustomWidth
                };
            }

            InitializeForm();
            BuildUI();
            LoadCurrentValues();
            ThemeManager.Apply(this);
        }

        public ShadowBoostSettingsForm(
            Display.DisplayInfo display,
            DisplayService displayService,
            Action onSettingsChanged)
            : this(display, new List<Display.DisplayInfo> { display }, displayService, onSettingsChanged, null)
        {
        }

        private void InitializeForm()
        {
            Text = _ko ? "블랙 이퀄라이저 설정 (Black Equalizer)" : "Black Equalizer Settings";
            ClientSize = new Size(780, 536);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
        }

        private void BuildUI()
        {
            // ==========================================
            // [좌측 영역]: 컨트롤 및 알고리즘 모드 설정
            // ==========================================

            // 상단 타이틀
            Label lblTitle = new Label
            {
                Text = _ko ? "🌑 블랙 이퀄라이저 (Black Equalizer)" : "🌑 Black Equalizer",
                Location = new Point(20, 16),
                AutoSize = true,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold)
            };
            Controls.Add(lblTitle);

            // 대상 모니터 선택 드롭다운
            Label lblMonitor = new Label
            {
                Text = _ko ? "모니터 선택:" : "Display:",
                Location = new Point(22, 44),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(lblMonitor);

            _comboMonitors = new ComboBox
            {
                Location = new Point(116, 40),
                Size = new Size(254, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5f)
            };
            if (_displays != null && _displays.Count > 0)
            {
                for (int i = 0; i < _displays.Count; i++)
                {
                    _comboMonitors.Items.Add($"{i + 1}) {_displays[i].displayName}");
                }
                int curIdx = _displays.IndexOf(_display);
                if (curIdx >= 0) _comboMonitors.SelectedIndex = curIdx;
                else _comboMonitors.SelectedIndex = 0;
            }
            else
            {
                _comboMonitors.Items.Add($"1) {_display.displayName}");
                _comboMonitors.SelectedIndex = 0;
            }
            _comboMonitors.SelectedIndexChanged += OnMonitorSelectedIndexChanged;
            Controls.Add(_comboMonitors);

            // 활성화 체크박스
            _chkEnabled = new CheckBox
            {
                Text = _ko ? "블랙 이퀄라이저 기능 활성화" : "Enable Black Equalizer",
                Location = new Point(22, 74),
                AutoSize = true,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
            };
            _chkEnabled.CheckedChanged += OnEnabledCheckedChanged;
            Controls.Add(_chkEnabled);

            // 부스터 모드 (알고리즘) 선택
            Label lblMode = new Label
            {
                Text = _ko ? "알고리즘 모드:" : "Algorithm Mode:",
                Location = new Point(22, 108),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(lblMode);

            _comboMode = new ComboBox
            {
                Location = new Point(116, 104),
                Size = new Size(254, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5f)
            };
            _comboMode.Items.AddRange(new object[]
            {
                _ko ? "1. FPS 표준 밸런스 (균형형)" : "1. Balanced Toe (FPS Standard)",
                _ko ? "2. 야간전 (극암부 집중 리프팅)" : "2. Night Mode (Deep Shadow Boost)",
                _ko ? "3. 정밀 분리형 (타깃 컷/윤곽 분리)" : "3. Precision Spline (Target Cut)",
                _ko ? "4. e스포츠 트루 블랙 (안개 방지/OLED)" : "4. True Black OLED Guard (Anti-Fog)",
                _ko ? "5. 사용자 정의 대역폭 (피크 & 범위 커스텀)" : "5. Custom Bandwidth (Peak & Width Tuning)"
            });
            _comboMode.SelectedIndexChanged += OnModeSelectedIndexChanged;
            Controls.Add(_comboMode);

            // 슬라이더 및 수치 조절
            Label lblSlider = new Label
            {
                Text = _ko ? "부스트 강도:" : "Boost Level:",
                Location = new Point(22, 148),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(lblSlider);

            _trackBoost = new TrackBar
            {
                Location = new Point(108, 144),
                Size = new Size(185, 45),
                Minimum = 0,
                Maximum = 100,
                TickStyle = TickStyle.None,
                SmallChange = 1,
                LargeChange = 10
            };
            _trackBoost.ValueChanged += OnSliderValueChanged;
            Controls.Add(_trackBoost);

            _numBoost = new NumericUpDown
            {
                Location = new Point(298, 146),
                Size = new Size(52, 28),
                Minimum = 0,
                Maximum = 100,
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Segoe UI", 9.5f)
            };
            _numBoost.ValueChanged += OnNumericValueChanged;
            Controls.Add(_numBoost);

            _lblValuePercent = new Label
            {
                Text = "%",
                Location = new Point(352, 150),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_lblValuePercent);

            // 빠른 프리셋 버튼 모음
            Label lblPresets = new Label
            {
                Text = _ko ? "빠른 설정:" : "Presets:",
                Location = new Point(22, 194),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(lblPresets);

            _btnPreset0 = CreatePresetButton(_ko ? "0% (끄기)" : "0% (Off)", 116, 190, 60, 0);
            _btnPreset25 = CreatePresetButton(_ko ? "25% (약)" : "25% (Low)", 181, 190, 60, 25);
            _btnPreset50 = CreatePresetButton(_ko ? "50% (중)" : "50% (Mid)", 246, 190, 60, 50);
            _btnPreset75 = CreatePresetButton(_ko ? "75% (강)" : "75% (High)", 311, 190, 60, 75);

            Controls.Add(_btnPreset0);
            Controls.Add(_btnPreset25);
            Controls.Add(_btnPreset50);
            Controls.Add(_btnPreset75);

            // 1. 하이라이트 보호 & 눈부심 방지 (가변 슬라이더 연동)
            _chkHighlightGuard = new CheckBox
            {
                Text = _ko ? "🛡️ 눈부심 방지" : "🛡️ Anti-Glare",
                Location = new Point(22, 228),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            _chkHighlightGuard.CheckedChanged += OnHighlightGuardCheckedChanged;
            Controls.Add(_chkHighlightGuard);

            _trackHighlightGuard = new TrackBar
            {
                Location = new Point(140, 224),
                Size = new Size(155, 30),
                Minimum = 0,
                Maximum = 100,
                SmallChange = 1,
                LargeChange = 10,
                TickStyle = TickStyle.None
            };
            _trackHighlightGuard.ValueChanged += OnHighlightGuardSliderChanged;
            Controls.Add(_trackHighlightGuard);

            _numHighlightGuard = new NumericUpDown
            {
                Location = new Point(298, 226),
                Size = new Size(48, 24),
                Minimum = 0,
                Maximum = 100,
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Segoe UI", 8.8f)
            };
            _numHighlightGuard.ValueChanged += OnHighlightGuardNumericChanged;
            Controls.Add(_numHighlightGuard);

            _lblHighlightGuardPct = new Label
            {
                Text = "%",
                Location = new Point(348, 229),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.0f)
            };
            Controls.Add(_lblHighlightGuardPct);

            // 2. 커스텀 대역폭 튜닝 패널 (모드 5 전용)
            _panelCustom = new Panel
            {
                Location = new Point(20, 262),
                Size = new Size(350, 72),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = ThemeManager.IsDark ? Color.FromArgb(28, 30, 38) : Color.FromArgb(240, 243, 248),
                Visible = false
            };

            Label lblPeak = new Label { Text = _ko ? "피크 위치:" : "Peak Pos:", Location = new Point(8, 8), AutoSize = true, Font = new Font("Segoe UI", 8.8f) };
            _panelCustom.Controls.Add(lblPeak);

            _trackCustomPeak = new TrackBar { Location = new Point(76, 4), Size = new Size(160, 30), Minimum = 10, Maximum = 40, Value = 25, TickStyle = TickStyle.None };
            _trackCustomPeak.ValueChanged += OnCustomParamsChanged;
            _panelCustom.Controls.Add(_trackCustomPeak);

            _numCustomPeak = new NumericUpDown { Location = new Point(242, 6), Size = new Size(46, 24), Minimum = 10, Maximum = 40, Value = 25, TextAlign = HorizontalAlignment.Center, Font = new Font("Segoe UI", 8.8f) };
            _numCustomPeak.ValueChanged += (s, e) => { if (!_isUpdatingUi) _trackCustomPeak.Value = (int)_numCustomPeak.Value; };
            _panelCustom.Controls.Add(_numCustomPeak);

            Label lblPeakPct = new Label { Text = "%", Location = new Point(292, 9), AutoSize = true, Font = new Font("Segoe UI", 8.8f) };
            _panelCustom.Controls.Add(lblPeakPct);

            Label lblWidth = new Label { Text = _ko ? "영향 범위:" : "Bandwidth:", Location = new Point(8, 38), AutoSize = true, Font = new Font("Segoe UI", 8.8f) };
            _panelCustom.Controls.Add(lblWidth);

            _trackCustomWidth = new TrackBar { Location = new Point(76, 36), Size = new Size(160, 30), Minimum = 1, Maximum = 5, Value = 3, TickStyle = TickStyle.None };
            _trackCustomWidth.ValueChanged += OnCustomParamsChanged;
            _panelCustom.Controls.Add(_trackCustomWidth);

            _lblCustomWidthValue = new Label { Text = _ko ? "3 (표준)" : "3 (Mid)", Location = new Point(242, 40), AutoSize = true, Font = new Font("Segoe UI", 8.8f, FontStyle.Bold) };
            _panelCustom.Controls.Add(_lblCustomWidthValue);

            Controls.Add(_panelCustom);

            // 원리 안내 카드
            Panel guideCard = new Panel
            {
                Location = new Point(20, 342),
                Size = new Size(350, 154),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = ThemeManager.IsDark ? Color.FromArgb(32, 35, 42) : Color.FromArgb(245, 247, 250),
                Padding = new Padding(10)
            };

            Label lblGuideTitle = new Label
            {
                Text = _ko ? "💡 선택된 모드 특성" : "💡 Selected Mode Characteristics",
                Location = new Point(10, 8),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = ThemeManager.IsDark ? Color.FromArgb(255, 215, 100) : Color.FromArgb(180, 120, 0)
            };
            guideCard.Controls.Add(lblGuideTitle);

            _lblGuideDesc = new Label
            {
                Location = new Point(10, 30),
                Size = new Size(328, 114),
                Font = new Font("Segoe UI", 8.6f),
                ForeColor = ThemeManager.IsDark ? Color.FromArgb(215, 215, 215) : Color.FromArgb(70, 70, 70)
            };
            guideCard.Controls.Add(_lblGuideDesc);
            Controls.Add(guideCard);

            // ==========================================
            // [우측 영역]: 실시간 커브 & 암부 분석 시각화
            // ==========================================

            Label lblVisualTitle = new Label
            {
                Text = _ko ? "📊 실시간 감마 커브 & 암부 분석" : "📊 Real-Time Curve & Shadow Analysis",
                Location = new Point(390, 16),
                AutoSize = true,
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold)
            };
            Controls.Add(lblVisualTitle);

            // 실시간 커브 2D 그래프 패널
            _curvePanel = new DoubleBufferedPanel
            {
                Location = new Point(390, 44),
                Size = new Size(366, 230),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = ThemeManager.IsDark ? Color.FromArgb(18, 20, 26) : Color.FromArgb(240, 243, 248)
            };
            _curvePanel.Paint += OnCurvePanelPaint;
            Controls.Add(_curvePanel);

            // 암부 계조 식별 테스트 바 체크박스 (체크/토글 가능)
            _chkTestBar = new CheckBox
            {
                Text = _ko ? "암부 계조 식별 테스트 (0 ~ 10단계)" : "Shadow Step Visibility Test (0 ~ 10)",
                Location = new Point(390, 284),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Checked = true
            };
            _chkTestBar.CheckedChanged += (s, e) =>
            {
                bool show = _chkTestBar.Checked;
                _testBarPanel.Visible = show;
                _lblTestTip.Visible = show;
                if (!_isUpdatingUi)
                {
                    IniFile.Shared.Write("ShadowBoostShowTestBar", show ? "True" : "False", "Settings");
                }
            };
            Controls.Add(_chkTestBar);

            // 암부 계조 테스트 바 패널
            _testBarPanel = new DoubleBufferedPanel
            {
                Location = new Point(390, 308),
                Size = new Size(366, 44),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.Black
            };
            _testBarPanel.Paint += OnTestBarPanelPaint;
            Controls.Add(_testBarPanel);

            // 암부 식별 가이드 팁
            _lblTestTip = new Label
            {
                Text = _ko
                    ? "※ 슬라이더를 올려 숫자가 2~3번부터 어렴풋이 보일 때 인게임 식별력이 가장 우수합니다."
                    : "※ Optimal visibility achieved when numbers 2-3 become faintly visible.",
                Location = new Point(390, 356),
                Size = new Size(366, 32),
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = ThemeManager.IsDark ? Color.FromArgb(160, 166, 178) : Color.FromArgb(110, 115, 125)
            };
            Controls.Add(_lblTestTip);

            // 4. 전체화면 캘리브레이션 버튼
            _btnFullScreenTest = new Button
            {
                Text = _ko ? "🖥️ 전체화면 암부 캘리브레이션" : "🖥️ Full-Screen Calibration",
                Location = new Point(390, 396),
                Size = new Size(366, 36),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                UseVisualStyleBackColor = true
            };
            _btnFullScreenTest.Click += (s, e) =>
            {
                using (var calib = new FullScreenCalibrationForm(_display))
                {
                    calib.ShowDialog(this);
                }
            };
            Controls.Add(_btnFullScreenTest);

            // ==========================================
            // [하단 영역]: 초기화 / 닫기 / 확인(저장) 버튼 그룹
            // 우측 영역(X=390, Width=366)에 나란히 모아 좌측 안내 카드(하단 514)와 수평 밑줄 맞춤
            // ==========================================

            _btnReset = new Button
            {
                Text = _ko ? "초기화" : "Reset",
                Location = new Point(390, 478),
                Size = new Size(72, 36),
                Font = new Font("Segoe UI", 9.0f),
                UseVisualStyleBackColor = true
            };
            _btnReset.Click += OnResetClick;
            Controls.Add(_btnReset);

            _btnCancel = new Button
            {
                Text = _ko ? "닫기 (저장 안 함)" : "Close (No Save)",
                Location = new Point(468, 478),
                Size = new Size(140, 36),
                DialogResult = DialogResult.Cancel,
                Font = new Font("Segoe UI", 9.2f),
                UseVisualStyleBackColor = true
            };
            _btnCancel.Click += OnCancelClick;
            Controls.Add(_btnCancel);

            _btnOk = new Button
            {
                Text = _ko ? "확인 (저장)" : "Save",
                Location = new Point(614, 478),
                Size = new Size(142, 36),
                DialogResult = DialogResult.OK,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                UseVisualStyleBackColor = true
            };
            _btnOk.Click += OnOkClick;
            Controls.Add(_btnOk);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private Button CreatePresetButton(string text, int x, int y, int width, int boostValue)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 28),
                Font = new Font("Segoe UI", 8.8f),
                UseVisualStyleBackColor = true
            };
            btn.Click += (s, e) =>
            {
                if (!_chkEnabled.Checked)
                {
                    _chkEnabled.Checked = true;
                    UpdateControlStates(true);
                }
                ApplyBoost(boostValue);
            };
            return btn;
        }

        private void OnMonitorSelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUi) return;
            if (_comboMonitors == null || _comboMonitors.SelectedIndex < 0 || _displays == null) return;
            if (_comboMonitors.SelectedIndex >= _displays.Count) return;

            Display.DisplayInfo nextDisplay = _displays[_comboMonitors.SelectedIndex];
            if (nextDisplay == null || nextDisplay == _display) return;

            // 현재 모니터의 UI 설정값 최종 기록
            ApplyBoost(_chkEnabled.Checked ? _trackBoost.Value : 0);

            // 새 모니터로 전환
            _display = nextDisplay;
            if (!_initialBackups.ContainsKey(_display))
            {
                _initialBackups[_display] = new MonitorBackup
                {
                    shadowBoost = _display.shadowBoost,
                    shadowBoostMode = _display.shadowBoostMode,
                    shadowBoostTint = _display.shadowBoostTint,
                    highlightGuard = _display.highlightGuard,
                    shadowBoostCustomPeak = _display.shadowBoostCustomPeak,
                    shadowBoostCustomWidth = _display.shadowBoostCustomWidth
                };
            }

            LoadCurrentValues();
            RefreshVisualizers();

            _onMonitorSwitched?.Invoke(_display);
        }

        private void LoadCurrentValues()
        {
            _isUpdatingUi = true;
            int current = Math.Max(0, Math.Min(100, _display.shadowBoost));
            int mode = Math.Max(0, Math.Min(4, _display.shadowBoostMode));
            int highlightGuardVal = Math.Max(0, Math.Min(100, _display.highlightGuard));
            int peak = Math.Max(10, Math.Min(40, _display.shadowBoostCustomPeak));
            int width = Math.Max(1, Math.Min(5, _display.shadowBoostCustomWidth));

            _chkEnabled.Checked = current > 0;
            _comboMode.SelectedIndex = mode;
            _chkHighlightGuard.Checked = highlightGuardVal > 0;
            int guardSliderVal = highlightGuardVal > 0 ? highlightGuardVal : 50;
            _trackHighlightGuard.Value = guardSliderVal;
            _numHighlightGuard.Value = guardSliderVal;
            _trackCustomPeak.Value = peak;
            _numCustomPeak.Value = peak;
            _trackCustomWidth.Value = width;
            UpdateCustomWidthLabel(width);
            _panelCustom.Visible = (mode == 4);

            _trackBoost.Value = current > 0 ? current : 35;
            _numBoost.Value = current > 0 ? current : 35;

            UpdateControlStates(current > 0);
            UpdateGuideDescription(mode);

            // 암부 계조 식별 테스트 바 표시 여부 INI 설정 로드 (기본값: True)
            string showTestBarStr = IniFile.Shared.Read("ShadowBoostShowTestBar", "Settings");
            bool showTestBar = string.IsNullOrEmpty(showTestBarStr) ||
                               showTestBarStr.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                               showTestBarStr == "1";
            _chkTestBar.Checked = showTestBar;
            _testBarPanel.Visible = showTestBar;
            _lblTestTip.Visible = showTestBar;

            _isUpdatingUi = false;

            RefreshVisualizers();
        }

        private void OnModeSelectedIndexChanged(object sender, EventArgs e)
        {
            int mode = Math.Max(0, Math.Min(4, _comboMode.SelectedIndex));
            _panelCustom.Visible = (mode == 4);
            UpdateGuideDescription(mode);

            if (_isUpdatingUi) return;

            _display.shadowBoostMode = mode;
            if (_displayService != null)
            {
                _displayService.ApplyGammaOnly(_display);
            }

            RefreshVisualizers();
        }


        private void OnHighlightGuardCheckedChanged(object sender, EventArgs e)
        {
            bool isChecked = _chkHighlightGuard.Checked;
            _trackHighlightGuard.Enabled = isChecked && _chkEnabled.Checked;
            _numHighlightGuard.Enabled = isChecked && _chkEnabled.Checked;

            if (_isUpdatingUi) return;
            _display.highlightGuard = isChecked ? _trackHighlightGuard.Value : 0;
            if (_displayService != null) _displayService.ApplyGammaOnly(_display);
            RefreshVisualizers();
        }

        private void OnHighlightGuardSliderChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUi) return;
            int val = _trackHighlightGuard.Value;
            _isUpdatingUi = true;
            _numHighlightGuard.Value = val;
            if (val > 0 && !_chkHighlightGuard.Checked)
            {
                _chkHighlightGuard.Checked = true;
                _trackHighlightGuard.Enabled = _chkEnabled.Checked;
                _numHighlightGuard.Enabled = _chkEnabled.Checked;
            }
            _isUpdatingUi = false;

            _display.highlightGuard = _chkHighlightGuard.Checked ? val : 0;
            if (_displayService != null) _displayService.ApplyGammaOnly(_display);
            RefreshVisualizers();
        }

        private void OnHighlightGuardNumericChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUi) return;
            int val = (int)_numHighlightGuard.Value;
            _isUpdatingUi = true;
            _trackHighlightGuard.Value = val;
            if (val > 0 && !_chkHighlightGuard.Checked)
            {
                _chkHighlightGuard.Checked = true;
                _trackHighlightGuard.Enabled = _chkEnabled.Checked;
                _numHighlightGuard.Enabled = _chkEnabled.Checked;
            }
            _isUpdatingUi = false;

            _display.highlightGuard = _chkHighlightGuard.Checked ? val : 0;
            if (_displayService != null) _displayService.ApplyGammaOnly(_display);
            RefreshVisualizers();
        }

        private void OnCustomParamsChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUi) return;
            int peak = _trackCustomPeak.Value;
            int width = _trackCustomWidth.Value;

            _isUpdatingUi = true;
            _numCustomPeak.Value = peak;
            UpdateCustomWidthLabel(width);
            _isUpdatingUi = false;

            _display.shadowBoostCustomPeak = peak;
            _display.shadowBoostCustomWidth = width;
            if (_displayService != null) _displayService.ApplyGammaOnly(_display);
            RefreshVisualizers();
        }

        private void UpdateCustomWidthLabel(int width)
        {
            if (_lblCustomWidthValue == null) return;
            string desc = _ko
                ? (width == 1 ? "1 (매우 좁음)" : width == 2 ? "2 (좁음)" : width == 3 ? "3 (표준)" : width == 4 ? "4 (넓음)" : "5 (매우 넓음)")
                : (width == 1 ? "1 (Very Narrow)" : width == 2 ? "2 (Narrow)" : width == 3 ? "3 (Standard)" : width == 4 ? "4 (Wide)" : "5 (Very Wide)");
            _lblCustomWidthValue.Text = desc;
        }

        private void UpdateGuideDescription(int mode)
        {
            switch (mode)
            {
                case 1:
                    _lblGuideDesc.Text = _ko
                        ? "• [야간전 모드 (Deep Shadow)]\n" +
                          "• 칠흑 같은 극암부(0.05~0.20)를 집중적으로 끄집어 올립니다.\n" +
                          "• 빛 하나 없는 캄캄한 실내 구석이나 어두운 야간 맵에서 최상의 시인성을 발휘합니다.\n" +
                          "• 야간 레이드나 지하실 교전에서 숨어있는 적을 식별하기에 적합합니다."
                        : "• [Night Mode (Deep Shadow)]\n" +
                          "• Aggressively lifts deep shadows (0.05~0.20) for dark visibility.\n" +
                          "• Excels in pitch-black interiors and dark environments.\n" +
                          "• Best for spotting enemies hidden in night raids or basements.";
                    break;

                case 2:
                    _lblGuideDesc.Text = _ko
                        ? "• [정밀 분리형 모드 (타깃 컷)]\n" +
                          "• 암부만 급감쇠 4차 곡선으로 올리고, 중간/하이라이트는 100% 원본을 보존합니다.\n" +
                          "• 어둠 속에 은폐한 대상의 외곽선(실루엣)과 명암 경계가 칼같이 분리됩니다.\n" +
                          "• 야외 전투에서 하늘/빛의 눈부심 없이 그늘진 적만 포착합니다."
                        : "• [Precision Spline Mode (Target Cut)]\n" +
                          "• Lifts dark shadows while keeping midtones & highlights 100% true to source.\n" +
                          "• Sharpens silhouette and edge contrast of targets in shadows.\n" +
                          "• Zero glare or blowout in outdoor combat.";
                    break;

                case 3:
                    _lblGuideDesc.Text = _ko
                        ? "• [e스포츠 트루 블랙 모드 (OLED 특화 / 안개 방지)]\n" +
                          "• 0~2% 극저조도(칠흑/레터박스)는 0에 고정하여 화면이 하얗게 뜨는 '안개 현상(Milky Fog)'을 완벽 차단합니다.\n" +
                          "• 3%~35% 적 실루엣 구간만 폭발적으로 리프팅합니다.\n" +
                          "• OLED 모니터나 고명암비 패널에서 딥블랙과 시인성을 동시에 극대화합니다."
                        : "• [True Black OLED Guard (Anti-Fog)]\n" +
                          "• Clamps 0~2% true blacks to prevent milky gray washout/fogging.\n" +
                          "• Steeply lifts 3%~35% shadow silhouettes for extreme target contrast.\n" +
                          "• Preserves OLED deep blacks while providing competitive clarity.";
                    break;

                case 4:
                    _lblGuideDesc.Text = _ko
                        ? "• [사용자 정의 대역폭 모드 (피크 & 범위 커스텀)]\n" +
                          "• 피크 중심점(10%~40%)과 대역폭을 슬라이더로 직접 조절합니다.\n" +
                          "• 게임별 암부 깊이에 맞게 곡선의 정점과 감쇠 폭을 자유자재로 튜닝할 수 있습니다.\n" +
                          "• 탈콥 야간(극암부)부터 배그/에이펙스(중암부)까지 전천후 대응이 가능합니다."
                        : "• [Custom Bandwidth Mode (Peak & Width Tuning)]\n" +
                          "• Adjust peak focal point (10%~40%) and falloff bandwidth via sliders.\n" +
                          "• Tailor the response to specific game shadow depths.\n" +
                          "• Perfect for both pitch-black raids and broad daylight shadows.";
                    break;

                default:
                    _lblGuideDesc.Text = _ko
                        ? "• [FPS 표준 밸런스 모드 (균형형)]\n" +
                          "• 가장 자연스러운 게이밍 표준 3차 토우(Toe) 곡선입니다.\n" +
                          "• 어두운 구석(0~35%)을 균일하게 밝혀주며, 계단 현상(밴딩) 없이 눈이 가장 편안합니다.\n" +
                          "• 일상적인 FPS/배틀로얄 플레이에 가장 추천되는 표준 모드입니다."
                        : "• [Balanced Toe Mode (FPS Standard)]\n" +
                          "• Industry-standard balanced cubic toe curve with zero banding.\n" +
                          "• Evenly lifts dark corners while keeping visuals natural.\n" +
                          "• Recommended all-round profile for everyday FPS gaming.";
                    break;
            }
        }

        private void OnEnabledCheckedChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUi) return;
            bool enabled = _chkEnabled.Checked;
            UpdateControlStates(enabled);

            if (enabled)
            {
                int current = _trackBoost.Value;
                if (current == 0)
                {
                    current = 35; // 켤 때 0이었으면 기본 추천값 35%
                    _isUpdatingUi = true;
                    _trackBoost.Value = current;
                    _numBoost.Value = current;
                    _isUpdatingUi = false;
                }
                ApplyBoost(current);
            }
            else
            {
                // 체크를 끄면 화면에만 0을 적용 (슬라이더 수치는 보존)
                _display.shadowBoost = 0;
                if (_displayService != null)
                {
                    _displayService.ApplyGammaOnly(_display);
                }
            }

            RefreshVisualizers();
        }

        private void UpdateControlStates(bool enabled)
        {
            _comboMode.Enabled = enabled;
            _trackBoost.Enabled = enabled;
            _numBoost.Enabled = enabled;
            _btnPreset0.Enabled = enabled;
            _btnPreset25.Enabled = enabled;
            _btnPreset50.Enabled = enabled;
            _btnPreset75.Enabled = enabled;
            _chkHighlightGuard.Enabled = enabled;
            _trackHighlightGuard.Enabled = enabled && _chkHighlightGuard.Checked;
            _numHighlightGuard.Enabled = enabled && _chkHighlightGuard.Checked;
            _panelCustom.Enabled = enabled;
        }

        private void OnSliderValueChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUi) return;
            int val = _trackBoost.Value;
            _isUpdatingUi = true;
            _numBoost.Value = val;
            if (val > 0 && !_chkEnabled.Checked)
            {
                _chkEnabled.Checked = true;
                UpdateControlStates(true);
            }
            _isUpdatingUi = false;

            ApplyBoost(val);
            RefreshVisualizers();
        }

        private void OnNumericValueChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUi) return;
            int val = (int)_numBoost.Value;
            _isUpdatingUi = true;
            _trackBoost.Value = val;
            if (val > 0 && !_chkEnabled.Checked)
            {
                _chkEnabled.Checked = true;
                UpdateControlStates(true);
            }
            _isUpdatingUi = false;

            ApplyBoost(val);
            RefreshVisualizers();
        }

        private void ApplyBoost(int boostValue)
        {
            boostValue = Math.Max(0, Math.Min(100, boostValue));
            _isUpdatingUi = true;
            _trackBoost.Value = boostValue;
            _numBoost.Value = boostValue;
            _isUpdatingUi = false;

            _display.shadowBoost = _chkEnabled.Checked ? boostValue : 0;
            _display.shadowBoostMode = Math.Max(0, Math.Min(4, _comboMode.SelectedIndex));
            _display.shadowBoostTint = 0;
            _display.highlightGuard = _chkHighlightGuard.Checked ? _trackHighlightGuard.Value : 0;
            _display.shadowBoostCustomPeak = _trackCustomPeak.Value;
            _display.shadowBoostCustomWidth = _trackCustomWidth.Value;

            if (_displayService != null)
            {
                _displayService.ApplyGammaOnly(_display);
            }
        }

        private void RefreshVisualizers()
        {
            _curvePanel?.Invalidate();
            _testBarPanel?.Invalidate();
        }

        // ==========================================
        // [시각화 1]: 실시간 감마 커브 2D 그래프 렌더링
        // ==========================================
        private void OnCurvePanelPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = _curvePanel.ClientSize.Width;
            int h = _curvePanel.ClientSize.Height;

            int padL = 34;
            int padR = 14;
            int padT = 16;
            int padB = 26;

            int plotW = w - padL - padR;
            int plotH = h - padT - padB;

            bool isDark = ThemeManager.IsDark;
            Color gridColor = isDark ? Color.FromArgb(40, 44, 54) : Color.FromArgb(218, 224, 232);
            Color axisColor = isDark ? Color.FromArgb(80, 88, 104) : Color.FromArgb(170, 180, 195);
            Color textMuted = isDark ? Color.FromArgb(140, 148, 162) : Color.FromArgb(115, 122, 135);

            // 1. 격자선 (25%, 50%, 75%)
            using (Pen penGrid = new Pen(gridColor, 1f) { DashStyle = DashStyle.Dot })
            {
                for (int i = 1; i <= 3; i++)
                {
                    float x = padL + (plotW * i / 4.0f);
                    float y = padT + (plotH * i / 4.0f);
                    g.DrawLine(penGrid, x, padT, x, padT + plotH);
                    g.DrawLine(penGrid, padL, y, padL + plotW, y);
                }
            }

            // 2. 축 테두리
            using (Pen penAxis = new Pen(axisColor, 1.2f))
            {
                g.DrawRectangle(penAxis, padL, padT, plotW, plotH);
            }

            // 3. 축 눈금 텍스트 (0, 64, 128, 192, 255)
            using (Font fAxis = new Font("Segoe UI", 7.2f))
            using (Brush bAxis = new SolidBrush(textMuted))
            using (StringFormat sfCenter = new StringFormat { Alignment = StringAlignment.Center })
            using (StringFormat sfRight = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
            {
                // X축
                g.DrawString("0", fAxis, bAxis, padL, padT + plotH + 5, sfCenter);
                g.DrawString("64", fAxis, bAxis, padL + plotW * 0.25f, padT + plotH + 5, sfCenter);
                g.DrawString("128", fAxis, bAxis, padL + plotW * 0.50f, padT + plotH + 5, sfCenter);
                g.DrawString("192", fAxis, bAxis, padL + plotW * 0.75f, padT + plotH + 5, sfCenter);
                g.DrawString("255", fAxis, bAxis, padL + plotW, padT + plotH + 5, sfCenter);

                // Y축
                g.DrawString("255", fAxis, bAxis, padL - 4, padT, sfRight);
                g.DrawString("128", fAxis, bAxis, padL - 4, padT + plotH * 0.50f, sfRight);
                g.DrawString("0", fAxis, bAxis, padL - 4, padT + plotH, sfRight);
            }

            // 4. 원본 1:1 대각선 기준선 (y = x)
            using (Pen penIdentity = new Pen(isDark ? Color.FromArgb(75, 82, 95) : Color.FromArgb(175, 180, 190), 1f) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(penIdentity, padL, padT + plotH, padL + plotW, padT);
            }

            // 5. 현재 부스트 및 모드 수치 계산
            int currentBoost = _chkEnabled.Checked ? _trackBoost.Value : 0;
            int currentMode = Math.Max(0, Math.Min(4, _comboMode.SelectedIndex));
            int currentGuard = _chkHighlightGuard.Checked ? _trackHighlightGuard.Value : 0;
            int currentCustomPeak = _trackCustomPeak.Value;
            int currentCustomWidth = _trackCustomWidth.Value;

            int samples = 128;
            PointF[] curvePoints = new PointF[samples + 1];
            PointF[] identityPoints = new PointF[samples + 1];

            float maxLift = 0f;
            float maxLiftInput = 0f;
            PointF peakPoint = PointF.Empty;

            for (int i = 0; i <= samples; i++)
            {
                double inVal = (double)i / samples;
                double outVal = inVal;

                if (currentBoost > 0)
                {
                    outVal = Gamma.ApplyChannelCurve(inVal, 1.0, 1.0, 0.0, currentBoost, currentMode, 1.0, currentGuard, currentCustomPeak, currentCustomWidth);
                }

                float px = padL + (float)(inVal * plotW);
                float py = padT + plotH - (float)(outVal * plotH);

                curvePoints[i] = new PointF(px, py);
                identityPoints[i] = new PointF(px, padT + plotH - (float)(inVal * plotH));

                float lift = (float)(outVal - inVal);
                if (lift > maxLift)
                {
                    maxLift = lift;
                    maxLiftInput = (float)inVal;
                    peakPoint = new PointF(px, py);
                }
            }

            // 6. 부스트 면적 채우기 (반투명 네온 시안 그라데이션)
            if (currentBoost > 0 && maxLift > 0.001f)
            {
                using (GraphicsPath fillPath = new GraphicsPath())
                {
                    fillPath.AddLines(curvePoints);
                    for (int i = samples; i >= 0; i--)
                    {
                        fillPath.AddLine(identityPoints[i], identityPoints[i]);
                    }
                    fillPath.CloseFigure();

                    Color topFill = Color.FromArgb(70, 0, 210, 255);
                    Color botFill = Color.FromArgb(15, 0, 120, 220);
                    using (LinearGradientBrush fillBrush = new LinearGradientBrush(
                        new Point(padL, padT),
                        new Point(padL, padT + plotH),
                        topFill,
                        botFill))
                    {
                        g.FillPath(fillBrush, fillPath);
                    }
                }
            }

            // 7. 메인 전달 함수 곡선 그리기
            Color curveColor = currentBoost > 0
                ? Color.FromArgb(0, 220, 255)
                : (isDark ? Color.FromArgb(150, 155, 165) : Color.FromArgb(100, 110, 125));

            using (Pen penCurve = new Pen(curveColor, 2.2f))
            {
                g.DrawLines(penCurve, curvePoints);
            }

            // 8. 최대 리프트(Peak Lift) 마커 및 정보 배지 렌더링
            if (currentBoost > 0 && maxLift > 0.001f)
            {
                // 원형 마커
                g.FillEllipse(Brushes.White, peakPoint.X - 4, peakPoint.Y - 4, 8, 8);
                using (Pen penMarker = new Pen(Color.FromArgb(255, 130, 0), 2f))
                {
                    g.DrawEllipse(penMarker, peakPoint.X - 5, peakPoint.Y - 5, 10, 10);
                }

                // 배지 텍스트: e.g. "암부 피크: +24% (18% 구간)"
                string badgeText = _ko
                    ? $"피크: +{(int)Math.Round(maxLift * 100)}% (입력 {(int)Math.Round(maxLiftInput * 100)}%)"
                    : $"Peak: +{(int)Math.Round(maxLift * 100)}% (@{(int)Math.Round(maxLiftInput * 100)}%)";

                using (Font fBadge = new Font("Segoe UI", 7.8f, FontStyle.Bold))
                {
                    SizeF bSize = g.MeasureString(badgeText, fBadge);
                    float bx = Math.Min(padL + plotW - bSize.Width - 8, Math.Max(padL + 6, peakPoint.X - bSize.Width / 2));
                    float by = Math.Max(padT + 6, peakPoint.Y - bSize.Height - 10);

                    RectangleF rectBadge = new RectangleF(bx, by, bSize.Width + 6, bSize.Height + 2);
                    using (SolidBrush bBg = new SolidBrush(Color.FromArgb(210, 25, 30, 40)))
                    using (Pen bBorder = new Pen(Color.FromArgb(255, 160, 40), 1.2f))
                    {
                        g.FillRectangle(bBg, rectBadge);
                        g.DrawRectangle(bBorder, rectBadge.X, rectBadge.Y, rectBadge.Width, rectBadge.Height);
                    }
                    using (SolidBrush bText = new SolidBrush(Color.FromArgb(255, 220, 100)))
                    {
                        g.DrawString(badgeText, fBadge, bText, rectBadge.X + 3, rectBadge.Y + 1);
                    }
                }
            }

            // 상단 레이블: 암부(Shadow) vs 명부(Highlight) 영역 가이드
            using (Font fZone = new Font("Segoe UI", 7.5f))
            using (Brush bZone = new SolidBrush(Color.FromArgb(120, textMuted)))
            {
                g.DrawString(_ko ? "← 암부 집중 부스트" : "← Shadow Lift", fZone, bZone, padL + 8, padT + 6);
                g.DrawString(_ko ? "명부 보존 →" : "Highlight Keep →", fZone, bZone, padL + plotW - 75, padT + 6);
            }
        }

        // ==========================================
        // [시각화 2]: 암부 계조 식별 테스트 바 렌더링
        // ==========================================
        private void OnTestBarPanelPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = _testBarPanel.ClientSize.Width;
            int h = _testBarPanel.ClientSize.Height;

            // 10단계 극저조도 RGB 레벨 (0 ~ 60)
            int[] levels = { 0, 3, 6, 9, 13, 18, 24, 32, 44, 60 };
            int count = levels.Length;
            float stepW = (float)w / count;

            using (Font fStep = new Font("Segoe UI", 8.2f, FontStyle.Bold))
            using (Font fLvl = new Font("Segoe UI", 6.8f))
            using (StringFormat sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i < count; i++)
                {
                    float x = i * stepW;
                    RectangleF rectBox = new RectangleF(x, 0, stepW, h);

                    int baseLvl = levels[i];
                    Color boxColor = Color.FromArgb(baseLvl, baseLvl, baseLvl);

                    using (SolidBrush bBox = new SolidBrush(boxColor))
                    {
                        g.FillRectangle(bBox, rectBox);
                    }

                    // 박스 구분선
                    using (Pen pLine = new Pen(Color.FromArgb(Math.Min(255, baseLvl + 15), Math.Min(255, baseLvl + 15), Math.Min(255, baseLvl + 15)), 1f))
                    {
                        g.DrawLine(pLine, x, 0, x, h);
                    }

                    // 박스 내부 번호 (1 ~ 10):
                    // 원본 암부 신호에 미세한 명암 차이(Delta)를 주어,
                    // 부스트 0%에서는 모니터 암부 뭉개짐(Black Crush)으로 인해 1~3번 숫자가 배경과 묻혀 안 보이지만,
                    // 부스트를 올리면 그래픽카드 감마 램프가 극암부를 분리시켜 숨어있던 숫자가 뚜렷하게 식별됨.
                    int contrastDiff = (i == 0) ? 2 : (i == 1 ? 4 : (i == 2 ? 6 : (i == 3 ? 9 : 14)));

                    int digitLvl = Math.Min(255, baseLvl + contrastDiff);
                    Color digitColor = Color.FromArgb(digitLvl, digitLvl, digitLvl);

                    using (SolidBrush bDigit = new SolidBrush(digitColor))
                    {
                        // 상단에 스텝 번호 (1..10)
                        g.DrawString((i + 1).ToString(), fStep, bDigit, x + stepW / 2.0f, h * 0.38f, sfCenter);
                    }

                    // 하단에 기준 레벨 표시
                    int subLvl = Math.Min(255, baseLvl + 22);
                    using (SolidBrush bSub = new SolidBrush(Color.FromArgb(subLvl, subLvl, subLvl)))
                    {
                        g.DrawString(baseLvl.ToString(), fLvl, bSub, x + stepW / 2.0f, h * 0.78f, sfCenter);
                    }
                }
            }

            // 바깥 테두리
            using (Pen pBorder = new Pen(Color.FromArgb(70, 75, 88), 1.2f))
            {
                g.DrawRectangle(pBorder, 0, 0, w - 1, h - 1);
            }
        }

        private void OnResetClick(object sender, EventArgs e)
        {
            _isUpdatingUi = true;
            _chkEnabled.Checked = false;
            _trackBoost.Value = 0;
            _numBoost.Value = 0;
            _comboMode.SelectedIndex = 0;
            _chkHighlightGuard.Checked = false;
            _trackHighlightGuard.Value = 50;
            _numHighlightGuard.Value = 50;
            _trackCustomPeak.Value = 25;
            _numCustomPeak.Value = 25;
            _trackCustomWidth.Value = 3;
            UpdateCustomWidthLabel(3);
            UpdateControlStates(false);
            _isUpdatingUi = false;

            ApplyBoost(0);
            RefreshVisualizers();
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            _display.shadowBoost = _chkEnabled.Checked ? _trackBoost.Value : 0;
            _display.shadowBoostMode = Math.Max(0, Math.Min(4, _comboMode.SelectedIndex));
            _display.shadowBoostTint = 0;
            _display.highlightGuard = _chkHighlightGuard.Checked ? _trackHighlightGuard.Value : 0;
            _display.shadowBoostCustomPeak = _trackCustomPeak.Value;
            _display.shadowBoostCustomWidth = _trackCustomWidth.Value;
            IniFile.Shared.Write("ShadowBoostShowTestBar", _chkTestBar.Checked ? "True" : "False", "Settings");
            _onSettingsChanged?.Invoke();
            Close();
        }

        private void RollbackAllMonitors()
        {
            foreach (var kvp in _initialBackups)
            {
                var disp = kvp.Key;
                var backup = kvp.Value;
                if (disp == null || backup == null) continue;

                disp.shadowBoost = backup.shadowBoost;
                disp.shadowBoostMode = backup.shadowBoostMode;
                disp.shadowBoostTint = backup.shadowBoostTint;
                disp.highlightGuard = backup.highlightGuard;
                disp.shadowBoostCustomPeak = backup.shadowBoostCustomPeak;
                disp.shadowBoostCustomWidth = backup.shadowBoostCustomWidth;

                if (_displayService != null)
                {
                    _displayService.ApplyGammaOnly(disp);
                }
            }
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            RollbackAllMonitors();
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK)
            {
                RollbackAllMonitors();
            }
            base.OnFormClosing(e);
        }
    }
}
