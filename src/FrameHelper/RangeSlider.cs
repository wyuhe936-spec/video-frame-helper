using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FrameHelper;

public sealed class RangeSlider : Control
{
	private static readonly Color TrackColor = Color.FromArgb(226, 232, 240);

	private static readonly Color SpanColor = Color.FromArgb(37, 99, 235);

	private static readonly Color HandleBorderColor = Color.FromArgb(37, 99, 235);

	private static readonly Color DisabledTrackColor = Color.FromArgb(232, 236, 242);

	private static readonly Color DisabledHandleColor = Color.FromArgb(148, 163, 184);

	private double minValue;

	private double maxValue = 1.0;

	private double startValue;

	private double endValue = 1.0;

	private int activeHandle = 1;

	private bool dragging;

	public double MinValue
	{
		get
		{
			return minValue;
		}
		set
		{
			if (!IsFinite(value) || Math.Abs(minValue - value) < 1E-09)
			{
				return;
			}
			minValue = value;
			if (maxValue < minValue)
			{
				maxValue = minValue;
			}
			startValue = Clamp(startValue, minValue, maxValue);
			endValue = Clamp(endValue, startValue, maxValue);
			Invalidate();
		}
	}

	public double MaxValue
	{
		get
		{
			return maxValue;
		}
		set
		{
			if (!IsFinite(value) || Math.Abs(maxValue - value) < 1E-09)
			{
				return;
			}
			maxValue = Math.Max(value, minValue);
			startValue = Clamp(startValue, minValue, maxValue);
			endValue = Clamp(endValue, startValue, maxValue);
			Invalidate();
		}
	}

	public double StartValue
	{
		get
		{
			return startValue;
		}
		set
		{
			if (!IsFinite(value))
			{
				return;
			}
			double value2 = Snap(value);
			double num = Math.Max(minValue, endValue - MinGap);
			if (value2 > num)
			{
				value2 = Snap(num);
			}
			SetStart(value2, notify: true);
		}
	}

	public double EndValue
	{
		get
		{
			return endValue;
		}
		set
		{
			if (!IsFinite(value))
			{
				return;
			}
			double value2 = Snap(value);
			double num = Math.Min(maxValue, startValue + MinGap);
			if (value2 < num)
			{
				value2 = Snap(num);
			}
			SetEnd(value2, notify: true);
		}
	}

	public event EventHandler ValuesChanged;

	private double Span
	{
		get
		{
			return Math.Max(0.0, maxValue - minValue);
		}
	}

	private double MinGap
	{
		get
		{
			return Math.Max(0.01, Span * 0.001);
		}
	}

	private float DpiScale
	{
		get
		{
			return Math.Max(1f, DeviceDpi / 96f);
		}
	}

	private int HandleRadius
	{
		get
		{
			return Math.Max(7, (int)Math.Round(8f * DpiScale));
		}
	}

	private int TrackHeight
	{
		get
		{
			return Math.Max(4, (int)Math.Round(5f * DpiScale));
		}
	}

	private int TrackLeft
	{
		get
		{
			return HandleRadius + 2;
		}
	}

	private int TrackRight
	{
		get
		{
			return Width - HandleRadius - 2;
		}
	}

