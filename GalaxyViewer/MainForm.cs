using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenTK.GLControl;
using OpenTK.Windowing.Common;

namespace GalaxyViewer
{
    public class MainForm : Form
    {
        private readonly GLControl _glControl;
        private readonly GalaxyRenderer _renderer;
        private readonly GalaxyGenerator _generator = new();
        private GalaxyParameters _parameters = new();
        private readonly Camera _camera = new();

        private readonly ScrubbableNumeric _starCountBox;
        private readonly ScrubbableNumeric _armCountBox;
        private readonly ScrubbableNumeric _armTwistBox;
        private readonly ScrubbableNumeric _armSpreadBox;
        private readonly ScrubbableNumeric _diskRadiusBox;
        private readonly ScrubbableNumeric _verticalThicknessBox;
        private readonly ScrubbableNumeric _noiseBox;
        private readonly ScrubbableNumeric _coreFalloffBox;
        private readonly ScrubbableNumeric _brightnessBox;
        private readonly ScrubbableNumeric _seedBox;
        private readonly ComboBox _presetBox;
        private readonly Button _resetButton;
        private readonly Button _randomizeSeedButton;
        private readonly Label _statusLabel;
        private readonly System.Windows.Forms.Timer _regenTimer;
        private static readonly Color PanelBackColor = Color.FromArgb(32, 32, 32);
        private static readonly Color PanelBorderColor = Color.FromArgb(55, 55, 55);
        private static readonly Color AccentColor = Color.FromArgb(78, 156, 255);

        private bool _suppressEvents;
        private bool _dragging;
        private Point _lastMouse;
        private CancellationTokenSource? _regenCts;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastFrameTime;
        private float _smoothedFps = 60f;
        private int _currentStarCount;

        public MainForm()
        {
            Text = "Galaxy Viewer";
            ClientSize = new Size(1280, 800);
            MinimumSize = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = PanelBackColor;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 360,
                FixedPanel = FixedPanel.Panel1,
                BackColor = PanelBackColor,
            };

            Controls.Add(split);

            var settingsPanel = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(10, 6, 10, 6),
                BackColor = PanelBackColor,
            };
            settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            split.Panel1.Controls.Add(settingsPanel);
            split.Panel1.BackColor = PanelBackColor;

