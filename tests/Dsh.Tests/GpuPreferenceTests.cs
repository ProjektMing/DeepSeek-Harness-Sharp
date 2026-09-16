using Dsh.Gui.Services;

namespace Dsh.Tests;

/** GPU 设置的纯逻辑: Linux sysfs 解析、显卡候选匹配、后端标签。 */
public sealed class GpuPreferenceTests
{
    private const string AmdUevent = """
        DRIVER=amdgpu
        PCI_CLASS=30000
        PCI_ID=1002:15BF
        PCI_SLOT_NAME=0000:64:00.0
        MODALIAS=pci:v00001002d000015BFsv00001043sd00001EE1bc03sc00i00
        """;

    private const string NvidiaUevent = """
        DRIVER=nvidia
        PCI_CLASS=30200
        PCI_ID=10DE:28E0
        PCI_SLOT_NAME=0000:01:00.0
        """;

    private const string VirtioUevent = """
        DRIVER=virtio-pci
        PCI_ID=1AF4:1050
        PCI_SLOT_NAME=0000:00:01.0
        """;

    [Fact]
    public void ParseLinuxUevent_ReadsDriverVendorAndSlot()
    {
        var amd = GpuPreference.ParseLinuxUevent(AmdUevent);
        var nvidia = GpuPreference.ParseLinuxUevent(NvidiaUevent);
        var virtio = GpuPreference.ParseLinuxUevent(VirtioUevent);

        Assert.NotNull(amd);
        Assert.Equal("AMD", amd.Vendor);
        Assert.Equal("0000:64:00.0", amd.Id);
        Assert.Contains("amdgpu", amd.Name, StringComparison.Ordinal);
        Assert.Contains("1002:15BF", amd.Detail, StringComparison.Ordinal);

        Assert.NotNull(nvidia);
        Assert.Equal("NVIDIA", nvidia.Vendor);
        Assert.Equal("0000:01:00.0", nvidia.Id);

        Assert.NotNull(virtio);
        Assert.Equal("Virtio", virtio.Vendor);
    }

    [Fact]
    public void ParseLinuxUevent_ReturnsNull_WithoutDriverOrPciId()
    {
        Assert.Null(GpuPreference.ParseLinuxUevent("DRIVER=amdgpu\n"));
        Assert.Null(GpuPreference.ParseLinuxUevent("PCI_ID=1002:15BF\n"));
        Assert.Null(GpuPreference.ParseLinuxUevent(""));
    }

    [Fact]
    public void MatchAdapterIndex_PrefersExactSubstring_ThenReverse()
    {
        string[] candidates = ["AMD Radeon 780M Graphics", "NVIDIA GeForce RTX 4060 Laptop GPU"];

        Assert.Equal(0, GpuPreference.MatchAdapterIndex(candidates, GpuPreference.AutoAdapter));
        Assert.Equal(0, GpuPreference.MatchAdapterIndex(candidates, "AMD Radeon 780M"));
        Assert.Equal(1, GpuPreference.MatchAdapterIndex(candidates, "NVIDIA GeForce RTX 4060 Laptop GPU"));
        Assert.Equal(1, GpuPreference.MatchAdapterIndex(candidates, "RTX 4060"));
        // Avalonia 只报部分名字时也能反向匹配上。
        Assert.Equal(1, GpuPreference.MatchAdapterIndex(["Intel", "4060"], "NVIDIA GeForce RTX 4060 Laptop GPU"));
        // 完全匹配不到时退回默认卡, 不抛异常。
        Assert.Equal(0, GpuPreference.MatchAdapterIndex(candidates, "Intel Arc B580"));
        Assert.Equal(0, GpuPreference.MatchAdapterIndex([], "NVIDIA"));
    }

    [Fact]
    public void BackendLabel_IsReadable()
    {
        Assert.Equal("软件渲染", GpuPreference.BackendLabel(GpuPreference.SoftwareBackend));
        Assert.Equal("Vulkan", GpuPreference.BackendLabel(GpuPreference.VulkanBackend));
        Assert.NotEqual(string.Empty, GpuPreference.BackendLabel(GpuPreference.AutoBackend));
    }

    [Fact]
    public void WindowsAdapterList_ContainsTheTwoRealCards_AndSkipsVirtualOnes()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var adapters = GpuPreference.ListAdapters();

        Assert.NotEmpty(adapters);
        Assert.Contains(adapters, adapter => adapter.Vendor == "AMD");
        Assert.Contains(adapters, adapter => adapter.Vendor == "NVIDIA");
        Assert.DoesNotContain(adapters, adapter => adapter.Name.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase));
    }
}
