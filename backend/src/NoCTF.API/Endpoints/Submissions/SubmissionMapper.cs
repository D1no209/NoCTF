using NoCTF.Application.Submissions.Intake;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Submissions;

[Mapper]
internal static partial class SubmissionMapper
{
    public static partial AcceptedSubmissionResponse ToResponse(SubmissionAccepted accepted);
}
