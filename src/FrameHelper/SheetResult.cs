using System.Collections.Generic;

namespace FrameHelper;

public sealed class SheetResult
{
	public string Path;

	public string Hash;

	public int VideoCount;

	public int Width;

	public int Height;

	public bool Skipped;

	public List<string> Sources = new List<string>();
}
