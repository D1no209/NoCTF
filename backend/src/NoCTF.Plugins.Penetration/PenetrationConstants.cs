namespace NoCTF.Plugins.Penetration;

public static class PenetrationConstants
{
    public const string TypeId = "penetration";

    public const string TemplateTopologyGet = "penetration.template.topology.get";
    public const string TemplateTopologyUpdate = "penetration.template.topology.update";
    public const string TemplateTopologyRevealFlag = "penetration.template.topology.flag.reveal";
    public const string TemplateCloneToChallenge = "penetration.template.clone-to-challenge";

    public const string CompetitionTopologyGet = "penetration.competition.topology.get";
    public const string CompetitionTopologyUpdate = "penetration.competition.topology.update";

    public const string PlayerDetail = "penetration.player.detail";
    public const string PlayerInstanceGet = "penetration.player.instance.get";
    public const string PlayerInstanceStart = "penetration.player.instance.start";
    public const string PlayerInstanceStop = "penetration.player.instance.stop";
    public const string PlayerInstanceReset = "penetration.player.instance.reset";
    public const string PlayerInstanceDestroy = "penetration.player.instance.destroy";

    public const string AdminInstancesList = "penetration.admin.instances.list";
    public const string AdminInstanceGet = "penetration.admin.instance.get";
    public const string AdminInstanceReset = "penetration.admin.instance.reset";
    public const string AdminInstanceDestroy = "penetration.admin.instance.destroy";
}