            _glControl = new GLControl(new GLControlSettings
            {
                API = ContextAPI.OpenGL,
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core,
                Flags = ContextFlags.ForwardCompatible,
                NumberOfSamples = 0,
                IsEventDriven = false,
            })
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
            };
            split.Panel2.Controls.Add(_glControl);

            _renderer = new GalaxyRenderer(_glControl);

            _starCountBox = CreateNumeric(1000, 500000, _parameters.StarCount, 1000);
            _armCountBox = CreateNumeric(1, 8, _parameters.ArmCount, 1);
            _armTwistBox = CreateNumeric(0, 12, (decimal)_parameters.ArmTwist, 0.1m, 1);
            _armSpreadBox = CreateNumeric(0, 1, (decimal)_parameters.ArmSpread, 0.01m, 2);
            _diskRadiusBox = CreateNumeric(5, 120, (decimal)_parameters.DiskRadius, 0.5m, 1);
            _verticalThicknessBox = CreateNumeric(0, 5, (decimal)_parameters.VerticalThickness, 0.05m, 2);
            _noiseBox = CreateNumeric(0, 1, (decimal)_parameters.Noise, 0.01m, 2);
            _coreFalloffBox = CreateNumeric(0.1m, 6, (decimal)_parameters.CoreFalloff, 0.1m, 2);
            _brightnessBox = CreateNumeric(0.1m, 2.5m, (decimal)_parameters.Brightness, 0.05m, 2);
            _seedBox = CreateNumeric(0, 1000000, _parameters.Seed, 1);

            _presetBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(36, 36, 36),
                ForeColor = Color.WhiteSmoke,
                Margin = new Padding(0, 2, 0, 2),
            };
            _presetBox.Items.AddRange(new object[] { "Default", "Barred spiral", "Compact core", "Diffuse arms" });
            _presetBox.SelectedIndex = 0;
            _presetBox.SelectedIndexChanged += (_, _) => LoadPreset(_presetBox.SelectedItem?.ToString() ?? "");

            _resetButton = CreateFlatButton("Reset");
            _resetButton.Click += (_, _) => LoadPreset("Default");

            _randomizeSeedButton = CreateFlatButton("Randomize");
            _randomizeSeedButton.Click += (_, _) => RandomizeSeed();

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = "Ready",
                ForeColor = Color.Gainsboro,
                Padding = new Padding(0, 6, 0, 0),
            };

            int row = 0;
            AddRow(settingsPanel, "Preset", _presetBox, row++);
            AttachDragHandle("Star count", settingsPanel, _starCountBox, row++);
            AttachDragHandle("Arm count", settingsPanel, _armCountBox, row++);
            AttachDragHandle("Arm twist", settingsPanel, _armTwistBox, row++);
            AttachDragHandle("Arm spread", settingsPanel, _armSpreadBox, row++);
            AttachDragHandle("Disk radius", settingsPanel, _diskRadiusBox, row++);
            AttachDragHandle("Vertical thickness", settingsPanel, _verticalThicknessBox, row++);
            AttachDragHandle("Noise", settingsPanel, _noiseBox, row++);
            AttachDragHandle("Core falloff", settingsPanel, _coreFalloffBox, row++);
            AttachDragHandle("Brightness", settingsPanel, _brightnessBox, row++);
            AttachDragHandle("Seed", settingsPanel, _seedBox, row++);

            settingsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            settingsPanel.Controls.Add(_resetButton, 0, row);
            settingsPanel.Controls.Add(_randomizeSeedButton, 1, row);
            row++;

            settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            settingsPanel.SetColumnSpan(_statusLabel, 2);
            settingsPanel.Controls.Add(_statusLabel, 0, row);

            _regenTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _regenTimer.Tick += (_, _) => { _regenTimer.Stop(); RegenerateGalaxy(); };

            HookEvents();
        }

        private void HookEvents()
        {
            _glControl.Load += (_, _) =>
            {
                try
                {
                    _renderer.Initialize();
                    RegenerateGalaxy();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to initialize renderer: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            _glControl.Paint += (_, _) => RenderFrame();
            _glControl.Resize += (_, _) => _glControl.Invalidate();

            _glControl.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    _dragging = true;
                    _lastMouse = e.Location;
                }
            };

            _glControl.MouseUp += (_, _) => _dragging = false;

            _glControl.MouseMove += (_, e) =>
            {
                if (_dragging)
                {
                    var delta = new Point(e.X - _lastMouse.X, e.Y - _lastMouse.Y);
                    _camera.Rotate(delta.X * 0.005f, -delta.Y * 0.005f);
                    _lastMouse = e.Location;
                }
            };

            _glControl.MouseWheel += (_, e) =>
            {
                _camera.Zoom(-e.Delta * 0.05f);
            };

            Application.Idle += OnIdle;

            _starCountBox.ValueChanged += OnParameterChanged;
            _armCountBox.ValueChanged += OnParameterChanged;
            _armTwistBox.ValueChanged += OnParameterChanged;
            _armSpreadBox.ValueChanged += OnParameterChanged;
            _diskRadiusBox.ValueChanged += OnParameterChanged;
            _verticalThicknessBox.ValueChanged += OnParameterChanged;
            _noiseBox.ValueChanged += OnParameterChanged;
            _coreFalloffBox.ValueChanged += OnParameterChanged;
            _brightnessBox.ValueChanged += OnParameterChanged;
            _seedBox.ValueChanged += OnParameterChanged;
        }

        private void OnParameterChanged(object? sender, EventArgs e)
        {
            if (_suppressEvents)
            {
                return;
            }

            _parameters.StarCount = (int)_starCountBox.Value;
            _parameters.ArmCount = (int)_armCountBox.Value;
            _parameters.ArmTwist = (float)_armTwistBox.Value;
            _parameters.ArmSpread = (float)_armSpreadBox.Value;
            _parameters.DiskRadius = (float)_diskRadiusBox.Value;
            _parameters.VerticalThickness = (float)_verticalThicknessBox.Value;
            _parameters.Noise = (float)_noiseBox.Value;
            _parameters.CoreFalloff = (float)_coreFalloffBox.Value;
            _parameters.Brightness = (float)_brightnessBox.Value;
            _parameters.Seed = (int)_seedBox.Value;

            _regenTimer.Stop();
            _regenTimer.Start();
            _statusLabel.Text = "Pending update...";
        }

        private void LoadPreset(string name)
        {
            var preset = new GalaxyParameters();
            switch (name.ToLowerInvariant())
            {
                case "barred spiral":
                    preset.StarCount = 80000;
                    preset.ArmCount = 2;
                    preset.ArmTwist = 8f;
                    preset.ArmSpread = 0.25f;
                    preset.DiskRadius = 50f;
                    preset.VerticalThickness = 0.45f;
                    preset.Noise = 0.2f;
                    preset.CoreFalloff = 1.6f;
                    preset.Brightness = 1.1f;
                    preset.Seed = _parameters.Seed;
                    break;
                case "compact core":
                    preset.StarCount = 60000;
                    preset.ArmCount = 3;
                    preset.ArmTwist = 6f;
                    preset.ArmSpread = 0.3f;
                    preset.DiskRadius = 35f;
                    preset.VerticalThickness = 0.7f;
                    preset.Noise = 0.18f;
                    preset.CoreFalloff = 2.5f;
                    preset.Brightness = 1.2f;
                    preset.Seed = _parameters.Seed;
                    break;
                case "diffuse arms":
                    preset.StarCount = 70000;
                    preset.ArmCount = 4;
                    preset.ArmTwist = 4.5f;
                    preset.ArmSpread = 0.45f;
                    preset.DiskRadius = 55f;
                    preset.VerticalThickness = 0.5f;
                    preset.Noise = 0.35f;
                    preset.CoreFalloff = 1.8f;
                    preset.Brightness = 1.0f;
                    preset.Seed = _parameters.Seed;
                    break;
                default:
                    preset = new GalaxyParameters { Seed = _parameters.Seed };
                    break;
            }

            _parameters = preset;
            _suppressEvents = true;
            SetValueClamped(_starCountBox, preset.StarCount);
            SetValueClamped(_armCountBox, preset.ArmCount);
            SetValueClamped(_armTwistBox, (decimal)preset.ArmTwist);
            SetValueClamped(_armSpreadBox, (decimal)preset.ArmSpread);
            SetValueClamped(_diskRadiusBox, (decimal)preset.DiskRadius);
            SetValueClamped(_verticalThicknessBox, (decimal)preset.VerticalThickness);
            SetValueClamped(_noiseBox, (decimal)preset.Noise);
            SetValueClamped(_coreFalloffBox, (decimal)preset.CoreFalloff);
            SetValueClamped(_brightnessBox, (decimal)preset.Brightness);
            SetValueClamped(_seedBox, preset.Seed);
            _suppressEvents = false;

            RegenerateGalaxy();
        }

        private static void SetValueClamped(ScrubbableNumeric control, decimal value)
        {
            control.Value = ClampDecimal(value, control.Minimum, control.Maximum);
        }

        private static decimal ClampDecimal(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void RandomizeSeed()
        {
            var seed = Guid.NewGuid().GetHashCode() & int.MaxValue;
            _parameters.Seed = seed;
            _suppressEvents = true;
            SetValueClamped(_seedBox, seed);
            _suppressEvents = false;
            RegenerateGalaxy();
        }

        private void OnIdle(object? sender, EventArgs e)
        {
            _glControl.Invalidate();
        }

        private void RenderFrame()
        {
            double now = _clock.Elapsed.TotalSeconds;
            float delta = (float)(now - _lastFrameTime);
            _lastFrameTime = now;

            _renderer.Render(_camera);
            UpdateFps(delta);
        }

        private void UpdateFps(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            float fps = 1f / deltaSeconds;
            _smoothedFps = (_smoothedFps * 0.9f) + (fps * 0.1f);
            _statusLabel.Text = $"Stars: {_currentStarCount:N0} | FPS: {_smoothedFps:F1}";
        }

        private void RegenerateGalaxy()
        {
            _regenCts?.Cancel();
            _regenCts = new CancellationTokenSource();
            var token = _regenCts.Token;
            var parameters = _parameters.Clone();
            _statusLabel.Text = "Generating...";

            Task.Run(() => _generator.Generate(parameters, token), token)
                .ContinueWith(t =>
                {
                    if (t.IsCanceled)
                    {
                        return;
                    }

                    if (t.IsFaulted)
                    {
                        MessageBox.Show($"Failed to generate galaxy: {t.Exception?.GetBaseException().Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    _currentStarCount = t.Result.Count;
                    _renderer.UpdateStars(t.Result);
                    _statusLabel.Text = $"Stars: {_currentStarCount:N0} | FPS: {_smoothedFps:F1}";
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static ScrubbableNumeric CreateNumeric(decimal min, decimal max, decimal value, decimal increment, int decimalPlaces = 0)
        {
            return new ScrubbableNumeric
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Min(Math.Max(value, min), max),
                DecimalPlaces = decimalPlaces,
                Increment = increment,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 1, 0, 1),
                MinimumSize = new Size(0, 26),
            };
        }

        private static Label AddRow(TableLayoutPanel panel, string label, Control control, int row)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            var lbl = new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 0, 0, 0),
                Margin = new Padding(0, 0, 4, 0),
                ForeColor = Color.Gainsboro,
            };
            panel.Controls.Add(lbl, 0, row);
            panel.Controls.Add(control, 1, row);
            return lbl;
        }

        private void AttachDragHandle(string label, TableLayoutPanel panel, ScrubbableNumeric control, int row)
        {
            var lbl = AddRow(panel, label, control, row);
            control.AttachDragHandle(lbl);
        }

        private static void AddSeparator(TableLayoutPanel panel, int row)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 1f));
            var line = new Panel
            {
                Height = 1,
                Dock = DockStyle.Fill,
                BackColor = PanelBorderColor,
                Margin = new Padding(0, 2, 0, 2),
            };
            panel.SetColumnSpan(line, 2);
            panel.Controls.Add(line, 0, row);
        }

        private Button CreateFlatButton(string text)
        {
            return new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.WhiteSmoke,
                Margin = new Padding(0, 2, 0, 2),
                Height = 26,
                FlatAppearance =
                {
                    BorderColor = AccentColor,
                    BorderSize = 1,
                    MouseOverBackColor = Color.FromArgb(60, 60, 60),
                    MouseDownBackColor = Color.FromArgb(55, 55, 55),
                },
            };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            _regenCts?.Cancel();
            _renderer.Dispose();
        }
    }
}
