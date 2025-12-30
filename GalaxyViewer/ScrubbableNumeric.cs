using System;
using System.Drawing;
using System.Globalization;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GalaxyViewer
{
    internal sealed class ScrubbableNumericTheme
    {
        public Color BorderColor { get; }
        public Color HoverBorderColor { get; }
        public Color FillColor { get; }
        public Color HoverFillColor { get; }
        public Color FocusFillColor { get; }
        public Color GripColor { get; }
        public Color AccentColor { get; }
        public Color StepperFillColor { get; }
        public Color StepperHoverColor { get; }
        public Color StepperPressedColor { get; }
        public Color TextColor { get; }

        private ScrubbableNumericTheme(
            Color borderColor,
            Color hoverBorderColor,
            Color fillColor,
            Color hoverFillColor,
            Color focusFillColor,
            Color gripColor,
            Color accentColor,
            Color stepperFillColor,
            Color stepperHoverColor,
            Color stepperPressedColor,
            Color textColor)
        {
            BorderColor = borderColor;
            HoverBorderColor = hoverBorderColor;
            FillColor = fillColor;
            HoverFillColor = hoverFillColor;
            FocusFillColor = focusFillColor;
            GripColor = gripColor;
            AccentColor = accentColor;
            StepperFillColor = stepperFillColor;
            StepperHoverColor = stepperHoverColor;
            StepperPressedColor = stepperPressedColor;
            TextColor = textColor;
        }

        public static readonly ScrubbableNumericTheme Monochrome = new(
            Color.FromArgb(70, 70, 70),
            Color.FromArgb(95, 120, 160),
            Color.FromArgb(42, 42, 42),
            Color.FromArgb(48, 48, 54),
            Color.FromArgb(50, 60, 80),
            Color.FromArgb(80, 90, 110),
            Color.FromArgb(78, 156, 255),
            Color.FromArgb(55, 55, 60),
            Color.FromArgb(64, 74, 94),
            Color.FromArgb(82, 112, 160),
            Color.Gainsboro);

        public static readonly ScrubbableNumericTheme Nebula = new(
            Color.FromArgb(36, 56, 78),
            Color.FromArgb(90, 150, 210),
            Color.FromArgb(18, 22, 38),
            Color.FromArgb(28, 32, 52),
            Color.FromArgb(40, 52, 82),
            Color.FromArgb(110, 150, 210),
            Color.FromArgb(255, 140, 90),
            Color.FromArgb(30, 36, 60),
            Color.FromArgb(54, 68, 104),
            Color.FromArgb(76, 108, 150),
            Color.FromArgb(235, 242, 255));
    }

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
        private const int StepperWidth = 14;
        private const int StepperSpacing = 2;
        private ScrubbableNumericTheme _theme = ScrubbableNumericTheme.Monochrome;
        private bool _hovering;
        private bool _arrowUpHot;
        private bool _arrowDownHot;
        private bool _arrowUpPressed;
        private bool _arrowDownPressed;

        public event EventHandler? ValueChanged;

        public ScrubbableNumeric()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Padding = new Padding(8, 3, GripWidth + StepperWidth + 6, 3);
            BackColor = Color.Transparent;
            MinimumSize = new Size(120, 28);

            _textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                TextAlign = HorizontalAlignment.Right,
                BackColor = _theme.FillColor,
                ForeColor = _theme.TextColor,
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
                _textBox.BackColor = _theme.FillColor;
                CommitText();
                Invalidate();
            };
            _textBox.Enter += (_, _) =>
            {
                _textBox.BackColor = _theme.FocusFillColor;
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
            ApplyTheme(_theme);
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

        public ScrubbableNumericTheme Theme
        {
            get => _theme;
            set => ApplyTheme(value ?? ScrubbableNumericTheme.Monochrome);
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
            // Only react to wheel when the control is focused or actively scrubbing to avoid accidental changes while scrolling past
            if (!_scrubbing && !_textBox.Focused && !Focused)
            {
                return;
            }

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

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var rect = GetDrawingRect();
            GetArrowRects(rect, out var upRect, out var downRect);

            bool hotUp = upRect.Contains(e.Location);
            bool hotDown = downRect.Contains(e.Location);
            if (hotUp != _arrowUpHot || hotDown != _arrowDownHot)
            {
                _arrowUpHot = hotUp;
                _arrowDownHot = hotDown;
                Cursor = (_arrowUpHot || _arrowDownHot) ? Cursors.Hand : Cursors.IBeam;
                Invalidate(Rectangle.Union(upRect, downRect));
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_arrowUpHot || _arrowDownHot || _arrowUpPressed || _arrowDownPressed)
            {
                _arrowUpHot = _arrowDownHot = _arrowUpPressed = _arrowDownPressed = false;
                Cursor = Cursors.IBeam;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            var rect = GetDrawingRect();
            GetArrowRects(rect, out var upRect, out var downRect);
            _arrowUpPressed = upRect.Contains(e.Location);
            _arrowDownPressed = !_arrowUpPressed && downRect.Contains(e.Location);
            if (_arrowUpPressed || _arrowDownPressed)
            {
                _arrowUpHot = _arrowUpPressed;
                _arrowDownHot = _arrowDownPressed;
                Capture = true;
                _textBox.Focus();
                Invalidate(Rectangle.Union(upRect, downRect));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_arrowUpPressed && !_arrowDownPressed)
            {
                return;
            }

            var rect = GetDrawingRect();
            GetArrowRects(rect, out var upRect, out var downRect);
            bool activateUp = _arrowUpPressed && upRect.Contains(e.Location);
            bool activateDown = _arrowDownPressed && downRect.Contains(e.Location);

            _arrowUpPressed = false;
            _arrowDownPressed = false;
            Capture = false;
            Invalidate(Rectangle.Union(upRect, downRect));

            if (activateUp)
            {
                Nudge(1);
            }
            else if (activateDown)
            {
                Nudge(-1);
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
            var rect = GetDrawingRect();

            using var path = CreateRoundedRectangle(rect, CornerRadius);
            var fill = _textBox.Focused ? _theme.FocusFillColor : (_hovering ? _theme.HoverFillColor : _theme.FillColor);
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(_textBox.Focused ? _theme.AccentColor : (_hovering ? _theme.HoverBorderColor : _theme.BorderColor), 1f);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);

            DrawStepper(e.Graphics, rect);

            var gripRect = GetGripRect(rect);
            using var gripBrush = new LinearGradientBrush(gripRect, Color.FromArgb(30, _theme.GripColor), Color.FromArgb(80, _theme.GripColor), LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(gripBrush, gripRect);

            using var gripPen = new Pen(_theme.AccentColor, 1f);
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

        private Rectangle GetDrawingRect()
        {
            var rect = ClientRectangle;
            rect.Inflate(-1, -1);
            return rect;
        }

        private Rectangle GetGripRect(Rectangle rect)
        {
            return new Rectangle(rect.Right - GripWidth, rect.Top + 2, GripWidth - 3, rect.Height - 4);
        }

        private Rectangle GetStepperRect(Rectangle rect)
        {
            return new Rectangle(rect.Right - GripWidth - StepperWidth - StepperSpacing, rect.Top + 2, StepperWidth, rect.Height - 4);
        }

        private void GetArrowRects(Rectangle rect, out Rectangle upRect, out Rectangle downRect)
        {
            var stepperRect = GetStepperRect(rect);
            int halfHeight = stepperRect.Height / 2;
            upRect = new Rectangle(stepperRect.Left, stepperRect.Top, stepperRect.Width, halfHeight);
            downRect = new Rectangle(stepperRect.Left, stepperRect.Top + halfHeight, stepperRect.Width, stepperRect.Height - halfHeight);
        }

        private void DrawStepper(Graphics graphics, Rectangle rect)
        {
            GetArrowRects(rect, out var upRect, out var downRect);
            var combined = Rectangle.Union(upRect, downRect);

            DrawArrowButton(graphics, upRect, _arrowUpHot, _arrowUpPressed && _arrowUpHot, true);
            DrawArrowButton(graphics, downRect, _arrowDownHot, _arrowDownPressed && _arrowDownHot, false);

            using var borderPen = new Pen(_theme.BorderColor);
            graphics.DrawRectangle(borderPen, new Rectangle(combined.X, combined.Y, combined.Width - 1, combined.Height - 1));
            graphics.DrawLine(borderPen, combined.Left, upRect.Bottom, combined.Right - 1, upRect.Bottom);
        }

        private void DrawArrowButton(Graphics graphics, Rectangle rect, bool hot, bool pressed, bool up)
        {
            Color background = _theme.StepperFillColor;
            if (pressed)
            {
                background = _theme.StepperPressedColor;
            }
            else if (hot)
            {
                background = _theme.StepperHoverColor;
            }

            using var backgroundBrush = new SolidBrush(background);
            graphics.FillRectangle(backgroundBrush, rect);
            DrawArrowGlyph(graphics, rect, up, pressed ? Color.White : _theme.AccentColor);
        }

        private static void DrawArrowGlyph(Graphics graphics, Rectangle rect, bool up, Color color)
        {
            int midX = rect.Left + rect.Width / 2;
            int halfWidth = Math.Max(2, Math.Min((rect.Width - 2) / 2, 5));
            int arrowHeight = Math.Max(4, Math.Min(rect.Height - 4, 8));
            int tipY = up ? rect.Top + 2 : rect.Bottom - 3;
            int baseY = up ? tipY + arrowHeight : tipY - arrowHeight;

            var points = new[]
            {
                new Point(midX, tipY),
                new Point(midX - halfWidth, baseY),
                new Point(midX + halfWidth, baseY),
            };

            using var arrowBrush = new SolidBrush(color);
            graphics.FillPolygon(arrowBrush, points);
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

        private void ApplyTheme(ScrubbableNumericTheme theme)
        {
            _theme = theme ?? ScrubbableNumericTheme.Monochrome;
            _textBox.ForeColor = _theme.TextColor;
            _textBox.BackColor = _textBox.Focused ? _theme.FocusFillColor : _theme.FillColor;
            Invalidate();
        }
    }
}
