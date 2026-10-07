using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class DefaultMismatchForm : Form
    {
        public enum Choice
        {
            ApplyNewDefault,
            RestoreSavedDefault,
            KeepCurrent
        }

        public Choice SelectedChoice { get; private set; } = Choice.KeepCurrent;

        public DefaultMismatchForm(string monitorName, List<string> differences)
        {
            bool ko = LanguageManager.Korean;
            Text = ko ? "💡 모니터 설정 변경 감지" : "💡 Display Settings Change Detected";
            Width = 460;
            Height = 370;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            BuildUI(monitorName, differences, ko);
            ThemeManager.Apply(this);
        }

        private void BuildUI(string monitorName, List<string> differences, bool ko)
        {
            Label lblTitle = new Label
            {
                Text = ko ? "현재 모니터 설정이 기존 [기본값]과 다릅니다." : "Current monitor settings differ from [Default].",
                Location = new Point(20, 16),
                Size = new Size(405, 22),
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
            };
            Controls.Add(lblTitle);

            Label lblMonitor = new Label
            {
                Text = (ko ? "대상 모니터: " : "Target Monitor: ") + monitorName,
                Location = new Point(20, 42),
                Size = new Size(405, 18),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.DimGray
            };
            Controls.Add(lblMonitor);

            // 변경 항목 목록 패널
            Panel panelDiff = new Panel
            {
                Location = new Point(20, 68),
                Size = new Size(405, 105),
                BorderStyle = BorderStyle.FixedSingle,
                AutoScroll = true,
                BackColor = SystemColors.Window
            };

            int yOffset = 8;
            if (differences != null)
            {
                foreach (string diff in differences)
                {
                    Label item = new Label
                    {
                        Text = "•  " + diff,
                        Location = new Point(12, yOffset),
                        Size = new Size(365, 20),
                        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                        AutoSize = false
                    };
                    panelDiff.Controls.Add(item);
                    yOffset += 24;
                }
            }
            Controls.Add(panelDiff);

            Label lblQuestion = new Label
            {
                Text = ko
                    ? "현재 설정을 새로운 기본값으로 갱신하시겠습니까?\r\n(※ 실수로 누르셨더라도 메인 화면의 [기본값 갱신]으로 언제든 재수정 가능합니다.)"
                    : "Update default settings with current values?\r\n(You can always change it later using [Update Default].)",
                Location = new Point(20, 182),
                Size = new Size(405, 36),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };
            Controls.Add(lblQuestion);

            // 3개 선택 버튼
            Button btnApplyNew = new Button
            {
                Text = ko ? "💾 새 기본값으로 저장 (권장)" : "💾 Save as New Default",
                Location = new Point(20, 230),
                Size = new Size(405, 32),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnApplyNew.Click += (s, e) =>
            {
                SelectedChoice = Choice.ApplyNewDefault;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnApplyNew);

            Button btnRestoreOld = new Button
            {
                Text = ko ? "🔄 기존 기본값 복원 (화면 원복)" : "🔄 Restore Saved Default",
                Location = new Point(20, 268),
                Size = new Size(198, 30),
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            btnRestoreOld.Click += (s, e) =>
            {
                SelectedChoice = Choice.RestoreSavedDefault;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnRestoreOld);

            Button btnKeep = new Button
            {
                Text = ko ? "취소 (이번만 유지)" : "Cancel (Keep Current)",
                Location = new Point(227, 268),
                Size = new Size(198, 30),
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            btnKeep.Click += (s, e) =>
            {
                SelectedChoice = Choice.KeepCurrent;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(btnKeep);

            AcceptButton = btnApplyNew;
            CancelButton = btnKeep;
        }
    }
}
