namespace FrameHelper;

public sealed class JobOptions
{
	public string OutputDirectory = "";

	public string Preset = "light";

	public string Existing = "auto";

	public bool SaveFrames;

	public bool ShowTimes = true;

	public bool ShowInfo = true;

	public int FilesPerSheet = 1;

	public string Acceleration = "auto";

	public double? StartTime;

	public double? EndTime;
}
