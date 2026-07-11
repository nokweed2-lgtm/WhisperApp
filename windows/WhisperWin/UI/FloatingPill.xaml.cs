using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using WhisperWin.Core;

namespace WhisperWin.UI
{
    /// <summary>
    /// Borderless, always-on-top, click-through pill shown at the bottom-center of the active
    /// monitor while recording/processing. Click-through is applied via WS_EX_TRANSPARENT so
    /// mouse events pass through to whatever is underneath.
    /// </summary>
    public partial class FloatingPill : Window
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int GWL_EXSTYLE = -20;

        private const int MaxBars = 12;
        private readonly Rectangle[] _bars = new Rectangle[MaxBars];

        public FloatingPill()
        {
            InitializeComponent();
            BuildWaveformBars();
            Loaded += (s, e) => MakeClickThrough();
        }

        private void BuildWaveformBars()
        {
            for (var i = 0; i < MaxBars; i++)
            {
                var bar = new Rectangle
                {
                    Width = 3,
                    Height = 4,
                    Margin = new Thickness(1, 0, 1, 0),
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                    Fill = Brushes.LightGreen,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _bars[i] = bar;
                WaveformPanel.Children.Add(bar);
            }
        }

        private void MakeClickThrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }

        /// <summary>Positions the pill at the bottom-center of the monitor containing the cursor/active window.</summary>
        public void PositionBottomCenter()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Bottom - Height - 24;
        }

        public void SetStage(DictationStage stage)
        {
            switch (stage)
            {
                case DictationStage.Recording:
                    StatusDot.Fill = Brushes.OrangeRed;
                    WaveformPanel.Visibility = Visibility.Visible;
                    StatusText.Visibility = Visibility.Collapsed;
                    break;
                case DictationStage.Transcribing:
                    ShowText("Transcribing...");
                    break;
                case DictationStage.Correcting:
                    ShowText("AI correction...");
                    break;
                case DictationStage.Error:
                    ShowText("Error");
                    break;
                default:
                    WaveformPanel.Visibility = Visibility.Collapsed;
                    StatusText.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void ShowText(string text)
        {
            WaveformPanel.Visibility = Visibility.Collapsed;
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = text;
            StatusDot.Fill = Brushes.DodgerBlue;
        }

        /// <summary>Updates the waveform bar heights from a mic level peak in [0, 1].</summary>
        public void UpdateLevel(float peak)
        {
            var rnd = new Random();
            foreach (var bar in _bars)
            {
                // Simple jittered visualization: scale each bar around the current peak.
                var jitter = 0.5 + rnd.NextDouble() * 0.5;
                bar.Height = Math.Max(4, Math.Min(28, peak * 28 * jitter));
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
