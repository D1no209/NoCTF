using System.Reflection;
using System.Text.Json;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionPermissionProtocolTests
{
    private const string ApiNamespace =
        "NoCTF.API.Endpoints.Administration.Competitions";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Test]
    public async Task Permissions_response_is_complete_without_expanding_the_public_contract()
    {
        var responseType = ApiType("CompetitionPermissionsResponse");

        await Assert.That(responseType).IsNotNull();
        var properties = responseType!.GetProperties()
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
        await Assert.That(properties.Keys)
            .IsEquivalentTo([
                "CompetitionId",
                "OwnerId",
                "ManagerIds",
                "JudgeIds",
                "ObserverIds",
                "PermissionRevision"
            ]);
        await Assert.That(properties["CompetitionId"].PropertyType).IsEqualTo(typeof(Guid));
        await Assert.That(properties["OwnerId"].PropertyType).IsEqualTo(typeof(Guid));
        await Assert.That(IsGuidCollection(properties["ManagerIds"].PropertyType)).IsTrue();
        await Assert.That(IsGuidCollection(properties["JudgeIds"].PropertyType)).IsTrue();
        await Assert.That(IsGuidCollection(properties["ObserverIds"].PropertyType)).IsTrue();
        await Assert.That(properties["PermissionRevision"].PropertyType).IsEqualTo(typeof(int));

        var publicProperties = typeof(CompetitionResponse).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        await Assert.That(publicProperties).DoesNotContain("ManagerIds");
        await Assert.That(publicProperties).DoesNotContain("JudgeIds");
        await Assert.That(publicProperties).DoesNotContain("ObserverIds");
        await Assert.That(publicProperties).DoesNotContain("PermissionRevision");
    }

    [Test]
    public async Task Permission_candidate_contract_is_minimal_and_typed()
    {
        var candidateType = ApiType("CompetitionPermissionCandidateResponse");
        var listType = ApiType("CompetitionPermissionCandidateListResponse");

        await Assert.That(candidateType).IsNotNull();
        await Assert.That(listType).IsNotNull();

        var properties = candidateType!.GetProperties()
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
        await Assert.That(properties.Keys)
            .IsEquivalentTo(["Id", "UserName", "Kind", "Role", "EmailVerified"]);
        await Assert.That(properties["Id"].PropertyType).IsEqualTo(typeof(Guid));
        await Assert.That(properties["UserName"].PropertyType).IsEqualTo(typeof(string));
        await Assert.That(properties["Kind"].PropertyType).IsEqualTo(typeof(UserKind));
        await Assert.That(properties["Role"].PropertyType).IsEqualTo(typeof(UserRole));
        await Assert.That(properties["EmailVerified"].PropertyType).IsEqualTo(typeof(bool));

        var items = listType!.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance);
        await Assert.That(items).IsNotNull();
        await Assert.That(CollectionElementType(items!.PropertyType))
            .IsEqualTo(candidateType);
    }

    [Test]
    public async Task Update_request_requires_a_non_negative_permission_revision()
    {
        var revision = typeof(UpdateCompetitionPermissionsRequest)
            .GetProperty("ExpectedPermissionRevision", BindingFlags.Public | BindingFlags.Instance);

        await Assert.That(revision).IsNotNull();
        await Assert.That(Nullable.GetUnderlyingType(revision!.PropertyType) ?? revision.PropertyType)
            .IsEqualTo(typeof(int));

        var request = (UpdateCompetitionPermissionsRequest)Activator.CreateInstance(
            typeof(UpdateCompetitionPermissionsRequest))!;
        var validator = new UpdateCompetitionPermissionsValidator();
        var missingValidation = validator.Validate(request);
        await Assert.That(missingValidation.Errors
            .Any(error => error.PropertyName == "ExpectedPermissionRevision"))
            .IsTrue();

        revision.SetValue(request, -1);
        var negativeValidation = validator.Validate(request);

        await Assert.That(negativeValidation.Errors
            .Any(error => error.PropertyName == "ExpectedPermissionRevision"))
            .IsTrue();
    }

    [Test]
    [Arguments("RevisionConflict")]
    [Arguments("EmailNotVerified")]
    public async Task New_conflict_codes_serialize_as_named_enums(string name)
    {
        var code = Enum.Parse<CompetitionResourceManagerConflictCode>(name);

        var json = JsonSerializer.Serialize(code, JsonOptions);

        await Assert.That(json).IsEqualTo($"\"{name}\"");
    }

    private static Type? ApiType(string name) =>
        typeof(UpdateCompetitionPermissionsRequest).Assembly.GetType(
            $"{ApiNamespace}.{name}",
            throwOnError: false,
            ignoreCase: false);

    private static bool IsGuidCollection(Type type) =>
        type != typeof(string)
        && typeof(IEnumerable<Guid>).IsAssignableFrom(type);

    private static Type? CollectionElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();
        var enumerable = type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? type
                : type.GetInterfaces().SingleOrDefault(candidate =>
                    candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0];
    }
}
