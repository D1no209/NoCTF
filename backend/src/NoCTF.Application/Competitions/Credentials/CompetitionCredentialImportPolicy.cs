using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Competitions.Credentials;

public static class CompetitionCredentialImportPolicy
{
    public static CompetitionTrackDefinition? ResolveTrack(Competition competition, string? requestedKey)
    {
        var tracks = CompetitionTrackConfiguration.FromPersisted(competition.Mode, competition.Tracks);
        if (!competition.TracksEnabled) return tracks.DefaultTrack;
        var track = tracks.Find(requestedKey);
        return track is { IsInternal: false } ? track : null;
    }

    public static List<CompetitionCredentialImportIssue> ValidateRows(
        IReadOnlyList<CompetitionCredentialImportRow> rows, Competition competition)
    {
        var issues = new List<CompetitionCredentialImportIssue>();
        foreach (var row in rows)
        {
            var username = row.UserName.Trim();
            if (username.Length is < 3 or > 64 || !username.All(value => char.IsAsciiLetterOrDigit(value) || value is '_' or '-'))
                issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.InvalidValue, "username"));
            if (!string.IsNullOrWhiteSpace(row.Email))
            {
                var email = EmailCanonicalizer.Canonicalize(row.Email);
                if (email.Length > 320 || !new EmailAddressAttribute().IsValid(email))
                    issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.InvalidValue, "email"));
            }
            if (row.TeamName.Trim().Length == 0)
                issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.MissingValue, "teamName"));
            if (row.TeamName.Trim().Length > 128)
                issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.ValueTooLong, "teamName"));
            if (row.FullName?.Trim().Length > 100)
                issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.ValueTooLong, "fullName"));
            if (row.StudentNumber?.Trim().Length > 64)
                issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.ValueTooLong, "studentNumber"));
            if (row.School?.Trim().Length > 200)
                issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.ValueTooLong, "school"));
            if (ResolveTrack(competition, row.TrackKey) is null)
                issues.Add(new(row.RowNumber, CompetitionCredentialImportIssueCode.InvalidTrackKey, row.TrackKey ?? string.Empty));
        }
        foreach (var duplicate in rows.GroupBy(row => row.UserName.Trim(), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            issues.AddRange(duplicate.Select(row => new CompetitionCredentialImportIssue(row.RowNumber,
                CompetitionCredentialImportIssueCode.DuplicateUserName, row.UserName)));
        foreach (var duplicate in rows.Where(row => !string.IsNullOrWhiteSpace(row.Email))
                     .GroupBy(row => EmailCanonicalizer.Canonicalize(row.Email!), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            issues.AddRange(duplicate.Select(row => new CompetitionCredentialImportIssue(row.RowNumber,
                CompetitionCredentialImportIssueCode.DuplicateEmail, row.Email!)));
        foreach (var team in rows.GroupBy(row => row.TeamName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            if (team.Count(row => row.Captain) != 1)
                issues.AddRange(team.Select(row => new CompetitionCredentialImportIssue(row.RowNumber, CompetitionCredentialImportIssueCode.TeamCaptainConflict, team.Key)));
            if (team.Count() > competition.MaxTeamMembers)
                issues.AddRange(team.Select(row => new CompetitionCredentialImportIssue(row.RowNumber, CompetitionCredentialImportIssueCode.TeamMemberLimitExceeded, team.Key)));
            if (competition.TracksEnabled && team.Select(row => CompetitionTrackConfiguration.NormalizeKey(row.TrackKey)).Distinct().Count() > 1)
                issues.AddRange(team.Select(row => new CompetitionCredentialImportIssue(row.RowNumber, CompetitionCredentialImportIssueCode.InvalidTrackKey, team.Key)));
        }
        return issues;
    }
}
