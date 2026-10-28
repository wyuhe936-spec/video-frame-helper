using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace FrameHelper;

internal static class Program
{
	private sealed class BatchArgumentException : Exception
	{
		public BatchArgumentException(string message)
			: base(message)
		{
		}
	}

	private const int AttachParentProcess = -1;

	private const string BatchUsage = "用法：\r\n  视频抽帧助手.exe --batch --input <视频或文件夹> [--input ...] [选项]\r\n\r\n选项：\r\n  --output <文件夹>              输出目录；留空保存到视频旁的【抽帧】\r\n  --frames <1-100>              每个视频抽取帧数（默认 5）\r\n  --start <时间>                抽帧开始时间（秒 / 分:秒 / 时:分:秒）\r\n  --end <时间>                  抽帧结束时间（默认到视频结尾）\r\n  --files-per-sheet <1-100>     每张拼图包含的视频数（默认 1）\r\n  --preset light|standard|high  图片清晰度（默认 light）\r\n  --existing auto|version|replace  重复处理方式（默认 auto）\r\n  --acceleration auto|cuda|d3d11va|cpu  加速方式（默认 auto）\r\n  --recursive                   扫描子文件夹\r\n  --save-frames                 同时保存单帧图片\r\n  --no-times                    不在拼图标注时间点\r\n  --no-info                     不显示视频信息\r\n  --report <json路径>           写入批次报告\r\n  --help                        显示本帮助";

	private static bool consolePrepared;

	[DllImport("user32.dll")]
	private static extern bool SetProcessDPIAware();

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool AttachConsole(int processId);

