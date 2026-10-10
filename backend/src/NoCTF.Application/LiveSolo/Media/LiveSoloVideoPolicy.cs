namespace NoCTF.Application.LiveSolo.Media;

/// <summary>Source budget is per screen; the programme budget is for the one composed output.</summary>
public sealed record LiveSoloVideoPolicy(int MaximumWidth,int MaximumHeight,int MaximumFramesPerSecond,
    int MaximumBitrateBitsPerSecond,int ProgrammeBitrateBitsPerSecond,Guid PolicyStamp)
{
    public static LiveSoloVideoPolicy Default {get;}=new(NoCTF.Domain.LiveSolo.LiveSoloVideoLimits.DefaultWidth,NoCTF.Domain.LiveSolo.LiveSoloVideoLimits.DefaultHeight,
        NoCTF.Domain.LiveSolo.LiveSoloVideoLimits.DefaultFramesPerSecond,NoCTF.Domain.LiveSolo.LiveSoloVideoLimits.DefaultBitrateBitsPerSecond,
        NoCTF.Domain.LiveSolo.LiveSoloVideoLimits.DefaultBitrateBitsPerSecond,Guid.Parse("00000000-0000-0000-0000-000000000005"));
    public int ProgrammeBitrateKilobitsPerSecond=>ProgrammeBitrateBitsPerSecond/1000;
}
