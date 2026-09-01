using System.Globalization;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Runner.Messages;

public sealed record ExpandedRuntimeUrls(IReadOnlyList<string> Urls);

public static class RuntimeUrlExpander
{
    public static ExpandedRuntimeUrls ExpandContainer(
        ContainerReceipt receipt,
        IReadOnlyList<RuntimeUrlBinding>? bindings)
    {
        var urls = new List<string>();
        foreach (var binding in bindings ?? [])
        {
            var url = ExpandPublicContainerBinding(receipt, binding);
            urls.Add(url);
        }
        return new(urls);
    }

    public static ExpandedRuntimeUrls ExpandCompose(
        ComposeReceipt receipt,
        ComposeStatus status,
        IReadOnlyList<RuntimeUrlBinding>? bindings)
    {
        var urls = new List<string>();
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
            var url = ExpandAccessUrl(binding.UrlTemplate, receipt.PublicHost, publicPort);
            urls.Add(url);
        }
        return new(urls);
    }

    public static ExpandedRuntimeUrls ExpandOva(
        OvaRuntimeReceipt receipt,
        IReadOnlyList<RuntimeUrlBinding>? bindings)
    {
        var urls = new List<string>();
        foreach (var binding in bindings ?? [])
        {
            var machine = FindOvaVirtualMachine(receipt, binding);
            var url = ExpandOvaBinding(machine, binding);
            urls.Add(url);
        }
        return new(urls);
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
        return ExpandAccessUrl(
            binding.UrlTemplate,
            receipt.PublicHost,
            publicPort);
    }

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
        return expanded;
    }

    private static string ExpandAccessUrl(string template, string? host, int port) =>
        Expand(template, host, port);

}
