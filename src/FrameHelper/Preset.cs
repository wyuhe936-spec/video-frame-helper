namespace FrameHelper;

public sealed class Preset
{
	public int FrameMax;

	public int CollageWidth;

	public int Quality;

	public static Preset Get(string name)
	{
		if (name == "high")
		{
			Preset preset = new Preset();
			preset.FrameMax = 0;
			preset.CollageWidth = 3600;
			preset.Quality = 92;
			return preset;
		}
		if (name == "standard")
		{
			Preset preset2 = new Preset();
			preset2.FrameMax = 1920;
			preset2.CollageWidth = 3000;
			preset2.Quality = 85;
			return preset2;
		}
		Preset preset3 = new Preset();
		preset3.FrameMax = 1280;
		preset3.CollageWidth = 2400;
		preset3.Quality = 80;
		return preset3;
	}
}
