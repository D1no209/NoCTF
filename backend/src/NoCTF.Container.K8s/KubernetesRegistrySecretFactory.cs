using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using k8s.Models;

namespace NoCTF.Container.K8s;

public static class KubernetesRegistrySecretFactory
{
    public static string SecretName(KubernetesRegistryCredential credential)
        => KubernetesNames.SafeName($"registry-{credential.UserName}-{KubernetesNames.ShortHash(credential.Registry)}", "registry-auth");

    public static V1Secret Build(string namespaceName, KubernetesRegistryCredential credential)
    {
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credential.UserName}:{credential.Password}"));
        var json = JsonSerializer.SerializeToUtf8Bytes(new DockerConfigJson(new Dictionary<string, DockerConfigAuth>
        {
            [credential.Registry] = new(auth, credential.UserName, credential.Password, credential.Email)
        }));

        return new V1Secret
        {
            Metadata = new V1ObjectMeta
            {
                Name = SecretName(credential),
                NamespaceProperty = namespaceName,
                Labels = KubernetesManifestFactory.CommonLabels("registry-secret", namespaceName)
            },
            Type = "kubernetes.io/dockerconfigjson",
            Data = new Dictionary<string, byte[]>
            {
                [".dockerconfigjson"] = json
            }
        };
    }

    private sealed record DockerConfigJson(
        [property: JsonPropertyName("auths")] Dictionary<string, DockerConfigAuth> Auths);

    private sealed record DockerConfigAuth(
        [property: JsonPropertyName("auth")] string Auth,
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("email")] string? Email);
}
