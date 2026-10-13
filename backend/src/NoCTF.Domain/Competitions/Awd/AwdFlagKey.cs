using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoCTF.Domain.Competitions.Awd;

[Table("awd_flag_keys")]
public sealed class AwdFlagKey
{
    [Key] public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public int Version { get; set; }
    [MaxLength(256)] public byte[] ProtectedSecret { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}
