using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Runtime.Docker.Containers;

public sealed record DockerRuntimeOptions(
    string Endpoint = "npipe://./pipe/docker_engine",
    string NetworkName = "noctf",
    string PublicHost = "localhost",
    string CallbackContainerName = "noctf-awdp-callback",
    string CallbackContainerLabelKey = "noctf.io/internal-role",
    string CallbackContainerLabelValue = "awdp-callback-gateway",
    long RuntimeLogMaxSizeBytes = 10_485_760,
    int RuntimeLogMaxFiles = 3,
    int OneShotOutputLimitBytesPerStream = 1_048_576);
