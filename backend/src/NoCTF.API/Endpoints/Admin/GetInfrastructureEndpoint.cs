using FastEndpoints;

namespace NoCTF.API.Endpoints.Admin;

public class InfrastructureDto
{
    public string RunnerProvider { get; set; } = "Docker";
    public string? RunnerBaseUrl { get; set; }
    public bool RunnerReachable { get; set; }
    public object? RunnerInfo { get; set; }
    public object Kubernetes { get; set; } = new { };
}

public class GetInfrastructureEndpoint(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : EndpointWithoutRequest<InfrastructureDto>
{
    public override void Configure()
    {
        Get("/api/admin/infrastructure");
        Roles("Admin");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var runnerBaseUrl = configuration["Runner:BaseUrl"];
        object? runnerInfo = null;
        var runnerReachable = false;
        if (!string.IsNullOrWhiteSpace(runnerBaseUrl))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var http = httpClientFactory.CreateClient();
                http.BaseAddress = new Uri(runnerBaseUrl);
                var runnerApiKey = configuration["Runner:ApiKey"];
                if (!string.IsNullOrWhiteSpace(runnerApiKey))
                    http.DefaultRequestHeaders.Add("X-Runner-Token", runnerApiKey);
                using var response = await http.GetAsync("/runner/info", timeout.Token);
                response.EnsureSuccessStatusCode();
                runnerInfo = await response.Content.ReadFromJsonAsync<object>(cancellationToken: timeout.Token);
                runnerReachable = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                runnerReachable = false;
            }
        }

        await SendAsync(new InfrastructureDto
        {
            RunnerProvider = configuration["Runner:Provider"] ?? ReadProviderFromInfo(runnerInfo) ?? "Docker",
            RunnerBaseUrl = runnerBaseUrl,
            RunnerReachable = runnerReachable,
            RunnerInfo = runnerInfo,
            Kubernetes = new
            {
                kubeConfigPath = configuration["K8s:KubeConfigPath"],
                publicEntry = configuration["K8s:PublicEntry"],
                defaultExposure = configuration["K8s:DefaultExposure"],
                ingressBaseDomain = configuration["K8s:IngressBaseDomain"],
                namespacePrefix = configuration["K8s:NamespacePrefix"],
                networkMode = configuration["K8s:NetworkMode"]
            }
        }, cancellation: ct);
    }

    private static string? ReadProviderFromInfo(object? info)
        => info?.ToString()?.Contains("Kubernetes", StringComparison.OrdinalIgnoreCase) == true
            ? "Kubernetes"
            : null;
}
