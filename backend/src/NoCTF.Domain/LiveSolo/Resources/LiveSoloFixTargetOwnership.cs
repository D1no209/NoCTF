using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.LiveSolo;

public sealed class LiveSoloFixTargetOwnership
{
    [Key] public Guid RuntimeInstanceId { get; set; }
    public Guid RoundQuestionId { get; set; }
    public LiveSoloSide Side { get; set; }
}
