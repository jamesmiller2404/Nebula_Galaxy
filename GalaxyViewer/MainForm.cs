using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
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
        private readonly Dictionary<string, GalaxyParameters> _presetLibrary;
        private readonly string[] _presetNames;
        private readonly SplitContainer _split;
        private readonly string _uiStatePath;
        private int _savedSplitterDistance;
        private static readonly Color PanelBackColor = Color.FromArgb(32, 32, 32);
        private static readonly Color PanelBorderColor = Color.FromArgb(55, 55, 55);
        private static readonly Color AccentColor = Color.FromArgb(78, 156, 255);
        private static readonly Color ControlSurfaceColor = Color.FromArgb(42, 42, 42);
        private static readonly Color SecondarySurfaceColor = Color.FromArgb(28, 28, 28);
        private static readonly Color HeaderTextColor = Color.FromArgb(210, 210, 210);
        private const float SidePanelScreenRatio = 0.28f;
        private const int MinimumSidePanelWidth = 320;
        private const int TargetViewportWidth = 920;
        private const int StandardControlHeight = 32;
        private const int StandardRowHeight = 36;
        private const int PanelComfortPadding = 28;

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
            _uiStatePath = Path.Combine(Application.UserAppDataPath, "ui-state.txt");
            _savedSplitterDistance = LoadSavedSplitterDistance();
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

            var presetDefinitions = BuildPresetDefinitions();
            _presetLibrary = new Dictionary<string, GalaxyParameters>(StringComparer.OrdinalIgnoreCase);
            _presetNames = new string[presetDefinitions.Length];
            for (int i = 0; i < presetDefinitions.Length; i++)
            {
                var (name, parameters) = presetDefinitions[i];
                _presetLibrary[name] = parameters;
                _presetNames[i] = name;
            }

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Panel1MinSize = MinimumSidePanelWidth,
                FixedPanel = FixedPanel.Panel1,
                BackColor = PanelBackColor,
            };

            Controls.Add(_split);
            _split.SplitterMoved += (_, _) => SaveSplitterDistance();

            var panelHost = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(14, 12, 14, 12),
                BackColor = PanelBackColor,
            };
            panelHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panelHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _split.Panel1.Controls.Add(panelHost);
            _split.Panel1.BackColor = PanelBackColor;

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
            _split.Panel2.Controls.Add(_glControl);
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
            _presetBox.Items.AddRange(_presetNames);
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

            var presetCard = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 3,
                Dock = DockStyle.Fill,
                BackColor = SecondarySurfaceColor,
                Padding = new Padding(10, 8, 10, 10),
                Margin = new Padding(0, 0, 0, 10),
            };
            presetCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
            presetCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            presetCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            presetCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            presetCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));

            _presetPreview.Margin = new Padding(0, 0, 12, 0);
            presetCard.Controls.Add(_presetPreview, 0, 0);
            presetCard.SetRowSpan(_presetPreview, 3);

            var presetHeader = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4),
            };
            presetHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            presetHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            presetHeader.Controls.Add(presetLabel, 0, 0);
            presetHeader.Controls.Add(_presetBox, 1, 0);
            presetCard.Controls.Add(presetHeader, 1, 0);

            var presetActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 4, 0, 8),
            };
            presetActions.Controls.Add(_resetButton);
            presetCard.Controls.Add(presetActions, 1, 1);

            var statusPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ControlSurfaceColor,
                Padding = new Padding(8, 4, 8, 4),
                Margin = new Padding(0, 6, 0, 0),
                Height = 32,
            };
            statusPanel.Controls.Add(_statusLabel);
            presetCard.SetColumnSpan(statusPanel, 2);
            presetCard.Controls.Add(statusPanel, 0, 2);

            var presetContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = PanelBorderColor,
                Padding = new Padding(1),
                Margin = new Padding(0, 0, 0, 8),
            };
            presetContainer.Controls.Add(presetCard);

            _settingsPanel = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(0, 0, 0, 10),
                BackColor = PanelBackColor,
            };
            _settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            panelHost.Controls.Add(presetContainer, 0, 0);
            panelHost.Controls.Add(_settingsPanel, 0, 1);

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
            AddSeedRow("Random seed for reproducible galaxies.", _settingsPanel, ref row);

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
            _split.SplitterDistance = ComputeInitialSplitterDistance(sidePanelWidth, panelHost);
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

        private int ComputeInitialSplitterDistance(int sidePanelWidth, TableLayoutPanel? panelHost)
        {
            int baseWidth = Math.Max(MinimumSidePanelWidth, Math.Min(sidePanelWidth, ClientSize.Width - TargetViewportWidth));
            int preferred = panelHost != null ? GetPreferredPanelWidth(panelHost) : baseWidth;
            int requested = Math.Max(baseWidth, preferred);
            if (_savedSplitterDistance > 0)
            {
                requested = _savedSplitterDistance;
            }

            return ClampSplitterDistance(requested);
        }

        private int GetPreferredPanelWidth(TableLayoutPanel panelHost)
        {
            int preferred = panelHost.GetPreferredSize(new Size(int.MaxValue, ClientSize.Height)).Width;
            return Math.Max(MinimumSidePanelWidth, preferred + PanelComfortPadding);
        }

        private int ClampSplitterDistance(int requested)
        {
            int max = Math.Max(MinimumSidePanelWidth, ClientSize.Width - TargetViewportWidth);
            if (_split.Width > 0)
            {
                max = Math.Min(max, _split.Width - _split.SplitterWidth - 200);
            }

            return Math.Max(MinimumSidePanelWidth, Math.Min(requested, max));
        }

        private int LoadSavedSplitterDistance()
        {
            try
            {
                if (File.Exists(_uiStatePath))
                {
                    var raw = File.ReadAllText(_uiStatePath).Trim();
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
                // Persistence is best-effort only.
            }

            return -1;
        }

        private void SaveSplitterDistance()
        {
            try
            {
                var directory = Path.GetDirectoryName(_uiStatePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_uiStatePath, ClampSplitterDistance(_split.SplitterDistance).ToString(CultureInfo.InvariantCulture));
            }
            catch
            {
                // Persistence is best-effort only.
            }
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
            var preset = ResolvePreset(name);

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

        private GalaxyParameters ResolvePreset(string name)
        {
            if (_presetLibrary.TryGetValue(name, out var preset))
            {
                var clone = preset.Clone();
                clone.Seed = _parameters.Seed;
                return clone;
            }

            var fallback = new GalaxyParameters { Seed = _parameters.Seed };
            return fallback;
        }

        private static (string Name, GalaxyParameters Parameters)[] BuildPresetDefinitions()
        {
            return new[]
            {
                ("Default", new GalaxyParameters()),
                ("Elliptical (E0-E7)", new GalaxyParameters
                {
                    StarCount = 80000,
                    ArmCount = 1,
                    ArmTwist = 0.6f,
                    ArmSpread = 0.95f,
                    DiskRadius = 42f,
                    VerticalThickness = 1.4f,
                    Noise = 0.5f,
                    CoreFalloff = 2.6f,
                    Brightness = 1.3f,
                    BulgeRadius = 12f,
                    BulgeStarCount = 70000,
                    BulgeFalloff = 1.6f,
                    BulgeVerticalScale = 1.2f,
                    BulgeBrightness = 3.2f,
                }),
                ("Lenticular (S0)", new GalaxyParameters
                {
                    StarCount = 65000,
                    ArmCount = 2,
                    ArmTwist = 4f,
                    ArmSpread = 0.12f,
                    DiskRadius = 45f,
                    VerticalThickness = 0.35f,
                    Noise = 0.1f,
                    CoreFalloff = 2.2f,
                    Brightness = 1.1f,
                    BulgeRadius = 9f,
                    BulgeStarCount = 35000,
                    BulgeFalloff = 2.1f,
                    BulgeVerticalScale = 0.8f,
                    BulgeBrightness = 2.8f,
                }),
                ("Spiral (Sa)", new GalaxyParameters
                {
                    StarCount = 80000,
                    ArmCount = 2,
                    ArmTwist = 10f,
                    ArmSpread = 0.18f,
                    DiskRadius = 50f,
                    VerticalThickness = 0.45f,
                    Noise = 0.16f,
                    CoreFalloff = 2.4f,
                    Brightness = 1.25f,
                    BulgeRadius = 8.5f,
                    BulgeStarCount = 32000,
                    BulgeFalloff = 2.4f,
                    BulgeVerticalScale = 0.9f,
                    BulgeBrightness = 2.8f,
                }),
                ("Spiral (Sb)", new GalaxyParameters
                {
                    StarCount = 78000,
                    ArmCount = 3,
                    ArmTwist = 8.5f,
                    ArmSpread = 0.24f,
                    DiskRadius = 52f,
                    VerticalThickness = 0.4f,
                    Noise = 0.2f,
                    CoreFalloff = 2.0f,
                    Brightness = 1.15f,
                    BulgeRadius = 7f,
                    BulgeStarCount = 26000,
                    BulgeFalloff = 2.2f,
                    BulgeVerticalScale = 0.8f,
                    BulgeBrightness = 2.4f,
                }),
                ("Spiral (Sc)", new GalaxyParameters
                {
                    StarCount = 76000,
                    ArmCount = 3,
                    ArmTwist = 6.5f,
                    ArmSpread = 0.32f,
                    DiskRadius = 54f,
                    VerticalThickness = 0.35f,
                    Noise = 0.22f,
                    CoreFalloff = 1.8f,
                    Brightness = 1.05f,
                    BulgeRadius = 5f,
                    BulgeStarCount = 20000,
                    BulgeFalloff = 2.0f,
                    BulgeVerticalScale = 0.7f,
                    BulgeBrightness = 2.0f,
                }),
                ("Spiral (Sd)", new GalaxyParameters
                {
                    StarCount = 70000,
                    ArmCount = 4,
                    ArmTwist = 5f,
                    ArmSpread = 0.42f,
                    DiskRadius = 58f,
                    VerticalThickness = 0.4f,
                    Noise = 0.3f,
                    CoreFalloff = 1.4f,
                    Brightness = 0.95f,
                    BulgeRadius = 3.2f,
                    BulgeStarCount = 12000,
                    BulgeFalloff = 1.6f,
                    BulgeVerticalScale = 0.6f,
                    BulgeBrightness = 1.7f,
                }),
                ("Barred Spiral (SBa)", new GalaxyParameters
                {
                    StarCount = 82000,
                    ArmCount = 2,
                    ArmTwist = 9f,
                    ArmSpread = 0.2f,
                    DiskRadius = 48f,
                    VerticalThickness = 0.45f,
                    Noise = 0.18f,
                    CoreFalloff = 2.0f,
                    Brightness = 1.2f,
                    BulgeRadius = 9f,
                    BulgeStarCount = 36000,
                    BulgeFalloff = 2.6f,
                    BulgeVerticalScale = 1.0f,
                    BulgeBrightness = 3.0f,
                }),
                ("Barred Spiral (SBb)", new GalaxyParameters
                {
                    StarCount = 80000,
                    ArmCount = 3,
                    ArmTwist = 7.5f,
                    ArmSpread = 0.28f,
                    DiskRadius = 52f,
                    VerticalThickness = 0.42f,
                    Noise = 0.22f,
                    CoreFalloff = 1.9f,
                    Brightness = 1.1f,
                    BulgeRadius = 7.5f,
                    BulgeStarCount = 30000,
                    BulgeFalloff = 2.2f,
                    BulgeVerticalScale = 0.9f,
                    BulgeBrightness = 2.6f,
                }),
                ("Barred Spiral (SBc)", new GalaxyParameters
                {
                    StarCount = 78000,
                    ArmCount = 4,
                    ArmTwist = 6f,
                    ArmSpread = 0.36f,
                    DiskRadius = 56f,
                    VerticalThickness = 0.38f,
                    Noise = 0.27f,
                    CoreFalloff = 1.6f,
                    Brightness = 1.0f,
                    BulgeRadius = 5f,
                    BulgeStarCount = 20000,
                    BulgeFalloff = 2.0f,
                    BulgeVerticalScale = 0.8f,
                    BulgeBrightness = 2.2f,
                }),
                ("Barred Spiral (SBd)", new GalaxyParameters
                {
                    StarCount = 72000,
                    ArmCount = 4,
                    ArmTwist = 4.5f,
                    ArmSpread = 0.44f,
                    DiskRadius = 60f,
                    VerticalThickness = 0.42f,
                    Noise = 0.34f,
                    CoreFalloff = 1.3f,
                    Brightness = 0.95f,
                    BulgeRadius = 3.2f,
                    BulgeStarCount = 14000,
                    BulgeFalloff = 1.7f,
                    BulgeVerticalScale = 0.7f,
                    BulgeBrightness = 1.9f,
                }),
                ("Irregular (Irr I)", new GalaxyParameters
                {
                    StarCount = 55000,
                    ArmCount = 1,
                    ArmTwist = 1.5f,
                    ArmSpread = 0.9f,
                    DiskRadius = 40f,
                    VerticalThickness = 0.9f,
                    Noise = 0.6f,
                    CoreFalloff = 1.0f,
                    Brightness = 0.9f,
                    BulgeRadius = 2.5f,
                    BulgeStarCount = 6000,
                    BulgeFalloff = 1.2f,
                    BulgeVerticalScale = 1.1f,
                    BulgeBrightness = 1.3f,
                }),
                ("Irregular (Irr II)", new GalaxyParameters
                {
                    StarCount = 65000,
                    ArmCount = 1,
                    ArmTwist = 0.8f,
                    ArmSpread = 1.0f,
                    DiskRadius = 42f,
                    VerticalThickness = 1.1f,
                    Noise = 0.8f,
                    CoreFalloff = 0.9f,
                    Brightness = 0.95f,
                    BulgeRadius = 2f,
                    BulgeStarCount = 5000,
                    BulgeFalloff = 1.0f,
                    BulgeVerticalScale = 1.2f,
                    BulgeBrightness = 1.4f,
                }),
                ("Dwarf Elliptical (dE)", new GalaxyParameters
                {
                    StarCount = 12000,
                    ArmCount = 1,
                    ArmTwist = 0.8f,
                    ArmSpread = 0.9f,
                    DiskRadius = 16f,
                    VerticalThickness = 0.6f,
                    Noise = 0.35f,
                    CoreFalloff = 2.2f,
                    Brightness = 0.8f,
                    BulgeRadius = 4f,
                    BulgeStarCount = 8000,
                    BulgeFalloff = 2.2f,
                    BulgeVerticalScale = 0.9f,
                    BulgeBrightness = 1.6f,
                }),
                ("Dwarf Spheroidal (dSph)", new GalaxyParameters
                {
                    StarCount = 8000,
                    ArmCount = 1,
                    ArmTwist = 0.3f,
                    ArmSpread = 0.95f,
                    DiskRadius = 14f,
                    VerticalThickness = 0.9f,
                    Noise = 0.45f,
                    CoreFalloff = 1.6f,
                    Brightness = 0.6f,
                    BulgeRadius = 3.5f,
                    BulgeStarCount = 6000,
                    BulgeFalloff = 1.4f,
                    BulgeVerticalScale = 1.0f,
                    BulgeBrightness = 1.2f,
                }),
                ("Dwarf Irregular (dIrr)", new GalaxyParameters
                {
                    StarCount = 10000,
                    ArmCount = 1,
                    ArmTwist = 0.6f,
                    ArmSpread = 1.0f,
                    DiskRadius = 18f,
                    VerticalThickness = 1.0f,
                    Noise = 0.75f,
                    CoreFalloff = 1.0f,
                    Brightness = 0.7f,
                    BulgeRadius = 2.5f,
                    BulgeStarCount = 4000,
                    BulgeFalloff = 1.2f,
                    BulgeVerticalScale = 1.1f,
                    BulgeBrightness = 1.1f,
                }),
                ("Dwarf Spiral (dSp)", new GalaxyParameters
                {
                    StarCount = 14000,
                    ArmCount = 2,
                    ArmTwist = 4.5f,
                    ArmSpread = 0.4f,
                    DiskRadius = 20f,
                    VerticalThickness = 0.35f,
                    Noise = 0.25f,
                    CoreFalloff = 1.7f,
                    Brightness = 0.9f,
                    BulgeRadius = 3f,
                    BulgeStarCount = 5000,
                    BulgeFalloff = 1.8f,
                    BulgeVerticalScale = 0.8f,
                    BulgeBrightness = 1.5f,
                }),
                ("Peculiar", new GalaxyParameters
                {
                    StarCount = 70000,
                    ArmCount = 3,
                    ArmTwist = 5.5f,
                    ArmSpread = 0.65f,
                    DiskRadius = 48f,
                    VerticalThickness = 0.8f,
                    Noise = 0.6f,
                    CoreFalloff = 1.2f,
                    Brightness = 1.0f,
                    BulgeRadius = 4f,
                    BulgeStarCount = 15000,
                    BulgeFalloff = 1.5f,
                    BulgeVerticalScale = 0.9f,
                    BulgeBrightness = 1.8f,
                }),
                ("Interacting", new GalaxyParameters
                {
                    StarCount = 90000,
                    ArmCount = 3,
                    ArmTwist = 6f,
                    ArmSpread = 0.72f,
                    DiskRadius = 60f,
                    VerticalThickness = 0.95f,
                    Noise = 0.55f,
                    CoreFalloff = 1.1f,
                    Brightness = 1.15f,
                    BulgeRadius = 5.5f,
                    BulgeStarCount = 22000,
                    BulgeFalloff = 1.7f,
                    BulgeVerticalScale = 1.0f,
                    BulgeBrightness = 2.1f,
                }),
                ("Merging", new GalaxyParameters
                {
                    StarCount = 120000,
                    ArmCount = 2,
                    ArmTwist = 4.5f,
                    ArmSpread = 0.85f,
                    DiskRadius = 65f,
                    VerticalThickness = 1.1f,
                    Noise = 0.7f,
                    CoreFalloff = 1.0f,
                    Brightness = 1.3f,
                    BulgeRadius = 7f,
                    BulgeStarCount = 32000,
                    BulgeFalloff = 1.4f,
                    BulgeVerticalScale = 1.1f,
                    BulgeBrightness = 2.5f,
                }),
                ("Ring (Resonance)", new GalaxyParameters
                {
                    StarCount = 65000,
                    ArmCount = 1,
                    ArmTwist = 10f,
                    ArmSpread = 0.08f,
                    DiskRadius = 50f,
                    VerticalThickness = 0.35f,
                    Noise = 0.12f,
                    CoreFalloff = 1.6f,
                    Brightness = 1.05f,
                    BulgeRadius = 6f,
                    BulgeStarCount = 18000,
                    BulgeFalloff = 2.0f,
                    BulgeVerticalScale = 0.8f,
                    BulgeBrightness = 2.2f,
                }),
                ("Ring (Collisional)", new GalaxyParameters
                {
                    StarCount = 75000,
                    ArmCount = 1,
                    ArmTwist = 11f,
                    ArmSpread = 0.18f,
                    DiskRadius = 55f,
                    VerticalThickness = 0.45f,
                    Noise = 0.25f,
                    CoreFalloff = 1.2f,
                    Brightness = 1.15f,
                    BulgeRadius = 4.5f,
                    BulgeStarCount = 15000,
                    BulgeFalloff = 1.6f,
                    BulgeVerticalScale = 0.9f,
                    BulgeBrightness = 2.0f,
                }),
                ("Ring (Polar)", new GalaxyParameters
                {
                    StarCount = 70000,
                    ArmCount = 1,
                    ArmTwist = 9f,
                    ArmSpread = 0.22f,
                    DiskRadius = 52f,
                    VerticalThickness = 1.3f,
                    Noise = 0.28f,
                    CoreFalloff = 1.5f,
                    Brightness = 1.05f,
                    BulgeRadius = 5.5f,
                    BulgeStarCount = 20000,
                    BulgeFalloff = 1.9f,
                    BulgeVerticalScale = 1.4f,
                    BulgeBrightness = 2.4f,
                }),
                ("Low Surface Brightness", new GalaxyParameters
                {
                    StarCount = 50000,
                    ArmCount = 2,
                    ArmTwist = 5f,
                    ArmSpread = 0.5f,
                    DiskRadius = 70f,
                    VerticalThickness = 0.55f,
                    Noise = 0.5f,
                    CoreFalloff = 0.9f,
                    Brightness = 0.45f,
                    BulgeRadius = 3f,
                    BulgeStarCount = 8000,
                    BulgeFalloff = 1.5f,
                    BulgeVerticalScale = 0.9f,
                    BulgeBrightness = 1.1f,
                }),
                ("Ultra-Diffuse", new GalaxyParameters
                {
                    StarCount = 30000,
                    ArmCount = 1,
                    ArmTwist = 2f,
                    ArmSpread = 0.85f,
                    DiskRadius = 80f,
                    VerticalThickness = 1.3f,
                    Noise = 0.65f,
                    CoreFalloff = 0.8f,
                    Brightness = 0.35f,
                    BulgeRadius = 2.5f,
                    BulgeStarCount = 6000,
                    BulgeFalloff = 1.3f,
                    BulgeVerticalScale = 1.2f,
                    BulgeBrightness = 1.0f,
                }),
                ("Seyfert", new GalaxyParameters
                {
                    StarCount = 65000,
                    ArmCount = 2,
                    ArmTwist = 7f,
                    ArmSpread = 0.26f,
                    DiskRadius = 45f,
                    VerticalThickness = 0.35f,
                    Noise = 0.2f,
                    CoreFalloff = 2.2f,
                    Brightness = 1.3f,
                    BulgeRadius = 6f,
                    BulgeStarCount = 26000,
                    BulgeFalloff = 2.6f,
                    BulgeVerticalScale = 0.9f,
                    BulgeBrightness = 3.5f,
                }),
                ("Quasar", new GalaxyParameters
                {
                    StarCount = 60000,
                    ArmCount = 2,
                    ArmTwist = 6f,
                    ArmSpread = 0.3f,
                    DiskRadius = 40f,
                    VerticalThickness = 0.6f,
                    Noise = 0.25f,
                    CoreFalloff = 2.4f,
                    Brightness = 2.0f,
                    BulgeRadius = 5f,
                    BulgeStarCount = 40000,
                    BulgeFalloff = 2.0f,
                    BulgeVerticalScale = 1.0f,
                    BulgeBrightness = 6.0f,
                }),
                ("Blazar", new GalaxyParameters
                {
                    StarCount = 55000,
                    ArmCount = 2,
                    ArmTwist = 5.5f,
                    ArmSpread = 0.32f,
                    DiskRadius = 38f,
                    VerticalThickness = 1.2f,
                    Noise = 0.22f,
                    CoreFalloff = 2.0f,
                    Brightness = 1.8f,
                    BulgeRadius = 4.5f,
                    BulgeStarCount = 28000,
                    BulgeFalloff = 1.8f,
                    BulgeVerticalScale = 1.6f,
                    BulgeBrightness = 5.0f,
                }),
                ("Radio Galaxy", new GalaxyParameters
                {
                    StarCount = 85000,
                    ArmCount = 2,
                    ArmTwist = 5f,
                    ArmSpread = 0.28f,
                    DiskRadius = 60f,
                    VerticalThickness = 0.9f,
                    Noise = 0.2f,
                    CoreFalloff = 1.5f,
                    Brightness = 1.2f,
                    BulgeRadius = 6.5f,
                    BulgeStarCount = 24000,
                    BulgeFalloff = 1.7f,
                    BulgeVerticalScale = 1.1f,
                    BulgeBrightness = 3.2f,
                }),
                ("LINER", new GalaxyParameters
                {
                    StarCount = 65000,
                    ArmCount = 2,
                    ArmTwist = 4.5f,
                    ArmSpread = 0.3f,
                    DiskRadius = 42f,
                    VerticalThickness = 0.6f,
                    Noise = 0.2f,
                    CoreFalloff = 2.3f,
                    Brightness = 1.0f,
                    BulgeRadius = 5.5f,
                    BulgeStarCount = 23000,
                    BulgeFalloff = 2.4f,
                    BulgeVerticalScale = 0.9f,
                    BulgeBrightness = 2.8f,
                }),
                ("Starburst", new GalaxyParameters
                {
                    StarCount = 100000,
                    ArmCount = 3,
                    ArmTwist = 6.5f,
                    ArmSpread = 0.48f,
                    DiskRadius = 50f,
                    VerticalThickness = 0.7f,
                    Noise = 0.35f,
                    CoreFalloff = 1.4f,
                    Brightness = 1.6f,
                    BulgeRadius = 5f,
                    BulgeStarCount = 32000,
                    BulgeFalloff = 1.6f,
                    BulgeVerticalScale = 1.0f,
                    BulgeBrightness = 3.5f,
                }),
                ("Post-Starburst (E+A / K+A)", new GalaxyParameters
                {
                    StarCount = 65000,
                    ArmCount = 2,
                    ArmTwist = 3.5f,
                    ArmSpread = 0.4f,
                    DiskRadius = 48f,
                    VerticalThickness = 0.6f,
                    Noise = 0.3f,
                    CoreFalloff = 1.8f,
                    Brightness = 0.9f,
                    BulgeRadius = 6.5f,
                    BulgeStarCount = 28000,
                    BulgeFalloff = 2.0f,
                    BulgeVerticalScale = 1.0f,
                    BulgeBrightness = 2.2f,
                }),
                ("cD Galaxy", new GalaxyParameters
                {
                    StarCount = 130000,
                    ArmCount = 1,
                    ArmTwist = 1.0f,
                    ArmSpread = 0.85f,
                    DiskRadius = 90f,
                    VerticalThickness = 1.3f,
                    Noise = 0.55f,
                    CoreFalloff = 1.5f,
                    Brightness = 1.1f,
                    BulgeRadius = 14f,
                    BulgeStarCount = 90000,
                    BulgeFalloff = 1.3f,
                    BulgeVerticalScale = 1.2f,
                    BulgeBrightness = 3.0f,
                }),
                ("Brightest Cluster Galaxy (BCG)", new GalaxyParameters
                {
                    StarCount = 115000,
                    ArmCount = 1,
                    ArmTwist = 0.9f,
                    ArmSpread = 0.8f,
                    DiskRadius = 85f,
                    VerticalThickness = 1.2f,
                    Noise = 0.5f,
                    CoreFalloff = 1.6f,
                    Brightness = 1.05f,
                    BulgeRadius = 12f,
                    BulgeStarCount = 80000,
                    BulgeFalloff = 1.4f,
                    BulgeVerticalScale = 1.1f,
                    BulgeBrightness = 2.8f,
                }),
                ("Flocculent Spiral", new GalaxyParameters
                {
                    StarCount = 72000,
                    ArmCount = 4,
                    ArmTwist = 5.5f,
                    ArmSpread = 0.6f,
                    DiskRadius = 48f,
                    VerticalThickness = 0.4f,
                    Noise = 0.5f,
                    CoreFalloff = 1.6f,
                    Brightness = 1.05f,
                    BulgeRadius = 4.5f,
                    BulgeStarCount = 16000,
                    BulgeFalloff = 1.7f,
                    BulgeVerticalScale = 0.8f,
                    BulgeBrightness = 2.0f,
                }),
                ("Grand-Design Spiral", new GalaxyParameters
                {
                    StarCount = 90000,
                    ArmCount = 2,
                    ArmTwist = 9.5f,
                    ArmSpread = 0.22f,
                    DiskRadius = 55f,
                    VerticalThickness = 0.45f,
                    Noise = 0.2f,
                    CoreFalloff = 2.0f,
                    Brightness = 1.2f,
                    BulgeRadius = 7f,
                    BulgeStarCount = 26000,
                    BulgeFalloff = 2.3f,
                    BulgeVerticalScale = 0.9f,
                    BulgeBrightness = 2.5f,
                }),
                ("Anemic Spiral", new GalaxyParameters
                {
                    StarCount = 50000,
                    ArmCount = 2,
                    ArmTwist = 6.5f,
                    ArmSpread = 0.3f,
                    DiskRadius = 60f,
                    VerticalThickness = 0.4f,
                    Noise = 0.26f,
                    CoreFalloff = 1.7f,
                    Brightness = 0.7f,
                    BulgeRadius = 5f,
                    BulgeStarCount = 14000,
                    BulgeFalloff = 1.8f,
                    BulgeVerticalScale = 0.8f,
                    BulgeBrightness = 1.6f,
                }),
                ("Barred spiral", new GalaxyParameters
                {
                    StarCount = 80000,
                    ArmCount = 2,
                    ArmTwist = 8f,
                    ArmSpread = 0.25f,
                    DiskRadius = 50f,
                    VerticalThickness = 0.45f,
                    Noise = 0.2f,
                    CoreFalloff = 1.6f,
                    Brightness = 1.1f,
                    BulgeRadius = 6f,
                    BulgeStarCount = 25000,
                    BulgeFalloff = 2.2f,
                    BulgeVerticalScale = 0.9f,
                    BulgeBrightness = 2.2f,
                }),
                ("Compact core", new GalaxyParameters
                {
                    StarCount = 60000,
                    ArmCount = 3,
                    ArmTwist = 6f,
                    ArmSpread = 0.3f,
                    DiskRadius = 35f,
                    VerticalThickness = 0.7f,
                    Noise = 0.18f,
                    CoreFalloff = 2.5f,
                    Brightness = 1.2f,
                    BulgeRadius = 7f,
                    BulgeStarCount = 30000,
                    BulgeFalloff = 2.5f,
                    BulgeVerticalScale = 0.7f,
                    BulgeBrightness = 2.5f,
                }),
                ("Diffuse arms", new GalaxyParameters
                {
                    StarCount = 70000,
                    ArmCount = 4,
                    ArmTwist = 4.5f,
                    ArmSpread = 0.45f,
                    DiskRadius = 55f,
                    VerticalThickness = 0.5f,
                    Noise = 0.35f,
                    CoreFalloff = 1.8f,
                    Brightness = 1.0f,
                    BulgeRadius = 4f,
                    BulgeStarCount = 15000,
                    BulgeFalloff = 1.8f,
                    BulgeVerticalScale = 1.0f,
                    BulgeBrightness = 1.8f,
                }),
            };
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
                MinimumSize = new Size(140, StandardControlHeight),
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
                MinimumSize = new Size(140, 0),
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

        private void AddSeedRow(string tooltip, TableLayoutPanel panel, ref int row)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, StandardRowHeight));

            var lbl = new Label
            {
                Text = "Seed",
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 2, 0, 0),
                Margin = new Padding(0, 4, 8, 4),
                ForeColor = Color.Gainsboro,
                MinimumSize = new Size(140, 0),
            };

            var seedRow = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 2),
            };
            seedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            seedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98f));

            _seedBox.Margin = new Padding(0, 2, 6, 2);
            seedRow.Controls.Add(_seedBox, 0, 0);
            seedRow.Controls.Add(_randomizeSeedButton, 1, 0);

            panel.Controls.Add(lbl, 0, row);
            panel.Controls.Add(seedRow, 1, row);

            _seedBox.AttachDragHandle(lbl);
            var scrubHint = "Drag to scrub. Shift = 10x, Alt = 0.1x. Double-click to type.";
            var body = string.IsNullOrWhiteSpace(tooltip) ? scrubHint : $"{tooltip}\n{scrubHint}";
            _toolTip.SetToolTip(lbl, body);
            _toolTip.SetToolTip(_seedBox, body);
            _toolTip.SetToolTip(_randomizeSeedButton, "Randomize seed and regenerate.");
            row++;
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
            SaveSplitterDistance();
            base.OnFormClosing(e);
            _regenCts?.Cancel();
            _renderer.Dispose();
        }
    }
}
