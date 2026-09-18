using Dsh.Gui.Services;
using Dsh.Tui;
using GpuAdapterInfo = Dsh.Tui.GpuAdapterInfo;

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
        var amd = GpuCatalog.ParseLinuxUevent(AmdUevent);
        var nvidia = GpuCatalog.ParseLinuxUevent(NvidiaUevent);
        var virtio = GpuCatalog.ParseLinuxUevent(VirtioUevent);

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
        Assert.Null(GpuCatalog.ParseLinuxUevent("DRIVER=amdgpu\n"));
        Assert.Null(GpuCatalog.ParseLinuxUevent("PCI_ID=1002:15BF\n"));
        Assert.Null(GpuCatalog.ParseLinuxUevent(""));
    }

    [Fact]
    public void MatchAdapterIndex_PrefersExactSubstring_ThenReverse()
    {
        string[] candidates = ["AMD Radeon 780M Graphics", "NVIDIA GeForce RTX 4060 Laptop GPU"];

        Assert.Equal(0, GpuCatalog.MatchAdapterIndex(candidates, GpuCatalog.AutoAdapter));
        Assert.Equal(0, GpuCatalog.MatchAdapterIndex(candidates, "AMD Radeon 780M"));
        Assert.Equal(1, GpuCatalog.MatchAdapterIndex(candidates, "NVIDIA GeForce RTX 4060 Laptop GPU"));
        Assert.Equal(1, GpuCatalog.MatchAdapterIndex(candidates, "RTX 4060"));
        // Avalonia 只报部分名字时也能反向匹配上。
        Assert.Equal(1, GpuCatalog.MatchAdapterIndex(["Intel", "4060"], "NVIDIA GeForce RTX 4060 Laptop GPU"));
        // 完全匹配不到时退回默认卡, 不抛异常。
        Assert.Equal(0, GpuCatalog.MatchAdapterIndex(candidates, "Intel Arc B580"));
        Assert.Equal(0, GpuCatalog.MatchAdapterIndex([], "NVIDIA"));
    }

    [Fact]
    public void IsDiscrete_ClassifiesByUmaFlag()
    {
        Assert.True(GpuCatalog.IsDiscrete(new GpuAdapterInfo("0001", "NVIDIA GeForce RTX 4060", "NVIDIA", "", false)));
        Assert.True(GpuCatalog.IsDiscrete(new GpuAdapterInfo("0002", "AMD Radeon RX 7900 XTX", "AMD", "", false)));
        Assert.False(GpuCatalog.IsDiscrete(new GpuAdapterInfo("0000", "AMD Radeon 780M Graphics", "AMD", "", true)));
        Assert.False(GpuCatalog.IsDiscrete(new GpuAdapterInfo("0003", "Intel UHD Graphics 770", "Intel", "", true)));
        // 架构位未知时回退: NVIDIA 桌面卡判独显, 其余保守判核显。
        Assert.True(GpuCatalog.IsDiscrete(new GpuAdapterInfo("0004", "NVIDIA 显卡", "NVIDIA", "", null)));
        Assert.False(GpuCatalog.IsDiscrete(new GpuAdapterInfo("0005", "未知显卡", "未知", "", null)));
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

        var adapters = GpuCatalog.ListAdapters();

        Assert.NotEmpty(adapters);
        Assert.Contains(adapters, adapter => adapter.Vendor == "AMD");
        Assert.Contains(adapters, adapter => adapter.Vendor == "NVIDIA");
        Assert.DoesNotContain(adapters, adapter => adapter.Name.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase));
        // 本机架构位: 780M 核显共享内存(UMA), RTX 4060 独显自带显存(非 UMA)。NVIDIA 驱动处于 Code 43 错误态时 DXGI 不枚举、架构位为 null(该状态曾在 2026-09-17 真实发生), 此时此断言会失败。
        Assert.True(adapters.First(adapter => adapter.Vendor == "AMD").IsUma);
        Assert.False(adapters.First(adapter => adapter.Vendor == "NVIDIA").IsUma);
    }
}
