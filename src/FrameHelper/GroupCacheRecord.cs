using System.Collections.Generic;

namespace FrameHelper;

public sealed class GroupCacheRecord
{
	public string Key;

	public List<Result> Results = new List<Result>();

	public List<SheetResult> Sheets = new List<SheetResult>();
}
