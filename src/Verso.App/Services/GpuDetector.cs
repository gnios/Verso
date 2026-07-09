using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Management;

namespace Verso.App.Services;

/// <summary>
/// Detecção das placas de vídeo físicas via WMI (Win32_VideoController). Lista
/// tanto a integrada (Intel/AMD Radeon iGPU) quanto a dedicada (NVIDIA), para que
/// o usuário saiba exatamente quais adaptadores existem na máquina — base para
/// entender qual backend (CPU/CUDA/Vulkan) o whisper.net poderá usar.
///
/// Windows-only (WMI); em outras plataformas retorna lista vazia sem lançar.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GpuDetector
{
    public IReadOnlyList<GpuInfo> Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<GpuInfo>();
        }

        var list = new List<GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility, DriverVersion, AdapterRAM FROM Win32_VideoController");
            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                var name = SafeString(obj, "Name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                list.Add(new GpuInfo(
                    Name: name,
                    Vendor: SafeString(obj, "AdapterCompatibility"),
                    DriverVersion: SafeString(obj, "DriverVersion"),
                    AdapterRamBytes: SafeUlong(obj, "AdapterRAM")));
            }
        }
        catch
        {
            // WMI indisponível (serviço parado, permissão) — não derruba a UI.
            return Array.Empty<GpuInfo>();
        }

        // Estável: placas dedicadas (NVIDIA/AMD) primeiro, depois integradas.
        return list
            .OrderByDescending(g => g.IsDedicated ? 1 : 0)
            .ThenBy(g => g.Name)
            .ToList();
    }

    private static string SafeString(ManagementObject obj, string prop)
    {
        try
        {
            return obj[prop]?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static ulong SafeUlong(ManagementObject obj, string prop)
    {
        try
        {
            return Convert.ToUInt64(obj[prop] ?? 0UL);
        }
        catch
        {
            return 0UL;
        }
    }
}

/// <summary>
/// Informação de uma placa de vídeo detectada. <see cref="IsDedicated"/> é heurística
/// por nome/vendor: NVIDIA sempre dedicada; Intel/AMD "Radeon" sem sufixo dedicado
/// tratadas como integradas. Suficiente para distinguir iGPU de dGPU no caso típico
/// (notebook com Intel iGPU + NVIDIA dGPU).
/// </summary>
public sealed record GpuInfo(string Name, string Vendor, string DriverVersion, ulong AdapterRamBytes)
{
    public bool IsDedicated =>
        Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Quadro", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("RTX", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("GTX", StringComparison.OrdinalIgnoreCase)
        || (Name.Contains("AMD Radeon", StringComparison.OrdinalIgnoreCase)
            && !Name.Contains("Integrated", StringComparison.OrdinalIgnoreCase)
            && !IsKnownIntegratedAmd);

    public bool IsIntegrated =>
        Name.Contains("Intel", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Integrated", StringComparison.OrdinalIgnoreCase)
        || IsKnownIntegratedAmd;

    public string KindLabel => IsDedicated ? "Dedicada" : IsIntegrated ? "Integrada" : "Adaptador";

    public string RamLabel => AdapterRamBytes switch
    {
        0 => "—",
        < (1024UL * 1024 * 1024) => $"{AdapterRamBytes / (1024 * 1024)} MB",
        _ => $"{AdapterRamBytes / (1024UL * 1024 * 1024):0.#} GB",
    };

    private bool IsKnownIntegratedAmd =>
        Name.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Radeon Vega", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("APU", StringComparison.OrdinalIgnoreCase);
}