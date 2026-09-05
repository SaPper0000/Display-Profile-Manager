using System;
using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class ShadowBoostSettingsForm : Form
    {
        private readonly Display.DisplayInfo _display;
        private readonly DisplayService _displayService;
        private readonly Action _onSettingsChanged;
        private readonly int _initialBoost;
        private readonly int _initialBoostMode;
        private readonly bool _ko;

        private CheckBox _chkEnabled;
        private ComboBox _comboMode;
        private TrackBar _trackBoost;
        private NumericUpDown _numBoost;
        private Label _lblValuePercent;
        private Button _btnPreset0;
        private Button _btnPreset25;
        private Button _btnPreset50;
        private Button _btnPreset75;
        private Label _lblGuideDesc;
        private Button _btnOk;
        private Button _btnCancel;

        private bool _isUpdatingUi;

        public ShadowBoostSettingsForm(
            Display.DisplayInfo display,
            DisplayService displayService,
            Action onSettingsChanged)
        {
            _display = display ?? throw new ArgumentNullException(nameof(display));
            _displayService = displayService;
            _onSettingsChanged = onSettingsChanged;
            _initialBoost = display.shadowBoost;
            _initialBoostMode = display.shadowBoostMode;
            _ko = LanguageManager.Korean;

            InitializeForm();
            BuildUI();
            LoadCurrentValues();
            ThemeManager.Apply(this);
        }

        private void InitializeForm()
        {
            Text = _ko ? "블랙 이퀄라이저 설정 (Black Equalizer)" : "Black Equalizer Settings";
            Width = 480;
            Height = 510;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
        }

        private void BuildUI()
        {
            // 상단 타이틀
            Label lblTitle = new Label
            {
                Text = _ko ? "🌑 블랙 이퀄라이저 (Black Equalizer)" : "🌑 Black Equalizer",
                Location = new Point(20, 16),
                AutoSize = true,
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold)
            };
            Controls.Add(lblTitle);

            // 대상 모니터 표시
            Label lblMonitor = new Label
            {
                Text = (_ko ? "대상 모니터: " : "Target Display: ") + _display.displayName,
                Location = new Point(22, 44),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = ThemeManager.IsDark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(100, 100, 100)
            };
            Controls.Add(lblMonitor);

            // 활성화 체크박스
            _chkEnabled = new CheckBox
            {
                Text = _ko ? "블랙 이퀄라이저 기능 활성화" : "Enable Black Equalizer",
                Location = new Point(22, 72),
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
                Location = new Point(130, 104),
                Size = new Size(310, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5f)
            };
            _comboMode.Items.AddRange(new object[]
            {
                _ko ? "1. FPS 표준 밸런스 (자연스러운 게이밍)" : "1. Balanced Toe (FPS Standard)",
                _ko ? "2. 야간전 (극암부 집중 리프팅)" : "2. Night Mode (Deep Shadow Boost)",
                _ko ? "3. 정밀 분리형 (중간/밝은톤 100% 원본 보존)" : "3. Precision Spline (Target Cut)"
            });
            _comboMode.SelectedIndexChanged += OnModeSelectedIndexChanged;
            Controls.Add(_comboMode);

            // 슬라이더 및 수치 조절
            Label lblSlider = new Label
            {
                Text = _ko ? "부스트 강도:" : "Boost Level:",
                Location = new Point(22, 150),
                AutoSize = true,
                Font = new Font("Segoe UI", 10f)
            };
            Controls.Add(lblSlider);

            _trackBoost = new TrackBar
            {
                Location = new Point(125, 146),
                Size = new Size(230, 45),
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
                Location = new Point(362, 148),
                Size = new Size(58, 28),
                Minimum = 0,
                Maximum = 100,
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Segoe UI", 10f)
            };
            _numBoost.ValueChanged += OnNumericValueChanged;
            Controls.Add(_numBoost);

            _lblValuePercent = new Label
            {
                Text = "%",
                Location = new Point(422, 152),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_lblValuePercent);

            // 빠른 프리셋 버튼 모음
            Label lblPresets = new Label
            {
                Text = _ko ? "빠른 설정:" : "Presets:",
                Location = new Point(22, 198),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(lblPresets);

            _btnPreset0 = CreatePresetButton(_ko ? "0% (끄기)" : "0% (Off)", 125, 194, 72, 0);
            _btnPreset25 = CreatePresetButton(_ko ? "25% (약)" : "25% (Low)", 203, 194, 72, 25);
            _btnPreset50 = CreatePresetButton(_ko ? "50% (중)" : "50% (Mid)", 281, 194, 72, 50);
            _btnPreset75 = CreatePresetButton(_ko ? "75% (강)" : "75% (High)", 359, 194, 72, 75);

            Controls.Add(_btnPreset0);
            Controls.Add(_btnPreset25);
            Controls.Add(_btnPreset50);
            Controls.Add(_btnPreset75);

            // 원리 안내 카드
            Panel guideCard = new Panel
            {
                Location = new Point(20, 240),
                Size = new Size(424, 160),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = ThemeManager.IsDark ? Color.FromArgb(35, 38, 42) : Color.FromArgb(245, 247, 250),
                Padding = new Padding(12)
            };

            Label lblGuideTitle = new Label
            {
                Text = _ko ? "💡 선택된 모드 특징" : "💡 Selected Mode Characteristics",
                Location = new Point(10, 10),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = ThemeManager.IsDark ? Color.FromArgb(255, 215, 100) : Color.FromArgb(180, 120, 0)
            };
            guideCard.Controls.Add(lblGuideTitle);

            _lblGuideDesc = new Label
            {
                Location = new Point(10, 36),
                Size = new Size(402, 112),
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = ThemeManager.IsDark ? Color.FromArgb(215, 215, 215) : Color.FromArgb(70, 70, 70)
            };
            guideCard.Controls.Add(_lblGuideDesc);
            Controls.Add(guideCard);

            // 하단 버튼들
            _btnOk = new Button
            {
                Text = _ko ? "확인 (저장)" : "Save",
                Location = new Point(234, 424),
                Size = new Size(100, 36),
                DialogResult = DialogResult.OK,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            _btnOk.Click += OnOkClick;
            Controls.Add(_btnOk);

            _btnCancel = new Button
            {
                Text = _ko ? "취소" : "Cancel",
                Location = new Point(344, 424),
                Size = new Size(100, 36),
                DialogResult = DialogResult.Cancel,
                Font = new Font("Segoe UI", 9.5f)
            };
            _btnCancel.Click += OnCancelClick;
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private Button CreatePresetButton(string text, int x, int y, int width, int boostValue)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 30),
                Font = new Font("Segoe UI", 9f),
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

        private void LoadCurrentValues()
        {
            _isUpdatingUi = true;
            int current = Math.Max(0, Math.Min(100, _display.shadowBoost));
            int mode = Math.Max(0, Math.Min(2, _display.shadowBoostMode));

            _chkEnabled.Checked = current > 0;
            _comboMode.SelectedIndex = mode;
            _trackBoost.Value = current > 0 ? current : 35;
            _numBoost.Value = current > 0 ? current : 35;

            UpdateControlStates(current > 0);
            UpdateGuideDescription(mode);
            _isUpdatingUi = false;
        }

        private void OnModeSelectedIndexChanged(object sender, EventArgs e)
        {
            int mode = Math.Max(0, Math.Min(2, _comboMode.SelectedIndex));
            UpdateGuideDescription(mode);

            if (_isUpdatingUi) return;

            _display.shadowBoostMode = mode;
            if (_displayService != null)
            {
                _displayService.ApplyGammaOnly(_display);
            }
        }

        private void UpdateGuideDescription(int mode)
        {
            switch (mode)
            {
                case 1:
                    _lblGuideDesc.Text = _ko
                        ? "• [야간전 모드]\n" +
                          "• 칠흑 같은 극암부(0.05~0.20)를 집중적으로 끄집어 올립니다.\n" +
                          "• 빛 하나 없는 캄캄한 실내 구석이나 짙은 어둠에서 최상의 시인성을 발휘합니다."
                        : "• [Night Mode]\n" +
                          "• Aggressively lifts deep shadows (0.05~0.20) for maximum dark visibility.\n" +
                          "• Excels in pitch-black interiors and dark environments.";
                    break;

                case 2:
                    _lblGuideDesc.Text = _ko
                        ? "• [정밀 분리형 모드 (타깃 컷)]\n" +
                          "• 암부만 급감쇠 곡선으로 올리고, 중간/밝은 톤은 원본 밝기를 온전히 유지합니다.\n" +
                          "• 어둠 속에 은폐한 대상의 외곽선(실루엣)과 명암 경계가 뚜렷하게 분리됩니다."
                        : "• [Precision Spline Mode (Target Cut)]\n" +
                          "• Lifts dark shadows while keeping mid-tones and highlights true to source.\n" +
                          "• Sharpens silhouette and edge contrast of targets in shadows.";
                    break;

                default:
                    _lblGuideDesc.Text = _ko
                        ? "• [FPS 표준 밸런스 모드]\n" +
                          "• 가장 자연스러운 게이밍 표준 3차 토우(Toe) 곡선입니다.\n" +
                          "• 어두운 구석(0~35%)을 균일하게 밝혀주며, 색 뭉침(밴딩) 없이 눈이 가장 편안합니다."
                        : "• [Balanced Toe Mode (FPS Standard)]\n" +
                          "• Industry-standard balanced cubic toe curve with zero banding.\n" +
                          "• Evenly lifts dark corners while keeping visuals natural and eye-friendly.";
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
        }

        private void OnSliderValueChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUi) return;
            int val = _trackBoost.Value;
            _isUpdatingUi = true;
            _numBoost.Value = val;
            // 0%가 되어도 체크박스를 해제하거나 컨트롤을 비활성화하지 않음
            if (val > 0 && !_chkEnabled.Checked)
            {
                _chkEnabled.Checked = true;
                UpdateControlStates(true);
            }
            _isUpdatingUi = false;

            ApplyBoost(val);
        }

        private void OnNumericValueChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUi) return;
            int val = (int)_numBoost.Value;
            _isUpdatingUi = true;
            _trackBoost.Value = val;
            // 0%가 되어도 체크박스를 해제하거나 컨트롤을 비활성화하지 않음
            if (val > 0 && !_chkEnabled.Checked)
            {
                _chkEnabled.Checked = true;
                UpdateControlStates(true);
            }
            _isUpdatingUi = false;

            ApplyBoost(val);
        }

        private void ApplyBoost(int boostValue)
        {
            boostValue = Math.Max(0, Math.Min(100, boostValue));
            _isUpdatingUi = true;
            _trackBoost.Value = boostValue;
            _numBoost.Value = boostValue;
            _isUpdatingUi = false;

            _display.shadowBoost = _chkEnabled.Checked ? boostValue : 0;
            _display.shadowBoostMode = Math.Max(0, Math.Min(2, _comboMode.SelectedIndex));

            // 실시간 감마 램프 화면 즉시 반영
            if (_displayService != null)
            {
                _displayService.ApplyGammaOnly(_display);
            }
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            _display.shadowBoost = _chkEnabled.Checked ? _trackBoost.Value : 0;
            _display.shadowBoostMode = Math.Max(0, Math.Min(2, _comboMode.SelectedIndex));
            _onSettingsChanged?.Invoke();
            Close();
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            // 원래 상태로 화면 복원
            _display.shadowBoost = _initialBoost;
            _display.shadowBoostMode = _initialBoostMode;
            if (_displayService != null)
            {
                _displayService.ApplyGammaOnly(_display);
            }
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 사용자가 X 버튼을 눌러 닫았을 때도 저장하지 않고 닫으면 원래 상태로 복원
            if (DialogResult != DialogResult.OK &&
                (_display.shadowBoost != _initialBoost || _display.shadowBoostMode != _initialBoostMode))
            {
                _display.shadowBoost = _initialBoost;
                _display.shadowBoostMode = _initialBoostMode;
                if (_displayService != null)
                {
                    _displayService.ApplyGammaOnly(_display);
                }
            }
            base.OnFormClosing(e);
        }
    }
}
