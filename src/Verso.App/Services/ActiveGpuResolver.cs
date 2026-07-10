using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using Verso.App.Services;

namespace Verso.App.Services;

/// <summary>
/// Resolve qual placa de vídeo FÍSICA será usada por cada backend do whisper.net
/// (CUDA/Vulkan), respondendo "qual placa está em uso?" no notebook com iGPU + dGPU.
///
/// - CUDA: o whisper.net usa o dispositivo CUDA 0. Consultamos o <c>nvidia-smi</c>
///   (presente em máquinas com driver NVIDIA) para listar as GPUs NVIDIA por índice;
///   a primeira (device 0) é a que o CUDA efetivamente usará.
/// - Vulkan: selecionamos ativamente a GPU dedicada (dGPU) via
///   <c>WhisperFactoryOptions.GpuDevice</c>, resolvendo seu índice no backend Vulkan
///   através de WMI (<c>VulkanDeviceIndexResolver</c>). A GPU listada aqui é a que
///   o Verso força o whisper.net a usar.
///
/// Tudo é best-effort: se nvidia-smi/WMI não estiverem disponíveis, devolvemos null
/// sem lançar, e a UI explica o que faltou.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ActiveGpuResolver
{
    private readonly GpuDetector _gpuDetector;

    public ActiveGpuResolver(GpuDetector gpuDetector)
    {
        _gpuDetector = gpuDetector;
    }

    /// <summary>
    /// Determina a GPU ativa para o backend informado ("CPU"/"CUDA"/"Vulkan"/null).
    /// Retorna <see cref="ActiveGpuInfo"/> com nome da placa, origem da detecção e
    /// uma nota orientando como trocar (Windows Graphics Settings) quando relevante.
    /// </summary>
    public ActiveGpuInfo Resolve(string? backend)
    {
        return backend switch
        {
            "CUDA" => ResolveCuda(),
            "Vulkan" => ResolveVulkan(),
            "CPU" => new ActiveGpuInfo("CPU", "Processador", "Nenhuma GPU — decodificação na CPU.", null),
            _ => new ActiveGpuInfo(null, "Desconhecido", "Nenhum runtime carregado ainda.", null),
        };
    }

    private ActiveGpuInfo ResolveCuda()
    {
        // nvidia-smi lista as GPUs NVIDIA por índice; device 0 é a que o CUDA usa.
        var nvidiaGpus = QueryNvidiaSmi();
        if (nvidiaGpus.Count > 0)
        {
            var primary = nvidiaGpus[0];
            var note = nvidiaGpus.Count > 1
                ? $"Há {nvidiaGpus.Count} GPUs NVIDIA; o CUDA usa a de índice 0. " +
                  "Para forçar outra, defina CUDA_VISIBLE_DEVICES ou troque a GPU preferida " +
                  "do app em Configurações do Windows → Sistema → Tela → Gráficos."
                : "Para trocar para a integrada, mude o dispositivo em Configurações do " +
                  "Windows → Sistema → Tela → Gráficos (GPU preferida do Verso.App).";
            return new ActiveGpuInfo(primary.Name, "nvidia-smi (CUDA device 0)", note, null);
        }

        // Sem nvidia-smi: provavelmente não há driver NVIDIA. Cai de volta para a
        // GPU dedicada do WMI, se houver, com um caveat.
        var dedicated = _gpuDetector.Detect().FirstOrDefault(g => g.IsDedicated);
        if (dedicated is not null)
        {
            return new ActiveGpuInfo(
                dedicated.Name,
                "WMI (nvidia-smi ausente)",
                "Não foi possível confirmar via nvidia-smi (driver NVIDIA ausente?). " +
                "Verifique se o driver NVIDIA está instalado.",
                dedicated);
        }

        return new ActiveGpuInfo(
            null,
            "Indisponível",
            "CUDA selecionado, mas nenhuma GPU NVIDIA foi detectada. O whisper.net " +
            "deve ter caído para CPU — confirme o runtime em uso.",
            null);
    }
    private ActiveGpuInfo ResolveVulkan()
    {
        // Determinamos o índice da GPU dedicada via VulkanDeviceIndexResolver (WMI em
        // ordem nativa) e passamos como GpuDevice ao WhisperFactoryOptions — o que
        // força o whisper.net/ggml_vulkan a usar a placa dedicada.
        var gpus = _gpuDetector.Detect();
        var candidate = gpus.FirstOrDefault(g => g.IsDedicated) ?? gpus.FirstOrDefault();
        if (candidate is null)
        {
            return new ActiveGpuInfo(
                null,
                "Indisponível",
                "Vulkan selecionado, mas nenhuma placa compatível foi detectada via WMI.",
                null);
        }

        var note = candidate.IsDedicated
            ? $"GPU dedicada selecionada ativamente. Para usar a integrada em vez desta, " +
              "troque a GPU preferida do Verso.App em Configurações do Windows → Sistema → " +
              "Tela → Gráficos."
            : "Apenas uma GPU (provavelmente integrada) foi detectada. Vulkan a usará.";
        return new ActiveGpuInfo(candidate.Name, "WMI (adaptador dedicado, selecionado via GpuDevice)", note, candidate);
    }

    /// <summary>
    /// Executa <c>nvidia-smi --query-gpu=index,name,driver_version,memory.total --format=csv,noheader,nounits</c>
    /// e parseia as GPUs NVIDIA em ordem de índice. Retorna lista vazia se nvidia-smi
    /// não existir ou falhar (driver ausente).
    /// </summary>
    internal IReadOnlyList<NvidiaGpu> QueryNvidiaSmi()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<NvidiaGpu>();
        }

        string output;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=index,name,driver_version,memory.total --format=csv,noheader,nounits",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return Array.Empty<NvidiaGpu>();
            }

            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(milliseconds: 5000);
            if (process.ExitCode != 0)
            {
                return Array.Empty<NvidiaGpu>();
            }
        }
        catch
        {
            // nvidia-smi não está no PATH (driver NVIDIA ausente).
            return Array.Empty<NvidiaGpu>();
        }

        var list = new List<NvidiaGpu>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                continue;
            }

            if (!int.TryParse(parts[0], out var index))
            {
                continue;
            }

            list.Add(new NvidiaGpu(
                Index: index,
                Name: parts[1],
                DriverVersion: parts[2],
                MemoryTotalMiB: long.TryParse(parts[3], out var mem) ? mem : 0));
        }

        return list
            .OrderBy(g => g.Index)
            .ToList();
    }
}

/// <summary>GPU NVIDIA detectada via nvidia-smi (uma por dispositivo CUDA).</summary>
public sealed record NvidiaGpu(int Index, string Name, string DriverVersion, long MemoryTotalMiB)
{
    public string MemoryLabel => MemoryTotalMiB <= 0
        ? "—"
        : MemoryTotalMiB >= 1024
            ? $"{MemoryTotalMiB / 1024.0:0.#} GB"
            : $"{MemoryTotalMiB} MiB";
}

/// <summary>
/// Resultado da resolução da GPU ativa para um backend. <see cref="Source"/> indica
/// como a placa foi determinada ("nvidia-smi (CUDA device 0)", "WMI (adaptador dedicado)",
/// etc.) e <see cref="Note"/> orienta o usuário sobre como trocar de placa.
/// </summary>
public sealed record ActiveGpuInfo(string? GpuName, string Source, string Note, GpuInfo? WmiSource);