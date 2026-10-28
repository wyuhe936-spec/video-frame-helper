namespace FrameHelper;

public sealed class SavedSettings
{
	public int FrameCount = 5;

	public int FilesPerSheet = 1;

	public int AccelerationIndex;

	public int PresetIndex;

	public int ExistingIndex;

	public bool SaveFrames;

	public bool Recursive;

	public bool ShowTimes = true;

	public bool ShowInfo = true;

	public string OutputDirectory = "";

	public string RangeStart = "";

	public string RangeEnd = "";

	public string LastInputDirectory = "";
}
