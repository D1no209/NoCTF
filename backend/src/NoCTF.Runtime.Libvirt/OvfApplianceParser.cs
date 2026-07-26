using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Runtime.Libvirt;

public sealed record OvfDiskPlan(string DiskId, string SourcePath);

public sealed record OvfVirtualMachinePlan(
    string VmId,
    int VirtualCpuCount,
    long MemoryBytes,
    IReadOnlyList<OvfDiskPlan> Disks);

public sealed record OvfAppliancePlan(
    IReadOnlyList<OvfVirtualMachinePlan> VirtualMachines);

public static class OvfApplianceParser
{
    public static OvfAppliancePlan Parse(
        ExtractedOvaArchive archive,
        RuntimeResourceLimits limits)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 16 * 1024 * 1024
        };
        XDocument document;
        using (var reader = XmlReader.Create(archive.DescriptorPath, settings))
            document = XDocument.Load(reader, LoadOptions.None);

        var envelope = document.Root;
        if (envelope is null || envelope.Name.LocalName != "Envelope")
            throw new InvalidOperationException("OVF descriptor must contain an Envelope root.");
        if (envelope.Descendants().Any(element =>
                element.Name.LocalName is "ScaleOutSection" or "DeploymentOptionSection"))
            throw new InvalidOperationException(
                "OVF scale-out and deployment options are not supported.");

        var contents = envelope.Elements()
            .Where(element => element.Name.LocalName is "VirtualSystem"
                or "VirtualSystemCollection")
            .ToArray();
        if (contents.Length != 1)
            throw new InvalidOperationException(
                "OVF must contain exactly one appliance content root.");

        XElement[] systems;
        if (contents[0].Name.LocalName == "VirtualSystem")
        {
            systems = [contents[0]];
        }
        else
        {
            if (contents[0].Elements().Any(element =>
                    element.Name.LocalName == "VirtualSystemCollection"))
                throw new InvalidOperationException(
                    "Nested OVF virtual system collections are not supported.");
            systems = contents[0].Elements()
                .Where(element => element.Name.LocalName == "VirtualSystem")
                .ToArray();
        }
        if (systems.Length == 0)
            throw new InvalidOperationException("OVF appliance does not contain any virtual machines.");

        var files = ReadFileReferences(envelope, archive.RootDirectory);
        var disks = ReadDiskReferences(envelope, files);
        var vmIds = new HashSet<string>(StringComparer.Ordinal);
        var virtualMachines = new List<OvfVirtualMachinePlan>(systems.Length);
        foreach (var system in systems)
        {
            var vmId = Attribute(system, "id");
            if (string.IsNullOrWhiteSpace(vmId) || !vmIds.Add(vmId))
                throw new InvalidOperationException(
                    "Every OVF virtual machine must have a unique non-empty ovf:id.");
            virtualMachines.Add(ParseVirtualMachine(system, vmId, disks));
        }

        ValidateAggregateResources(virtualMachines, limits);
        return new(virtualMachines);
    }

    private static IReadOnlyDictionary<string, string> ReadFileReferences(
        XElement envelope,
        string archiveRoot)
    {
        var root = Path.GetFullPath(archiveRoot);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in envelope.Descendants()
                     .Where(element => element.Name.LocalName == "File"))
        {
            var id = Attribute(file, "id");
            var href = Attribute(file, "href");
            if (string.IsNullOrWhiteSpace(id)
                || string.IsNullOrWhiteSpace(href)
                || Attribute(file, "compression") is not null
                || Uri.TryCreate(href, UriKind.Absolute, out _))
                throw new InvalidOperationException("OVF contains an invalid file reference.");
            var relativePath = Uri.UnescapeDataString(href)
                .Replace('/', Path.DirectorySeparatorChar);
            if (relativePath.Contains('\\', StringComparison.Ordinal)
                && Path.DirectorySeparatorChar != '\\')
                throw new InvalidOperationException("OVF contains an invalid file path.");
            var path = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!path.StartsWith(rootPrefix, StringComparison.Ordinal)
                || !File.Exists(path)
                || !files.TryAdd(id, path))
                throw new InvalidOperationException(
                    "OVF file reference is missing, duplicated, or escapes the appliance.");
        }
        return files;
    }

    private static IReadOnlyDictionary<string, OvfDiskPlan> ReadDiskReferences(
        XElement envelope,
        IReadOnlyDictionary<string, string> files)
    {
        var disks = new Dictionary<string, OvfDiskPlan>(StringComparer.Ordinal);
        foreach (var disk in envelope.Descendants()
                     .Where(element => element.Name.LocalName == "Disk"))
        {
            var diskId = Attribute(disk, "diskId");
            var fileRef = Attribute(disk, "fileRef");
            if (string.IsNullOrWhiteSpace(diskId)
                || string.IsNullOrWhiteSpace(fileRef)
                || !files.TryGetValue(fileRef, out var path)
                || !disks.TryAdd(diskId, new(diskId, path)))
                throw new InvalidOperationException("OVF contains an invalid disk reference.");
        }
        return disks;
    }

    private static OvfVirtualMachinePlan ParseVirtualMachine(
        XElement system,
        string vmId,
        IReadOnlyDictionary<string, OvfDiskPlan> disks)
    {
        int? virtualCpuCount = null;
        long? memoryBytes = null;
        var machineDisks = new List<OvfDiskPlan>();
        foreach (var item in system.Descendants()
                     .Where(element => element.Name.LocalName == "Item"))
        {
            var resourceType = ChildValue(item, "ResourceType");
            switch (resourceType)
            {
                case "3":
                    if (virtualCpuCount is not null)
                        throw new InvalidOperationException(
                            $"OVF virtual machine '{vmId}' declares CPU more than once.");
                    virtualCpuCount = ParseVirtualCpuCount(ChildValue(item, "VirtualQuantity"));
                    break;
                case "4":
                    if (memoryBytes is not null)
                        throw new InvalidOperationException(
                            $"OVF virtual machine '{vmId}' declares memory more than once.");
                    memoryBytes = ParseMemory(
                        ChildValue(item, "VirtualQuantity"),
                        ChildValue(item, "AllocationUnits"));
                    break;
                case "17":
                    var diskId = ParseDiskId(ChildValue(item, "HostResource"));
                    if (!disks.TryGetValue(diskId, out var disk))
                        throw new InvalidOperationException(
                            $"OVF virtual machine '{vmId}' references unknown disk '{diskId}'.");
                    machineDisks.Add(disk);
                    break;
            }
        }

        if (virtualCpuCount is null || memoryBytes is null || machineDisks.Count == 0)
            throw new InvalidOperationException(
                $"OVF virtual machine '{vmId}' must declare CPU, memory, and at least one disk.");
        if (machineDisks.Select(disk => disk.DiskId).Distinct(StringComparer.Ordinal).Count()
            != machineDisks.Count)
            throw new InvalidOperationException(
                $"OVF virtual machine '{vmId}' references the same disk more than once.");
        return new(vmId, virtualCpuCount.Value, memoryBytes.Value, machineDisks);
    }

    private static int ParseVirtualCpuCount(string? value)
    {
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var count)
            || count <= 0)
            throw new InvalidOperationException("OVF virtual CPU quantity must be a positive integer.");
        return count;
    }

    private static long ParseMemory(string? quantityText, string? unitsText)
    {
        if (!long.TryParse(
                quantityText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var quantity)
            || quantity <= 0
            || string.IsNullOrWhiteSpace(unitsText))
            throw new InvalidOperationException("OVF memory quantity and allocation units are required.");
        var normalizedUnits = string.Concat(
            unitsText.Where(character => !char.IsWhiteSpace(character)))
            .ToLowerInvariant();
        var multiplier = normalizedUnits switch
        {
            "byte" => 1L,
            "byte*2^10" => 1L << 10,
            "byte*2^20" => 1L << 20,
            "byte*2^30" => 1L << 30,
            _ => throw new InvalidOperationException(
                $"OVF memory allocation unit '{unitsText}' is not supported.")
        };
        var bytes = checked(quantity * multiplier);
        if (bytes % (1024 * 1024) != 0)
            throw new InvalidOperationException(
                "OVF memory allocation must resolve to whole MiB units.");
        return bytes;
    }

    private static string ParseDiskId(string? hostResource)
    {
        const string marker = "/disk/";
        if (string.IsNullOrWhiteSpace(hostResource))
            throw new InvalidOperationException("OVF disk HostResource is required.");
        var markerIndex = hostResource.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0 || markerIndex + marker.Length == hostResource.Length)
            throw new InvalidOperationException("OVF disk HostResource is invalid.");
        return Uri.UnescapeDataString(hostResource[(markerIndex + marker.Length)..]);
    }

    private static void ValidateAggregateResources(
        IReadOnlyList<OvfVirtualMachinePlan> machines,
        RuntimeResourceLimits limits)
    {
        try
        {
            var memory = machines.Sum(machine => machine.MemoryBytes);
            var nanoCpus = machines.Sum(machine =>
                checked((long)machine.VirtualCpuCount * 1_000_000_000L));
            if (memory > limits.MemoryBytes || nanoCpus > limits.NanoCpus)
                throw new InvalidOperationException(
                    "OVF virtual machine resources exceed the Runtime total limits.");
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException(
                "OVF aggregate resources exceed the supported range.",
                exception);
        }
    }

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == localName)
            ?.Value;

    private static string? ChildValue(XElement element, string localName) =>
        element.Elements()
            .SingleOrDefault(child => child.Name.LocalName == localName)
            ?.Value;
}