	public RangeSlider()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, value: true);
		BackColor = Color.White;
		TabStop = true;
		Cursor = Cursors.Hand;
		AccessibleRole = AccessibleRole.Slider;
	}

	public void SetRange(double min, double max, double start, double end)
	{
		if (!IsFinite(min) || !IsFinite(max) || !IsFinite(start) || !IsFinite(end))
		{
			return;
		}
		minValue = min;
		maxValue = Math.Max(min, max);
		double num = MinGap;
		startValue = Snap(Clamp(start, minValue, maxValue));
		endValue = Snap(Clamp(end, startValue, maxValue));
		if (endValue - startValue < num)
		{
			if (startValue + num <= maxValue)
			{
				endValue = Snap(startValue + num);
			}
			else
			{
				startValue = Snap(Math.Max(minValue, endValue - num));
			}
		}
		Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		int left = TrackLeft;
		int right = Math.Max(left, TrackRight);
		int centerY = Height / 2;
		int trackHeight = TrackHeight;
		Rectangle track = new Rectangle(left, centerY - trackHeight / 2, Math.Max(0, right - left), trackHeight);
		if (track.Width <= 0)
		{
			return;
		}
		using (GraphicsPath path = Rounded(track, trackHeight / 2))
		{
			using SolidBrush brush = new SolidBrush(Enabled ? TrackColor : DisabledTrackColor);
			graphics.FillPath(brush, path);
		}
		int num = ValueToX(startValue);
		int num2 = ValueToX(endValue);
		if (num2 > num)
		{
			Rectangle rect = new Rectangle(num, centerY - trackHeight / 2, num2 - num, trackHeight);
			using GraphicsPath path2 = Rounded(rect, trackHeight / 2);
			using SolidBrush brush2 = new SolidBrush(Enabled ? SpanColor : DisabledTrackColor);
			graphics.FillPath(brush2, path2);
		}
		DrawHandle(graphics, num, centerY, activeHandle == 0 && Focused);
		DrawHandle(graphics, num2, centerY, activeHandle == 1 && Focused);
	}

	private void DrawHandle(Graphics graphics, int x, int y, bool active)
	{
		int handleRadius = HandleRadius;
		if (active && Enabled)
		{
			using SolidBrush brush = new SolidBrush(Color.FromArgb(46, SpanColor));
			graphics.FillEllipse(brush, x - handleRadius - 3, y - handleRadius - 3, 2 * (handleRadius + 3), 2 * (handleRadius + 3));
		}
		using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(26, 15, 23, 42)))
		{
			graphics.FillEllipse(brush2, x - handleRadius + 1, y - handleRadius + 2, 2 * handleRadius, 2 * handleRadius);
		}
		Rectangle rect = new Rectangle(x - handleRadius, y - handleRadius, 2 * handleRadius, 2 * handleRadius);
		using (SolidBrush brush3 = new SolidBrush(Enabled ? Color.White : Color.FromArgb(248, 250, 252)))
		{
			graphics.FillEllipse(brush3, rect);
		}
		using Pen pen = new Pen(Enabled ? HandleBorderColor : DisabledHandleColor, Math.Max(2f, 2.2f * DpiScale));
		graphics.DrawEllipse(pen, rect);
		if (Enabled)
		{
			using SolidBrush brush4 = new SolidBrush(SpanColor);
			int num = Math.Max(2, handleRadius / 3);
			graphics.FillEllipse(brush4, x - num / 2, y - num / 2, num, num);
		}
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		if (!Enabled || Span <= 0.0)
		{
			return;
		}
		Focus();
		int num = HitHandle(e.Location);
		if (num < 0)
		{
			double num2 = XToValue(e.X);
			num = ((Math.Abs(num2 - startValue) <= Math.Abs(num2 - endValue)) ? 0 : 1);
		}
		activeHandle = num;
		dragging = true;
		Capture = true;
		MoveActive(e.X);
		Invalidate();
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		if (dragging)
		{
			MoveActive(e.X);
			return;
		}
		if (!Enabled)
		{
			Cursor = Cursors.Default;
			return;
		}
		if (HitHandle(e.Location) >= 0)
		{
			Cursor = Cursors.SizeWE;
		}
		else if (e.X >= TrackLeft - HandleRadius && e.X <= TrackRight + HandleRadius)
		{
			Cursor = Cursors.Hand;
		}
		else
		{
			Cursor = Cursors.Default;
		}
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		if (dragging)
		{
			dragging = false;
			Capture = false;
			Invalidate();
		}
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		base.OnMouseLeave(e);
		if (!dragging)
		{
			Cursor = Cursors.Default;
		}
	}

	protected override bool IsInputKey(Keys keyData)
	{
		Keys keys = keyData & Keys.KeyCode;
		if (keys == Keys.Left || keys == Keys.Right || keys == Keys.Home || keys == Keys.End || keys == Keys.PageUp || keys == Keys.PageDown)
		{
			return true;
		}
		return base.IsInputKey(keyData);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (!Enabled || Span <= 0.0)
		{
			return;
		}
		double num = Math.Max(0.01, Span / 1000.0);
		if ((e.Modifiers & Keys.Shift) != 0)
		{
			num *= 10.0;
		}
		else if ((e.Modifiers & Keys.Control) != 0)
		{
			num *= 0.1;
		}
		double num2 = ((activeHandle == 0) ? startValue : endValue);
		switch (e.KeyCode)
		{
		case Keys.Left:
			num2 -= num;
			break;
		case Keys.Right:
			num2 += num;
			break;
		case Keys.PageDown:
			num2 -= Span / 100.0;
			break;
		case Keys.PageUp:
			num2 += Span / 100.0;
			break;
		case Keys.Home:
			num2 = ((activeHandle == 0) ? minValue : (startValue + MinGap));
			break;
		case Keys.End:
			num2 = ((activeHandle == 0) ? (endValue - MinGap) : maxValue);
			break;
		default:
			return;
		}
		if (activeHandle == 0)
		{
			StartValue = num2;
		}
		else
		{
			EndValue = num2;
		}
		e.Handled = true;
		e.SuppressKeyPress = true;
		Invalidate();
	}

	protected override void OnGotFocus(EventArgs e)
	{
		base.OnGotFocus(e);
		Invalidate();
	}

	protected override void OnLostFocus(EventArgs e)
	{
		base.OnLostFocus(e);
		Invalidate();
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		base.OnEnabledChanged(e);
		Cursor = (Enabled ? Cursors.Hand : Cursors.Default);
		Invalidate();
	}

	protected override void OnBackColorChanged(EventArgs e)
	{
		base.OnBackColorChanged(e);
		Invalidate();
	}

	private void MoveActive(int x)
	{
		double value = XToValue(x);
		if (activeHandle == 0)
		{
			StartValue = value;
		}
		else
		{
			EndValue = value;
		}
	}

	private void SetStart(double value, bool notify)
	{
		if (Math.Abs(startValue - value) < 1E-09)
		{
			return;
		}
		startValue = value;
		Invalidate();
		if (notify)
		{
			this.ValuesChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private void SetEnd(double value, bool notify)
	{
		if (Math.Abs(endValue - value) < 1E-09)
		{
			return;
		}
		endValue = value;
		Invalidate();
		if (notify)
		{
			this.ValuesChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private int HitHandle(Point point)
	{
		int num = ValueToX(startValue);
		int num2 = ValueToX(endValue);
		int num3 = HandleRadius + 6;
		bool flag = Math.Abs(point.X - num) <= num3;
		bool flag2 = Math.Abs(point.X - num2) <= num3;
		if (flag && flag2)
		{
			return (Math.Abs(point.X - num) <= Math.Abs(point.X - num2)) ? 0 : 1;
		}
		if (flag)
		{
			return 0;
		}
		if (flag2)
		{
			return 1;
		}
		return -1;
	}

	private int ValueToX(double value)
	{
		int num = TrackLeft;
		int num2 = TrackRight;
		if (num2 <= num)
		{
			return num;
		}
		double num3 = ((Span > 0.0) ? ((Clamp(value, minValue, maxValue) - minValue) / Span) : 0.0);
		return num + (int)Math.Round(num3 * (double)(num2 - num));
	}

	private double XToValue(int x)
	{
		int num = TrackLeft;
		int num2 = TrackRight;
		if (num2 <= num)
		{
			return minValue;
		}
		double num3 = (double)(Math.Max(num, Math.Min(num2, x)) - num) / (double)(num2 - num);
		return Snap(minValue + num3 * Span);
	}

	private double Snap(double value)
	{
		value = Clamp(value, minValue, maxValue);
		return Math.Round(value, 2, MidpointRounding.AwayFromZero);
	}

	private static double Clamp(double value, double min, double max)
	{
		if (value < min)
		{
			return min;
		}
		if (value > max)
		{
			return max;
		}
		return value;
	}

	private static bool IsFinite(double value)
	{
		if (!double.IsNaN(value))
		{
			return !double.IsInfinity(value);
		}
		return false;
	}

	private static GraphicsPath Rounded(Rectangle rect, int radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		if (rect.Width <= 0 || rect.Height <= 0)
		{
			return graphicsPath;
		}
		int num = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
		if (num <= 0)
		{
			graphicsPath.AddRectangle(rect);
			return graphicsPath;
		}
		int num2 = num * 2;
		graphicsPath.AddArc(rect.X, rect.Y, num2, num2, 180f, 90f);
		graphicsPath.AddArc(rect.Right - num2, rect.Y, num2, num2, 270f, 90f);
		graphicsPath.AddArc(rect.Right - num2, rect.Bottom - num2, num2, num2, 0f, 90f);
		graphicsPath.AddArc(rect.X, rect.Bottom - num2, num2, num2, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
