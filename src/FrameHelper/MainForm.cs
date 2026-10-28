using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FrameHelper;

public sealed class MainForm : Form
{
	private sealed class RowState
	{
		public string Path;

		public bool CustomCount;

		public double Duration;

		public Result Result;
	}

	private readonly Color Ink = Color.FromArgb(29, 45, 66);

	private readonly Color Muted = Color.FromArgb(100, 116, 139);

	private readonly Color Blue = Color.FromArgb(37, 99, 235);

	private DataGridView grid;

	private NumericUpDown frames;

	private NumericUpDown filesPerSheet;

	private ChoiceBox quality;

	private ChoiceBox existing;

	private ChoiceBox acceleration;

	private CheckBox saveFrames;

	private CheckBox showTimes;

	private CheckBox showInfo;

	private CheckBox recursive;

	private TextBox output;

	private RangeSlider rangeSlider;

	private Label rangeStartText;

	private Label rangeEndText;

	private Label rangeInfo;

	private Button rangeReset;

	private Button start;

	private Button cancel;

	private Button preview;

	private Button retry;

	private Button openOutput;

	private Button viewResults;

	private Button addFolder;

	private Button addFiles;

	private Button clear;

	private Button remove;

	private Button browse;

	private Label status;

	private Label counts;

	private Label outputHint;

	private ProgressBar progress;

	private CancellationTokenSource cancellation;

	private CancellationTokenSource scanCancellation;

	private bool closeAfterScan;

	private string lastPreviewRoot;

	private bool running;

	private bool scanning;

	private bool updating;

	private bool uiBusy;

	private SavedSettings settings;

	private readonly VideoEngine durationEngine = new VideoEngine();

	private CancellationTokenSource durationCancellation;

	private double rangeTotal;

	private double rangeBegin;

	private double rangeFinish;

	private bool rangeLimited;

	private bool syncingRange;

	private readonly List<Result> results = new List<Result>();

	private readonly List<string> logs = new List<string>();

	private readonly HashSet<string> knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private string settingsPath = Path.Combine(VideoEngine.AppDirectory, "settings.json");

