namespace NoCTF.Runtime.Libvirt;

public sealed record LibvirtApplianceRequest(
    Guid RuntimeInstanceId,
    int Generation,
    Uri OvaSource,
    string NetworkName,
    TimeSpan AddressDiscoveryTimeout);

public sealed record LibvirtVirtualMachine(string VmId, string DomainName, string? Address);

public sealed record LibvirtApplianceReceipt(
    Guid RuntimeInstanceId,
    int Generation,
    string NetworkName,
    IReadOnlyList<LibvirtVirtualMachine> VirtualMachines);

public sealed class LibvirtApplianceLifecycle(ILibvirtProcessAdapter processes)
{
    public async Task<LibvirtApplianceReceipt> ImportAsync(
        LibvirtApplianceRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.OvaSource.IsAbsoluteUri)
        {
            throw new ArgumentException("OVA source must be an absolute URI.", nameof(request));
        }

        var importName = $"noctf-{request.RuntimeInstanceId:N}-{request.Generation}";
        var result = await processes.RunAsync(
            "virt-install",
            [
                "--name", importName,
                "--import",
                "--disk", $"path={request.OvaSource.LocalPath}",
                "--network", $"network={request.NetworkName}",
                "--noautoconsole"
            ],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("Libvirt rejected the appliance import.");
        }

        return new LibvirtApplianceReceipt(
            request.RuntimeInstanceId,
            request.Generation,
            request.NetworkName,
            [new(importName, importName, null)]);
    }

    public async Task DestroyAsync(
        LibvirtApplianceReceipt receipt,
        CancellationToken cancellationToken)
    {
        foreach (var machine in receipt.VirtualMachines)
        {
            await processes.RunAsync(
                "virsh",
                ["destroy", machine.DomainName],
                cancellationToken);
            await processes.RunAsync(
                "virsh",
                ["undefine", machine.DomainName, "--remove-all-storage", "--nvram"],
                cancellationToken);
        }

        await processes.RunAsync(
            "virsh",
            ["net-destroy", receipt.NetworkName],
            cancellationToken);
        await processes.RunAsync(
            "virsh",
            ["net-undefine", receipt.NetworkName],
            cancellationToken);
    }
}
