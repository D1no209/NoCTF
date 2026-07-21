namespace NoCTF.Runtime.Kubernetes.Configuration;

public sealed record KubernetesRuntimeOptions(
    string Namespace = "noctf",
    string PublicHost = "localhost",
    string ImagePullPolicy = "IfNotPresent");
