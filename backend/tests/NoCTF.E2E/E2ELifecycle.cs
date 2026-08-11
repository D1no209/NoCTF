using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NoCTF.E2E;

internal static class E2ELifecycle
{
    public static async Task MakeScheduleDueAsync(
        HttpClient admin,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        using var currentResponse = await admin.GetAsync(
            $"/api/v1/admin/competitions/{competitionId}",
            cancellationToken);
        var currentBody = await currentResponse.Content.ReadAsStringAsync(cancellationToken);
        if (currentResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Expected 200 while reading competition schedule, but received {(int)currentResponse.StatusCode}: {currentBody}");
        }

        using var current = JsonDocument.Parse(currentBody);
        var root = current.RootElement;
        if (root.GetProperty("status").GetString() != "Visible")
        {
            throw new InvalidOperationException(
                $"Competition must remain Visible while its E2E setup is staged. Current response: {currentBody}");
        }

        using var updateResponse = await admin.PutAsJsonAsync(
            $"/api/v1/admin/competitions/{competitionId}",
            new
            {
                title = root.GetProperty("title").GetString(),
                description = root.GetProperty("description").GetString(),
                startTime = DateTimeOffset.UtcNow.AddMinutes(-1),
                endTime = root.GetProperty("endTime").GetDateTimeOffset(),
                teamRegistrationAutoApprove = root.GetProperty("teamRegistrationAutoApprove").GetBoolean(),
                allowTeamRegistrationWhileRunning = root.GetProperty("allowTeamRegistrationWhileRunning").GetBoolean(),
                maxTeamMembers = root.GetProperty("maxTeamMembers").GetInt32(),
                maxConcurrentRuntimeInstancesPerTeam = root.GetProperty("maxConcurrentRuntimeInstancesPerTeam").GetInt32(),
                maxActiveQuestionsPerTeam = root.GetProperty("maxActiveQuestionsPerTeam").GetInt32(),
                maxParticipantMessagesBeforeHandlerReply = root.GetProperty("maxParticipantMessagesBeforeHandlerReply").GetInt32(),
                allowChallengeOwnersToHandleQuestions = root.GetProperty("allowChallengeOwnersToHandleQuestions").GetBoolean()
            },
            cancellationToken);
        var updateBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
        if (updateResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Expected 200 while making competition schedule due, but received {(int)updateResponse.StatusCode}: {updateBody}");
        }

        using var updated = JsonDocument.Parse(updateBody);
        if (updated.RootElement.GetProperty("status").GetString() != "Visible")
        {
            throw new InvalidOperationException(
                $"Competition schedule update changed its lifecycle before publication. Update response: {updateBody}");
        }
    }

    public static async Task StartOrObserveRunningAsync(
        HttpClient admin,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/competitions/{competitionId}/start");
        using var response = await admin.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
            return;

        var failure = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            using var currentResponse = await admin.GetAsync(
                $"/api/v1/admin/competitions/{competitionId}",
                cancellationToken);
            var currentBody = await currentResponse.Content.ReadAsStringAsync(cancellationToken);
            if (currentResponse.StatusCode == HttpStatusCode.OK)
            {
                using var current = JsonDocument.Parse(currentBody);
                if (current.RootElement.GetProperty("status").GetString() == "Running")
                    return;
            }

            throw new InvalidOperationException(
                $"Competition start conflicted before reaching Running. Start response: {failure}; current response: {currentBody}");
        }

        throw new InvalidOperationException(
            $"Expected 204 or an already-Running 409 while starting competition, but received {(int)response.StatusCode}: {failure}");
    }
}
