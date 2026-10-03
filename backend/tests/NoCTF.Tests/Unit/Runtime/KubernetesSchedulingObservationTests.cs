using k8s.Models;
using NoCTF.Runtime.Kubernetes.Capacity;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class KubernetesSchedulingObservationTests
{
    [Test]
    public async Task Observation_excludes_unschedulable_nodes_and_completed_pods_and_counts_distinct_ports()
    {
        var ready = new V1Node
        {
            Spec = new(), Status = new()
            {
                Conditions = [new() { Type = "Ready", Status = "True" }],
                Allocatable = new Dictionary<string, ResourceQuantity> { ["pods"] = new("110") }
            }
        };
        var cordoned = new V1Node { Spec = new() { Unschedulable = true }, Status = ready.Status };
        V1Pod[] pods =
        [
            new() { Status = new() { Phase = "Succeeded" } },
            new() { Status = new() { Phase = "Running" } },
            new() { Status = new() { Phase = "Pending", Conditions = [new() { Type = "PodScheduled", Status = "False", Reason = "Unschedulable" }] } },
            new() { Status = new() { Phase = "Pending", ContainerStatuses = [new() { State = new() { Waiting = new() { Reason = "ImagePullBackOff" } } }] } }
        ];
        V1Service[] services = [new() { Spec = new() { Ports = [new() { NodePort = 30002 }, new() { NodePort = 30002 }, new() { Port = 80 }] } }];
        var result = KubernetesSchedulingObservation.Read([ready, cordoned], pods, services);
        await Assert.That(result).IsEqualTo(new KubernetesSchedulingObservation(110, 3, 2, 1, 1, 1));
    }
}
