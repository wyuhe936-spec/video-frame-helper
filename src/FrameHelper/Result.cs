using System.Collections.Generic;

namespace FrameHelper;

public sealed class Result
{
	public string Source = "";

	public string Status = "";

	public string Collage = "";

	public string FramesDirectory = "";

	public string Message = "";

	public int RequestedFrames;

	public int ActualFrames;

	public double Duration;

	public long Bytes;

	public List<double> Times = new List<double>();

	public string Decoder = "CPU";

	public string HardwareEvidence = "";

	public string CollageHash = "";

	public int GpuFrames;

	public int CpuFrames;

	public int SheetVideoCount = 1;
}
