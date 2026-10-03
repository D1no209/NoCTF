using k8s.Models;

namespace NoCTF.Runtime.Kubernetes.Capacity;

/// <summary>Provider observations, not reservations or authoritative admission decisions.</summary>
public sealed record KubernetesSchedulingObservation(
    long AllocatablePodSlots, int ManagedPods, int PendingPods,
    int UnschedulablePods, int ImagePullBlockedPods, int PublishedNodePorts)
{
    public static KubernetesSchedulingObservation Read(
        IEnumerable<V1Node> nodes, IEnumerable<V1Pod> pods, IEnumerable<V1Service> services)
    {
        var eligible = nodes.Where(node => node.Spec?.Unschedulable != true
            && node.Status?.Conditions?.Any(condition => condition.Type == "Ready"
                && condition.Status == "True") == true);
        var slots = eligible.Sum(node => node.Status.Allocatable.TryGetValue("pods", out var value)
            ? checked((long)value.ToDecimal()) : 0);
        var active = pods.Where(pod => pod.Status?.Phase is not ("Succeeded" or "Failed")).ToArray();
        return new(slots, active.Length,
            active.Count(pod => pod.Status?.Phase == "Pending"),
            active.Count(pod => pod.Status?.Conditions?.Any(condition =>
                condition.Type == "PodScheduled" && condition.Status == "False"
                && condition.Reason == "Unschedulable") == true),
            active.Count(pod => pod.Status?.ContainerStatuses?.Any(status =>
                status.State?.Waiting?.Reason is "ImagePullBackOff" or "ErrImagePull") == true
                || pod.Status?.InitContainerStatuses?.Any(status =>
                    status.State?.Waiting?.Reason is "ImagePullBackOff" or "ErrImagePull") == true),
            services.SelectMany(service => service.Spec?.Ports ?? [])
                .Where(port => port.NodePort is > 0)
                .Select(port => port.NodePort!.Value).Distinct().Count());
    }
}
