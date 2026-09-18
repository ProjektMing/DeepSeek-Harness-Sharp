using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Dsh.Tui;

/**
 * 显存架构探测: 分辨核显(与 CPU 共享内存的统一内存架构 UMA)与独显(自带显存)。
 * Windows 用 D3D12 的 D3D12_FEATURE_DATA_ARCHITECTURE.UMA 特性位;
 * Linux 上 amdgpu 用 libdrm 的 AMDGPU_IDS_FLAGS_FUSION 位, i915 看 LMEM 显存 sysfs 是否存在(仅独显平台暴露), NVIDIA 桌面卡一律独显。
 * 探测失败一律返回 null(未知), 不影响显卡列表。
 */
internal static partial class GpuArchitectureProbe
{
    /** Windows: 按 PCI 厂商/设备号找到 DXGI 适配器, 创建 D3D12 设备读 UMA 位。 */
    [SupportedOSPlatform("windows")]
    public static bool? QueryUmaWindows(uint vendorId, uint deviceId)
        => WindowsProbe.QueryUma(vendorId, deviceId);

    /** Linux: cardName=cardN, driver 来自 uevent DRIVER=, pciSlot 来自 PCI_SLOT_NAME=。 */
    [SupportedOSPlatform("linux")]
    public static bool? QueryUmaLinux(string cardName, string driver, string? pciSlot)
        => LinuxProbe.QueryUma(cardName, driver, pciSlot);

    [SupportedOSPlatform("windows")]
    private static partial class WindowsProbe
    {
        private const uint FeatureLevel11 = 0xB000;
        private const uint FeatureArchitecture = 1;
        private const uint DxgiAdapterFlagSoftware = 2;
        private const int SlotRelease = 2;
        private const int SlotGetDesc1 = 10;
        private const int SlotEnumAdapters1 = 12;
        private const int SlotCheckFeatureSupport = 13;
        private static readonly Guid Factory1Iid = new(0x770aae78, 0xf26f, 0x4dba, 0xa8, 0x29, 0x25, 0x3c, 0x83, 0xd1, 0xb3, 0x87);
        private static readonly Guid DeviceIid = new(0x189819f1, 0x1db6, 0x4b57, 0xbe, 0x54, 0x18, 0x21, 0x33, 0x9b, 0x85, 0xf7);

