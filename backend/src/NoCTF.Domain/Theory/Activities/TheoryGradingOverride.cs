using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Theory.Activities;

public enum TheoryCorrectionMode : short { ReplaceAnswer, FullCredit }

public sealed class TheoryGradingOverride : IConcurrencyTracked
{
    public Guid ActivityId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid TargetRevision { get; set; }
    public Guid? AppliedRevision { get; set; }
    public TheoryCorrectionMode Mode { get; set; }
    public List<TheoryGradingOption> Options { get; set; } = [];
}

public sealed class TheoryGradingOption
{
    public Guid ActivityId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid OptionId { get; set; }
}
