using System.Globalization;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Runner.Messages;

public sealed record ExpandedRuntimeUrls(
    IReadOnlyList<string> Urls,
    IReadOnlyList<int> ParticipantUrlIndexes,
    string? ControlCheckUrl,
    string? AwdCheckerTargetUrl,
    string? AwdCheckerTargetServiceName);

public static class RuntimeUrlExpander
{
    public static ExpandedRuntimeUrls ExpandContainer(
        ContainerReceipt receipt,
        IReadOnlyList<RuntimeUrlBinding>? bindings,
        RuntimeUrlBinding? controlCheckBinding,
        RuntimeInternalEndpointBinding? awdCheckerTargetBinding = null)
    {
        var urls = new List<string>();
        var participantIndexes = new List<int>();
        foreach (var binding in bindings ?? [])
        {
            var url = ExpandPublicContainerBinding(receipt, binding);
            if (binding.Exposure == RuntimeExposure.Participants)
                participantIndexes.Add(urls.Count);
            urls.Add(url);
        }

        var controlCheckUrl = controlCheckBinding is null
            ? null
            : ExpandInternalContainerBinding(receipt, controlCheckBinding);
        var awdCheckerTargetUrl = awdCheckerTargetBinding is null
            ? null
            : ExpandInternalContainerBinding(receipt, awdCheckerTargetBinding);
        return new(
            urls,
            participantIndexes,
            controlCheckUrl,
            awdCheckerTargetUrl,
            null);
    }

    public static ExpandedRuntimeUrls ExpandCompose(
        ComposeReceipt receipt,
        ComposeStatus status,
        IReadOnlyList<RuntimeUrlBinding>? bindings,
        RuntimeUrlBinding? controlCheckBinding,
        RuntimeInternalEndpointBinding? awdCheckerTargetBinding = null)
    {
        var urls = new List<string>();
        var participantIndexes = new List<int>();
        foreach (var binding in bindings ?? [])
        {
            var service = FindComposeService(status, binding);
            var containerPort = binding.ContainerPort
                ?? throw new InvalidOperationException(
                    "Compose URL binding requires ContainerPort.");
            if (!service.PublishedPorts.TryGetValue(containerPort, out var publicPort)
                || publicPort is < 1 or > 65535)
                throw new InvalidOperationException(
                    "Compose URL binding has no dynamic public port.");
            var url = Expand(binding.UrlTemplate, receipt.PublicHost, publicPort);
            if (binding.Exposure == RuntimeExposure.Participants)
                participantIndexes.Add(urls.Count);
            urls.Add(url);
        }

        string? controlCheckUrl = null;
        if (controlCheckBinding is not null)
        {
            var service = FindComposeService(status, controlCheckBinding);
            var containerPort = controlCheckBinding.ContainerPort
                ?? throw new InvalidOperationException(
                    "Compose control URL binding requires ContainerPort.");
            controlCheckUrl = Expand(
                controlCheckBinding.UrlTemplate,
                service.InternalHost,
                containerPort);
        }
        string? awdCheckerTargetUrl = null;
        if (awdCheckerTargetBinding is not null)
        {
            var service = FindComposeService(status, awdCheckerTargetBinding.ServiceName);
            awdCheckerTargetUrl = Expand(
                awdCheckerTargetBinding.UrlTemplate,
                service.InternalHost,
                awdCheckerTargetBinding.ContainerPort);
        }
        return new(
            urls,
            participantIndexes,
            controlCheckUrl,
            awdCheckerTargetUrl,
            awdCheckerTargetBinding?.ServiceName);
    }

    public static ExpandedRuntimeUrls ExpandOva(
        OvaRuntimeReceipt receipt,
        IReadOnlyList<RuntimeUrlBinding>? bindings,
        RuntimeUrlBinding? controlCheckBinding)
    {
        var urls = new List<string>();
        var participantIndexes = new List<int>();
        foreach (var binding in bindings ?? [])
        {
            var machine = FindOvaVirtualMachine(receipt, binding);
            var url = ExpandOvaBinding(machine, binding);
            if (binding.Exposure == RuntimeExposure.Participants)
                participantIndexes.Add(urls.Count);
            urls.Add(url);
        }

        var controlCheckUrl = controlCheckBinding is null
            ? null
            : ExpandOvaBinding(
                FindOvaVirtualMachine(receipt, controlCheckBinding),
                controlCheckBinding);
        return new(urls, participantIndexes, controlCheckUrl, null, null);
    }

