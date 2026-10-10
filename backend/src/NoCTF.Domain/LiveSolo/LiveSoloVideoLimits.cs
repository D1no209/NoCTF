namespace NoCTF.Domain.LiveSolo;

public static class LiveSoloVideoLimits
{
    public const int DefaultWidth=1280,DefaultHeight=720,DefaultFramesPerSecond=10,DefaultBitrateBitsPerSecond=1_000_000;
    public const int MinimumWidth=320,MaximumWidth=3840,MinimumHeight=180,MaximumHeight=2160;
    public const int MinimumFramesPerSecond=1,MaximumFramesPerSecond=30,MinimumBitrateBitsPerSecond=128_000,MaximumBitrateBitsPerSecond=8_000_000;
    public static bool Valid(int width,int height,int fps,int bitrate)=>width is >=MinimumWidth and <=MaximumWidth&&width%2==0
        &&height is >=MinimumHeight and <=MaximumHeight&&height%2==0&&fps is >=MinimumFramesPerSecond and <=MaximumFramesPerSecond
        &&bitrate is >=MinimumBitrateBitsPerSecond and <=MaximumBitrateBitsPerSecond;
}