	public MainForm()
	{
		settings = LoadSettings();
		LoadSavedRange();
		CleanupStalePreviews();
		Text = "视频抽帧助手 1.2.2";
		Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
		ForeColor = Ink;
		BackColor = Color.FromArgb(244, 247, 251);
		base.AutoScaleDimensions = new SizeF(96f, 96f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		base.ClientSize = new Size(1100, 890);
		MinimumSize = new Size(760, 500);
		base.StartPosition = FormStartPosition.CenterScreen;
		try
		{
			base.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		}
		catch
		{
		}
		BuildUi();
		AllowDrop = true;
		base.DragEnter += OnDragEnter;
		base.DragDrop += OnDragDrop;
		base.FormClosing += OnClosing;
		base.Shown += delegate
		{
			Rectangle workingArea = Screen.FromControl(this).WorkingArea;
			MinimumSize = new Size(Math.Min(MinimumSize.Width, workingArea.Width), Math.Min(MinimumSize.Height, workingArea.Height));
			if (base.Height > workingArea.Height)
			{
				base.Height = workingArea.Height;
			}
			if (base.Width > workingArea.Width)
			{
				base.Width = workingArea.Width;
			}
			CenterToScreen();
		};
	}

	private void BuildUi()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Padding = new Padding(24, 16, 24, 18);
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowCount = 7;
		TableLayoutPanel tableLayoutPanel2 = tableLayoutPanel;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 35f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 300f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 112f));
		Panel host = new Panel();
		host.Dock = DockStyle.Fill;
		host.AutoScroll = true;
		host.BackColor = BackColor;
		host.Controls.Add(tableLayoutPanel2);
		base.Controls.Add(host);
		host.Layout += delegate
		{
			UpdateLayoutMinimum(host, tableLayoutPanel2);
		};
		UpdateLayoutMinimum(host, tableLayoutPanel2);
		Panel panel = new Panel();
		panel.Dock = DockStyle.Fill;
		Panel panel2 = panel;
		panel2.Controls.Add(new Label
		{
			Text = "视频抽帧助手",
			Font = new Font(Font.FontFamily, 32f, FontStyle.Bold, GraphicsUnit.Pixel),
			AutoSize = true,
			Location = new Point(0, 0),
			ForeColor = Ink
		});
		panel2.Controls.Add(new Label
		{
			Text = "多视频合拼 · GPU 解码 · 文件名与时间标注",
			AutoSize = true,
			Location = new Point(2, 51),
			ForeColor = Muted
		});
		Panel panel3 = new Panel();
		panel3.Dock = DockStyle.Right;
		panel3.Width = 260;
		Panel panel4 = panel3;
		panel4.Controls.Add(new Label
		{
			Text = "本地运行  /  原视频不改动",
			TextAlign = ContentAlignment.MiddleRight,
			Dock = DockStyle.Top,
			Height = 30,
			ForeColor = Muted
		});
		LinkLabel linkLabel = new LinkLabel();
		linkLabel.Text = "使用说明";
		linkLabel.Dock = DockStyle.Bottom;
		linkLabel.Height = 29;
		linkLabel.TextAlign = ContentAlignment.MiddleRight;
		linkLabel.LinkColor = Blue;
		linkLabel.Margin = new Padding(0);
		LinkLabel linkLabel2 = linkLabel;
		linkLabel2.LinkClicked += delegate
		{
			ShowHelp();
		};
		panel4.Controls.Add(linkLabel2);
		panel2.Controls.Add(panel4);
		tableLayoutPanel2.Controls.Add(panel2, 0, 0);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel();
		flowLayoutPanel.Dock = DockStyle.Fill;
		flowLayoutPanel.FlowDirection = FlowDirection.LeftToRight;
		flowLayoutPanel.WrapContents = false;
		flowLayoutPanel.Padding = new Padding(0, 5, 0, 0);
		FlowLayoutPanel flowLayoutPanel2 = flowLayoutPanel;
		addFolder = Button("添加文件夹", 122, primary: false, delegate
		{
			ChooseFolder();
		});
		addFiles = Button("添加视频", 110, primary: false, delegate
		{
			ChooseVideos();
		});
		remove = Button("移除选中", 106, primary: false, delegate
		{
			RemoveSelected();
		});
		clear = Button("清空", 70, primary: false, delegate
		{
			CommitGrid();
			CancelDurationProbes();
			grid.Rows.Clear();
			knownPaths.Clear();
			results.Clear();
			progress.Value = 0;
			ResetRange();
			UpdateCounts();
			SetBusy(busy: false);
			SetStatus("拖入视频或文件夹，开始整理素材。", error: false);
		});
		recursive = Check("包含子文件夹", settings.Recursive);
		recursive.Margin = new Padding(20, 8, 0, 0);
		flowLayoutPanel2.Controls.AddRange(new Control[5] { addFolder, addFiles, remove, clear, recursive });
		tableLayoutPanel2.Controls.Add(flowLayoutPanel2, 0, 1);
		grid = new DataGridView
		{
			Dock = DockStyle.Fill,
			BackgroundColor = Color.White,
			BorderStyle = BorderStyle.None,
			RowHeadersVisible = false,
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			AutoGenerateColumns = false,
			AllowUserToResizeRows = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			MultiSelect = true,
			EnableHeadersVisualStyles = false,
			CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
			GridColor = Color.FromArgb(236, 241, 247),
			ColumnHeadersHeight = 42,
			ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
			RowTemplate = 
			{
				Height = 38
			},
			EditMode = DataGridViewEditMode.EditOnEnter,
			AllowDrop = true
		};
		grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(231, 238, 248);
		grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
		grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
		grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
		grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 235, 254);
		grid.DefaultCellStyle.SelectionForeColor = Ink;
		grid.DefaultCellStyle.Padding = new Padding(7, 3, 7, 3);
		grid.DefaultCellStyle.BackColor = Color.White;
		grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 252, 255);
		grid.Columns.Add(new DataGridViewCheckBoxColumn
		{
			Name = "checked",
			HeaderText = "处理",
			Width = 62
		});
		grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "number",
			HeaderText = "序号",
			Width = 65,
			ReadOnly = true
		});
		grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "file",
			HeaderText = "视频文件名",
			AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
			MinimumWidth = 220,
			ReadOnly = true
		});
		grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "duration",
			HeaderText = "时长",
			Width = 110,
			ReadOnly = true
		});
		grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "frames",
			HeaderText = "帧数 ✎",
			Width = 86
		});
		grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "status",
			HeaderText = "状态",
			Width = 175,
			ReadOnly = true
		});
		foreach (DataGridViewColumn column in grid.Columns)
		{
			column.SortMode = DataGridViewColumnSortMode.NotSortable;
		}
		grid.CellValidating += ValidateCount;
		grid.CellEndEdit += delegate(object sender, DataGridViewCellEventArgs e)
		{
			if (!updating && e.ColumnIndex == 4 && e.RowIndex >= 0)
			{
				DataGridViewRow dataGridViewRow3 = grid.Rows[e.RowIndex];
				int count = ParseCount(dataGridViewRow3.Cells[4].Value);
				if (count == 0)
				{
					count = (int)frames.Value;
					dataGridViewRow3.Cells[4].Value = count;
				}
				((RowState)dataGridViewRow3.Tag).CustomCount = count != (int)frames.Value;
				dataGridViewRow3.ErrorText = "";
			}
			UpdateCounts();
		};
		grid.CurrentCellDirtyStateChanged += delegate
		{
			if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
			{
				grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
			}
		};
		grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
		{
			if (!updating && e.RowIndex >= 0 && e.ColumnIndex == 4)
			{
				DataGridViewRow dataGridViewRow2 = grid.Rows[e.RowIndex];
				if (dataGridViewRow2.Tag is RowState rowState2)
				{
					int count = ParseCount(dataGridViewRow2.Cells[4].Value);
					if (count > 0)
					{
						rowState2.CustomCount = count != (int)frames.Value;
					}
				}
			}
			UpdateCounts();
		};
		grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
		{
			if (e.RowIndex >= 0 && e.ColumnIndex != 4 && e.ColumnIndex != 0)
			{
				RowState rowState = (RowState)grid.Rows[e.RowIndex].Tag;
				if (rowState.Result != null && File.Exists(rowState.Result.Collage))
				{
					ShowPreview(new List<Result> { rowState.Result });
				}
			}
		};
		grid.DragEnter += OnDragEnter;
		grid.DragDrop += OnDragDrop;
		grid.SelectionChanged += delegate
		{
			UpdateRangeReference();
		};
		grid.CurrentCellChanged += delegate
		{
			UpdateRangeReference();
		};
		tableLayoutPanel2.Controls.Add(grid, 0, 2);
		counts = new Label
		{
			Text = "0 个视频",
			Dock = DockStyle.Fill,
			ForeColor = Muted,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true
		};
		tableLayoutPanel2.Controls.Add(counts, 0, 3);
		tableLayoutPanel2.Controls.Add(BuildRangePanel(), 0, 4);
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
		tableLayoutPanel3.Dock = DockStyle.Fill;
		tableLayoutPanel3.BackColor = Color.White;
		tableLayoutPanel3.Padding = new Padding(18, 10, 18, 10);
		tableLayoutPanel3.ColumnCount = 3;
		tableLayoutPanel3.RowCount = 4;
		TableLayoutPanel tableLayoutPanel4 = tableLayoutPanel3;
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29f));
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36f));
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel flowLayoutPanel3 = new FlowLayoutPanel();
		flowLayoutPanel3.Dock = DockStyle.Fill;
		flowLayoutPanel3.FlowDirection = FlowDirection.LeftToRight;
		flowLayoutPanel3.WrapContents = true;
		flowLayoutPanel3.Margin = new Padding(0);
		FlowLayoutPanel flowLayoutPanel4 = flowLayoutPanel3;
		flowLayoutPanel4.Controls.Add(new Label
		{
			Text = "每个视频抽取",
			AutoSize = true,
			Margin = new Padding(0, 9, 8, 0)
		});
		frames = new NumericUpDown
		{
			Minimum = 1m,
			Maximum = 100m,
			Value = Math.Max(1, Math.Min(100, settings.FrameCount)),
			Width = 70,
			Font = new Font(Font.FontFamily, 18f, FontStyle.Regular, GraphicsUnit.Pixel),
			Margin = new Padding(0, 4, 4, 0)
		};
		flowLayoutPanel4.Controls.Add(frames);
		flowLayoutPanel4.Controls.Add(new Label
		{
			Text = "帧",
			AutoSize = true,
			Margin = new Padding(0, 9, 0, 0)
		});
		flowLayoutPanel4.Controls.Add(new Label
		{
			Text = "1–100 帧；表格内可单独修改",
			AutoSize = true,
			ForeColor = Muted,
			Font = new Font(Font.FontFamily, 12f, FontStyle.Regular, GraphicsUnit.Pixel),
			Margin = new Padding(0, 8, 0, 0)
		});
		frames.ValueChanged += delegate
		{
			updating = true;
			foreach (DataGridViewRow item in (IEnumerable)grid.Rows)
			{
				if (!((RowState)item.Tag).CustomCount)
				{
					item.Cells[4].Value = (int)frames.Value;
				}
			}
			updating = false;
			UpdateCounts();
		};
		tableLayoutPanel4.Controls.Add(flowLayoutPanel4, 0, 0);
		FlowLayoutPanel flowLayoutPanel5 = new FlowLayoutPanel();
		flowLayoutPanel5.Dock = DockStyle.Fill;
		flowLayoutPanel5.FlowDirection = FlowDirection.TopDown;
		flowLayoutPanel5.WrapContents = false;
		flowLayoutPanel5.Margin = new Padding(10, 0, 12, 0);
		FlowLayoutPanel flowLayoutPanel6 = flowLayoutPanel5;
		flowLayoutPanel6.Controls.Add(new Label
		{
			Text = "图片清晰度 / 体积",
			AutoSize = true,
			Margin = new Padding(0, 0, 0, 6)
		});
		quality = new ChoiceBox
		{
			Width = 270,
			AccessibleName = "图片清晰度"
		};
		quality.Items.AddRange(new object[3] { "轻量预览 · 更省空间", "标准清晰 · 适合查看细节", "高清 · 单帧保留原始分辨率" });
		quality.SelectedIndex = Math.Max(0, Math.Min(2, settings.PresetIndex));
		flowLayoutPanel6.Controls.Add(quality);
		tableLayoutPanel4.Controls.Add(flowLayoutPanel6, 1, 0);
		FlowLayoutPanel flowLayoutPanel7 = new FlowLayoutPanel();
		flowLayoutPanel7.Dock = DockStyle.Fill;
		flowLayoutPanel7.FlowDirection = FlowDirection.TopDown;
		flowLayoutPanel7.WrapContents = false;
		flowLayoutPanel7.Margin = new Padding(10, 0, 0, 0);
		FlowLayoutPanel flowLayoutPanel8 = flowLayoutPanel7;
		saveFrames = Check("同时保存单帧图片", settings.SaveFrames);
		showTimes = Check("拼图标注每帧时间点", settings.ShowTimes);
		showInfo = Check("显示视频时长与分辨率", settings.ShowInfo);
		flowLayoutPanel8.Controls.AddRange(new Control[3] { saveFrames, showTimes, showInfo });
		tableLayoutPanel4.Controls.Add(flowLayoutPanel8, 2, 0);
		FlowLayoutPanel flowLayoutPanel9 = new FlowLayoutPanel();
		flowLayoutPanel9.Dock = DockStyle.Fill;
		flowLayoutPanel9.FlowDirection = FlowDirection.LeftToRight;
		flowLayoutPanel9.WrapContents = false;
		flowLayoutPanel9.Padding = new Padding(0, 5, 0, 0);
		FlowLayoutPanel flowLayoutPanel10 = flowLayoutPanel9;
		flowLayoutPanel10.Controls.Add(new Label
		{
			Text = "每张图最多",
			AutoSize = true,
			Margin = new Padding(0, 7, 8, 0)
		});
		filesPerSheet = new NumericUpDown
		{
			Minimum = 1m,
			Maximum = 100m,
			Value = Math.Max(1, Math.Min(100, settings.FilesPerSheet)),
			Width = 65,
			Font = new Font(Font.FontFamily, 18f, FontStyle.Regular, GraphicsUnit.Pixel),
			Margin = new Padding(0, 2, 6, 0)
		};
		flowLayoutPanel10.Controls.Add(filesPerSheet);
		flowLayoutPanel10.Controls.Add(new Label
		{
			Text = "个视频",
			AutoSize = true,
			Margin = new Padding(0, 7, 16, 0)
		});
		flowLayoutPanel10.Controls.Add(new Label
		{
			Text = "1 = 各自成图；超大时自动分页",
			AutoSize = true,
			Font = new Font(Font.FontFamily, 12f, FontStyle.Regular, GraphicsUnit.Pixel),
			ForeColor = Muted,
			Margin = new Padding(0, 8, 24, 0)
		});
		flowLayoutPanel10.Controls.Add(new Label
		{
			Text = "处理方式",
			AutoSize = true,
			Margin = new Padding(0, 7, 10, 0)
		});
		acceleration = new ChoiceBox
		{
			Width = 285,
			AccessibleName = "显卡加速方式",
			Margin = new Padding(0)
		};
		acceleration.Items.AddRange(new object[4] { "自动 · 优先 GPU，失败转 CPU", "NVIDIA CUDA（解码与缩放）", "兼容硬解（D3D11）", "仅使用 CPU" });
		acceleration.SelectedIndex = Math.Max(0, Math.Min(3, settings.AccelerationIndex));
		flowLayoutPanel10.Controls.Add(acceleration);
		filesPerSheet.ValueChanged += delegate
		{
			UpdateOutputHint();
			UpdateCounts();
		};
		tableLayoutPanel4.Controls.Add(flowLayoutPanel10, 0, 1);
		tableLayoutPanel4.SetColumnSpan(flowLayoutPanel10, 3);
		FlowLayoutPanel flowLayoutPanel11 = new FlowLayoutPanel();
		flowLayoutPanel11.Dock = DockStyle.Fill;
		flowLayoutPanel11.FlowDirection = FlowDirection.LeftToRight;
		flowLayoutPanel11.WrapContents = false;
		flowLayoutPanel11.Padding = new Padding(0, 7, 0, 0);
		FlowLayoutPanel flowLayoutPanel12 = flowLayoutPanel11;
		flowLayoutPanel12.Controls.Add(new Label
		{
			Text = "重复处理",
			AutoSize = true,
			Margin = new Padding(0, 6, 12, 0)
		});
		existing = new ChoiceBox
		{
			Width = 340,
			AccessibleName = "重复处理方式"
		};
		existing.Items.AddRange(new object[3] { "自动：相同设置跳过，其他保留新版", "始终保留新版", "覆盖同名拼图（原视频不改动）" });
		existing.SelectedIndex = Math.Max(0, Math.Min(2, settings.ExistingIndex));
		flowLayoutPanel12.Controls.Add(existing);
		flowLayoutPanel12.Controls.Add(new Label
		{
			Text = "默认整段均匀抽取 · 自动多路并发加速",
			AutoSize = true,
			ForeColor = Muted,
			Margin = new Padding(24, 6, 0, 0)
		});
		tableLayoutPanel4.Controls.Add(flowLayoutPanel12, 0, 2);
		tableLayoutPanel4.SetColumnSpan(flowLayoutPanel12, 3);
		TableLayoutPanel tableLayoutPanel5 = new TableLayoutPanel();
		tableLayoutPanel5.Dock = DockStyle.Fill;
		tableLayoutPanel5.ColumnCount = 3;
		tableLayoutPanel5.RowCount = 2;
		tableLayoutPanel5.Margin = new Padding(0);
		TableLayoutPanel tableLayoutPanel6 = tableLayoutPanel5;
		tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88f));
		tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
		tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel6.Controls.Add(new Label
		{
			Text = "输出位置",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft
		}, 0, 0);
		output = new TextBox
		{
			Text = settings.OutputDirectory,
			Dock = DockStyle.Fill,
			Margin = new Padding(0, 3, 8, 0)
		};
		tableLayoutPanel6.Controls.Add(output, 1, 0);
		browse = Button("浏览…", 80, primary: false, delegate
		{
			using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
			{
				Description = "选择输出文件夹（直接保存到此处）",
				ShowNewFolderButton = true
			};
			if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
			{
				output.Text = folderBrowserDialog.SelectedPath;
			}
		});
		browse.Height = 30;
		browse.Margin = new Padding(0);
		tableLayoutPanel6.Controls.Add(browse, 2, 0);
		outputHint = new Label
		{
			Text = "",
			ForeColor = Muted,
			AutoSize = true,
			Font = new Font(Font.FontFamily, 12f, FontStyle.Regular, GraphicsUnit.Pixel),
			Margin = new Padding(0, 3, 0, 0)
		};
		tableLayoutPanel6.Controls.Add(outputHint, 1, 1);
		UpdateOutputHint();
		tableLayoutPanel6.SetColumnSpan(tableLayoutPanel6.GetControlFromPosition(1, 1), 2);
		tableLayoutPanel4.Controls.Add(tableLayoutPanel6, 0, 3);
		tableLayoutPanel4.SetColumnSpan(tableLayoutPanel6, 3);
		tableLayoutPanel2.Controls.Add(tableLayoutPanel4, 0, 5);
		TableLayoutPanel tableLayoutPanel7 = new TableLayoutPanel();
		tableLayoutPanel7.Dock = DockStyle.Fill;
		tableLayoutPanel7.ColumnCount = 1;
		tableLayoutPanel7.RowCount = 3;
		tableLayoutPanel7.Margin = new Padding(0, 10, 0, 0);
		TableLayoutPanel tableLayoutPanel8 = tableLayoutPanel7;
		tableLayoutPanel8.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
		tableLayoutPanel8.RowStyles.Add(new RowStyle(SizeType.Absolute, 9f));
		tableLayoutPanel8.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		status = new Label
		{
			Text = "拖入视频或文件夹，开始整理素材。",
			Dock = DockStyle.Fill,
			ForeColor = Muted,
			AutoEllipsis = true
		};
		tableLayoutPanel8.Controls.Add(status, 0, 0);
		progress = new ProgressBar
		{
			Dock = DockStyle.Fill,
			Maximum = 1000,
			Style = ProgressBarStyle.Continuous,
			Margin = new Padding(0)
		};
		tableLayoutPanel8.Controls.Add(progress, 0, 1);
		FlowLayoutPanel flowLayoutPanel13 = new FlowLayoutPanel();
		flowLayoutPanel13.Dock = DockStyle.Fill;
		flowLayoutPanel13.FlowDirection = FlowDirection.RightToLeft;
		flowLayoutPanel13.WrapContents = false;
		flowLayoutPanel13.Padding = new Padding(0, 10, 0, 0);
		FlowLayoutPanel flowLayoutPanel14 = flowLayoutPanel13;
		start = Button("开始抽帧", 134, primary: true, delegate
		{
			StartBatch(isPreview: false, failedOnly: false);
		});
		cancel = Button("取消", 76, primary: false, delegate
		{
			if (running)
			{
				if (cancellation != null)
				{
					cancellation.Cancel();
				}
				SetStatus("正在取消并停止当前读取…", error: false);
			}
			else if (scanning)
			{
				if (scanCancellation != null)
				{
					scanCancellation.Cancel();
				}
				SetStatus("正在取消扫描…", error: false);
			}
			cancel.Enabled = false;
		});
		cancel.Enabled = false;
		preview = Button("预览首个视频", 120, primary: false, delegate
		{
			StartBatch(isPreview: true, failedOnly: false);
		});
		retry = Button("重试失败", 96, primary: false, delegate
		{
			StartBatch(isPreview: false, failedOnly: true);
		});
		retry.Enabled = false;
		openOutput = Button("打开结果文件夹", 128, primary: false, delegate
		{
			OpenOutput();
		});
		openOutput.Enabled = false;
		viewResults = Button("查看拼图", 96, primary: false, delegate
		{
			ShowPreview(results.Where((Result r) => !string.IsNullOrEmpty(r.Collage) && File.Exists(r.Collage)).ToList());
		});
		viewResults.Enabled = false;
		Button button = Button("运行日志", 92, primary: false, delegate
		{
			ShowLogs();
		});
		flowLayoutPanel14.Controls.AddRange(new Control[7] { start, cancel, preview, retry, openOutput, viewResults, button });
		tableLayoutPanel8.Controls.Add(flowLayoutPanel14, 0, 2);
		tableLayoutPanel2.Controls.Add(tableLayoutPanel8, 0, 6);
		ToolTip toolTip = new ToolTip();
		toolTip.SetToolTip(frames, "每个视频均匀抽取 1–100 帧；修改列表中的帧数可以为单个视频单独设置。");
		toolTip.SetToolTip(quality, "轻量：单帧最长边1280，拼图宽度至多2400；标准：1920/3000；高清：原分辨率/3600。");
		toolTip.SetToolTip(output, "可以手动输入本地路径或 NAS 网络路径。留空使用视频目录中的【抽帧】。");
		toolTip.SetToolTip(filesPerSheet, "选择一张大图最多拼入几个视频，范围1–100。每个视频有独立文件名和时间标注；最后不足一组也会生成。");
		toolTip.SetToolTip(acceleration, "CUDA用于显卡解码与缩放，D3D11用于兼容硬件解码。拼图和JPEG保存仍由CPU完成；不支持的素材自动回退。");
		toolTip.SetToolTip(rangeSlider, "拖动两端的圆点选择抽帧开始和结束时间；默认整段。滑块时间以当前选中视频为基准，范围按每个视频自身时间轴生效。");
		UpdateRangeReference();
	}

	private static void UpdateLayoutMinimum(Panel host, TableLayoutPanel layout)
	{
		float fixedHeight = layout.Padding.Vertical;
		foreach (RowStyle rowStyle in layout.RowStyles)
		{
			if (rowStyle.SizeType == SizeType.Absolute)
			{
				fixedHeight += rowStyle.Height;
			}
		}
		float scale = Math.Max(1f, fixedHeight / 709f);
		Size minimumSize = new Size((int)Math.Round(1000f * scale), (int)Math.Round(fixedHeight + 110f * scale));
		if (layout.MinimumSize != minimumSize)
		{
			layout.MinimumSize = minimumSize;
		}
		if (host.AutoScrollMinSize != minimumSize)
		{
			host.AutoScrollMinSize = minimumSize;
		}
	}

	private Control BuildRangePanel()
	{
		TableLayoutPanel panel = new TableLayoutPanel();
		panel.Dock = DockStyle.Fill;
		panel.BackColor = Color.White;
		panel.Padding = new Padding(18, 7, 18, 7);
		panel.ColumnCount = 1;
		panel.RowCount = 3;
		panel.Margin = new Padding(0);
		panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
		panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		TableLayoutPanel header = new TableLayoutPanel();
		header.Dock = DockStyle.Fill;
		header.ColumnCount = 3;
		header.RowCount = 1;
		header.Margin = new Padding(0);
		header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
		header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74f));
		header.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		header.Controls.Add(new Label
		{
			Text = "抽帧范围",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Font = new Font(Font, FontStyle.Bold),
			Margin = new Padding(0)
		}, 0, 0);
		rangeInfo = new Label
		{
			Text = "添加视频后会自动读取总时长",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = Muted,
			AutoEllipsis = true,
			Font = new Font(Font.FontFamily, 12f, FontStyle.Regular, GraphicsUnit.Pixel),
			Margin = new Padding(4, 0, 8, 0)
		};
		header.Controls.Add(rangeInfo, 1, 0);
		rangeReset = Button("整段", 68, primary: false, delegate
		{
			ResetRange();
		});
		rangeReset.Height = 24;
		rangeReset.Width = 68;
		rangeReset.Margin = new Padding(2, 2, 0, 0);
		rangeReset.Anchor = AnchorStyles.Right | AnchorStyles.Top;
		header.Controls.Add(rangeReset, 2, 0);
		panel.Controls.Add(header, 0, 0);
		TableLayoutPanel sliderRow = new TableLayoutPanel();
		sliderRow.Dock = DockStyle.Fill;
		sliderRow.ColumnCount = 3;
		sliderRow.RowCount = 1;
		sliderRow.Margin = new Padding(0);
		sliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
		sliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		sliderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
		sliderRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		rangeStartText = new Label
		{
			Text = "开始 —",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true,
			Margin = new Padding(0)
		};
		sliderRow.Controls.Add(rangeStartText, 0, 0);
		rangeSlider = new RangeSlider
		{
			Dock = DockStyle.Fill,
			Margin = new Padding(8, 4, 8, 4),
			AccessibleName = "抽帧范围"
		};
		rangeSlider.ValuesChanged += delegate
		{
			OnRangeChanged();
		};
		sliderRow.Controls.Add(rangeSlider, 1, 0);
		rangeEndText = new Label
		{
			Text = "结束 —",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleRight,
			AutoEllipsis = true,
			Margin = new Padding(0)
		};
		sliderRow.Controls.Add(rangeEndText, 2, 0);
		panel.Controls.Add(sliderRow, 0, 1);
		panel.Controls.Add(new Label
		{
			Text = "拖动两端选择区间 · 默认整段 · 范围按每个视频自身时间轴生效",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = Muted,
			AutoEllipsis = true,
			Font = new Font(Font.FontFamily, 12f, FontStyle.Regular, GraphicsUnit.Pixel),
			Margin = new Padding(0)
		}, 0, 2);
		return panel;
	}

	private Button Button(string text, int width, bool primary, EventHandler click)
	{
		Button button = new Button();
		button.Text = text;
		button.Width = width;
		button.Height = 36;
		button.FlatStyle = FlatStyle.Flat;
		button.BackColor = (primary ? Blue : Color.White);
		button.ForeColor = (primary ? Color.White : Ink);
		button.Cursor = Cursors.Hand;
		button.Margin = new Padding(0, 0, 10, 0);
		button.UseVisualStyleBackColor = false;
		Button button2 = button;
		button2.FlatAppearance.BorderColor = (primary ? Blue : Color.FromArgb(211, 221, 234));
		button2.FlatAppearance.BorderSize = 1;
		button2.Click += click;
		return button2;
	}

	private CheckBox Check(string text, bool value)
	{
		CheckBox checkBox = new CheckBox();
		checkBox.Text = text;
		checkBox.Checked = value;
		checkBox.AutoSize = true;
		checkBox.Margin = new Padding(0, 0, 0, 4);
		checkBox.Cursor = Cursors.Hand;
		return checkBox;
	}

	private static int ParseCount(object value)
	{
		if (!int.TryParse(Convert.ToString(value), out var count) || count < 1 || count > 100)
		{
			return 0;
		}
		return count;
	}

	private void LoadSavedRange()
	{
		string startText = (settings.RangeStart ?? "").Trim();
		string endText = (settings.RangeEnd ?? "").Trim();
		if (startText.Length == 0 || endText.Length == 0)
		{
			return;
		}
		if (!VideoEngine.TryParseTime(startText, out double start) || !VideoEngine.TryParseTime(endText, out double end))
		{
			return;
		}
		if (start < 0.0 || end <= start)
		{
			return;
		}
		rangeLimited = true;
		rangeBegin = start;
		rangeFinish = end;
	}

	private CancellationToken DurationToken
	{
		get
		{
			if (durationCancellation == null)
			{
				durationCancellation = new CancellationTokenSource();
			}
			return durationCancellation.Token;
		}
	}

	private void CancelDurationProbes()
	{
		CancellationTokenSource cancellationTokenSource = durationCancellation;
		durationCancellation = null;
		if (cancellationTokenSource != null)
		{
			cancellationTokenSource.Cancel();
		}
	}

	private async void ProbeDurations(List<Tuple<DataGridViewRow, string>> rows)
	{
		CancellationToken token = DurationToken;
		foreach (Tuple<DataGridViewRow, string> entry in rows)
		{
			if (token.IsCancellationRequested || base.IsDisposed)
			{
				return;
			}
			DataGridViewRow row = entry.Item1;
			string path = entry.Item2;
			if (!RowBelongs(row, path))
			{
				continue;
			}
			row.Cells[3].Value = "读取中…";
			try
			{
				double duration = await Task.Run(() => durationEngine.ReadDuration(path, token), token);
				if (token.IsCancellationRequested || base.IsDisposed || !RowBelongs(row, path))
				{
					continue;
				}
				RowState rowState = (RowState)row.Tag;
				rowState.Duration = duration;
				row.Cells[3].Value = VideoEngine.Stamp(duration);
				row.Cells[3].ToolTipText = "视频总时长 " + VideoEngine.Stamp(duration);
				UpdateRangeReference();
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception ex)
			{
				if (!base.IsDisposed && RowBelongs(row, path))
				{
					row.Cells[3].Value = "读取失败";
					row.Cells[3].ToolTipText = ex.Message;
					AddLog("读取时长失败：" + path + " · " + ex.Message);
				}
			}
		}
	}

	private void ResumePendingDurations()
	{
		if (base.IsDisposed)
		{
			return;
		}
		List<Tuple<DataGridViewRow, string>> list = new List<Tuple<DataGridViewRow, string>>();
		foreach (DataGridViewRow row in (IEnumerable)grid.Rows)
		{
			if (row.Tag is RowState rowState && rowState.Duration <= 0.0 && string.Equals(Convert.ToString(row.Cells[3].Value), "读取中…", StringComparison.Ordinal))
			{
				list.Add(Tuple.Create(row, rowState.Path));
			}
		}
		if (list.Count > 0)
		{
			ProbeDurations(list);
		}
	}

	private bool RowBelongs(DataGridViewRow row, string path)
	{
		return row != null && row.DataGridView == grid && row.Index >= 0 && row.Tag is RowState rowState && string.Equals(rowState.Path, path, StringComparison.OrdinalIgnoreCase);
	}

	private DataGridViewRow FindRangeRow()
	{
		if (grid == null || grid.Rows.Count == 0)
		{
			return null;
		}
		DataGridViewRow currentRow = grid.CurrentRow;
		if (HasDuration(currentRow))
		{
			return currentRow;
		}
		foreach (DataGridViewRow selectedRow in grid.SelectedRows)
		{
			if (HasDuration(selectedRow))
			{
				return selectedRow;
			}
		}
		foreach (DataGridViewRow row in (IEnumerable)grid.Rows)
		{
			if (Convert.ToBoolean(row.Cells[0].Value) && HasDuration(row))
			{
				return row;
			}
		}
		foreach (DataGridViewRow row2 in (IEnumerable)grid.Rows)
		{
			if (HasDuration(row2))
			{
				return row2;
			}
		}
		return null;
	}

	private static bool HasDuration(DataGridViewRow row)
	{
		return row != null && row.Index >= 0 && row.Tag is RowState rowState && rowState.Duration > 0.0;
	}

	private void UpdateRangeReference()
	{
		if (rangeSlider == null)
		{
			return;
		}
		DataGridViewRow row = FindRangeRow();
		double num = ((row != null && row.Tag is RowState rowState) ? rowState.Duration : 0.0);
		if (rangeLimited && num > 0.0)
		{
			if (rangeBegin >= num)
			{
				rangeLimited = false;
				rangeBegin = 0.0;
				rangeFinish = 0.0;
			}
			else
			{
				if (rangeFinish > num)
				{
					rangeFinish = num;
				}
				if (rangeFinish <= rangeBegin)
				{
					rangeLimited = false;
					rangeBegin = 0.0;
					rangeFinish = 0.0;
				}
			}
			if (rangeLimited && rangeBegin <= 0.005 && rangeFinish >= num - 0.005)
			{
				rangeLimited = false;
			}
		}
		rangeTotal = num;
		syncingRange = true;
		try
		{
			double num2 = ((num > 0.0) ? num : 1.0);
			rangeSlider.SetRange(0.0, num2, rangeLimited ? rangeBegin : 0.0, rangeLimited ? rangeFinish : num2);
			rangeSlider.Enabled = !uiBusy && num > 0.0;
			if (num > 0.0)
			{
				rangeStartText.Text = "开始 " + RangeText(rangeLimited ? rangeBegin : 0.0);
				rangeEndText.Text = "结束 " + RangeText(rangeLimited ? rangeFinish : num);
				rangeInfo.Text = "总时长 " + RangeText(num) + "  ·  基准：" + Path.GetFileName(((RowState)row.Tag).Path);
			}
			else
			{
				rangeStartText.Text = "开始 —";
				rangeEndText.Text = "结束 —";
				rangeInfo.Text = ((grid != null && grid.Rows.Count > 0) ? "正在读取视频总时长…" : "添加视频后会自动读取总时长");
			}
			rangeReset.Enabled = !uiBusy && num > 0.0 && rangeLimited;
		}
		finally
		{
			syncingRange = false;
		}
	}

	private void OnRangeChanged()
	{
		if (syncingRange || rangeTotal <= 0.0)
		{
			return;
		}
		rangeBegin = rangeSlider.StartValue;
		rangeFinish = rangeSlider.EndValue;
		rangeLimited = !(rangeBegin <= 0.005 && rangeFinish >= rangeTotal - 0.005);
		rangeStartText.Text = "开始 " + RangeText(rangeBegin);
		rangeEndText.Text = "结束 " + RangeText(rangeFinish);
		rangeReset.Enabled = !uiBusy && rangeLimited;
	}

	private void ResetRange()
	{
		rangeLimited = false;
		rangeBegin = 0.0;
		rangeFinish = 0.0;
		UpdateRangeReference();
	}

	private bool TryGetTimeRange(out double? startTime, out double? endTime)
	{
		startTime = null;
		endTime = null;
		if (!rangeLimited)
		{
			return true;
		}
		if (rangeTotal <= 0.0)
		{
			SetStatus("尚未读取到视频总时长，请稍候再试。", error: true);
			return false;
		}
		double num = rangeBegin;
		double num2 = Math.Min(rangeFinish, rangeTotal);
		if (num2 - num < 0.01)
		{
			SetStatus("抽帧区间过短，请重新拖动滑块选择。", error: true);
			return false;
		}
		startTime = num;
		endTime = num2;
		return true;
	}

	private static string RangeText(double seconds)
	{
		if (double.IsNaN(seconds) || seconds < 0.0)
		{
			seconds = 0.0;
		}
		TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
		if (timeSpan.TotalHours >= 1.0)
		{
			return timeSpan.ToString("h\\:mm\\:ss\\.ff", CultureInfo.InvariantCulture);
		}
		return timeSpan.ToString("m\\:ss\\.ff", CultureInfo.InvariantCulture);
	}

	private void ValidateCount(object sender, DataGridViewCellValidatingEventArgs e)
	{
		if (e.ColumnIndex == 4 && e.RowIndex >= 0 && ParseCount(e.FormattedValue) == 0)
		{
			e.Cancel = true;
			grid.Rows[e.RowIndex].ErrorText = "请输入 1–100 之间的整数。";
			SetStatus("帧数需要填写 1–100 之间的整数。", error: true);
		}
	}

	private void ChooseFolder()
	{
		FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
		folderBrowserDialog.Description = "选择含有视频素材的文件夹";
		folderBrowserDialog.ShowNewFolderButton = false;
		folderBrowserDialog.SelectedPath = settings.LastInputDirectory;
		using FolderBrowserDialog folderBrowserDialog2 = folderBrowserDialog;
		if (folderBrowserDialog2.ShowDialog(this) == DialogResult.OK)
		{
			settings.LastInputDirectory = folderBrowserDialog2.SelectedPath;
			AddPaths(new string[1] { folderBrowserDialog2.SelectedPath });
		}
	}

	private void ChooseVideos()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog();
		openFileDialog.Title = "选择视频素材（可以多选）";
		openFileDialog.Multiselect = true;
		openFileDialog.Filter = "视频文件|*.mp4;*.mov;*.mkv;*.avi;*.mxf;*.m4v;*.mts;*.m2ts;*.wmv;*.webm;*.mpg;*.mpeg;*.3gp;*.ts;*.flv;*.rmvb;*.rm;*.vob;*.asf;*.ogv;*.insv;*.lrv;*.f4v;*.m2t|所有文件|*.*";
		openFileDialog.InitialDirectory = settings.LastInputDirectory;
		using OpenFileDialog openFileDialog2 = openFileDialog;
		if (openFileDialog2.ShowDialog(this) == DialogResult.OK)
		{
			settings.LastInputDirectory = Path.GetDirectoryName(openFileDialog2.FileNames[0]);
			AddPaths(openFileDialog2.FileNames);
		}
	}

	private async void AddPaths(IEnumerable<string> paths)
	{
		if (running || scanning)
		{
			return;
		}
		scanning = true;
		if (scanCancellation != null)
		{
			scanCancellation.Dispose();
		}
		scanCancellation = new CancellationTokenSource();
		SetBusy(busy: true);
		SetStatus("正在扫描素材，请稍候…", error: false);
		bool recurse = recursive.Checked;
		string[] input = paths.ToArray();
		List<string> warnings = new List<string>();
		List<Tuple<DataGridViewRow, string>> newRows = new List<Tuple<DataGridViewRow, string>>();
		try
		{
			try
			{
				CancellationToken token = scanCancellation.Token;
				List<string> found = await Task.Run(delegate
				{
					List<string> list = new List<string>();
					string[] array = input;
					foreach (string text in array)
					{
						token.ThrowIfCancellationRequested();
						if (Directory.Exists(text))
						{
							list.AddRange(VideoEngine.FindVideos(text, recurse, warnings.Add, token));
						}
						else if (File.Exists(text) && VideoEngine.IsVideo(text))
						{
							list.Add(text);
						}
						else if (File.Exists(text))
						{
							warnings.Add("已跳过不支持的文件：" + text);
						}
						else
						{
							warnings.Add("路径不存在：" + text);
						}
					}
					return list.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy((string p) => p, StringComparer.OrdinalIgnoreCase).ToList();
				});
				int added = 0;
				grid.SuspendLayout();
				updating = true;
				try
				{
					foreach (string item in found)
					{
						string fullPath = Path.GetFullPath(item);
						if (knownPaths.Add(fullPath))
						{
							int index = grid.Rows.Add(true, grid.Rows.Count + 1, Path.GetFileName(fullPath), "待读取", (int)frames.Value, "待处理");
							grid.Rows[index].Tag = new RowState
							{
								Path = fullPath
							};
							grid.Rows[index].Cells[2].ToolTipText = fullPath;
							newRows.Add(Tuple.Create(grid.Rows[index], fullPath));
							added++;
						}
					}
				}
				finally
				{
					updating = false;
					grid.ResumeLayout();
				}
				foreach (string item2 in warnings)
				{
					AddLog("扫描提示：" + item2);
				}
				UpdateCounts();
				UpdateRangeReference();
				SetStatus("已添加 " + added + " 个视频；可在帧数列单独修改数量。" + ((warnings.Count > 0) ? " 部分路径被跳过或无法读取，详情见日志。" : ""), warnings.Count > 0);
				if (newRows.Count > 0)
				{
					ProbeDurations(newRows);
				}
			}
			catch (OperationCanceledException)
			{
				SetStatus("已取消扫描。", error: false);
				AddLog("扫描已取消。");
			}
			catch (Exception ex)
			{
				SetStatus("添加素材失败：" + ex.Message, error: true);
				AddLog(ex.ToString());
			}
		}
		finally
		{
			updating = false;
			scanning = false;
			if (scanCancellation != null)
			{
				scanCancellation.Dispose();
				scanCancellation = null;
			}
			SetBusy(busy: false);
			if (closeAfterScan)
			{
				closeAfterScan = false;
				Close();
			}
		}
	}

	private void RemoveSelected()
	{
		CommitGrid();
		foreach (DataGridViewRow selectedRow in grid.SelectedRows)
		{
			knownPaths.Remove(((RowState)selectedRow.Tag).Path);
			grid.Rows.Remove(selectedRow);
		}
		for (int i = 0; i < grid.Rows.Count; i++)
		{
			grid.Rows[i].Cells[1].Value = i + 1;
		}
		if (grid.Rows.Count == 0)
		{
			CancelDurationProbes();
			ResetRange();
		}
		else
		{
			UpdateRangeReference();
		}
		UpdateCounts();
	}

	private void OnDragEnter(object sender, DragEventArgs e)
	{
		if (!running && !scanning && e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			e.Effect = DragDropEffects.Copy;
		}
	}

	private void OnDragDrop(object sender, DragEventArgs e)
	{
		if (!running && !scanning)
		{
			AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));
		}
	}

	private void UpdateCounts()
	{
		if (counts == null || grid == null || updating)
		{
			return;
		}
		int num = 0;
		int num2 = 0;
		foreach (DataGridViewRow item in (IEnumerable)grid.Rows)
		{
			if (Convert.ToBoolean(item.Cells[0].Value))
			{
				if (int.TryParse(Convert.ToString(item.Cells[4].Value), out var result))
				{
					num2 += result;
				}
				num++;
			}
		}
		int num3 = ((filesPerSheet == null) ? 1 : ((int)filesPerSheet.Value));
		counts.Text = grid.Rows.Count + " 个视频  ·  已勾选 " + num + " 个  ·  预计抽取 " + num2 + " 帧  ·  约 " + (num + num3 - 1) / num3 + " 张拼图（超大时分页）     帧数列可单独编辑。";
	}

	private void UpdateOutputHint()
	{
		if (outputHint != null)
		{
			outputHint.Text = ((filesPerSheet.Value > 1m) ? "合拼模式留空：保存到首个视频目录的【抽帧】；按文件分区显示。" : "留空时保存到原视频目录的【抽帧】文件夹；文件名始终显示在拼图顶部。");
		}
	}

	private async void StartBatch(bool isPreview, bool failedOnly)
	{
		if (running || scanning || !grid.EndEdit())
		{
			return;
		}
		List<VideoJob> jobs = new List<VideoJob>();
		int invalidRow = -1;
		foreach (DataGridViewRow item2 in (IEnumerable)grid.Rows)
		{
			RowState rowState = (RowState)item2.Tag;
			bool isFailed = rowState.Result != null && rowState.Result.Status == "failed";
			if (!Convert.ToBoolean(item2.Cells[0].Value) || (failedOnly && !isFailed))
			{
				continue;
			}
			int count = ParseCount(item2.Cells[4].Value);
			if (count == 0)
			{
				invalidRow = item2.Index;
				break;
			}
			jobs.Add(new VideoJob
			{
				Path = rowState.Path,
				FrameCount = count,
				Row = item2.Index
			});
			if (isPreview)
			{
				break;
			}
		}
		if (invalidRow >= 0)
		{
			SetStatus("第 " + (invalidRow + 1) + " 行的帧数无效，已恢复默认值，请重新确认。", error: true);
			DataGridViewRow dataGridViewRow = grid.Rows[invalidRow];
			dataGridViewRow.Cells[4].Value = (int)frames.Value;
			if (dataGridViewRow.Tag is RowState rowState3)
			{
				rowState3.CustomCount = false;
			}
			grid.CurrentCell = dataGridViewRow.Cells[4];
			return;
		}
		if (jobs.Count == 0)
		{
			SetStatus(failedOnly ? "没有已勾选的失败视频。" : "请先添加并勾选需要处理的视频。", error: true);
			return;
		}
		if (!TryGetTimeRange(out double? startTime, out double? endTime))
		{
			return;
		}
		if (startTime.HasValue)
		{
			foreach (VideoJob job in jobs)
			{
				DataGridViewRow dataGridViewRow6 = grid.Rows[job.Row];
				if (dataGridViewRow6.Tag is RowState rowState4 && rowState4.Duration > 0.0 && rowState4.Duration <= startTime.Value)
				{
					SetStatus("第 " + (job.Row + 1) + " 行《" + Path.GetFileName(rowState4.Path) + "》时长 " + RangeText(rowState4.Duration) + "，短于开始时间 " + RangeText(startTime.Value) + "；请调整滑块或取消勾选该视频。", error: true);
					return;
				}
			}
		}
		JobOptions options = new JobOptions
		{
			OutputDirectory = output.Text.Trim(),
			Preset = (new string[3] { "light", "standard", "high" })[quality.SelectedIndex],
			Existing = (new string[3] { "auto", "version", "replace" })[existing.SelectedIndex],
			SaveFrames = saveFrames.Checked,
			ShowTimes = showTimes.Checked,
			ShowInfo = showInfo.Checked,
			FilesPerSheet = (int)filesPerSheet.Value,
			Acceleration = (new string[4] { "auto", "cuda", "d3d11va", "cpu" })[acceleration.SelectedIndex],
			StartTime = startTime,
			EndTime = endTime
		};
		if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
		{
			try
			{
				options.OutputDirectory = Path.GetFullPath(options.OutputDirectory);
			}
			catch
			{
				SetStatus("输出路径无效，请重新选择。", error: true);
				return;
			}
		}
		if (options.Existing == "replace" && !isPreview && MessageBox.Show(this, "将覆盖输出目录内同名的拼图图片。原视频不会改动。继续处理？", "覆盖已有拼图", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
		{
			return;
		}
		SaveSettings();
		string previewRoot = null;
		bool keepPreview = false;
		if (isPreview)
		{
			if (lastPreviewRoot != null)
			{
				CleanupPreview(lastPreviewRoot);
				lastPreviewRoot = null;
			}
			previewRoot = Path.Combine(Path.GetTempPath(), "VideoFrameHelperPreview", Guid.NewGuid().ToString("N"));
			options.OutputDirectory = previewRoot;
			options.SaveFrames = false;
			options.Existing = "version";
			options.FilesPerSheet = 1;
		}
		CancelDurationProbes();
		running = true;
		cancellation = new CancellationTokenSource();
		SetBusy(busy: true);
		cancel.Enabled = true;
		progress.Value = 0;
		start.Text = "正在处理…";
		if (!isPreview)
		{
			foreach (VideoJob item3 in jobs)
			{
				grid.Rows[item3.Row].Cells[5].Value = "等待处理";
				((RowState)grid.Rows[item3.Row].Tag).Result = null;
			}
		}
		SetStatus(isPreview ? "正在生成首个视频的拼图预览…" : "正在初始化内置视频组件…", error: false);
		try
		{
			int num = default(int);
			_ = num;
			_ = 0;
			try
			{
				Progress<ProgressUpdate> reporter = new Progress<ProgressUpdate>(delegate(ProgressUpdate update)
				{
					if (!base.IsDisposed)
					{
						progress.Value = Math.Min(1000, Math.Max(0, (int)(1000.0 * ((double)update.Completed + update.Fraction) / (double)Math.Max(1, update.Total))));
						if (!isPreview && update.Row >= 0 && update.Row < grid.Rows.Count)
						{
							DataGridViewRow dataGridViewRow2 = grid.Rows[update.Row];
							dataGridViewRow2.Cells[5].Value = update.Status;
							if (update.Result != null)
							{
								RowState rowState5 = (RowState)dataGridViewRow2.Tag;
								rowState5.Result = update.Result;
								if (update.Result.Duration > 0.0)
								{
									rowState5.Duration = update.Result.Duration;
								}
								dataGridViewRow2.Cells[3].Value = ((update.Result.Duration > 0.0) ? VideoEngine.Stamp(update.Result.Duration) : "—");
								dataGridViewRow2.Cells[5].ToolTipText = update.Result.Message;
								dataGridViewRow2.Cells[5].Style.ForeColor = ((update.Result.Status == "failed") ? Color.FromArgb(190, 50, 50) : Color.FromArgb(18, 115, 87));
								UpdateRangeReference();
							}
						}
						SetStatus((isPreview ? "预览 · " : (update.Completed + " / " + update.Total + " · ")) + update.Status + " · " + update.Message, update.Result != null && update.Result.Status == "failed");
						if (update.Result != null || update.Row < 0)
						{
							AddLog(update.Status + "：" + update.Message + ((update.Result == null) ? "" : (" " + update.Result.Decoder + " " + update.Result.Message)));
						}
					}
				});
				BatchSummary summary = await Task.Run(() => new VideoEngine().Run(jobs, options, cancellation.Token, ((IProgress<ProgressUpdate>)reporter).Report));
				if (isPreview)
				{
					List<Result> list = summary.Results.Where((Result r) => r.Status != "failed").ToList();
					if (list.Count > 0)
					{
						keepPreview = true;
						SetStatus("预览已生成。关闭预览后可开始批量处理。", error: false);
						ShowPreview(list);
					}
					else
					{
						Result previewFailure = summary.Results.FirstOrDefault((Result r) => r.Status == "failed");
						SetStatus(summary.Cancelled ? "预览已取消。" : ("预览失败：" + ((previewFailure != null && !string.IsNullOrEmpty(previewFailure.Message)) ? previewFailure.Message : "请查看运行日志。")), !summary.Cancelled);
					}
					return;
				}
				foreach (Result item in summary.Results)
				{
					List<Result> list2 = results;
					Predicate<Result> match = (Result r) => string.Equals(r.Source, item.Source, StringComparison.OrdinalIgnoreCase);
					list2.RemoveAll(match);
					results.Add(item);
				}
				double num2 = (double)summary.Results.Where((Result r) => r.Status == "success").Sum((Result r) => r.Bytes) / 1024.0 / 1024.0;
				if (!summary.Cancelled)
				{
					progress.Value = 1000;
				}
				int num3 = summary.Results.Count((Result r) => r.Status == "success" && r.GpuFrames > 0);
				SetStatus((summary.Cancelled ? "已取消。" : "处理完成。") + "成功 " + summary.Success + "，跳过 " + summary.Skipped + "，失败 " + summary.Failed + "；拼图 " + summary.Sheets.Count + " 张，GPU处理 " + num3 + " 个视频" + ((summary.Success > 0) ? ("；新图片约 " + num2.ToString("0.0") + " MB。") : "。"), summary.Failed > 0);
				AddLog(status.Text);
			}
			catch (Exception ex)
			{
				SetStatus("处理失败：" + ex.Message, error: true);
				AddLog(ex.ToString());
			}
		}
		finally
		{
			cancellation.Dispose();
			cancellation = null;
			running = false;
			start.Text = "开始抽帧";
			SetBusy(busy: false);
			ResumePendingDurations();
			if (previewRoot != null)
			{
				if (keepPreview)
				{
					lastPreviewRoot = previewRoot;
				}
				else
				{
					CleanupPreview(previewRoot);
				}
			}
		}
	}

	private static void CleanupStalePreviews()
	{
		try
		{
			string path = Path.Combine(Path.GetTempPath(), "VideoFrameHelperPreview");
			if (!Directory.Exists(path))
			{
				return;
			}
			DateTime cutoff = DateTime.UtcNow.AddDays(-1.0);
			foreach (string directory in Directory.GetDirectories(path))
			{
				try
				{
					if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
					{
						CleanupPreview(directory);
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	private static void CleanupPreview(string root)
	{
		if (!Guid.TryParseExact(Path.GetFileName(root), "N", out var _) || !string.Equals(Path.GetDirectoryName(root), Path.Combine(Path.GetTempPath(), "VideoFrameHelperPreview"), StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		try
		{
			string[] directories = Directory.GetDirectories(root);
			foreach (string path in directories)
			{
				string[] files = Directory.GetFiles(path);
				foreach (string path2 in files)
				{
					File.Delete(path2);
				}
				Directory.Delete(path);
			}
			string[] files2 = Directory.GetFiles(root);
			foreach (string path3 in files2)
			{
				File.Delete(path3);
			}
			Directory.Delete(root);
		}
		catch
		{
		}
	}

	private void CommitGrid()
	{
		if (grid == null || !grid.IsCurrentCellInEditMode)
		{
			return;
		}
		if (!grid.EndEdit())
		{
			grid.CancelEdit();
		}
	}

	private void SetBusy(bool busy)
	{
		CommitGrid();
		uiBusy = busy;
		Control[] array = new Control[19]
		{
			addFolder, addFiles, clear, remove, recursive, frames, filesPerSheet, acceleration, quality, existing,
			saveFrames, showTimes, showInfo, output, rangeSlider, rangeReset, browse, start, preview
		};
		foreach (Control control in array)
		{
			if (control != null)
			{
				control.Enabled = !busy;
			}
		}
		if (grid != null)
		{
			try
			{
				grid.ReadOnly = busy;
			}
			catch (InvalidOperationException)
			{
				grid.CancelEdit();
				try
				{
					grid.ReadOnly = busy;
				}
				catch (InvalidOperationException ex)
				{
					AddLog("表格编辑状态未能切换：" + ex.Message);
				}
			}
		}
		if (cancel != null)
		{
			cancel.Enabled = busy && (running || scanning);
		}
		if (retry != null)
		{
			retry.Enabled = !busy && results.Any((Result r) => r.Status == "failed");
		}
		bool flag = results.Any((Result r) => !string.IsNullOrEmpty(r.Collage) && File.Exists(r.Collage));
		if (openOutput != null)
		{
			openOutput.Enabled = !busy && flag;
		}
		if (viewResults != null)
		{
			viewResults.Enabled = !busy && flag;
		}
		UpdateRangeReference();
	}

	private void SetStatus(string text, bool error)
	{
		if (status != null)
		{
			status.Text = text;
			status.ForeColor = (error ? Color.FromArgb(181, 62, 53) : Muted);
		}
	}

	private void AddLog(string line)
	{
		logs.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
		if (logs.Count > 2000)
		{
			logs.RemoveRange(0, logs.Count - 2000);
		}
	}

	private void OpenOutput()
	{
		List<string> list = (from r in results
			where !string.IsNullOrEmpty(r.Collage) && File.Exists(r.Collage)
			select Path.GetDirectoryName(r.Collage)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		if (list.Count == 1)
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo(list[0]);
			processStartInfo.UseShellExecute = true;
			Process.Start(processStartInfo);
			return;
		}
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
		foreach (string item in list)
		{
			string path = item;
			contextMenuStrip.Items.Add(path, null, delegate
			{
				Process.Start(new ProcessStartInfo(path)
				{
					UseShellExecute = true
				});
			});
		}
		contextMenuStrip.Show(openOutput, new Point(0, openOutput.Height));
	}

	private void ShowLogs()
	{
		Form form = new Form();
		form.Text = "运行日志";
		form.StartPosition = FormStartPosition.CenterParent;
		form.Size = new Size(940, 540);
		form.Font = Font;
		using Form form2 = form;
		form2.Controls.Add(new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Both,
			WordWrap = false,
			Text = ((logs.Count == 0) ? "还没有运行记录。" : string.Join(Environment.NewLine, logs)),
			BackColor = Color.White
		});
		form2.ShowDialog(this);
	}

	private void ShowHelp()
	{
		MessageBox.Show(this, "1. 添加视频或文件夹，也可拖入窗口；软件会自动读取每个视频的总时长。\n\n2. 选择每个视频抽取 1–100 帧。直接编辑表格的【帧数】，可为单个视频设置数量。\n\n3. 【抽帧范围】默认整段。拖动滑块两端的圆点选择开始和结束时间；点【整段】可恢复全片。滑块时间以当前选中视频为基准，范围按每个视频自身时间轴生效。\n\n4. 设置【每张图最多】包含几个视频。1表示各自成图，2–100表示按文件分区合拼；超大时自动分页。\n\n5. 处理方式默认优先GPU。CUDA用于解码和缩放，不支持时自动转用其他方式；拼图与JPEG保存仍由CPU完成。抽帧会自动多路并发（最多3路），无需手动设置；1080p素材在核心较多的电脑上，【仅使用CPU】有时更快。\n\n6. 默认只保存轻量拼图；可勾选同时保存单帧。合拼时输出留空，会保存到首个视频目录下的【抽帧】。\n\n7. 可以先预览首个勾选视频的拼图，再开始批量处理。相同设置会自动跳过，修改设置则保留新版。\n\n视频仅在本机处理，原视频不会被修改。", "视频抽帧助手 · 使用说明", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void ShowPreview(List<Result> items)
	{
		items = (from g in items.GroupBy((Result r) => r.Collage, StringComparer.OrdinalIgnoreCase)
			select g.First()).ToList();
		if (items.Count == 0)
		{
			return;
		}
		Form dialog = new Form
		{
			Text = "拼图预览",
			StartPosition = FormStartPosition.CenterParent,
			Size = new Size(1100, 760),
			MinimumSize = new Size(600, 450),
			Font = Font,
			BackColor = Color.FromArgb(231, 236, 243)
		};
		try
		{
			PictureBox picture = new PictureBox
			{
				Dock = DockStyle.Fill,
				SizeMode = PictureBoxSizeMode.Zoom,
				BackColor = Color.FromArgb(231, 236, 243)
			};
			try
			{
				int index = 0;
				FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel();
				flowLayoutPanel.Dock = DockStyle.Bottom;
				flowLayoutPanel.Height = 58;
				flowLayoutPanel.Padding = new Padding(12);
				flowLayoutPanel.BackColor = Color.White;
				flowLayoutPanel.WrapContents = false;
				FlowLayoutPanel flowLayoutPanel2 = flowLayoutPanel;
				Label position = new Label
				{
					Width = 550,
					Height = 34,
					TextAlign = ContentAlignment.MiddleLeft,
					AutoEllipsis = true
				};
				Action refresh = delegate
				{
					if (picture.Image != null)
					{
						Image image = picture.Image;
						picture.Image = null;
						image.Dispose();
					}
					picture.Image = Image.FromFile(items[index].Collage);
					string text = ((items[index].SheetVideoCount > 1) ? ("合拼 " + items[index].SheetVideoCount + " 个视频 · " + Path.GetFileName(items[index].Collage)) : Path.GetFileName(items[index].Source));
					position.Text = index + 1 + " / " + items.Count + "   " + text;
					dialog.Text = "拼图预览 · " + text;
				};
				flowLayoutPanel2.Controls.Add(Button("上一张", 88, primary: false, delegate
				{
					index = (index + items.Count - 1) % items.Count;
					refresh();
				}));
				flowLayoutPanel2.Controls.Add(Button("下一张", 88, primary: false, delegate
				{
					index = (index + 1) % items.Count;
					refresh();
				}));
				flowLayoutPanel2.Controls.Add(Button("原尺寸打开", 112, primary: false, delegate
				{
					Process.Start(new ProcessStartInfo(items[index].Collage)
					{
						UseShellExecute = true
					});
				}));
				flowLayoutPanel2.Controls.Add(position);
				dialog.Controls.Add(picture);
				dialog.Controls.Add(flowLayoutPanel2);
				refresh();
				try
				{
					dialog.ShowDialog(this);
				}
				finally
				{
					if (picture.Image != null)
					{
						picture.Image.Dispose();
						picture.Image = null;
					}
				}
			}
			finally
			{
				if (picture != null)
				{
					((IDisposable)picture).Dispose();
				}
			}
		}
		finally
		{
			if (dialog != null)
			{
				((IDisposable)dialog).Dispose();
			}
		}
	}

	private SavedSettings LoadSettings()
	{
		try
		{
			if (File.Exists(settingsPath))
			{
				return new JavaScriptSerializer().Deserialize<SavedSettings>(File.ReadAllText(settingsPath)) ?? new SavedSettings();
			}
		}
		catch
		{
		}
		return new SavedSettings();
	}

	private void SaveSettings()
	{
		settings.FrameCount = (int)frames.Value;
		settings.PresetIndex = quality.SelectedIndex;
		settings.ExistingIndex = existing.SelectedIndex;
		settings.FilesPerSheet = (int)filesPerSheet.Value;
		settings.AccelerationIndex = acceleration.SelectedIndex;
		settings.SaveFrames = saveFrames.Checked;
		settings.Recursive = recursive.Checked;
		settings.ShowTimes = showTimes.Checked;
		settings.ShowInfo = showInfo.Checked;
		settings.OutputDirectory = output.Text.Trim();
		settings.RangeStart = (rangeLimited ? rangeBegin.ToString("0.###", CultureInfo.InvariantCulture) : "");
		settings.RangeEnd = (rangeLimited ? rangeFinish.ToString("0.###", CultureInfo.InvariantCulture) : "");
		try
		{
			VideoEngine.WriteJson(settingsPath, settings);
		}
		catch (Exception ex)
		{
			AddLog("设置保存失败：" + ex.Message);
		}
	}

	private void OnClosing(object sender, FormClosingEventArgs e)
	{
		CancelDurationProbes();
		if (running || scanning)
		{
			e.Cancel = true;
			if (running)
			{
				if (cancellation != null)
				{
					cancellation.Cancel();
				}
				SetStatus("正在停止处理，请稍后关闭窗口。", error: false);
			}
			else if (scanning)
			{
				if (scanCancellation != null)
				{
					scanCancellation.Cancel();
				}
				closeAfterScan = true;
				SetStatus("正在停止扫描，请稍后关闭窗口。", error: false);
			}
		}
		else
		{
			if (lastPreviewRoot != null)
			{
				CleanupPreview(lastPreviewRoot);
				lastPreviewRoot = null;
			}
			SaveSettings();
		}
	}
}
