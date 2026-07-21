namespace NoCTF.Runtime.Docker.Containers;

public sealed record DockerRuntimeOptions(
    string Endpoint = "npipe://./pipe/docker_engine",
    string NetworkName = "noctf",
    string PublicHost = "localhost");
