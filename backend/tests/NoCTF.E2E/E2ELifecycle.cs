using System.Net;
using System.Text.Json;

namespace NoCTF.E2E;

internal static class E2ELifecycle
{
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
