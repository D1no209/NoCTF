using System.Globalization;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runner.Messages;

public sealed record ExpandedRuntimeAccess(
    IReadOnlyList<RuntimeAccessEndpointMapping> AccessEndpoints)
{
    public IReadOnlyList<string> DirectAddresses => AccessEndpoints
        .Select(endpoint => endpoint.DirectAddress)
        .OfType<string>()
        .ToArray();
}

public static class RuntimeUrlExpander
{
    public static ExpandedRuntimeAccess ExpandContainer(
        ContainerDeploymentReceipt receipt,
        ContainerRuntimeStatus status,
        IReadOnlyList<RuntimeUrlBinding>? bindings)
    {
        var endpoints = new List<RuntimeAccessEndpointMapping>();
        var index = 0;
        foreach (var binding in bindings ?? [])
        {
            var service = FindContainerService(status, binding);
            var containerPort = binding.ContainerPort
                ?? throw new InvalidOperationException(
                    "Container URL binding requires ContainerPort.");
            if (!service.PublishedPorts.TryGetValue(containerPort, out var publicPort)
                || publicPort is < 1 or > 65535)
                throw new InvalidOperationException(
                    "Container URL binding has no dynamic public port.");
            var url = ExpandAccessUrl(binding.UrlTemplate, receipt.PublicHost, publicPort);
            endpoints.Add(new(index++, url, null, null));
        }
        return ToResult(endpoints);
    }

    public static ExpandedRuntimeAccess ExpandContainer(
        ContainerDeploymentReceipt receipt,
        ContainerRuntimeStatus status,
        IReadOnlyList<RuntimeUrlBinding>? bindings,
        RuntimeAccessMode accessMode)
    {
        var endpoints = new List<RuntimeAccessEndpointMapping>();
        var index = 0;
        foreach (var binding in bindings ?? [])
        {
            var service = FindContainerService(status, binding);
            var containerPort = binding.ContainerPort
                ?? throw new InvalidOperationException("Container URL binding requires ContainerPort.");
            string? directAddress = null;
            if (PublishesDirect(accessMode))
            {
                if (!service.PublishedPorts.TryGetValue(containerPort, out var publicPort)
                    || publicPort is < 1 or > 65535)
                    throw new InvalidOperationException(
                        "Container URL binding has no dynamic public port.");
                directAddress = ExpandAccessUrl(
                    binding.UrlTemplate,
                    receipt.PublicHost,
                    publicPort);
            }
            endpoints.Add(new(
                index++,
                directAddress,
                SupportsWsrx(accessMode)
                    ? RequiredInternalHost(service.InternalHost)
                    : null,
                SupportsWsrx(accessMode) ? containerPort : null));
        }
        return ToResult(endpoints);
    }

    public static ExpandedRuntimeAccess ExpandOva(
        OvaRuntimeReceipt receipt,
        IReadOnlyList<RuntimeUrlBinding>? bindings)
    {
        var addresses = new List<string>();
        foreach (var binding in bindings ?? [])
        {
            var machine = FindOvaVirtualMachine(receipt, binding);
            var address = ExpandOvaBinding(machine, binding);
            addresses.Add(address);
        }
        return new(addresses.Select((address, index) =>
            new RuntimeAccessEndpointMapping(index, address, null, null)).ToArray());
    }

    private static ContainerServiceStatus FindContainerService(
        ContainerRuntimeStatus status,
        RuntimeUrlBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.ServiceName))
            throw new InvalidOperationException(
                "Container URL binding requires ServiceName.");
        return status.Services.SingleOrDefault(service =>
                   string.Equals(
                       service.Name,
                       binding.ServiceName,
                       StringComparison.Ordinal))
               ?? throw new InvalidOperationException(
                   $"Runtime service '{binding.ServiceName}' was not found.");
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

    private static ExpandedRuntimeAccess ToResult(
        IReadOnlyList<RuntimeAccessEndpointMapping> endpoints) =>
        new(endpoints);

    private static string RequiredInternalHost(string? host) =>
        !string.IsNullOrWhiteSpace(host)
            ? host
            : throw new InvalidOperationException(
                "Runtime receipt does not contain an internal proxy host.");

    private static bool PublishesDirect(RuntimeAccessMode? requestedMode) =>
        requestedMode is null or RuntimeAccessMode.Direct or RuntimeAccessMode.DirectAndWsrx;

    private static bool SupportsWsrx(RuntimeAccessMode mode) =>
        mode is RuntimeAccessMode.DirectAndWsrx or RuntimeAccessMode.WsrxOnly;

}
