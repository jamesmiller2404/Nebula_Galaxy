using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private readonly ScrubbableNumeric _bulgeRadiusBox;
        private readonly ScrubbableNumeric _bulgeStarCountBox;
        private readonly ScrubbableNumeric _bulgeFalloffBox;
        private readonly ScrubbableNumeric _bulgeVerticalScaleBox;
        private readonly ScrubbableNumeric _bulgeBrightnessBox;
        private readonly ComboBox _presetBox;
        private readonly Button _resetButton;
        private readonly Button _randomizeSeedButton;
        private readonly Label _statusLabel;
        private readonly TableLayoutPanel _settingsPanel;
        private readonly Panel _presetPreview;
        private readonly ToolTip _toolTip;
        private readonly Label _helpOverlay;
        private readonly System.Windows.Forms.Timer _helpTimer;
        private readonly System.Windows.Forms.Timer _regenTimer;
        private static readonly Color PanelBackColor = Color.FromArgb(32, 32, 32);
        private static readonly Color PanelBorderColor = Color.FromArgb(55, 55, 55);
        private static readonly Color AccentColor = Color.FromArgb(78, 156, 255);
        private static readonly Color ControlSurfaceColor = Color.FromArgb(42, 42, 42);
        private static readonly Color SecondarySurfaceColor = Color.FromArgb(28, 28, 28);
        private static readonly Color HeaderTextColor = Color.FromArgb(210, 210, 210);
        private const float SidePanelScreenRatio = 1f / 6f;
        private const int MinimumSidePanelWidth = 260;
        private const int TargetViewportWidth = 920;
        private const int StandardControlHeight = 30;
        private const int StandardRowHeight = 34;

        private bool _suppressEvents;
        private bool _dragging;
        private Point _lastMouse;
        private CancellationTokenSource? _regenCts;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastFrameTime;
        private float _smoothedFps = 60f;
        private int _currentStarCount;
        private bool _isGenerating;

        public MainForm()
        {
            Text = "Galaxy Viewer";
            var hostWidth = Screen.PrimaryScreen?.WorkingArea.Width ?? 1920;
            var sidePanelWidth = Math.Max(MinimumSidePanelWidth, (int)(hostWidth * SidePanelScreenRatio));
            ClientSize = new Size(sidePanelWidth + TargetViewportWidth, 800);
            MinimumSize = new Size(Math.Max(1000, sidePanelWidth + 100), 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = PanelBackColor;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            _toolTip = new ToolTip
            {
                AutomaticDelay = 200,
                AutoPopDelay = 10000,
                InitialDelay = 200,
                ReshowDelay = 100,
                BackColor = Color.FromArgb(48, 48, 48),
                ForeColor = Color.WhiteSmoke,
            };

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Panel1MinSize = MinimumSidePanelWidth,
                FixedPanel = FixedPanel.Panel1,
                BackColor = PanelBackColor,
            };

            Controls.Add(split);
            // Set after docking so we don't get clamped by the default control size.
            split.SplitterDistance = Math.Max(
                MinimumSidePanelWidth,
                Math.Min(sidePanelWidth, split.Width - split.Panel2MinSize - split.SplitterWidth));

            var panelHost = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12, 10, 12, 10),
                BackColor = PanelBackColor,
            };
            panelHost.RowStyles.Add(new RowStyle(SizeType.Absolute, StandardRowHeight + 12));
            panelHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            panelHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            split.Panel1.Controls.Add(panelHost);
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
            _helpOverlay = new Label
            {
                AutoSize = false,
                Height = 32,
                Width = Math.Max(200, _glControl.ClientSize.Width - 24),
                Location = new Point(12, 12),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = "Left-drag: orbit | Scroll: zoom | Drag values to scrub (Shift=10x, Alt=0.1x)",
                BackColor = Color.FromArgb(150, 16, 16, 16),
                ForeColor = Color.WhiteSmoke,
                Padding = new Padding(10, 6, 10, 6),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false,
            };
            _glControl.Controls.Add(_helpOverlay);
            _helpTimer = new System.Windows.Forms.Timer { Interval = 2800 };
            _helpTimer.Tick += (_, _) =>
            {
                _helpTimer.Stop();
                _helpOverlay.Visible = false;
            };

            _renderer = new GalaxyRenderer(_glControl);

            _starCountBox = CreateNumeric(1000, 5_000_000, _parameters.StarCount, 10_000);
            _armCountBox = CreateNumeric(1, 8, _parameters.ArmCount, 1);
            _armTwistBox = CreateNumeric(0, 12, (decimal)_parameters.ArmTwist, 0.1m, 1);
            _armSpreadBox = CreateNumeric(0, 1, (decimal)_parameters.ArmSpread, 0.01m, 2);
            _diskRadiusBox = CreateNumeric(5, 120, (decimal)_parameters.DiskRadius, 0.5m, 1);
            _verticalThicknessBox = CreateNumeric(0, 5, (decimal)_parameters.VerticalThickness, 0.05m, 2);
            _noiseBox = CreateNumeric(0, 1, (decimal)_parameters.Noise, 0.01m, 2);
            _coreFalloffBox = CreateNumeric(0.1m, 6, (decimal)_parameters.CoreFalloff, 0.1m, 2);
            _brightnessBox = CreateNumeric(0.1m, 2.5m, (decimal)_parameters.Brightness, 0.05m, 2);
            _seedBox = CreateNumeric(0, 1000000, _parameters.Seed, 1);

            _bulgeRadiusBox = CreateNumeric(0.1m, 20, (decimal)_parameters.BulgeRadius, 0.1m, 1);
            _bulgeStarCountBox = CreateNumeric(0, 100000, _parameters.BulgeStarCount, 1000);
            _bulgeFalloffBox = CreateNumeric(0.5m, 10, (decimal)_parameters.BulgeFalloff, 0.1m, 1);
            _bulgeVerticalScaleBox = CreateNumeric(0.1m, 4, (decimal)_parameters.BulgeVerticalScale, 0.05m, 2);
            _bulgeBrightnessBox = CreateNumeric(0.1m, 6, (decimal)_parameters.BulgeBrightness, 0.05m, 2);

            _presetPreview = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 10, 0),
                MinimumSize = new Size(72, StandardControlHeight),
            };
            _presetPreview.Paint += DrawPresetPreview;
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
                AutoSize = false,
                Text = "Ready",
                ForeColor = Color.Gainsboro,
                Padding = new Padding(4, 4, 4, 2),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            var headerPanel = new TableLayoutPanel
            {
                ColumnCount = 5,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6),
                BackColor = PanelBackColor,
            };
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76f));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56f));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88f));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
            var presetLabel = new Label
            {
                Text = "Preset",
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 0, 6, 0),
                Margin = new Padding(0, 2, 0, 2),
                ForeColor = Color.Gainsboro,
            };
            headerPanel.Controls.Add(_presetPreview, 0, 0);
            headerPanel.Controls.Add(presetLabel, 1, 0);
            headerPanel.Controls.Add(_presetBox, 2, 0);
            headerPanel.Controls.Add(_resetButton, 3, 0);
            headerPanel.Controls.Add(_randomizeSeedButton, 4, 0);

            var statusPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SecondarySurfaceColor,
                Padding = new Padding(8, 4, 8, 4),
                Margin = new Padding(0, 0, 0, 8),
                Height = 32,
            };
            statusPanel.Controls.Add(_statusLabel);

            _settingsPanel = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(0, 0, 0, 10),
                BackColor = PanelBackColor,
            };
            _settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            _settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));

            panelHost.Controls.Add(headerPanel, 0, 0);
            panelHost.Controls.Add(statusPanel, 0, 1);
            panelHost.Controls.Add(_settingsPanel, 0, 2);

            int row = 0;
            AddSectionHeader(_settingsPanel, "Galaxy disk", ref row);
            AttachDragHandle("Star count", "Total number of disk stars. Higher counts cost performance but look denser.", _settingsPanel, _starCountBox, row++);
            AttachDragHandle("Arm count", "Number of spiral arms.", _settingsPanel, _armCountBox, row++);
            AttachDragHandle("Arm twist", "How tightly the arms wind around the core.", _settingsPanel, _armTwistBox, row++);
            AttachDragHandle("Arm spread", "Thickness/dispersion of the arms.", _settingsPanel, _armSpreadBox, row++);
            AttachDragHandle("Disk radius", "Overall radius of the main disk.", _settingsPanel, _diskRadiusBox, row++);
            AttachDragHandle("Vertical thickness", "Height of the disk in the Z axis.", _settingsPanel, _verticalThicknessBox, row++);

            AddSectionHeader(_settingsPanel, "Noise & light", ref row);
            AttachDragHandle("Noise", "Random jitter applied to star positions.", _settingsPanel, _noiseBox, row++);
            AttachDragHandle("Core falloff", "How quickly density fades from the center.", _settingsPanel, _coreFalloffBox, row++);
            AttachDragHandle("Brightness", "Overall brightness multiplier for stars.", _settingsPanel, _brightnessBox, row++);

            AddSectionHeader(_settingsPanel, "Randomness", ref row);
            AttachDragHandle("Seed", "Random seed for reproducible galaxies.", _settingsPanel, _seedBox, row++);

            AddSeparator(_settingsPanel, row++);

            AddSectionHeader(_settingsPanel, "Bulge", ref row);
            AttachDragHandle("Bulge radius", "Radius of the central bulge.", _settingsPanel, _bulgeRadiusBox, row++);
            AttachDragHandle("Bulge star count", "Number of stars clustered in the bulge.", _settingsPanel, _bulgeStarCountBox, row++);
            AttachDragHandle("Bulge falloff", "Density falloff inside the bulge.", _settingsPanel, _bulgeFalloffBox, row++);
            AttachDragHandle("Bulge vertical scale", "Height scale of the bulge.", _settingsPanel, _bulgeVerticalScaleBox, row++);
            AttachDragHandle("Bulge brightness", "Brightness multiplier for bulge stars.", _settingsPanel, _bulgeBrightnessBox, row++);

            _regenTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _regenTimer.Tick += (_, _) => { _regenTimer.Stop(); RegenerateGalaxy(); };

            HookEvents();
            SetupPresetTooltips(presetLabel);
        }

        private void HookEvents()
        {
            _glControl.Load += (_, _) =>
            {
                try
                {
                    _renderer.Initialize();
                    ShowHelpOverlay();
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
            _bulgeRadiusBox.ValueChanged += OnParameterChanged;
            _bulgeStarCountBox.ValueChanged += OnParameterChanged;
            _bulgeFalloffBox.ValueChanged += OnParameterChanged;
            _bulgeVerticalScaleBox.ValueChanged += OnParameterChanged;
            _bulgeBrightnessBox.ValueChanged += OnParameterChanged;
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
            _parameters.BulgeRadius = (float)_bulgeRadiusBox.Value;
            _parameters.BulgeStarCount = (int)_bulgeStarCountBox.Value;
            _parameters.BulgeFalloff = (float)_bulgeFalloffBox.Value;
            _parameters.BulgeVerticalScale = (float)_bulgeVerticalScaleBox.Value;
            _parameters.BulgeBrightness = (float)_bulgeBrightnessBox.Value;

            _presetPreview.Invalidate();
            RegenerateGalaxy();
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
                    preset.BulgeRadius = 6f;
                    preset.BulgeStarCount = 25000;
                    preset.BulgeFalloff = 2.2f;
                    preset.BulgeVerticalScale = 0.9f;
                    preset.BulgeBrightness = 2.2f;
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
                    preset.BulgeRadius = 7f;
                    preset.BulgeStarCount = 30000;
                    preset.BulgeFalloff = 2.5f;
                    preset.BulgeVerticalScale = 0.7f;
                    preset.BulgeBrightness = 2.5f;
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
                    preset.BulgeRadius = 4f;
                    preset.BulgeStarCount = 15000;
                    preset.BulgeFalloff = 1.8f;
                    preset.BulgeVerticalScale = 1.0f;
                    preset.BulgeBrightness = 1.8f;
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
            SetValueClamped(_bulgeRadiusBox, (decimal)preset.BulgeRadius);
            SetValueClamped(_bulgeStarCountBox, preset.BulgeStarCount);
            SetValueClamped(_bulgeFalloffBox, (decimal)preset.BulgeFalloff);
            SetValueClamped(_bulgeVerticalScaleBox, (decimal)preset.BulgeVerticalScale);
            SetValueClamped(_bulgeBrightnessBox, (decimal)preset.BulgeBrightness);
            _suppressEvents = false;

            _presetPreview.Invalidate();
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
            _presetPreview.Invalidate();
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
            if (_isGenerating)
            {
                return;
            }
            _statusLabel.Text = $"Stars: {_currentStarCount:N0} | FPS: {_smoothedFps:F1}";
        }

        private void RegenerateGalaxy()
        {
            _regenCts?.Cancel();
            _regenCts = new CancellationTokenSource();
            var token = _regenCts.Token;
            var parameters = _parameters.Clone();
            _isGenerating = true;
            SetControlsEnabled(false);
            _statusLabel.Text = $"Generating... Bulge: {parameters.BulgeStarCount:N0}";

            Task.Run(() => _generator.Generate(parameters, token), token)
                .ContinueWith(t =>
                {
                    if (token.IsCancellationRequested || _regenCts?.Token != token)
                    {
                        return;
                    }

                    if (t.IsCanceled)
                    {
                        return;
                    }

                    if (t.IsFaulted)
                    {
                        _isGenerating = false;
                        SetControlsEnabled(true);
                        MessageBox.Show($"Failed to generate galaxy: {t.Exception?.GetBaseException().Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    _isGenerating = false;
                    SetControlsEnabled(true);
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
                Margin = new Padding(0, 2, 0, 2),
                MinimumSize = new Size(0, StandardControlHeight),
            };
        }

        private static Label AddRow(TableLayoutPanel panel, string label, Control control, int row)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, StandardRowHeight));
            var lbl = new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 2, 0, 0),
                Margin = new Padding(0, 4, 8, 4),
                ForeColor = Color.Gainsboro,
            };
            panel.Controls.Add(lbl, 0, row);
            panel.Controls.Add(control, 1, row);
            return lbl;
        }

        private void AddSectionHeader(TableLayoutPanel panel, string text, ref int row)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            var header = new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.BottomLeft,
                Font = new Font(panel.Font, FontStyle.Bold),
                ForeColor = HeaderTextColor,
                Padding = new Padding(0, 8, 0, 2),
                Margin = new Padding(0, 10, 0, 4),
            };
            panel.SetColumnSpan(header, 2);
            panel.Controls.Add(header, 0, row++);
        }

        private Label AttachDragHandle(string label, string tooltip, TableLayoutPanel panel, ScrubbableNumeric control, int row)
        {
            var lbl = AddRow(panel, label, control, row);
            control.AttachDragHandle(lbl);
            var scrubHint = "Drag to scrub. Shift = 10x, Alt = 0.1x. Double-click to type.";
            var body = string.IsNullOrWhiteSpace(tooltip) ? scrubHint : $"{tooltip}\n{scrubHint}";
            _toolTip.SetToolTip(lbl, body);
            _toolTip.SetToolTip(control, body);
            return lbl;
        }

        private static void AddSeparator(TableLayoutPanel panel, int row)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 1f));
            var line = new Panel
            {
                Height = 1,
                Dock = DockStyle.Fill,
                BackColor = PanelBorderColor,
                Margin = new Padding(0, 6, 0, 6),
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
                Height = StandardControlHeight,
                FlatAppearance =
                {
                    BorderColor = AccentColor,
                    BorderSize = 1,
                    MouseOverBackColor = Color.FromArgb(60, 60, 60),
                    MouseDownBackColor = Color.FromArgb(55, 55, 55),
                },
            };
        }

        private void SetupPresetTooltips(Label presetLabel)
        {
            _toolTip.SetToolTip(presetLabel, "Choose a starting configuration.");
            _toolTip.SetToolTip(_presetBox, "Choose a starting configuration.");
            _toolTip.SetToolTip(_presetPreview, "Quick preview of the current preset.");
            _toolTip.SetToolTip(_resetButton, "Restore the default preset values.");
            _toolTip.SetToolTip(_randomizeSeedButton, "Generate a fresh random seed and update the view.");
            _toolTip.SetToolTip(_glControl, "Left-drag: orbit | Scroll: zoom");
        }

        private void ShowHelpOverlay()
        {
            _helpOverlay.Width = Math.Max(200, _glControl.ClientSize.Width - 24);
            _helpOverlay.Visible = true;
            _helpOverlay.BringToFront();
            _helpTimer.Stop();
            _helpTimer.Start();
        }

        private void SetControlsEnabled(bool enabled)
        {
            _presetBox.Enabled = enabled;
            _resetButton.Enabled = enabled;
            _randomizeSeedButton.Enabled = enabled;
            _settingsPanel.Enabled = enabled;
        }

        private void DrawPresetPreview(object? sender, PaintEventArgs e)
        {
            var rect = _presetPreview.ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(ControlSurfaceColor);
            using (var border = new Pen(PanelBorderColor))
            {
                e.Graphics.DrawRectangle(border, 0, 0, rect.Width - 1, rect.Height - 1);
            }

            var inner = Rectangle.Inflate(rect, -4, -4);
            using (var brush = new LinearGradientBrush(inner, AccentColor, Color.FromArgb(30, 30, 30), LinearGradientMode.ForwardDiagonal))
            {
                e.Graphics.FillEllipse(brush, inner);
            }

            var rnd = new Random(_parameters.Seed);
            for (int i = 0; i < 14; i++)
            {
                int size = rnd.Next(2, 5);
                int x = rnd.Next(inner.Left, inner.Right - size);
                int y = rnd.Next(inner.Top, inner.Bottom - size);
                using var starBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
                e.Graphics.FillEllipse(starBrush, x, y, size, size);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            _regenCts?.Cancel();
            _renderer.Dispose();
        }
    }
}