    private static ComposeServiceStatus FindComposeService(
        ComposeStatus status,
        RuntimeUrlBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.ServiceName))
            throw new InvalidOperationException(
                "Compose URL binding requires ServiceName.");
        return status.Services.SingleOrDefault(service =>
                   string.Equals(
                       service.Name,
                       binding.ServiceName,
                       StringComparison.Ordinal))
               ?? throw new InvalidOperationException(
                   $"Compose service '{binding.ServiceName}' was not found.");
    }

    private static ComposeServiceStatus FindComposeService(
        ComposeStatus status,
        string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new InvalidOperationException(
                "Compose internal endpoint requires ServiceName.");
        return status.Services.SingleOrDefault(service =>
                   string.Equals(service.Name, serviceName, StringComparison.Ordinal))
               ?? throw new InvalidOperationException(
                   $"Compose service '{serviceName}' was not found.");
    }

    private static OvaVirtualMachineReceipt FindOvaVirtualMachine(
        OvaRuntimeReceipt receipt,
        RuntimeUrlBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.VmId))
            throw new InvalidOperationException("OVA URL binding requires VmId.");
        return receipt.VirtualMachines.SingleOrDefault(machine =>
                   string.Equals(machine.VmId, binding.VmId, StringComparison.Ordinal))
               ?? throw new InvalidOperationException(
                   $"OVA virtual machine '{binding.VmId}' was not found.");
    }

    private static string ExpandOvaBinding(
        OvaVirtualMachineReceipt machine,
        RuntimeUrlBinding binding)
    {
        if (string.IsNullOrWhiteSpace(machine.Address))
            throw new InvalidOperationException(
                "OVA virtual machine receipt does not contain an address.");
        if (binding.GuestPort is null
            && binding.UrlTemplate.Contains("{PORT}", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "OVA URL binding cannot expand PORT without GuestPort.");
        var expanded = binding.UrlTemplate.Replace(
            "{HOST}",
            machine.Address,
            StringComparison.Ordinal);
        if (binding.GuestPort is int guestPort)
        {
            expanded = expanded.Replace(
                "{PORT}",
                guestPort.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }
        if (!Uri.TryCreate(expanded, UriKind.Absolute, out _))
            throw new InvalidOperationException(
                "OVA URL binding did not expand to an absolute URI.");
        return expanded;
    }

    private static string ExpandPublicContainerBinding(
        ContainerReceipt receipt,
        RuntimeUrlBinding binding)
    {
        var containerPort = binding.ContainerPort
            ?? throw new InvalidOperationException("Container URL binding requires ContainerPort.");
        if (!receipt.PortMappings.TryGetValue(containerPort, out var publicPort)
            || publicPort is < 1 or > 65535)
            throw new InvalidOperationException("Container URL binding has no dynamic public port.");
        return Expand(
            binding.UrlTemplate,
            receipt.PublicHost,
            publicPort);
    }

    private static string ExpandInternalContainerBinding(
        ContainerReceipt receipt,
        RuntimeUrlBinding binding)
    {
        var containerPort = binding.ContainerPort
            ?? throw new InvalidOperationException("Control URL binding requires ContainerPort.");
        return Expand(
            binding.UrlTemplate,
            receipt.InternalHost,
            containerPort);
    }

    private static string ExpandInternalContainerBinding(
        ContainerReceipt receipt,
        RuntimeInternalEndpointBinding binding) =>
        Expand(binding.UrlTemplate, receipt.InternalHost, binding.ContainerPort);

    private static string Expand(string template, string? host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("Runtime receipt does not contain the required host.");
        var expanded = template
            .Replace("{HOST}", host, StringComparison.Ordinal)
            .Replace(
                "{PORT}",
                port.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        if (!Uri.TryCreate(expanded, UriKind.Absolute, out _))
            throw new InvalidOperationException("Runtime URL binding did not expand to an absolute URI.");
        return expanded;
    }
}
