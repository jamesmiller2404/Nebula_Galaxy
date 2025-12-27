using System;
using System.Drawing;
using System.Globalization;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GalaxyViewer
{
    internal class ScrubbableNumeric : UserControl
    {
        private readonly TextBox _textBox;
        private decimal _minimum = 0m;
        private decimal _maximum = 100m;
        private decimal _increment = 1m;
        private int _decimalPlaces;
        private decimal _value;
        private bool _scrubbing;
        private bool _mouseDown;
        private Control? _scrubHandle;
        private int _scrubStartScreenX;
        private decimal _scrubStartValue;
        private const int ScrubActivationThreshold = 2; // pixels of movement before scrubbing starts
        private const int CornerRadius = 6;
        private const int GripWidth = 16;
        private readonly Color _borderColor = Color.FromArgb(70, 70, 70);
        private readonly Color _hoverBorderColor = Color.FromArgb(95, 120, 160);
        private readonly Color _fillColor = Color.FromArgb(42, 42, 42);
        private readonly Color _hoverFillColor = Color.FromArgb(48, 48, 54);
        private readonly Color _focusFillColor = Color.FromArgb(50, 60, 80);
        private readonly Color _gripColor = Color.FromArgb(80, 90, 110);
        private readonly Color _accentColor = Color.FromArgb(78, 156, 255);
        private bool _hovering;

        public event EventHandler? ValueChanged;

        public ScrubbableNumeric()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Padding = new Padding(8, 3, GripWidth + 6, 3);
            BackColor = Color.Transparent;

            _textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                TextAlign = HorizontalAlignment.Right,
                BackColor = _fillColor,
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.None,
                Cursor = Cursors.SizeWE,
                Margin = new Padding(0),
            };

            Controls.Add(_textBox);

            MouseEnter += (_, _) => { _hovering = true; Invalidate(); };
            MouseLeave += (_, _) => { _hovering = false; Invalidate(); };
            _textBox.KeyDown += OnTextBoxKeyDown;
            _textBox.Leave += (_, _) =>
            {
                _textBox.BackColor = _fillColor;
                CommitText();
                Invalidate();
            };
            _textBox.Enter += (_, _) =>
            {
                _textBox.BackColor = _focusFillColor;
                Invalidate();
            };
            _textBox.MouseEnter += (_, _) => { _hovering = true; Invalidate(); };
            _textBox.MouseLeave += (_, _) => { _hovering = false; Invalidate(); };
            _textBox.MouseWheel += HandleMouseWheel;
            _textBox.GotFocus += (_, _) => _textBox.Cursor = Cursors.IBeam;
            _textBox.LostFocus += (_, _) => _textBox.Cursor = Cursors.SizeWE;

            Minimum = 0m;
            Maximum = 100m;
            Increment = 1m;
            DecimalPlaces = 0;
            Value = 0m;
        }

        public decimal Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;
                if (_maximum < _minimum)
                {
                    _maximum = _minimum;
                }
                Value = Value;
            }
        }

        public decimal Maximum
        {
            get => _maximum;
            set
            {
                _maximum = value;
                if (_minimum > _maximum)
                {
                    _minimum = _maximum;
                }
                Value = Value;
            }
        }

        public decimal Increment
        {
            get => _increment;
            set => _increment = Math.Max(value, 0.0001m);
        }

        public int DecimalPlaces
        {
            get => _decimalPlaces;
            set
            {
                _decimalPlaces = Math.Max(0, Math.Min(value, 6));
                UpdateText();
            }
        }

        public decimal Value
        {
            get => _value;
            set => SetValue(value, false);
        }

        public void AttachDragHandle(Control control)
        {
            control.MouseDown += OnScrubStart;
            control.MouseMove += OnScrubMove;
            control.MouseUp += OnScrubEnd;
            control.Cursor = Cursors.SizeWE;

            // Allow scrubbing directly on the value like After Effects
            _textBox.MouseDown += OnScrubStart;
            _textBox.MouseMove += OnScrubMove;
            _textBox.MouseUp += OnScrubEnd;
        }

        private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                CommitText();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                Nudge(1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                Nudge(-1);
                e.Handled = true;
            }
        }

        private void CommitText()
        {
            if (TryParseText(out var parsed))
            {
                SetValue(parsed, true);
            }
            else
            {
                UpdateText();
            }
        }

        private bool TryParseText(out decimal value)
        {
            var text = _textBox.Text.Trim();
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return true;
            }

            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private void HandleMouseWheel(object? sender, MouseEventArgs e)
        {
            int steps = e.Delta / SystemInformation.MouseWheelScrollDelta;
            if (steps == 0)
            {
                return;
            }

            decimal scale = GetModifierScale();
            SetValue(_value + steps * _increment * scale, true);
        }

        private void OnScrubStart(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (e.Clicks > 1)
            {
                BeginTextEntry();
                return;
            }

            if (ReferenceEquals(sender, _textBox))
            {
                _textBox.Focus();
            }

            _mouseDown = true;
            _scrubHandle = sender as Control;
            _scrubStartScreenX = Control.MousePosition.X;
            _scrubStartValue = _value;
            _scrubbing = false;
        }

        private void OnScrubMove(object? sender, MouseEventArgs e)
        {
            if (!_mouseDown)
            {
                return;
            }

            int deltaX = Control.MousePosition.X - _scrubStartScreenX;
            if (!_scrubbing)
            {
                if (Math.Abs(deltaX) < ScrubActivationThreshold)
                {
                    return;
                }

                _scrubbing = true;
                Cursor = Cursors.SizeWE;
                (_scrubHandle ?? this).Capture = true;
            }

            decimal scale = GetModifierScale();
            decimal delta = deltaX * _increment * scale;
            SetValue(_scrubStartValue + delta, true);
        }

        private void OnScrubEnd(object? sender, EventArgs e)
        {
            bool wasScrubbing = _scrubbing;
            if (!_mouseDown && !_scrubbing)
            {
                return;
            }

            _mouseDown = false;
            _scrubbing = false;
            Cursor = Cursors.IBeam;
            (_scrubHandle ?? this).Capture = false;
            _scrubHandle = null;
            if (wasScrubbing)
            {
                UpdateText();
            }
        }

        private void Nudge(int direction)
        {
            decimal scale = GetModifierScale();
            SetValue(_value + direction * _increment * scale, true);
        }

        private decimal GetModifierScale()
        {
            decimal scale = 1m;
            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                scale *= 10m;
            }

            if ((ModifierKeys & Keys.Alt) == Keys.Alt)
            {
                scale *= 0.1m;
            }

            return scale;
        }

        private void SetValue(decimal value, bool fromUser)
        {
            decimal quantized = Math.Round(value, _decimalPlaces, MidpointRounding.AwayFromZero);
            decimal clamped = Math.Min(Math.Max(quantized, _minimum), _maximum);
            if (clamped == _value)
            {
                if (!fromUser)
                {
                    UpdateText();
                }
                return;
            }

            _value = clamped;
            UpdateText();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateText()
        {
            string text = _decimalPlaces > 0
                ? _value.ToString($"F{_decimalPlaces}", CultureInfo.CurrentCulture)
                : decimal.ToInt32(Math.Round(_value)).ToString(CultureInfo.CurrentCulture);
            if (_textBox.Text != text)
            {
                _textBox.Text = text;
                _textBox.SelectionStart = _textBox.Text.Length;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = ClientRectangle;
            rect.Inflate(-1, -1);

            using var path = CreateRoundedRectangle(rect, CornerRadius);
            var fill = _textBox.Focused ? _focusFillColor : (_hovering ? _hoverFillColor : _fillColor);
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(_textBox.Focused ? _accentColor : (_hovering ? _hoverBorderColor : _borderColor), 1f);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);

            var gripRect = new Rectangle(rect.Right - GripWidth, rect.Top + 2, GripWidth - 3, rect.Height - 4);
            using var gripBrush = new LinearGradientBrush(gripRect, Color.FromArgb(30, _gripColor), Color.FromArgb(80, _gripColor), LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(gripBrush, gripRect);

            using var gripPen = new Pen(_accentColor, 1f);
            int centerX = gripRect.Left + GripWidth / 2 - 1;
            for (int i = 0; i < 3; i++)
            {
                int y = gripRect.Top + 6 + (i * 8);
                e.Graphics.DrawLine(gripPen, centerX - 1, y, centerX + 2, y);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            var arc = new Rectangle(rect.Location, new Size(diameter, diameter));

            // top left
            path.AddArc(arc, 180, 90);

            // top right
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);

            // bottom right
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            // bottom left
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }

        private void BeginTextEntry()
        {
            _mouseDown = false;
            _scrubbing = false;
            (_scrubHandle ?? this).Capture = false;
            _scrubHandle = null;
            Cursor = Cursors.IBeam;
            _textBox.Focus();
            _textBox.SelectAll();
        }
    }
}
