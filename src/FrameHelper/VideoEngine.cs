using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FrameHelper;

public sealed class VideoEngine
{
	private sealed class DecoderSession
	{
		public string Active;

		public string Evidence = "";

		public int GpuFrames;

		public int CpuFrames;

		public readonly HashSet<string> Rejected = new HashSet<string>();

		public readonly HashSet<string> Used = new HashSet<string>();

		public readonly List<string> Failures = new List<string>();
	}

	private sealed class SheetLayout
	{
		public int Width;

		public int Height;

		public int Header;

		public int Gap;

		public int CellWidth;

		public int Columns;

		public List<int> CardHeights = new List<int>();

		public List<int> RowHeights = new List<int>();
	}

	public static readonly string AppDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoFrameHelper");

	private static readonly string[] Extensions = new string[24]
	{
		".mp4", ".mov", ".mkv", ".avi", ".mxf", ".m4v", ".mts", ".m2ts", ".wmv", ".webm",
		".mpg", ".mpeg", ".3gp", ".ts", ".flv", ".rmvb", ".rm", ".vob", ".asf", ".ogv",
		".insv", ".lrv", ".f4v", ".m2t"
	};

	private string ffmpeg;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool MoveFileEx(string existingName, string newName, int flags);

	public static bool IsVideo(string path)
	{
		return Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());
	}

	public double ReadDuration(string path, CancellationToken cancel)
	{
		if (ffmpeg == null)
		{
			lock (this)
			{
				if (ffmpeg == null)
				{
					ffmpeg = EnsureFfmpeg();
				}
			}
		}
		return Probe(path, cancel).Duration;
	}

	public static List<string> FindVideos(string folder, bool recursive, Action<string> warning, CancellationToken cancel = default(CancellationToken))
	{
		List<string> list = new List<string>();
		Queue<string> queue = new Queue<string>();
		queue.Enqueue(folder);
		while (queue.Count > 0)
		{
			cancel.ThrowIfCancellationRequested();
			string text = queue.Dequeue();
			try
			{
				list.AddRange(Directory.GetFiles(text).Where(IsVideo));
				cancel.ThrowIfCancellationRequested();
				if (!recursive)
				{
					continue;
				}
				string[] directories = Directory.GetDirectories(text);
				foreach (string text2 in directories)
				{
					if ((File.GetAttributes(text2) & FileAttributes.ReparsePoint) == 0 && Path.GetFileName(text2) != "抽帧")
					{
						queue.Enqueue(text2);
					}
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				warning?.Invoke(text + "：" + ex.Message);
			}
		}
		list.Sort(StringComparer.OrdinalIgnoreCase);
		return list;
	}

	public BatchSummary Run(List<VideoJob> jobs, JobOptions options, CancellationToken cancel, Action<ProgressUpdate> progress)
	{
		if (options.FilesPerSheet < 1 || options.FilesPerSheet > 100)
		{
			throw new Exception("每张图包含的视频数量范围是 1–100。");
		}
		if (!new string[4] { "auto", "cuda", "d3d11va", "cpu" }.Contains(options.Acceleration))
		{
			throw new Exception("不支持的加速方式。");
		}
		if (options.StartTime.HasValue && options.StartTime.Value < 0.0)
		{
			throw new Exception("开始时间不能为负数。");
		}
		if (options.EndTime.HasValue && options.EndTime.Value <= 0.0)
		{
			throw new Exception("结束时间必须大于 0。");
		}
		if (options.StartTime.HasValue && options.EndTime.HasValue && options.EndTime.Value <= options.StartTime.Value)
		{
			throw new Exception("结束时间必须大于开始时间。");
		}
		if (options.FilesPerSheet > 1)
		{
			return RunGrouped(jobs, options, cancel, progress);
		}
		BatchSummary batchSummary = new BatchSummary();
		ffmpeg = EnsureFfmpeg();
		List<string> reportRoots = ReportRoots(jobs, options);
		for (int i = 0; i < jobs.Count; i++)
		{
			VideoJob job = jobs[i];
			if (cancel.IsCancellationRequested)
			{
				batchSummary.Cancelled = true;
				break;
			}
			int complete = i;
			try
			{
				Emit(progress, job.Row, i, jobs.Count, 0.0, "读取视频", Path.GetFileName(job.Path), null);
				Result result = Process(job, options, cancel, delegate(double f, string status)
				{
					Emit(progress, job.Row, complete, jobs.Count, f, status, Path.GetFileName(job.Path), null);
				});
				batchSummary.Results.Add(result);
				if (result.Status == "skipped")
				{
					batchSummary.Skipped++;
				}
				else
				{
					batchSummary.Success++;
				}
				batchSummary.Sheets.Add(new SheetResult				{
					Path = result.Collage,
					VideoCount = 1,
					Skipped = (result.Status == "skipped"),
					Sources = new List<string> { result.Source }
				});
				Emit(progress, job.Row, i + 1, jobs.Count, 0.0, ResultStatus(result), result.Collage, result);
			}
			catch (OperationCanceledException)
			{
				batchSummary.Cancelled = true;
				Emit(progress, job.Row, i, jobs.Count, 0.0, "已取消", "", null);
				break;
			}
			catch (Exception ex2)
			{
				batchSummary.Failed++;
				Result result2 = new Result();
				result2.Source = job.Path;
				result2.Status = "failed";
				result2.RequestedFrames = job.FrameCount;
				result2.Message = ex2.Message;
				Result result3 = result2;
				batchSummary.Results.Add(result3);
				Emit(progress, job.Row, i + 1, jobs.Count, 0.0, "失败 · 可重试", ex2.Message, result3);
			}
		}
		SaveReports(batchSummary, reportRoots, progress, jobs.Count);
		return batchSummary;
	}

	private static string ResultStatus(Result result)
	{
		if (!(result.Status == "skipped"))
		{
			return "已完成 · " + result.ActualFrames + " 帧 · " + ((result.GpuFrames > 0) ? "GPU" : "CPU");
		}
		return "已跳过 · 相同结果";
	}

	private static List<string> ReportRoots(List<VideoJob> jobs, JobOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
		{
			return new List<string> { Path.GetFullPath(options.OutputDirectory) };
		}
		List<string> list = new List<string>();
		foreach (VideoJob job in jobs)
		{
			try
			{
				list.Add(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(job.Path)), "抽帧"));
			}
			catch
			{
			}
		}
		return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static string RecordDirectory(string outputRoot)
	{
		string text = outputRoot ?? "";
		try
		{
			text = Path.GetFullPath(text);
		}
		catch
		{
		}
		text = text.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
		string text2 = Path.Combine(AppDirectory, "records", Hash(text));
		MigrateLegacyRecords(text, text2);
		return text2;
	}

	private static void MigrateLegacyRecords(string outputRoot, string recordsDirectory)
	{
		try
		{
			string text = Path.Combine(outputRoot, "记录");
			if (!Directory.Exists(text))
			{
				return;
			}
			Directory.CreateDirectory(recordsDirectory);
			foreach (string file in Directory.GetFiles(text))
			{
				string fileName = Path.GetFileName(file);
				if (!IsRecordFile(fileName))
				{
					continue;
				}
				string path = Path.Combine(recordsDirectory, fileName);
				try
				{
					if (!File.Exists(path))
					{
						File.Move(file, path);
					}
					else if (FileHash(file) == FileHash(path))
					{
						File.Delete(file);
					}
				}
				catch
				{
				}
			}
			if (Directory.GetFileSystemEntries(text).Length == 0)
			{
				Directory.Delete(text);
			}
		}
		catch
		{
		}
	}

	private static bool IsRecordFile(string name)
	{
		if ((name.StartsWith("批次_", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) || (name.StartsWith("清单_", StringComparison.Ordinal) && name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) || (name.StartsWith("合拼_", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
		{
			return true;
		}
		if (name.Length == 69 && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
		{
			for (int i = 0; i < 64; i++)
			{
				if (!Uri.IsHexDigit(name[i]))
				{
					return false;
				}
			}
			return true;
		}
		return false;
	}

	private static void SaveReports(BatchSummary summary, IEnumerable<string> roots, Action<ProgressUpdate> progress, int total)
	{
		summary.Finished = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
		string text2 = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
		foreach (string root in roots)
		{
			try
			{
				string text = RecordDirectory(root);
				Directory.CreateDirectory(text);
				WriteJson(Path.Combine(text, "批次_" + text2 + ".json"), summary);
				StringBuilder stringBuilder = new StringBuilder("源文件路径,视频文件名,状态,时长（秒）,请求帧数,实际帧数,抽帧时间点（秒）,拼图路径,加速方式,GPU帧数,备注\r\n");
				foreach (Result result in summary.Results)
				{
					stringBuilder.AppendLine(string.Join(",", new string[11]
					{
						result.Source,
						Path.GetFileName(result.Source),
						result.Status,
						result.Duration.ToString("0.###", CultureInfo.InvariantCulture),
						result.RequestedFrames.ToString(),
						result.ActualFrames.ToString(),
						string.Join(" / ", result.Times.Select((double t) => t.ToString("0.###", CultureInfo.InvariantCulture))),
						result.Collage,
						result.Decoder,
						result.GpuFrames.ToString(),
						result.Message
					}.Select(Csv)));
				}
				File.WriteAllText(Path.Combine(text, "清单_" + text2 + ".csv"), stringBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			}
			catch (Exception ex)
			{
				Emit(progress, -1, summary.Results.Count, total, 0.0, "清单保存失败", ex.Message, null);
			}
		}
	}

	private static string Csv(string value)
	{
		return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
	}

	private static void Emit(Action<ProgressUpdate> callback, int row, int completed, int total, double fraction, string status, string message, Result result)
	{
		callback?.Invoke(new ProgressUpdate
		{
			Row = row,
			Completed = completed,
			Total = total,
			Fraction = fraction,
			Status = status,
			Message = message,
			Result = result
		});
	}

	private static bool HasResult(BatchSummary summary, string source)
	{
		return summary.Results.Any((Result r) => string.Equals(r.Source, source, StringComparison.OrdinalIgnoreCase));
	}

	private static void RetainGroupResults(BatchSummary summary, List<Tuple<VideoJob, Result>> entries, Action<ProgressUpdate> progress, int total, int completed, string note)
	{
		foreach (Tuple<VideoJob, Result> entry in entries)
		{
			Result result = entry.Item2;
			if (HasResult(summary, result.Source))
			{
				continue;
			}
			result.Status = "success";
			result.Message = (string.IsNullOrEmpty(result.Message) ? note : (result.Message + " " + note));
			summary.Results.Add(result);
			summary.Success++;
			Emit(progress, entry.Item1.Row, completed, total, 0.0, "已保留单视频拼图", result.Collage, result);
		}
	}

	private Result Process(VideoJob job, JobOptions options, CancellationToken cancel, Action<double, string> progress)
	{
		if (job.FrameCount < 1 || job.FrameCount > 100)
		{
			throw new Exception("抽帧数量必须在 1–100 之间。");
		}
		FileInfo fileInfo = new FileInfo(job.Path);
		if (!fileInfo.Exists)
		{
			throw new FileNotFoundException("视频不存在或暂时无法访问。", job.Path);
		}
		string text = (string.IsNullOrWhiteSpace(options.OutputDirectory) ? Path.Combine(fileInfo.DirectoryName, "抽帧") : Path.GetFullPath(options.OutputDirectory));
		string text2 = Hash(fileInfo.FullName.ToLowerInvariant() + "|" + fileInfo.Length + "|" + fileInfo.LastWriteTimeUtc.Ticks + "|" + job.FrameCount + "|" + options.Preset + "|" + options.SaveFrames + "|" + options.ShowTimes + "|" + options.ShowInfo + "|" + options.Acceleration + RangeKey(options) + "|v2");
		string text3 = RecordDirectory(text);
		string path = Path.Combine(text3, text2 + ".json");
		if (options.Existing == "auto" && File.Exists(path))
		{
			try
			{
				CacheRecord cacheRecord = new JavaScriptSerializer().Deserialize<CacheRecord>(File.ReadAllText(path, Encoding.UTF8));
				if (cacheRecord != null && cacheRecord.Key == text2 && cacheRecord.Result != null && File.Exists(cacheRecord.Result.Collage) && !string.IsNullOrEmpty(cacheRecord.Result.CollageHash) && FileHash(cacheRecord.Result.Collage) == cacheRecord.Result.CollageHash && (!options.SaveFrames || (Directory.Exists(cacheRecord.Result.FramesDirectory) && Directory.GetFiles(cacheRecord.Result.FramesDirectory, "*.jpg").Length == cacheRecord.Result.ActualFrames)))
				{
					cacheRecord.Result.Status = "skipped";
					return cacheRecord.Result;
				}
			}
			catch
			{
			}
		}
		VideoInfo videoInfo = Probe(job.Path, cancel);
		Preset preset = Preset.Get(options.Preset);
		double rangeStart = options.StartTime ?? 0.0;
		double rangeEnd = (options.EndTime.HasValue ? Math.Min(options.EndTime.Value, videoInfo.Duration) : videoInfo.Duration);
		if (rangeStart >= videoInfo.Duration)
		{
			throw new Exception("开始时间 " + Stamp(rangeStart) + " 超出视频时长 " + Stamp(videoInfo.Duration) + "。");
		}
		if (rangeEnd <= rangeStart)
		{
			throw new Exception("抽帧时间范围无效：" + Stamp(rangeStart) + " – " + Stamp(rangeEnd) + "。");
		}
		double rangeSpan = rangeEnd - rangeStart;
		int num = job.FrameCount;
		if (videoInfo.Fps > 0.0)
		{
			num = Math.Min(num, Math.Max(1, (int)Math.Floor(rangeSpan * videoInfo.Fps)));
		}
		List<double> list = new List<double>();
		for (int i = 0; i < num; i++)
		{
			double num2 = rangeStart + rangeSpan * ((double)i + 0.5) / (double)num;
			if (videoInfo.Fps > 0.0)
			{
				double val = Math.Max(0.0, Math.Floor(videoInfo.Duration * videoInfo.Fps + 1E-05) - 1.0);
				num2 = Math.Min(val, Math.Floor(num2 * videoInfo.Fps)) / videoInfo.Fps;
			}
			list.Add(num2);
		}
		string text4 = Path.Combine(Path.GetTempPath(), "VideoFrameHelper", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text4);
		try
		{
			List<string> list2 = new List<string>();
			DecoderSession decoderSession = new DecoderSession();
			int frameWidth = 0;
			int frameHeight = 0;
			List<string> raws = new List<string>();
			for (int i = 0; i < num; i++)
			{
				raws.Add(Path.Combine(text4, "raw" + i.ToString("D3") + ".jpg"));
			}
			object progressLock = new object();
			int completed = 0;
			Action<int> report = delegate(int count)
			{
				progress((double)count / (double)(num + 1), "抽取 " + count + " / " + num + " 帧 · " + DecoderLabel(decoderSession.Active));
			};
			cancel.ThrowIfCancellationRequested();
			ExtractFrame(job.Path, list[0], videoInfo, preset, options.Acceleration, decoderSession, raws[0], cancel);
			completed = 1;
			report(1);
			if (num > 1)
			{
				string active = decoderSession.Active ?? "cpu";
				List<List<int>> chunks = new List<List<int>>();
				for (int i = 1; i < num; i += ExtractChunkSize)
				{
					List<int> chunk = new List<int>();
					for (int j = i; j < Math.Min(num, i + ExtractChunkSize); j++)
					{
						chunk.Add(j);
					}
					chunks.Add(chunk);
				}
				bool[] chunkOk = new bool[chunks.Count];
				ParallelOptions parallelOptions = new ParallelOptions
				{
					MaxDegreeOfParallelism = Math.Min(ExtractParallelism(), chunks.Count),
					CancellationToken = cancel
				};
				try
				{
					Parallel.ForEach(Enumerable.Range(0, chunks.Count), parallelOptions, delegate(int c)
					{
						List<int> indexes = chunks[c];
						List<string> outputs = new List<string>();
						foreach (int index in indexes)
						{
							outputs.Add(raws[index]);
						}
						bool num2 = ExtractChunk(job.Path, indexes, list, outputs, videoInfo, preset, active, cancel);
						chunkOk[c] = num2;
						if (num2)
						{
							lock (progressLock)
							{
								completed += indexes.Count;
								report(completed);
							}
						}
					});
				}
				catch (AggregateException ex)
				{
					OperationCanceledException ex2 = ex.Flatten().InnerExceptions.OfType<OperationCanceledException>().FirstOrDefault();
					if (ex2 != null)
					{
						throw ex2;
					}
					throw ex.Flatten().InnerExceptions.First();
				}
				for (int c = 0; c < chunks.Count; c++)
				{
					List<int> indexes = chunks[c];
					if (chunkOk[c])
					{
						decoderSession.Used.Add(active);
						if (active == "cpu")
						{
							decoderSession.CpuFrames += indexes.Count;
						}
						else
						{
							decoderSession.GpuFrames += indexes.Count;
						}
						continue;
					}
					foreach (int index in indexes)
					{
						cancel.ThrowIfCancellationRequested();
						ExtractFrame(job.Path, list[index], videoInfo, preset, options.Acceleration, decoderSession, raws[index], cancel);
						lock (progressLock)
						{
							completed++;
							report(completed);
						}
					}
				}
			}
			cancel.ThrowIfCancellationRequested();
			for (int i = 0; i < num; i++)
			{
				string text5 = Path.Combine(text4, "帧" + (i + 1).ToString("D3") + "_" + Stamp(list[i]).Replace(':', '-') + ".jpg");
				using (Bitmap bitmap = new Bitmap(raws[i]))
				{
					frameWidth = bitmap.Width;
					frameHeight = bitmap.Height;
					SaveJpeg(bitmap, text5, preset.Quality);
				}
				File.Delete(raws[i]);
				list2.Add(text5);
			}
			cancel.ThrowIfCancellationRequested();
			progress((double)num / (double)(num + 1), "正在合成拼图");
			string text7 = Path.Combine(text4, "collage.jpg");
			BuildCollage(fileInfo.Name, videoInfo, list2, list, options, preset, frameWidth, frameHeight, text7, cancel);
			using (Bitmap bitmap2 = new Bitmap(text7))
			{
				if (bitmap2.Width < 1)
				{
					throw new Exception("拼图校验失败。");
				}
			}
			cancel.ThrowIfCancellationRequested();
			Directory.CreateDirectory(text);
			string text8 = fileInfo.Name;
			string text9 = Path.Combine(text, text8 + "_拼图.jpg");
			if (options.Existing != "replace" || (File.Exists(text9) && !OwnsTarget(text9, fileInfo.FullName, text3)))
			{
				int num3 = 2;
				while (File.Exists(text9) || Directory.Exists(Path.Combine(text, "单帧", text8)))
				{
					text8 = fileInfo.Name + "_v" + num3++;
					text9 = Path.Combine(text, text8 + "_拼图.jpg");
				}
			}
			string text10 = "";
			long num4 = new FileInfo(text7).Length;
			if (options.SaveFrames)
			{
				text10 = Path.Combine(text, "单帧", text8);
				if (Directory.Exists(text10))
				{
					text10 = text10 + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
				}
				Directory.CreateDirectory(text10);
				foreach (string item in list2)
				{
					Commit(item, Path.Combine(text10, Path.GetFileName(item)));
					num4 += new FileInfo(item).Length;
				}
			}
			Commit(text7, text9);
			Result result = new Result();
			result.Source = fileInfo.FullName;
			result.Status = "success";
			result.Collage = text9;
			result.FramesDirectory = text10;
			result.RequestedFrames = job.FrameCount;
			result.ActualFrames = num;
			result.Duration = videoInfo.Duration;
			result.Bytes = num4;
			result.Times = list;
			result.Message = ((num < job.FrameCount) ? ("短视频有效帧不足，已减少至 " + num + " 帧。") : "");
			Result result2 = result;
			result2.GpuFrames = decoderSession.GpuFrames;
			result2.CpuFrames = decoderSession.CpuFrames;
			result2.Decoder = string.Join(" / ", decoderSession.Used.Select(DecoderLabel));
			result2.HardwareEvidence = decoderSession.Evidence;
			try
			{
				result2.CollageHash = FileHash(text9);
			}
			catch
			{
			}
			if (decoderSession.Failures.Count > 0)
			{
				result2.Message = result2.Message + " 加速回退：" + string.Join("；", decoderSession.Failures);
			}
			try
			{
				WriteJson(path, new CacheRecord
				{
					Key = text2,
					Result = result2,
					SaveFrames = options.SaveFrames
				});
			}
			catch (Exception ex)
			{
				result2.Message = (result2.Message + " 结果缓存写入失败：" + ex.Message).Trim();
			}
			return result2;
		}
		finally
		{
			try
			{
				string[] files = Directory.GetFiles(text4);
				foreach (string path2 in files)
				{
					File.Delete(path2);
				}
				Directory.Delete(text4);
			}
			catch
			{
			}
		}
	}

	private VideoInfo Probe(string path, CancellationToken cancel)
	{
		int exitCode;
		string text = Execute(new string[4] { "-hide_banner", "-nostdin", "-i", path }, cancel, 60000, out exitCode);
		Match match = Regex.Match(text, "Duration:\\s*(\\d+):(\\d+):(\\d+(?:\\.\\d+)?)");
		Match match2 = Regex.Match(text, "Stream[^\\r\\n]*Video:[^\\r\\n]*");
		if (!match.Success || !match2.Success)
		{
			throw new Exception("无法读取有效视频轨道或时长。" + Tail(text));
		}
		Match match3 = Regex.Match(match2.Value, "(?<!\\d)(\\d{2,6})x(\\d{2,6})(?!\\d)");
		Match match4 = Regex.Match(match2.Value, "(\\d+(?:\\.\\d+)?)\\s+fps");
		VideoInfo videoInfo = new VideoInfo();
		videoInfo.Duration = (double)(int.Parse(match.Groups[1].Value) * 3600 + int.Parse(match.Groups[2].Value) * 60) + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
		VideoInfo videoInfo2 = videoInfo;
		if (match3.Success)
		{
			videoInfo2.Width = int.Parse(match3.Groups[1].Value);
			videoInfo2.Height = int.Parse(match3.Groups[2].Value);
		}
		if (match4.Success)
		{
			videoInfo2.Fps = double.Parse(match4.Groups[1].Value, CultureInfo.InvariantCulture);
		}
		if (Regex.IsMatch(match2.Value, "10le|10be|Main 10|10-bit", RegexOptions.IgnoreCase))
		{
			videoInfo2.HardwarePixelFormat = "p010le";
		}
		if (videoInfo2.Duration <= 0.0)
		{
			throw new Exception("视频时长为零。");
		}
		return videoInfo2;
	}

	private static void BuildCollage(string name, VideoInfo info, List<string> frames, List<double> times, JobOptions options, Preset preset, int frameWidth, int frameHeight, string output, CancellationToken cancel)
	{
		int num = ((frames.Count <= 3) ? frames.Count : ((int)Math.Ceiling(Math.Sqrt(frames.Count))));
		int num2 = (int)Math.Ceiling((double)frames.Count / (double)num);
		int num3 = Math.Min(preset.CollageWidth, num * ((frameWidth >= frameHeight) ? 1200 : 750) + 64);
		int num4 = Math.Max(16, num3 / 80);
		int num5 = Math.Max(12, num3 / 120);
		int num6 = (num3 - 2 * num4 - num5 * (num - 1)) / num;
		int num7 = Math.Max(30, (int)Math.Round((double)num6 * (double)frameHeight / (double)frameWidth));
		int num8 = (options.ShowTimes ? Math.Max(28, (int)((double)num6 * 0.07)) : 0);
		int num9 = Math.Max(60, num3 / 24);
		int num10 = (options.ShowInfo ? Math.Max(35, num3 / 58) : 12);
		int num11 = num9 + num10;
		int height = num11 + num4 + num2 * (num7 + num8) + (num2 - 1) * num5 + num4;
		using Bitmap image = new Bitmap(num3, height, PixelFormat.Format24bppRgb);
		using Graphics graphics = Graphics.FromImage(image);
		using Font font = new Font("Microsoft YaHei UI", Math.Max(22f, (float)num3 / 58f), FontStyle.Bold, GraphicsUnit.Pixel);
		using Font font2 = new Font("Microsoft YaHei UI", Math.Max(15f, (float)num3 / 95f), FontStyle.Regular, GraphicsUnit.Pixel);
		using Font font3 = new Font("Microsoft YaHei UI", Math.Max(12f, (float)num6 / 32f), FontStyle.Regular, GraphicsUnit.Pixel);
		using SolidBrush brush = new SolidBrush(Color.FromArgb(28, 43, 60));
		using SolidBrush brush2 = new SolidBrush(Color.FromArgb(93, 107, 122));
		StringFormat stringFormat = new StringFormat();
		stringFormat.Trimming = StringTrimming.EllipsisCharacter;
		stringFormat.FormatFlags = StringFormatFlags.NoWrap;
		stringFormat.LineAlignment = StringAlignment.Center;
		using StringFormat format = stringFormat;
		graphics.Clear(Color.FromArgb(242, 245, 249));
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
		graphics.DrawString(name, font, brush, new RectangleF(num4, 4f, num3 - 2 * num4, num9), format);
		if (options.ShowInfo)
		{
			string s = "时长 " + Stamp(info.Duration) + "    ·    " + frames.Count + " 帧    ·    原视频 " + info.Width + " × " + info.Height;
			graphics.DrawString(s, font2, brush2, new RectangleF(num4, num9, num3 - 2 * num4, num10), format);
		}
		for (int i = 0; i < frames.Count; i++)
		{
			cancel.ThrowIfCancellationRequested();
			int num12 = num4 + i % num * (num6 + num5);
			int num13 = num11 + num4 + i / num * (num7 + num8 + num5);
			graphics.FillRectangle(Brushes.White, num12, num13, num6, num7 + num8);
			using (Bitmap image2 = new Bitmap(frames[i]))
			{
				graphics.DrawImage(image2, new Rectangle(num12, num13, num6, num7));
			}
			if (options.ShowTimes)
			{
				graphics.DrawString((i + 1).ToString("D2") + "  /  " + Stamp(times[i]), font3, brush, new RectangleF(num12 + Math.Max(5, num6 / 50), num13 + num7, num6 - 10, num8), format);
			}
		}
		SaveJpeg(image, output, preset.Quality);
	}

	public static string Stamp(double value)
	{
		long num = Math.Max(0L, (long)Math.Round(value * 1000.0));
		return $"{num / 3600000:00}:{num / 60000 % 60:00}:{num / 1000 % 60:00}.{num % 1000:000}";
	}

	public static bool TryParseTime(string text, out double seconds)
	{
		seconds = 0.0;
		text = (text ?? "").Trim().Replace('：', ':');
		if (text.Length == 0)
		{
			return false;
		}
		string[] parts = text.Split(':');
		if (parts.Length > 3)
		{
			return false;
		}
		double[] values = new double[parts.Length];
		for (int i = 0; i < parts.Length; i++)
		{
			string part = parts[i].Trim();
			if (part.Length == 0 || !double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out values[i]) || double.IsNaN(values[i]) || double.IsInfinity(values[i]) || values[i] < 0.0)
			{
				return false;
			}
		}
		if (parts.Length == 1)
		{
			seconds = values[0];
		}
		else if (parts.Length == 2)
		{
			if (values[1] >= 60.0)
			{
				return false;
			}
			seconds = values[0] * 60.0 + values[1];
		}
		else
		{
			if (values[1] >= 60.0 || values[2] >= 60.0)
			{
				return false;
			}
			seconds = values[0] * 3600.0 + values[1] * 60.0 + values[2];
		}
		return !double.IsNaN(seconds) && !double.IsInfinity(seconds);
	}

	private static string OptionTime(double? value)
	{
		return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-";
	}

	private static string RangeKey(JobOptions options)
	{
		return ((options.StartTime.HasValue || options.EndTime.HasValue) ? ("|range|" + OptionTime(options.StartTime) + "|" + OptionTime(options.EndTime)) : "");
	}

	private static void SaveJpeg(Image image, string path, long quality)
	{
		ImageCodecInfo encoder = ImageCodecInfo.GetImageEncoders().First((ImageCodecInfo c) => c.MimeType == "image/jpeg");
		using EncoderParameters encoderParameters = new EncoderParameters(1);
		encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
		image.Save(path, encoder, encoderParameters);
	}

	public static void WriteJson(string path, object value)
	{
		string directoryName = Path.GetDirectoryName(Path.GetFullPath(path));
		Directory.CreateDirectory(directoryName);
		string text = path + "." + Guid.NewGuid().ToString("N") + ".partial";
		File.WriteAllText(text, new JavaScriptSerializer
		{
			MaxJsonLength = int.MaxValue
		}.Serialize(value), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		try
		{
			if (!MoveFileEx(text, path, 9))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	private static void Commit(string source, string target)
	{
		string text = target + "." + Guid.NewGuid().ToString("N") + ".partial";
		try
		{
			File.Copy(source, text, overwrite: false);
			if (!MoveFileEx(text, target, 9))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	private static string Hash(string text)
	{
		using SHA256 sHA = SHA256.Create();
		return BitConverter.ToString(sHA.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
	}

	private static string FileHash(string path)
	{
		using SHA256 sHA = SHA256.Create();
		using FileStream inputStream = File.OpenRead(path);
		return BitConverter.ToString(sHA.ComputeHash(inputStream)).Replace("-", "").ToLowerInvariant();
	}

	private static bool OwnsTarget(string collage, string source, string recordsDirectory)
	{
		try
		{
			if (!Directory.Exists(recordsDirectory))
			{
				return false;
			}
			string fullCollage = Path.GetFullPath(collage);
			foreach (string file in Directory.GetFiles(recordsDirectory, "*.json"))
			{
				try
				{
					CacheRecord cacheRecord = new JavaScriptSerializer().Deserialize<CacheRecord>(File.ReadAllText(file, Encoding.UTF8));
					if (cacheRecord != null && cacheRecord.Result != null && !string.IsNullOrEmpty(cacheRecord.Result.Collage) && string.Equals(Path.GetFullPath(cacheRecord.Result.Collage), fullCollage, StringComparison.OrdinalIgnoreCase) && string.Equals(cacheRecord.Result.Source, source, StringComparison.OrdinalIgnoreCase))
					{
						return true;
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
		return false;
	}

	private static bool OwnsGroupSheet(string cachePath, string target)
	{
		try
		{
			if (!File.Exists(cachePath))
			{
				return false;
			}
			GroupCacheRecord groupCacheRecord = new JavaScriptSerializer().Deserialize<GroupCacheRecord>(File.ReadAllText(cachePath, Encoding.UTF8));
			return groupCacheRecord != null && groupCacheRecord.Sheets != null && groupCacheRecord.Sheets.Any((SheetResult s) => !string.IsNullOrEmpty(s.Path) && string.Equals(Path.GetFullPath(s.Path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase));
		}
		catch
		{
			return false;
		}
	}

	private const long FfmpegLength = 60993536L;

	private const string FfmpegSha256 = "6001ad699ca9894c01273ee22474ea7f60de642764c374d1234a9a75c41763b1";

	private static bool IsValidFfmpeg(string path)
	{
		try
		{
			FileInfo fileInfo = new FileInfo(path);
			return fileInfo.Exists && fileInfo.Length == FfmpegLength && string.Equals(FileHash(path), FfmpegSha256, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private static string EnsureFfmpeg()
	{
		string text = Path.Combine(AppDirectory, "tools");
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, "ffmpeg_5.0.1.exe");
		using Mutex mutex = new Mutex(initiallyOwned: false, "Local\\VideoFrameHelper_Ffmpeg_v1");
		bool flag;
		try
		{
			flag = mutex.WaitOne(60000);
		}
		catch (AbandonedMutexException)
		{
			flag = true;
		}
		if (!flag)
		{
			throw new Exception("视频处理组件正在初始化，请稍后重试。");
		}
		try
		{
			if (IsValidFfmpeg(text2))
			{
				return text2;
			}
			if (File.Exists(text2))
			{
				try
				{
					File.Delete(text2);
				}
				catch (Exception ex)
				{
					throw new Exception("视频处理组件损坏且无法替换，请关闭其他正在运行的窗口后重试。", ex);
				}
			}
			string text3 = text2 + "." + Guid.NewGuid().ToString("N") + ".partial";
			try
			{
				using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FrameHelper.ffmpeg.gz"))
				{
					if (stream == null)
					{
						throw new Exception("缺少内置的视频处理组件。");
					}
					using GZipStream gZipStream = new GZipStream(stream, CompressionMode.Decompress);
					using FileStream destination = File.Create(text3);
					gZipStream.CopyTo(destination);
				}
				if (!IsValidFfmpeg(text3))
				{
					throw new Exception("内置视频处理组件校验失败，请重新下载程序。");
				}
				if (!MoveFileEx(text3, text2, 9))
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}
			}
			finally
			{
				if (File.Exists(text3))
				{
					File.Delete(text3);
				}
			}
			return text2;
		}
		finally
		{
			mutex.ReleaseMutex();
		}
	}

	private string Execute(IEnumerable<string> args, CancellationToken cancel, int timeoutMs, out int exitCode)
	{
		StringBuilder output = new StringBuilder();
		object sync = new object();
		using Process process = new Process();
		process.StartInfo = new ProcessStartInfo(ffmpeg, string.Join(" ", args.Select(Quote)))
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			StandardErrorEncoding = Encoding.UTF8
		};
		process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				lock (sync)
				{
					if (output.Length < 262144)
					{
						output.AppendLine(e.Data);
					}
				}
			}
		};
		process.OutputDataReceived += delegate
		{
		};
		process.Start();
		process.BeginErrorReadLine();
		process.BeginOutputReadLine();
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (!process.WaitForExit(120))
		{
			if (cancel.IsCancellationRequested || stopwatch.ElapsedMilliseconds > timeoutMs)
			{
				try
				{
					process.Kill();
					process.WaitForExit(5000);
				}
				catch
				{
				}
				cancel.ThrowIfCancellationRequested();
				throw new TimeoutException("读取视频超时，请检查视频文件和网络连接。");
			}
		}
		process.WaitForExit();
		cancel.ThrowIfCancellationRequested();
		exitCode = process.ExitCode;
		lock (sync)
		{
			return output.ToString();
		}
	}

	private static string Quote(string value)
	{
		if (value.Length > 0 && value.IndexOfAny(new char[5] { ' ', '\t', '\n', '\v', '"' }) < 0)
		{
			return value;
		}
		StringBuilder stringBuilder = new StringBuilder("\"");
		int num = 0;
		foreach (char c in value)
		{
			switch (c)
			{
			case '\\':
				num++;
				break;
			case '"':
				stringBuilder.Append('\\', num * 2 + 1);
				stringBuilder.Append('"');
				num = 0;
				break;
			default:
				stringBuilder.Append('\\', num);
				num = 0;
				stringBuilder.Append(c);
				break;
			}
		}
		stringBuilder.Append('\\', num * 2);
		stringBuilder.Append('"');
		return stringBuilder.ToString();
	}

	private static string Tail(string text)
	{
		text = (text ?? "").Trim();
		if (text.Length > 1200)
		{
			return text.Substring(text.Length - 1200);
		}
		return text;
	}

	private static string DecoderLabel(string backend)
	{
		return backend switch
		{
			"cuda" => "CUDA 解码与缩放", 
			"cuda-decode" => "CUDA 硬件解码", 
			"d3d11va" => "D3D11 硬件解码", 
			_ => "CPU", 
		};
	}

	private void ExtractFrame(string source, double time, VideoInfo info, Preset preset, string requested, DecoderSession session, string output, CancellationToken cancel)
	{
		List<string> list = new List<string>();
		if (session.Active != null)
		{
			list.Add(session.Active);
		}
		if (requested == "auto" || requested == "cuda")
		{
			list.AddRange(new string[2] { "cuda", "cuda-decode" });
		}
		if (requested == "auto" || requested == "d3d11va")
		{
			list.Add("d3d11va");
		}
		list.Add("cpu");
		string text = ((preset.FrameMax > 0) ? ("scale=w='min(" + preset.FrameMax + ",iw)':h='min(" + preset.FrameMax + ",ih)':force_original_aspect_ratio=decrease") : "");
		foreach (string item in list.Distinct())
		{
			if (session.Rejected.Contains(item))
			{
				continue;
			}
			cancel.ThrowIfCancellationRequested();
			if (File.Exists(output))
			{
				File.Delete(output);
			}
			List<string> list2 = new List<string>();
			list2.Add("-hide_banner");
			list2.Add("-nostdin");
			list2.Add("-loglevel");
			list2.Add((item == "cpu") ? "error" : "verbose");
			list2.Add("-ss");
			list2.Add(time.ToString("0.000000", CultureInfo.InvariantCulture));
			List<string> list3 = list2;
			switch (item)
			{
			case "cuda":
			case "cuda-decode":
				list3.AddRange(new string[4] { "-hwaccel", "cuda", "-hwaccel_output_format", "cuda" });
				break;
			case "d3d11va":
				list3.AddRange(new string[4] { "-hwaccel", "d3d11va", "-hwaccel_output_format", "d3d11" });
				break;
			}
			list3.AddRange(new string[11]
			{
				"-threads", "2", "-i", source, "-map", "0:v:0", "-frames:v", "1", "-an", "-sn",
				"-dn"
			});
			string text2 = text;
			if (item == "cuda")
			{
				string text3 = ((preset.FrameMax > 0) ? ("'min(" + preset.FrameMax + ",iw)'") : "iw");
				string text4 = ((preset.FrameMax > 0) ? ("'min(" + preset.FrameMax + ",ih)'") : "ih");
				text2 = "scale_cuda=w=" + text3 + ":h=" + text4 + ":force_original_aspect_ratio=decrease:force_divisible_by=2:format=yuv420p:interp_algo=lanczos,hwdownload,format=yuv420p";
			}
			else if (item != "cpu")
			{
				text2 = "hwdownload,format=" + info.HardwarePixelFormat + ((text.Length > 0) ? ("," + text) : "");
			}
			if (text2.Length > 0)
			{
				list3.AddRange(new string[2] { "-vf", text2 });
			}
			list3.AddRange(new string[8] { "-threads", "1", "-c:v", "mjpeg", "-q:v", "2", "-y", output });
			string text5;
			int exitCode;
			try
			{
				text5 = Execute(list3, cancel, (item == "cpu") ? 180000 : 60000, out exitCode);
			}
			catch (TimeoutException)
			{
				if (item == "cpu")
				{
					throw;
				}
				session.Rejected.Add(item);
				session.Failures.Add(DecoderLabel(item) + "超时，已尝试其他方式。");
				continue;
			}
			if (exitCode == 0 && File.Exists(output) && new FileInfo(output).Length > 0)
			{
				session.Active = item;
				session.Used.Add(item);
				if (item == "cpu")
				{
					session.CpuFrames++;
				}
				else
				{
					session.GpuFrames++;
				}
				if (item != "cpu" && session.Evidence.Length == 0)
				{
					IEnumerable<string> values = from line in text5.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
						where line.Contains("NVDEC capabilities") || line.Contains("pixfmt:cuda") || line.Contains("pixfmt:d3d11") || line.Contains("Parsed_scale_cuda")
						select line;
					session.Evidence = string.Join("\n", values);
				}
				break;
			}
			if (item == "cpu")
			{
				throw new Exception("视频抽帧失败：" + (string.IsNullOrWhiteSpace(text5) ? "未能生成该时间点的图片。" : Tail(text5)));
			}
			session.Rejected.Add(item);
			IEnumerable<string> values2 = (from line in text5.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
				where line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Cannot", StringComparison.OrdinalIgnoreCase) >= 0
				select line).Take(2);
			string text6 = string.Join(" ", values2);
			session.Failures.Add(DecoderLabel(item) + "不可用" + ((text6.Length > 0) ? ("（" + ((text6.Length > 260) ? text6.Substring(0, 260) : text6) + "）") : ""));
		}
	}

	private const int ExtractChunkSize = 8;

	private static int ExtractParallelism()
	{
		return Math.Min(3, Math.Max(1, Environment.ProcessorCount / 2));
	}

	private static string ExtractFilter(string decoder, Preset preset, VideoInfo info)
	{
		string text = ((preset.FrameMax > 0) ? ("scale=w='min(" + preset.FrameMax + ",iw)':h='min(" + preset.FrameMax + ",ih)':force_original_aspect_ratio=decrease") : "");
		if (decoder == "cuda")
		{
			string text2 = ((preset.FrameMax > 0) ? ("'min(" + preset.FrameMax + ",iw)'") : "iw");
			string text3 = ((preset.FrameMax > 0) ? ("'min(" + preset.FrameMax + ",ih)'") : "ih");
			return "scale_cuda=w=" + text2 + ":h=" + text3 + ":force_original_aspect_ratio=decrease:force_divisible_by=2:format=yuv420p:interp_algo=lanczos,hwdownload,format=yuv420p";
		}
		if (decoder != "cpu")
		{
			return "hwdownload,format=" + info.HardwarePixelFormat + ((text.Length > 0) ? ("," + text) : "");
		}
		return text;
	}

	private bool ExtractChunk(string source, List<int> indexes, List<double> times, List<string> outputs, VideoInfo info, Preset preset, string decoder, CancellationToken cancel)
	{
		List<string> list = new List<string>();
		list.Add("-hide_banner");
		list.Add("-nostdin");
		list.Add("-loglevel");
		list.Add("error");
		for (int i = 0; i < indexes.Count; i++)
		{
			list.Add("-ss");
			list.Add(times[indexes[i]].ToString("0.000000", CultureInfo.InvariantCulture));
			if (decoder == "cuda" || decoder == "cuda-decode")
			{
				list.AddRange(new string[4] { "-hwaccel", "cuda", "-hwaccel_output_format", "cuda" });
			}
			else if (decoder == "d3d11va")
			{
				list.AddRange(new string[4] { "-hwaccel", "d3d11va", "-hwaccel_output_format", "d3d11" });
			}
			list.AddRange(new string[3] { "-threads", "1", "-i" });
			list.Add(source);
		}
		string filter = ExtractFilter(decoder, preset, info);
		for (int j = 0; j < indexes.Count; j++)
		{
			list.AddRange(new string[7] { "-map", j + ":v:0", "-frames:v", "1", "-an", "-sn", "-dn" });
			if (filter.Length > 0)
			{
				list.Add("-vf");
				list.Add(filter);
			}
			list.AddRange(new string[7] { "-threads", "1", "-c:v", "mjpeg", "-q:v", "2", "-y" });
			list.Add(outputs[j]);
		}
		int exitCode;
		try
		{
			Execute(list, cancel, (decoder == "cpu") ? 300000 : 180000, out exitCode);
		}
		catch (TimeoutException)
		{
			return false;
		}
		if (exitCode != 0)
		{
			return false;
		}
		for (int k = 0; k < outputs.Count; k++)
		{
			FileInfo fileInfo = new FileInfo(outputs[k]);
			if (!fileInfo.Exists || fileInfo.Length <= 0)
			{
				return false;
			}
		}
		return true;
	}

	private BatchSummary RunGrouped(List<VideoJob> jobs, JobOptions options, CancellationToken cancel, Action<ProgressUpdate> progress)
	{
		BatchSummary summary = new BatchSummary();
		if (jobs.Count == 0)
		{
			return summary;
		}
		ffmpeg = EnsureFfmpeg();
		string text = (string.IsNullOrWhiteSpace(options.OutputDirectory) ? Path.Combine(Path.GetDirectoryName(jobs[0].Path), "抽帧") : Path.GetFullPath(options.OutputDirectory));
		string text2 = RecordDirectory(text);
		string workRoot = Path.Combine(AppDirectory, "work");
		int num = 0;
		for (int i = 0; i < jobs.Count; i += options.FilesPerSheet)
		{
			if (cancel.IsCancellationRequested)
			{
				summary.Cancelled = true;
				break;
			}
			List<VideoJob> list = jobs.Skip(i).Take(options.FilesPerSheet).ToList();
			string workDir = Path.Combine(workRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N"));
			List<Tuple<VideoJob, Result>> list2 = new List<Tuple<VideoJob, Result>>();
			bool groupDone = false;
			try
			{
				string text4 = GroupKey(list, options);
				string path = Path.Combine(text2, "合拼_" + text4 + ".json");
				GroupCacheRecord groupCacheRecord = null;
				if (options.Existing == "auto" && File.Exists(path))
				{
					try
					{
						GroupCacheRecord groupCacheRecord2 = new JavaScriptSerializer().Deserialize<GroupCacheRecord>(File.ReadAllText(path, Encoding.UTF8));
						if (groupCacheRecord2 != null && groupCacheRecord2.Key == text4 && groupCacheRecord2.Results.Count == list.Count && groupCacheRecord2.Sheets.All((SheetResult s) => File.Exists(s.Path) && !string.IsNullOrEmpty(s.Hash) && FileHash(s.Path) == s.Hash) && groupCacheRecord2.Results.All((Result r) => File.Exists(r.Collage) && (!options.SaveFrames || (Directory.Exists(r.FramesDirectory) && Directory.GetFiles(r.FramesDirectory, "*.jpg").Length == r.ActualFrames))))
						{
							groupCacheRecord = groupCacheRecord2;
						}
					}
					catch
					{
					}
				}
				if (groupCacheRecord != null)
				{
					foreach (SheetResult sheet in groupCacheRecord.Sheets)
					{
						sheet.Skipped = true;
						summary.Sheets.Add(sheet);
						num++;
					}
					for (int k = 0; k < list.Count; k++)
					{
						Result result = groupCacheRecord.Results[k];
						result.Status = "skipped";
						summary.Results.Add(result);
						summary.Skipped++;
						Emit(progress, list[k].Row, i + k + 1, jobs.Count, 0.0, "已跳过 · 合拼结果完整", result.Collage, result);
					}
					continue;
				}
				Directory.CreateDirectory(workDir);
				int num2 = 0;
				for (int l = 0; l < list.Count; l++)
				{
					VideoJob job = list[l];
					int complete = i + l;
					cancel.ThrowIfCancellationRequested();
					Emit(progress, job.Row, complete, jobs.Count, 0.0, "读取视频 · 等待合拼", Path.GetFileName(job.Path), null);
					try
					{
						JobOptions jobOptions = new JobOptions();
						jobOptions.OutputDirectory = workDir;
						jobOptions.Preset = options.Preset;
						jobOptions.Existing = "auto";
						jobOptions.SaveFrames = options.SaveFrames;
						jobOptions.ShowTimes = options.ShowTimes;
						jobOptions.ShowInfo = options.ShowInfo;
						jobOptions.Acceleration = options.Acceleration;
						jobOptions.StartTime = options.StartTime;
						jobOptions.EndTime = options.EndTime;
						JobOptions options2 = jobOptions;
						Result item = Process(job, options2, cancel, delegate(double f, string status)
						{
							Emit(progress, job.Row, complete, jobs.Count, f * 0.95, status, Path.GetFileName(job.Path), null);
						});
						list2.Add(Tuple.Create(job, item));
						Emit(progress, job.Row, complete, jobs.Count, 0.95, "抽帧完成 · 等待合拼", Path.GetFileName(job.Path), null);
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception ex2)
					{
						num2++;
						summary.Failed++;
						Result result2 = new Result();
						result2.Source = job.Path;
						result2.RequestedFrames = job.FrameCount;
						result2.Status = "failed";
						result2.Message = ex2.Message;
						Result result3 = result2;
						summary.Results.Add(result3);
						Emit(progress, job.Row, complete + 1, jobs.Count, 0.0, "失败 · 可重试", ex2.Message, result3);
					}
				}
				if (list2.Count == 0)
				{
					continue;
				}
				cancel.ThrowIfCancellationRequested();
				Directory.CreateDirectory(text);
				List<List<Tuple<VideoJob, Result>>> list3 = Paginate(list2, Preset.Get(options.Preset).CollageWidth);
				GroupCacheRecord groupCacheRecord3 = new GroupCacheRecord();
				groupCacheRecord3.Key = text4;
				GroupCacheRecord groupCacheRecord4 = groupCacheRecord3;
				foreach (List<Tuple<VideoJob, Result>> item4 in list3)
				{
					cancel.ThrowIfCancellationRequested();
					num++;
					Emit(progress, -1, Math.Min(jobs.Count - 1, i + list.Count - 1), jobs.Count, 0.99, "正在合成第 " + num + " 张 · " + item4.Count + " 个视频", "", null);
					string text5 = "视频总览_" + num.ToString("D3");
					string text6 = Path.Combine(text, text5 + ".jpg");
					if (options.Existing != "replace" || (File.Exists(text6) && !OwnsGroupSheet(path, text6)))
					{
						int num3 = 2;
						while (File.Exists(text6))
						{
							text6 = Path.Combine(text, text5 + "_v" + num3++ + ".jpg");
						}
					}
					string text7 = Path.Combine(workDir, "合拼_" + num + ".jpg");
					SheetResult sheetResult = BuildGroupSheet(item4, num, Preset.Get(options.Preset), text7, cancel, GroupColumns(list2.Count));
					sheetResult.Path = text6;
					long length = new FileInfo(text7).Length;
					Dictionary<Result, long> dictionary = new Dictionary<Result, long>();
					foreach (Tuple<VideoJob, Result> item5 in item4)
					{
						Result item2 = item5.Item2;
						long num4 = 0L;
						if (options.SaveFrames)
						{
							string text8 = Path.Combine(text, "单帧", Path.GetFileName(item2.Source));
							int num5 = 2;
							string text9 = text8;
							while (Directory.Exists(text9))
							{
								text9 = text8 + "_v" + num5++;
							}
							Directory.CreateDirectory(text9);
							string[] files = Directory.GetFiles(item2.FramesDirectory, "*.jpg");
							foreach (string text10 in files)
							{
								Commit(text10, Path.Combine(text9, Path.GetFileName(text10)));
								num4 += new FileInfo(text10).Length;
							}
							item2.FramesDirectory = text9;
						}
						dictionary[item2] = num4;
					}
					Commit(text7, text6);
					sheetResult.Hash = FileHash(text6);
					foreach (Tuple<VideoJob, Result> item6 in item4)
					{
						Result item3 = item6.Item2;
						item3.Collage = text6;
						item3.SheetVideoCount = item4.Count;
						item3.Bytes = dictionary[item3];
						item3.CollageHash = sheetResult.Hash;
					}
					item4[0].Item2.Bytes += length;
					if (list3.Count > 1)
					{
						foreach (Tuple<VideoJob, Result> item7 in item4)
						{
							item7.Item2.Message += " 合拼图片过大，已自动分页。";
						}
					}
					summary.Sheets.Add(sheetResult);
					groupCacheRecord4.Sheets.Add(sheetResult);
					foreach (Tuple<VideoJob, Result> item8 in item4)
					{
						Result item9 = item8.Item2;
						summary.Results.Add(item9);
						summary.Success++;
						groupCacheRecord4.Results.Add(item9);
						Emit(progress, item8.Item1.Row, i + list.Count, jobs.Count, 0.0, ResultStatus(item9) + " · 合拼", text6, item9);
					}
				}
				if (num2 == 0)
				{
					try
					{
						WriteJson(path, groupCacheRecord4);
					}
					catch (Exception ex3)
					{
						Emit(progress, -1, i + list.Count, jobs.Count, 0.0, "缓存写入失败", ex3.Message, null);
					}
					groupDone = true;
				}
			}
			catch (OperationCanceledException)
			{
				summary.Cancelled = true;
				RetainGroupResults(summary, list2, progress, jobs.Count, i + list.Count, "已取消合拼，仅保留单视频拼图。");
				foreach (VideoJob item10 in list.Where((VideoJob j) => !HasResult(summary, j.Path)))
				{
					Emit(progress, item10.Row, i, jobs.Count, 0.0, "已取消", "当前合拼组尚未完成。", null);
				}
				Emit(progress, -1, i + list.Count, jobs.Count, 0.0, "合拼已取消", "未合拼的中间文件保留在：" + workDir, null);
				break;
			}
			catch (Exception ex4)
			{
				RetainGroupResults(summary, list2, progress, jobs.Count, i + list.Count, "合拼未完成，已保留单视频拼图。");
				foreach (VideoJob item11 in list.Where((VideoJob j) => !HasResult(summary, j.Path)))
				{
					summary.Failed++;
					Result result4 = new Result();
					result4.Source = item11.Path;
					result4.RequestedFrames = item11.FrameCount;
					result4.Status = "failed";
					result4.Message = "合拼失败：" + ex4.Message;
					Result result5 = result4;
					summary.Results.Add(result5);
					Emit(progress, item11.Row, i + list.Count, jobs.Count, 0.0, "合拼失败 · 可重试", result5.Message, result5);
				}
				Emit(progress, -1, i + list.Count, jobs.Count, 0.0, "合拼未完成", ex4.Message + "；中间文件保留在：" + workDir, null);
			}
			finally
			{
				if (groupDone || list2.Count == 0)
				{
					CleanupGroupWork(workRoot, workDir);
				}
			}
		}
		Dictionary<string, int> dictionary2 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		for (int m = 0; m < jobs.Count; m++)
		{
			dictionary2[jobs[m].Path] = m;
		}
		summary.Results = summary.Results.OrderBy((Result r) => dictionary2.ContainsKey(r.Source) ? dictionary2[r.Source] : int.MaxValue).ToList();
		SaveReports(summary, new List<string> { text }, progress, jobs.Count);
		return summary;
	}

	private static string GroupKey(List<VideoJob> group, JobOptions options)
	{
		List<string> list = new List<string>();
		list.Add("group_v2_grid");
		list.Add(options.FilesPerSheet.ToString());
		list.Add(options.Preset);
		list.Add(options.SaveFrames.ToString());
		list.Add(options.ShowTimes.ToString());
		list.Add(options.ShowInfo.ToString());
		list.Add(options.Acceleration);
		if (options.StartTime.HasValue || options.EndTime.HasValue)
		{
			list.Add("range");
			list.Add(OptionTime(options.StartTime));
			list.Add(OptionTime(options.EndTime));
		}
		List<string> list2 = list;
		foreach (VideoJob item in group)
		{
			FileInfo fileInfo = new FileInfo(item.Path);
			list2.Add(fileInfo.FullName.ToLowerInvariant() + "|" + (fileInfo.Exists ? (fileInfo.Length.ToString() + "|" + fileInfo.LastWriteTimeUtc.Ticks) : "missing") + "|" + item.FrameCount);
		}
		return Hash(string.Join("\n", list2));
	}

	private static List<List<Tuple<VideoJob, Result>>> Paginate(List<Tuple<VideoJob, Result>> entries, int maxWidth)
	{
		int columns = GroupColumns(entries.Count);
		List<Size> list = new List<Size>();
		foreach (Tuple<VideoJob, Result> entry in entries)
		{
			using Bitmap bitmap = new Bitmap(entry.Item2.Collage);
			list.Add(bitmap.Size);
		}
		List<List<Tuple<VideoJob, Result>>> list2 = new List<List<Tuple<VideoJob, Result>>>();
		List<Tuple<VideoJob, Result>> list3 = new List<Tuple<VideoJob, Result>>();
		List<Size> list4 = new List<Size>();
		for (int i = 0; i < entries.Count; i++)
		{
			List<Size> sizes = list4.Concat(new Size[1] { list[i] }).ToList();
			SheetLayout sheetLayout = Layout(sizes, maxWidth, columns);
			if (list3.Count > 0 && (sheetLayout.Height > 30000 || (double)sheetLayout.Width * (double)sheetLayout.Height > 64000000.0))
			{
				list2.Add(list3);
				list3 = new List<Tuple<VideoJob, Result>>();
				list4.Clear();
			}
			list3.Add(entries[i]);
			list4.Add(list[i]);
		}
		if (list3.Count > 0)
		{
			list2.Add(list3);
		}
		return list2;
	}

	private static int GroupColumns(int count)
	{
		if (count > 3)
		{
			if (count > 8)
			{
				if (count > 20)
				{
					if (count > 48)
					{
						return 5;
					}
					return 4;
				}
				return 3;
			}
			return 2;
		}
		return 1;
	}

	private static SheetLayout Layout(List<Size> sizes, int maxWidth, int columns)
	{
		int cell = Math.Min((columns == 1) ? maxWidth : (maxWidth / 2), sizes.Max((Size s) => s.Width));
		int num = Math.Max(16, cell / 60);
		int num2 = columns * cell + num * (columns + 1);
		int num3 = Math.Max(90, num2 / 25);
		SheetLayout sheetLayout = new SheetLayout();
		sheetLayout.Width = num2;
		sheetLayout.Header = num3;
		sheetLayout.CellWidth = cell;
		sheetLayout.Gap = num;
		sheetLayout.Columns = columns;
		SheetLayout sheetLayout2 = sheetLayout;
		sheetLayout2.CardHeights = sizes.Select((Size s) => (int)Math.Round((double)s.Height * (double)cell / (double)s.Width)).ToList();
		for (int i = 0; i < sizes.Count; i += columns)
		{
			sheetLayout2.RowHeights.Add(sheetLayout2.CardHeights.Skip(i).Take(columns).Max());
		}
		sheetLayout2.Height = num3 + sheetLayout2.RowHeights.Sum() + num * (sheetLayout2.RowHeights.Count + 1);
		return sheetLayout2;
	}

	private static SheetResult BuildGroupSheet(List<Tuple<VideoJob, Result>> entries, int number, Preset preset, string output, CancellationToken cancel, int columns)
	{
		List<Size> list = new List<Size>();
		foreach (Tuple<VideoJob, Result> entry in entries)
		{
			using Bitmap bitmap = new Bitmap(entry.Item2.Collage);
			list.Add(bitmap.Size);
		}
		SheetLayout sheetLayout = Layout(list, preset.CollageWidth, columns);
		double num = Math.Min(1.0, Math.Min(24000.0 / (double)sheetLayout.Height, Math.Sqrt(32000000.0 / ((double)sheetLayout.Width * (double)sheetLayout.Height))));
		if (num < 1.0)
		{
			sheetLayout = Layout(list, Math.Max(400, (int)((double)preset.CollageWidth * num * 0.98)), columns);
		}
		int width = sheetLayout.Width;
		int height = sheetLayout.Height;
		int gap = sheetLayout.Gap;
		int header = sheetLayout.Header;
		using (Bitmap image = new Bitmap(width, height, PixelFormat.Format24bppRgb))
		{
			using Graphics graphics = Graphics.FromImage(image);
			using Font font = new Font("Microsoft YaHei UI", Math.Max(24f, (float)width / 58f), FontStyle.Bold, GraphicsUnit.Pixel);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(29, 45, 66));
			graphics.Clear(Color.FromArgb(223, 232, 244));
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
			graphics.DrawString("视频抽帧总览  /  " + number.ToString("D3") + "  /  " + entries.Count + " 个视频", font, brush, new RectangleF(gap, 0f, width - 2 * gap, header), new StringFormat
			{
				LineAlignment = StringAlignment.Center
			});
			int num2 = header + gap;
			for (int i = 0; i < entries.Count; i++)
			{
				cancel.ThrowIfCancellationRequested();
				int x = gap + i % columns * (sheetLayout.CellWidth + gap);
				using (Bitmap image2 = new Bitmap(entries[i].Item2.Collage))
				{
					graphics.DrawImage(image2, new Rectangle(x, num2, sheetLayout.CellWidth, sheetLayout.CardHeights[i]));
				}
				if (i % columns == columns - 1 || i == entries.Count - 1)
				{
					num2 += sheetLayout.RowHeights[i / columns] + gap;
				}
			}
			SaveJpeg(image, output, preset.Quality);
		}
		using (Bitmap bitmap2 = new Bitmap(output))
		{
			if (bitmap2.Width != width || bitmap2.Height != height)
			{
				throw new Exception("合拼图片校验失败。");
			}
		}
		SheetResult sheetResult = new SheetResult();
		sheetResult.VideoCount = entries.Count;
		sheetResult.Width = width;
		sheetResult.Height = height;
		sheetResult.Sources = entries.Select((Tuple<VideoJob, Result> e) => e.Item2.Source).ToList();
		return sheetResult;
	}

	private static void CleanupGroupWork(string workRoot, string folder)
	{
		try
		{
			string fullFolder = Path.GetFullPath(folder);
			string parent = Path.GetDirectoryName(fullFolder);
			if (parent == null || !string.Equals(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(workRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			if (Directory.Exists(fullFolder))
			{
				DeleteScratchDirectory(fullFolder);
			}
		}
		catch
		{
		}
	}

	private static void DeleteScratchDirectory(string folder)
	{
		if (!Directory.Exists(folder))
		{
			return;
		}
		string[] files = Directory.GetFiles(folder);
		foreach (string path in files)
		{
			File.Delete(path);
		}
		string[] directories = Directory.GetDirectories(folder);
		foreach (string text in directories)
		{
			if ((File.GetAttributes(text) & FileAttributes.ReparsePoint) == 0)
			{
				DeleteScratchDirectory(text);
			}
		}
		Directory.Delete(folder);
	}
}