        public static bool? QueryUma(uint vendorId, uint deviceId)
        {
            try
            {
                if (CreateDXGIFactory1(in Factory1Iid, out var factory) < 0)
                    return null;
                try
                {
                    for (uint index = 0; ; index++)
                    {
                        if (VtableCall<EnumAdapters1Fn>(factory, SlotEnumAdapters1)(factory, index, out var adapter) < 0)
                            break;
                        try
                        {
                            var result = ProbeAdapter(adapter, vendorId, deviceId);
                            if (result is not null)
                                return result;
                        }
                        finally
                        {
                            VtableCall<ReleaseFn>(adapter, SlotRelease)(adapter);
                        }
                    }
                    return null;
                }
                finally
                {
                    VtableCall<ReleaseFn>(factory, SlotRelease)(factory);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool? ProbeAdapter(IntPtr adapter, uint vendorId, uint deviceId)
        {
            if (VtableCall<GetDesc1Fn>(adapter, SlotGetDesc1)(adapter, out var desc) < 0)
                return null;
            if ((desc.Flags & DxgiAdapterFlagSoftware) != 0)
                return null;
            if (desc.VendorId != vendorId || desc.DeviceId != deviceId)
                return null;
            if (D3D12CreateDevice(adapter, FeatureLevel11, in DeviceIid, out var device) < 0)
                return null;
            try
            {
                var data = new D3d12FeatureDataArchitecture();
                if (VtableCall<CheckFeatureSupportFn>(device, SlotCheckFeatureSupport)(device, FeatureArchitecture, ref data, (uint)Marshal.SizeOf<D3d12FeatureDataArchitecture>()) < 0)
                    return null;
                return data.Uma != 0;
            }
            finally
            {
                VtableCall<ReleaseFn>(device, SlotRelease)(device);
            }
        }

        private static T VtableCall<T>(IntPtr com, int slot) where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(com), slot * IntPtr.Size));

        [LibraryImport("dxgi.dll")]
        private static partial int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

        [LibraryImport("d3d12.dll")]
        private static partial int D3D12CreateDevice(IntPtr adapter, uint featureLevel, in Guid riid, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int EnumAdapters1Fn(IntPtr factory, uint index, out IntPtr adapter);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetDesc1Fn(IntPtr adapter, out DxgiAdapterDesc1 desc);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CheckFeatureSupportFn(IntPtr device, uint feature, ref D3d12FeatureDataArchitecture data, uint dataSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint ReleaseFn(IntPtr com);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DxgiAdapterDesc1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory;
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public long AdapterLuid;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3d12FeatureDataArchitecture
        {
            public uint NodeIndex;
            public int TileBasedRenderer;
            public int Uma;
            public int CacheCoherentUma;
        }
    }

    [SupportedOSPlatform("linux")]
    private static partial class LinuxProbe
    {
        private const ulong FusionFlag = 0x01;
        private const int IdsFlagsOffset = 16;
        private const int GpuInfoBufferSize = 4096;
        private const int O_RDWR = 2;
        private const string DrmRoot = "/sys/class/drm";

        public static bool? QueryUma(string cardName, string driver, string? pciSlot)
        {
            try
            {
                if (driver.Equals("amdgpu", StringComparison.Ordinal))
                    return pciSlot is null ? null : QueryAmdFusion(pciSlot);
                if (driver.Equals("i915", StringComparison.Ordinal))
                    return !File.Exists(Path.Combine(DrmRoot, cardName, "device", "mem_info_vram_total"));
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool? QueryAmdFusion(string pciSlot)
        {
            var renderNode = FindRenderNode(pciSlot);
            if (renderNode is null || !TryLoadLibdrm(out var library))
                return null;
            var initialize = Marshal.GetDelegateForFunctionPointer<AmdgpuDeviceInitializeFn>(NativeLibrary.GetExport(library, "amdgpu_device_initialize"));
            var queryInfo = Marshal.GetDelegateForFunctionPointer<AmdgpuQueryGpuInfoFn>(NativeLibrary.GetExport(library, "amdgpu_query_gpu_info"));
            var deinitialize = Marshal.GetDelegateForFunctionPointer<AmdgpuDeviceDeinitializeFn>(NativeLibrary.GetExport(library, "amdgpu_device_deinitialize"));
            var descriptor = open(renderNode, O_RDWR);
            if (descriptor < 0)
                return null;
            try
            {
                if (initialize(descriptor, out _, out _, out var device) != 0)
                    return null;
                try
                {
                    var buffer = Marshal.AllocHGlobal(GpuInfoBufferSize);
                    try
                    {
                        Marshal.Copy(new byte[GpuInfoBufferSize], 0, buffer, GpuInfoBufferSize);
                        if (queryInfo(device, buffer) != 0)
                            return null;
                        var idsFlags = (ulong)Marshal.ReadInt64(buffer, IdsFlagsOffset);
                        return (idsFlags & FusionFlag) != 0;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    deinitialize(device);
                }
            }
            finally
            {
                close(descriptor);
            }
        }

        private static bool TryLoadLibdrm(out IntPtr library)
            => NativeLibrary.TryLoad("libdrm_amdgpu.so.2", out library) || NativeLibrary.TryLoad("libdrm_amdgpu.so", out library);

        private static string? FindRenderNode(string pciSlot)
        {
            foreach (var entry in Directory.EnumerateDirectories(DrmRoot, "renderD*"))
            {
                var target = Directory.ResolveLinkTarget(Path.Combine(entry, "device"), returnFinalTarget: true);
                if (target?.FullName.Contains(pciSlot, StringComparison.Ordinal) == true)
                    return $"/dev/dri/{Path.GetFileName(entry)}";
            }
            return null;
        }

        [LibraryImport("libc", SetLastError = true)]
        private static partial int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

        [LibraryImport("libc")]
        private static partial int close(int descriptor);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AmdgpuDeviceInitializeFn(int descriptor, out uint major, out uint minor, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AmdgpuQueryGpuInfoFn(IntPtr device, IntPtr info);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AmdgpuDeviceDeinitializeFn(IntPtr device);
    }
}