	[STAThread]
	private static int Main(string[] args)
	{
		if (Array.IndexOf(args, "--batch") >= 0)
		{
			return RunBatch(args);
		}
		try
		{
			SetProcessDPIAware();
		}
		catch
		{
		}
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
		{
			string text = WriteError(e.Exception);
			MessageBox.Show(e.Exception.Message + ((text == null) ? "" : ("\r\n\r\n错误详情已保存到：\r\n" + text)), "视频抽帧助手", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
		{
			WriteError(e.ExceptionObject as Exception);
		};
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		int num = Array.IndexOf(args, "--ui-snapshot");
		if (num >= 0 && num + 1 < args.Length)
		{
			using (MainForm mainForm = new MainForm())
			{
				mainForm.Show();
				Application.DoEvents();
				using (Bitmap bitmap = new Bitmap(mainForm.Width, mainForm.Height))
				{
					mainForm.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
					bitmap.Save(args[num + 1], ImageFormat.Png);
				}
				mainForm.Close();
			}
			return 0;
		}
		Application.Run(new MainForm());
		return 0;
	}

	private static string WriteError(Exception error)
	{
		try
		{
			Directory.CreateDirectory(VideoEngine.AppDirectory);
			string text = Path.Combine(VideoEngine.AppDirectory, "error.log");
			File.AppendAllText(text, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  v1.2.2\r\n" + Convert.ToString(error) + "\r\n\r\n");
			return text;
		}
		catch
		{
			return null;
		}
	}

	private static void PrepareConsole()
	{
		if (consolePrepared)
		{
			return;
		}
		consolePrepared = true;
		try
		{
			AttachConsole(AttachParentProcess);
		}
		catch
		{
		}
	}

	private static void WriteLine(string text)
	{
		PrepareConsole();
		try
		{
			Console.Out.WriteLine(text);
			Console.Out.Flush();
		}
		catch
		{
		}
	}

	private static void WriteErrorLine(string text)
	{
		PrepareConsole();
		try
		{
			Console.Error.WriteLine(text);
			Console.Error.Flush();
		}
		catch
		{
		}
	}

	private static string RequireValue(string[] args, ref int index, string name)
	{
		if (index + 1 >= args.Length)
		{
			throw new BatchArgumentException("参数 " + name + " 缺少取值。");
		}
		return args[++index];
	}

	private static int RequireRange(string[] args, ref int index, string name, int min, int max)
	{
		string text = RequireValue(args, ref index, name);
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || result < min || result > max)
		{
			throw new BatchArgumentException(name + " 需要 " + min + "–" + max + " 之间的整数，收到：" + text);
		}
		return result;
	}

	private static string RequireChoice(string[] args, ref int index, string name, params string[] allowed)
	{
		string text = RequireValue(args, ref index, name);
		if (!allowed.Contains(text))
		{
			throw new BatchArgumentException(name + " 只支持 " + string.Join(" / ", allowed) + "，收到：" + text);
		}
		return text;
	}

	private static double RequireTime(string[] args, ref int index, string name)
	{
		string text = RequireValue(args, ref index, name);
		if (!VideoEngine.TryParseTime(text, out double result))
		{
			throw new BatchArgumentException(name + " 需要秒、分:秒或时:分:秒（例如 30、1:30、01:02:03），收到：" + text);
		}
		return result;
	}

	private static int RunBatch(string[] args)
	{
		PrepareConsole();
		string report = null;
		try
		{
			JobOptions options = new JobOptions();
			List<string> inputs = new List<string>();
			int frameCount = 5;
			bool recursive = false;
			for (int i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
				case "--batch":
					break;
				case "--input":
					inputs.Add(RequireValue(args, ref i, "--input"));
					break;
				case "--output":
					options.OutputDirectory = RequireValue(args, ref i, "--output");
					break;
				case "--frames":
					frameCount = RequireRange(args, ref i, "--frames", 1, 100);
					break;
				case "--start":
					options.StartTime = RequireTime(args, ref i, "--start");
					break;
				case "--end":
					options.EndTime = RequireTime(args, ref i, "--end");
					break;
				case "--files-per-sheet":
					options.FilesPerSheet = RequireRange(args, ref i, "--files-per-sheet", 1, 100);
					break;
				case "--preset":
					options.Preset = RequireChoice(args, ref i, "--preset", "light", "standard", "high");
					break;
				case "--existing":
					options.Existing = RequireChoice(args, ref i, "--existing", "auto", "version", "replace");
					break;
				case "--acceleration":
					options.Acceleration = RequireChoice(args, ref i, "--acceleration", "auto", "cuda", "d3d11va", "cpu");
					break;
				case "--report":
					report = RequireValue(args, ref i, "--report");
					break;
				case "--recursive":
					recursive = true;
					break;
				case "--save-frames":
					options.SaveFrames = true;
					break;
				case "--no-times":
					options.ShowTimes = false;
					break;
				case "--no-info":
					options.ShowInfo = false;
					break;
				case "--help":
				case "-h":
					WriteLine(BatchUsage);
					return 0;
				default:
					throw new BatchArgumentException("未知参数：" + args[i]);
				}
			}
			if (options.EndTime.HasValue && options.EndTime.Value <= 0.0)
			{
				throw new BatchArgumentException("--end 必须大于 0。");
			}
			if (options.StartTime.HasValue && options.EndTime.HasValue && options.EndTime.Value <= options.StartTime.Value)
			{
				throw new BatchArgumentException("--end 必须大于 --start。");
			}
			if (inputs.Count == 0)
			{
				throw new BatchArgumentException("至少需要一个 --input 视频或文件夹。");
			}
			List<VideoJob> list = new List<VideoJob>();
			HashSet<string> knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string input in inputs)
			{
				if (Directory.Exists(input))
				{
					foreach (string item in VideoEngine.FindVideos(input, recursive, delegate(string warning)
					{
						WriteErrorLine("扫描提示：" + warning);
					}))
					{
						string fullPath = Path.GetFullPath(item);
						if (knownPaths.Add(fullPath))
						{
							list.Add(new VideoJob
							{
								Path = fullPath,
								FrameCount = frameCount,
								Row = list.Count
							});
						}
					}
				}
				else
				{
					if (!File.Exists(input))
					{
						throw new BatchArgumentException("输入不存在：" + input);
					}
					string fullPath2 = Path.GetFullPath(input);
					if (knownPaths.Add(fullPath2))
					{
						list.Add(new VideoJob
						{
							Path = fullPath2,
							FrameCount = frameCount,
							Row = list.Count
						});
					}
				}
			}
			if (list.Count == 0)
			{
				throw new BatchArgumentException("没有找到待处理视频。");
			}
			WriteLine("视频抽帧助手 · 批处理");
			WriteLine("待处理视频：" + list.Count + " 个" + (string.IsNullOrWhiteSpace(options.OutputDirectory) ? "" : ("；输出目录：" + options.OutputDirectory)));
			Action<ProgressUpdate> progress = delegate(ProgressUpdate update)
			{
				if (update.Result != null)
				{
					WriteLine(Path.GetFileName(update.Result.Source) + " · " + update.Status + (string.IsNullOrEmpty(update.Message) ? "" : (" · " + update.Message)));
				}
				else if (update.Row < 0 && !string.IsNullOrEmpty(update.Status))
				{
					WriteLine(update.Status + (string.IsNullOrEmpty(update.Message) ? "" : ("：" + update.Message)));
				}
			};
			BatchSummary batchSummary = new VideoEngine().Run(list, options, CancellationToken.None, progress);
			WriteLine("");
			WriteLine("处理结束：" + (batchSummary.Cancelled ? "已取消" : "完成") + "；成功 " + batchSummary.Success + "，跳过 " + batchSummary.Skipped + "，失败 " + batchSummary.Failed + "，拼图 " + batchSummary.Sheets.Count + " 张。");
			foreach (SheetResult sheet in batchSummary.Sheets)
			{
				WriteLine("拼图：" + sheet.Path);
			}
			foreach (Result result in batchSummary.Results.Where((Result r) => r.Status == "failed"))
			{
				WriteErrorLine("失败：" + result.Source + " · " + result.Message);
			}
			TryWriteReport(report, batchSummary);
			return (batchSummary.Failed > 0) ? 1 : 0;
		}
		catch (BatchArgumentException ex)
		{
			WriteErrorLine("参数错误：" + ex.Message);
			WriteErrorLine("");
			WriteErrorLine(BatchUsage);
			TryWriteReport(report, new
			{
				error = ex.ToString()
			});
			return 2;
		}
		catch (Exception ex2)
		{
			WriteErrorLine("处理失败：" + ex2.Message);
			string text = WriteError(ex2);
			if (text != null)
			{
				WriteErrorLine("错误详情已保存到：" + text);
			}
			TryWriteReport(report, new
			{
				error = ex2.ToString()
			});
			return 2;
		}
	}

	private static void TryWriteReport(string path, object value)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		try
		{
			VideoEngine.WriteJson(path, value);
			WriteLine("报告已写入：" + Path.GetFullPath(path));
		}
		catch (Exception ex)
		{
			WriteErrorLine("报告写入失败：" + ex.Message);
		}
	}
}
