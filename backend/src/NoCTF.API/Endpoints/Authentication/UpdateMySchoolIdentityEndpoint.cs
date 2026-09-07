using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Privacy;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class UpdateMySchoolIdentityRequest
{
    public string? FullName { get; set; }
    public string? StudentNumber { get; set; }
}
public sealed class UpdateMySchoolIdentityValidator : Validator<UpdateMySchoolIdentityRequest>
{
    public UpdateMySchoolIdentityValidator()
    {
        RuleFor(x => x.FullName).MaximumLength(100).WithMessage("姓名最多 100 个字符。");
        RuleFor(x => x.StudentNumber).MaximumLength(64).WithMessage("学号最多 64 个字符。");
        RuleFor(x => x.FullName).Must(value => value?.Any(char.IsControl) != true).WithMessage("姓名不能包含控制字符。");
        RuleFor(x => x.StudentNumber).Must(value => value?.Any(char.IsControl) != true).WithMessage("学号不能包含控制字符。");
    }
}
public sealed class UpdateMySchoolIdentityEndpoint(AccountPrivacy privacy, IUserContext user)
    : Endpoint<UpdateMySchoolIdentityRequest, Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Put("/auth/me/school-identity"); AuthSchemes("Bearer");
        Validator<UpdateMySchoolIdentityValidator>();
        Description(builder => builder.WithName("AuthenticationUpdateMySchoolIdentity"));
        Summary(summary => summary.Summary = "Updates or clears optional school identity without verifying it.");
    }
    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(UpdateMySchoolIdentityRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        return await privacy.SaveOwnAsync(user.UserId, new(request.FullName, request.StudentNumber), ct)
            ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
