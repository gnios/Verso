using System.Reflection;
using System.Runtime.InteropServices;

namespace Verso.Core.Engine;

/// <summary>Tipos de dispositivo GPU definidos por VkPhysicalDeviceType.</summary>
internal enum VulkanDeviceType
{
    Other = 0,
    IntegratedGpu = 1,
    DiscreteGpu = 2,
    VirtualGpu = 3,
    Cpu = 4,
}

/// <summary>Informação de um dispositivo físico Vulkan enumerado.</summary>
internal sealed record VulkanDeviceInfo(int Index, VulkanDeviceType DeviceType, string Name);

/// <summary>
/// Enumerador de dispositivos físicos Vulkan via P/Invoke (vulkan-1.dll / libvulkan).
/// Constrói as estruturas VkInstanceCreateInfo em memória não-gerenciada (byte a byte)
/// para evitar problemas de alinhamento/layout de struct que causaram crashes em
/// tentativas anteriores com StructLayout marshaling automático.
///
/// Apenas o essencial: uma VkInstance minimalista (sem layers/extensions), chamada
/// vkEnumeratePhysicalDevices + vkGetPhysicalDeviceProperties, lendo deviceType
/// e deviceName em offsets fixos do VkPhysicalDeviceProperties.
///
/// Toda exceção é capturada em <see cref="TryEnumerateDevices"/> — nunca derruba o app.
/// </summary>
internal static class VulkanDeviceEnumerator
{
    private const string VulkanLibraryAlias = "verso_vulkan";

    // VK_MAKE_API_VERSION(0, 1, 0, 0)
    private const uint VkApiVersion1_0 = 4_194_304;

    // VkStructureType
    private const int VkStructureTypeApplicationInfo = 0;
    private const int VkStructureTypeInstanceCreateInfo = 1;

    // VkResult
    private const int VkSuccess = 0;

    // Tamanhos das structs manuais em x64 (conferidos com Marshal.SizeOf via reflection)
    private const int AppInfoSize = 48;
    private const int CreateInfoSize = 64;

    // VkMemoryHeapFlags
    private const uint VkMemoryHeapDeviceLocalBit = 0x00000001;

    // Tamanho de VkPhysicalDeviceMemoryProperties (528 bytes em x64):
    //   uint32_t memoryTypeCount                    →  4 bytes
    //   padding                                    →  4 bytes
    //   VkMemoryType memoryTypes[32] (8 bytes/ea)  → 256 bytes
    //   uint32_t memoryHeapCount                   →  4 bytes
    //   padding                                    →  4 bytes
    //   VkMemoryHeap memoryHeaps[16] (16 bytes/ea) → 256 bytes
    private const int MemoryPropertiesSize = 528;

    static VulkanDeviceEnumerator()
    {
        NativeLibrary.SetDllImportResolver(typeof(VulkanDeviceEnumerator).Assembly, ResolveVulkanLibrary);
    }

