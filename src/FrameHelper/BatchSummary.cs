using System;
using System.Collections.Generic;

namespace FrameHelper;

public sealed class BatchSummary
{
	public string Version = "1.2.2";

	public string Started = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

	public string Finished;

	public bool Cancelled;

	public int Success;

	public int Skipped;

	public int Failed;

	public List<Result> Results = new List<Result>();

	public List<SheetResult> Sheets = new List<SheetResult>();
}
