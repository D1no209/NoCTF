using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Runtime.Docker.Containers;

public sealed record DockerRuntimeOptions(
    string Endpoint = "npipe://./pipe/docker_engine",
    string NetworkName = "noctf-challenges",
    string PublicHost = "localhost",
    string CallbackContainerName = "",
    string CallbackContainerLabelKey = "noctf.io/internal-role",
    string CallbackContainerLabelValue = "scoring-callback-gateway",
    long RuntimeLogMaxSizeBytes = 10_485_760,
    int RuntimeLogMaxFiles = 3,
    int OneShotOutputLimitBytesPerStream = 1_048_576,
    string ProxyContainerName = "",
    string ProxyContainerLabelKey = "noctf.io/runtime-proxy-gateway",
    string ProxyContainerLabelValue = "true",
    string? RegistryConfigDirectory = null,
    string CallbackNetworkName = "noctf-runtime-callback",
    string ExecutionNetworkName = "",
    string ExecutionProxyContainerName = "",
    string ExecutionProbeImage = "nginx@sha256:df221db836e1754089190208cee7eeda94f233197056426eda74a43ab1abeac2");