    private static IntPtr ResolveVulkanLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != VulkanLibraryAlias)
            return IntPtr.Zero;

        string[] candidates = OperatingSystem.IsWindows()
            ? ["vulkan-1.dll"]
            : OperatingSystem.IsLinux()
                ? ["libvulkan.so.1", "libvulkan.so"]
                : ["libvulkan.1.dylib", "libvulkan.dylib"];

        foreach (var candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    [DllImport(VulkanLibraryAlias)]
    private static extern int vkCreateInstance(IntPtr pCreateInfo, IntPtr pAllocator, out IntPtr pInstance);

    [DllImport(VulkanLibraryAlias)]
    private static extern void vkDestroyInstance(IntPtr instance, IntPtr pAllocator);

    [DllImport(VulkanLibraryAlias)]
    private static extern int vkEnumeratePhysicalDevices(IntPtr instance, ref uint pPhysicalDeviceCount, IntPtr[]? pPhysicalDevices);

    [DllImport(VulkanLibraryAlias)]
    private static extern void vkGetPhysicalDeviceProperties(IntPtr physicalDevice, IntPtr pProperties);

    [DllImport(VulkanLibraryAlias)]
    private static extern void vkGetPhysicalDeviceMemoryProperties(IntPtr physicalDevice, IntPtr pMemoryProperties);

    /// <summary>
    /// Hook de teste: quando definido, <see cref="TryEnumerateDevices"/>
    /// retorna esta lista em vez de consultar Vulkan via P/Invoke.
    /// </summary>
    internal static Func<List<VulkanDeviceInfo>>? DevicesOverride { get; set; }

    /// <summary>
    /// Tenta enumerar dispositivos Vulkan. Retorna lista vazia em qualquer falha
    /// (vulkan-1.dll ausente, sem driver, erro de inicialização, etc.).
    /// </summary>
    public static List<VulkanDeviceInfo> TryEnumerateDevices()
    {
        if (DevicesOverride is { } fn)
            return fn();

        try
        {
            return EnumerateDevicesCore();
        }
        catch
        {
            return [];
        }
    }

    private static List<VulkanDeviceInfo> EnumerateDevicesCore()
    {
        AppInfoMemory appInfo = default;
        CreateInfoMemory createInfo = default;
        IntPtr instance = IntPtr.Zero;
        IntPtr propBuffer = IntPtr.Zero;

        try
        {
            // --- VkApplicationInfo ---
            // Offset 0:  sType (int32)              = VK_STRUCTURE_TYPE_APPLICATION_INFO (0)
            // Offset 4:  padding (4 bytes)
            // Offset 8:  pNext (IntPtr)             = null
            // Offset 16: pApplicationName (IntPtr)  = "verso"
            // Offset 24: applicationVersion (uint32) = 1
            // Offset 28: padding (4 bytes)
            // Offset 32: pEngineName (IntPtr)       = "verso"
            // Offset 40: engineVersion (uint32)      = 1
            // Offset 44: apiVersion (uint32)         = VK_API_VERSION_1_0
            appInfo = new AppInfoMemory();
            Marshal.WriteInt32(appInfo.Ptr, 0, VkStructureTypeApplicationInfo);
            Marshal.WriteIntPtr(appInfo.Ptr, 8, IntPtr.Zero);
            Marshal.WriteIntPtr(appInfo.Ptr, 16, appInfo.AppNamePtr);
            Marshal.WriteInt32(appInfo.Ptr, 24, 1);
            Marshal.WriteIntPtr(appInfo.Ptr, 32, appInfo.EngineNamePtr);
            Marshal.WriteInt32(appInfo.Ptr, 40, 1);
            Marshal.WriteInt32(appInfo.Ptr, 44, (int)VkApiVersion1_0);

            // --- VkInstanceCreateInfo ---
            // Offset 0:  sType (int32)              = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO (1)
            // Offset 4:  padding (4 bytes)
            // Offset 8:  pNext (IntPtr)             = null
            // Offset 16: flags (IntPtr)             = null
            // Offset 24: pApplicationInfo (IntPtr)  = &appInfo
            // Offset 32: enabledLayerCount (uint32)  = 0
            // Offset 36: padding (4 bytes)
            // Offset 40: ppEnabledLayerNames (IntPtr) = null
            // Offset 48: enabledExtensionCount (uint32) = 0
            // Offset 52: padding (4 bytes)
            // Offset 56: ppEnabledExtensionNames (IntPtr) = null
            createInfo = new CreateInfoMemory();
            Marshal.WriteInt32(createInfo.Ptr, 0, VkStructureTypeInstanceCreateInfo);
            Marshal.WriteIntPtr(createInfo.Ptr, 8, IntPtr.Zero);
            Marshal.WriteIntPtr(createInfo.Ptr, 16, IntPtr.Zero);
            Marshal.WriteIntPtr(createInfo.Ptr, 24, appInfo.Ptr);
            Marshal.WriteInt32(createInfo.Ptr, 32, 0);
            Marshal.WriteIntPtr(createInfo.Ptr, 40, IntPtr.Zero);
            Marshal.WriteInt32(createInfo.Ptr, 48, 0);
            Marshal.WriteIntPtr(createInfo.Ptr, 56, IntPtr.Zero);

            if (vkCreateInstance(createInfo.Ptr, IntPtr.Zero, out instance) != VkSuccess)
                return [];

            // Enumerate physical devices
            uint count = 0;
            if (vkEnumeratePhysicalDevices(instance, ref count, null) != VkSuccess || count == 0)
                return [];

            var handles = new IntPtr[count];
            if (vkEnumeratePhysicalDevices(instance, ref count, handles) != VkSuccess)
                return [];

            var result = new List<VulkanDeviceInfo>((int)count);
            propBuffer = Marshal.AllocHGlobal(512);

            for (uint i = 0; i < count; i++)
            {
                vkGetPhysicalDeviceProperties(handles[i], propBuffer);

                var deviceType = (VulkanDeviceType)Marshal.ReadInt32(propBuffer, 16);
                var name = ReadFixedString(propBuffer, 20, 256);

                result.Add(new VulkanDeviceInfo((int)i, deviceType, name));
            }

            return result;
        }
        finally
        {
            if (propBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(propBuffer);

            if (instance != IntPtr.Zero)
                vkDestroyInstance(instance, IntPtr.Zero);

            createInfo.Dispose();
            appInfo.Dispose();
        }
    }

    private static string ReadFixedString(IntPtr basePtr, int offset, int maxLength)
    {
        var raw = Marshal.PtrToStringAnsi(IntPtr.Add(basePtr, offset), maxLength);
        if (raw is null) return string.Empty;
        var nullIdx = raw.IndexOf('\0');
        return nullIdx >= 0 ? raw[..nullIdx] : raw;
    }

    /// <summary>
    /// Gerencia a memória não-gerenciada de VkApplicationInfo + as duas strings ANSI.
    /// </summary>
    private ref struct AppInfoMemory
    {
        public IntPtr Ptr;
        public IntPtr AppNamePtr;
        public IntPtr EngineNamePtr;

        public AppInfoMemory()
        {
            Ptr = Marshal.AllocHGlobal(AppInfoSize);
            AppNamePtr = Marshal.StringToCoTaskMemAnsi("verso");
            EngineNamePtr = Marshal.StringToCoTaskMemAnsi("verso");
        }

        public readonly void Dispose()
        {
            if (Ptr != IntPtr.Zero)
                Marshal.FreeHGlobal(Ptr);
            if (AppNamePtr != IntPtr.Zero)
                Marshal.FreeCoTaskMem(AppNamePtr);
            if (EngineNamePtr != IntPtr.Zero)
                Marshal.FreeCoTaskMem(EngineNamePtr);
        }
    }

    /// <summary>Gerencia a memória não-gerenciada de VkInstanceCreateInfo.</summary>
    private ref struct CreateInfoMemory
    {
        public IntPtr Ptr;

        public CreateInfoMemory()
        {
            Ptr = Marshal.AllocHGlobal(CreateInfoSize);
        }

        public readonly void Dispose()
        {
            if (Ptr != IntPtr.Zero)
                Marshal.FreeHGlobal(Ptr);
        }
    }

    /// <summary>
    /// Consulta a VRAM total (bytes) da GPU dedicada via Vulkan.
    /// Retorna 0 em qualquer falha (vulkan-1.dll ausente, sem driver, etc.).
    /// Best-effort: o valor retornado é a soma de todos os heaps com flag
    /// VK_MEMORY_HEAP_DEVICE_LOCAL_BIT no dispositivo dedicado (DiscreteGpu).
    /// Não subtrai uso de outras aplicações — pode superestimar o disponível.
    /// </summary>
    public static long TryGetDedicatedGpuVramBytes()
    {
        if (VramBytesOverride is { } fn)
            return fn();

        try
        {
            return QueryDedicatedGpuVramCore();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Hook de teste: quando definido, <see cref="TryGetDedicatedGpuVramBytes"/>
    /// retorna este valor em vez de consultar Vulkan via P/Invoke.
    /// </summary>
    internal static Func<long>? VramBytesOverride { get; set; }

    private static long QueryDedicatedGpuVramCore()
    {
        AppInfoMemory appInfo = default;
        CreateInfoMemory createInfo = default;
        IntPtr instance = IntPtr.Zero;
        IntPtr propBuffer = IntPtr.Zero;
        IntPtr memPropsBuffer = IntPtr.Zero;

        try
        {
            appInfo = new AppInfoMemory();
            Marshal.WriteInt32(appInfo.Ptr, 0, VkStructureTypeApplicationInfo);
            Marshal.WriteIntPtr(appInfo.Ptr, 8, IntPtr.Zero);
            Marshal.WriteIntPtr(appInfo.Ptr, 16, appInfo.AppNamePtr);
            Marshal.WriteInt32(appInfo.Ptr, 24, 1);
            Marshal.WriteIntPtr(appInfo.Ptr, 32, appInfo.EngineNamePtr);
            Marshal.WriteInt32(appInfo.Ptr, 40, 1);
            Marshal.WriteInt32(appInfo.Ptr, 44, (int)VkApiVersion1_0);

            createInfo = new CreateInfoMemory();
            Marshal.WriteInt32(createInfo.Ptr, 0, VkStructureTypeInstanceCreateInfo);
            Marshal.WriteIntPtr(createInfo.Ptr, 8, IntPtr.Zero);
            Marshal.WriteIntPtr(createInfo.Ptr, 16, IntPtr.Zero);
            Marshal.WriteIntPtr(createInfo.Ptr, 24, appInfo.Ptr);
            Marshal.WriteInt32(createInfo.Ptr, 32, 0);
            Marshal.WriteIntPtr(createInfo.Ptr, 40, IntPtr.Zero);
            Marshal.WriteInt32(createInfo.Ptr, 48, 0);
            Marshal.WriteIntPtr(createInfo.Ptr, 56, IntPtr.Zero);

            if (vkCreateInstance(createInfo.Ptr, IntPtr.Zero, out instance) != VkSuccess)
                return 0;

            uint count = 0;
            if (vkEnumeratePhysicalDevices(instance, ref count, null) != VkSuccess || count == 0)
                return 0;

            var handles = new IntPtr[count];
            if (vkEnumeratePhysicalDevices(instance, ref count, handles) != VkSuccess)
                return 0;

            // Localiza GPU dedicada (DiscreteGpu). Se nenhuma for encontrada,
            // retorna 0 — a RAM compartilhada da iGPU não é VRAM dedicada e não
            // deve ser tratada como tal (false-positive na checagem de VRAM).
            propBuffer = Marshal.AllocHGlobal(512);
            IntPtr? dedicatedHandle = null;

            for (uint i = 0; i < count; i++)
            {
                vkGetPhysicalDeviceProperties(handles[i], propBuffer);
                var deviceType = (VulkanDeviceType)Marshal.ReadInt32(propBuffer, 16);
                if (deviceType == VulkanDeviceType.DiscreteGpu)
                {
                    dedicatedHandle = handles[i];
                    break;
                }
            }

            if (dedicatedHandle is null)
                return 0;

            // Consulta propriedades de memória do dispositivo dedicado
            memPropsBuffer = Marshal.AllocHGlobal(MemoryPropertiesSize);
            vkGetPhysicalDeviceMemoryProperties(dedicatedHandle.Value, memPropsBuffer);

            // memoryHeapCount está no offset 264 (ver constantes acima)
            var heapCount = Marshal.ReadInt32(memPropsBuffer, 264);
            long totalVram = 0;

            for (int i = 0; i < heapCount; i++)
            {
                // VkMemoryHeap: size (uint64) no offset 0, flags (uint32) no offset 8
                var heapOffset = 272 + i * 16;
                var flags = (uint)Marshal.ReadInt32(memPropsBuffer, heapOffset + 8);
                if ((flags & VkMemoryHeapDeviceLocalBit) != 0)
                {
                    totalVram += (long)Marshal.ReadInt64(memPropsBuffer, heapOffset);
                }
            }

            return totalVram;
        }
        finally
        {
            if (memPropsBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(memPropsBuffer);
            if (propBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(propBuffer);
            if (instance != IntPtr.Zero)
                vkDestroyInstance(instance, IntPtr.Zero);
            createInfo.Dispose();
            appInfo.Dispose();
        }
    }
}
