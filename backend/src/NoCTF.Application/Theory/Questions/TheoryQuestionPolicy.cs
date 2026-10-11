using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Application.Theory.Questions;

public enum TheoryQuestionFailure
{
    InvalidKind, InvalidDifficulty, TitleRequired, BodyRequired, InvalidOptionCount,
    InvalidOptionIdentity, EmptyOption, InvalidCorrectCount, InvalidTruthOptions,
    InvalidClassification, DuplicateClassification, InvalidFileReference
}

public static class TheoryQuestionPolicy
{
    public static TheoryQuestionFailure? Validate(TheoryQuestionRevision revision)
    {
        if (!Enum.IsDefined(revision.Kind)) return TheoryQuestionFailure.InvalidKind;
        if (!Enum.IsDefined(revision.Difficulty)) return TheoryQuestionFailure.InvalidDifficulty;
        if (string.IsNullOrWhiteSpace(revision.Title) || revision.Title.Length > 160)
            return TheoryQuestionFailure.TitleRequired;
        if (string.IsNullOrWhiteSpace(revision.Body) || revision.Body.Length > 64000
            || revision.Explanation?.Length > 64000) return TheoryQuestionFailure.BodyRequired;
        if (revision.Options.Count is < 2 or > 8) return TheoryQuestionFailure.InvalidOptionCount;
        if (revision.Options.Any(option => option.Id == Guid.Empty)
            || revision.Options.Select(option => option.Id).Distinct().Count() != revision.Options.Count
            || !revision.Options.Select(option => option.Order).Order().SequenceEqual(Enumerable.Range(0, revision.Options.Count)))
            return TheoryQuestionFailure.InvalidOptionIdentity;
        if (revision.Options.Any(option => string.IsNullOrWhiteSpace(option.Content) || option.Content.Length > 16000))
            return TheoryQuestionFailure.EmptyOption;
        var correct = revision.Options.Count(option => option.IsCorrect);
        if (revision.Kind == TheoryQuestionKind.MultipleChoice ? correct < 2 : correct != 1)
            return TheoryQuestionFailure.InvalidCorrectCount;
        if (revision.Kind == TheoryQuestionKind.TrueFalse)
        {
            if (revision.Options.Count != 2
                || !revision.Options.Select(option => option.TruthValue).ToHashSet()
                    .SetEquals([TheoryTruthValue.True, TheoryTruthValue.False]))
                return TheoryQuestionFailure.InvalidTruthOptions;
        }
        else if (revision.Options.Any(option => option.TruthValue is not null))
            return TheoryQuestionFailure.InvalidTruthOptions;
        if (revision.Classifications.Any(item => !Enum.IsDefined(item.Kind)
            || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 80
            || item.Name != item.Name.Trim() || item.NormalizedName != Normalize(item.Name)))
            return TheoryQuestionFailure.InvalidClassification;
        if (revision.Classifications.Select(item => (item.Kind, item.NormalizedName)).Distinct().Count()
            != revision.Classifications.Count) return TheoryQuestionFailure.DuplicateClassification;
        if (revision.Files.Any(item => item.FileId == Guid.Empty)
            || revision.Files.Select(item => item.FileId).Distinct().Count() != revision.Files.Count)
            return TheoryQuestionFailure.InvalidFileReference;
        return null;
    }

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    public static bool Allows(TheoryQuestion question, Guid actorId, bool isAdministrator,
        TheoryQuestionPermission required) => isAdministrator || question.OwnerId == actorId
        || question.Grants.Any(grant => grant.UserId == actorId && grant.Permission >= required);
}
