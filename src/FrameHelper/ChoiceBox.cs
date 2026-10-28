using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FrameHelper;

public sealed class ChoiceBox : Control
{
	public List<object> Items = new List<object>();

	private readonly ContextMenuStrip choices;

	private int index = -1;

	public int SelectedIndex
	{
		get
		{
			return index;
		}
		set
		{
			if (value < 0 || value >= Items.Count)
			{
				throw new ArgumentOutOfRangeException("value");
			}
			index = value;
			Text = Convert.ToString(Items[value]);
			Invalidate();
		}
	}

	public ChoiceBox()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		base.Height = 32;
		base.TabStop = true;
		Cursor = Cursors.Hand;
		BackColor = Color.White;
		base.AccessibleRole = AccessibleRole.ComboBox;
		choices = new ContextMenuStrip
		{
			ShowImageMargin = false,
			ShowCheckMargin = true
		};
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.Clear(base.Enabled ? Color.White : Color.FromArgb(244, 246, 249));
		using (Pen pen = new Pen(Focused ? Color.FromArgb(37, 99, 235) : Color.FromArgb(200, 211, 225)))
		{
			e.Graphics.DrawRectangle(pen, 0, 0, base.Width - 1, base.Height - 1);
		}
		TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(9, 0, Math.Max(1, base.Width - 35), base.Height), base.Enabled ? ForeColor : SystemColors.GrayText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		using SolidBrush brush = new SolidBrush(Color.FromArgb(100, 116, 139));
		e.Graphics.FillPolygon(brush, new Point[3]
		{
			new Point(base.Width - 20, base.Height / 2 - 2),
			new Point(base.Width - 10, base.Height / 2 - 2),
			new Point(base.Width - 15, base.Height / 2 + 3)
		});
	}

	protected override void OnClick(EventArgs e)
	{
		base.OnClick(e);
		Focus();
		OpenChoices();
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

	protected override bool IsInputKey(Keys keyData)
	{
		if (keyData != Keys.Down && keyData != Keys.Up)
		{
			return base.IsInputKey(keyData);
		}
		return true;
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (e.KeyCode == Keys.Down && index < Items.Count - 1)
		{
			SelectedIndex = index + 1;
			e.Handled = true;
		}
		if (e.KeyCode == Keys.Up && index > 0)
		{
			SelectedIndex = index - 1;
			e.Handled = true;
		}
		if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Return)
		{
			OpenChoices();
			e.Handled = true;
		}
	}

	private void OpenChoices()
	{
		if (base.IsDisposed || base.Disposing || !base.Enabled || Items.Count == 0 || choices.Visible)
		{
			return;
		}
		choices.Font = Font;
		while (choices.Items.Count > 0)
		{
			choices.Items[0].Dispose();
		}
		for (int i = 0; i < Items.Count; i++)
		{
			int selected = i;
			ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem(Convert.ToString(Items[i]));
			toolStripMenuItem.Checked = i == index;
			toolStripMenuItem.AutoSize = true;
			ToolStripMenuItem toolStripMenuItem2 = toolStripMenuItem;
			toolStripMenuItem2.Click += delegate
			{
				SelectedIndex = selected;
			};
			choices.Items.Add(toolStripMenuItem2);
		}
		choices.Show(this, new Point(0, base.Height));
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			choices.Dispose();
		}
		base.Dispose(disposing);
	}
}
