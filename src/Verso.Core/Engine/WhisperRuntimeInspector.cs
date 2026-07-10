using Verso.Core.Data.Entities;
using Whisper.net.LibraryLoader;
using Whisper.net;

namespace Verso.Core.Engine;

/// <summary>
/// Inspetor do runtime Whisper.net em uso. Expõe a diferença entre a preferência
/// configurada pelo usuário (<see cref="ExecutionDevice"/> → ordem em
/// <see cref="WhisperRuntimeConfigurator"/>) e o runtime que foi **realmente
/// carregado** (<see cref="RuntimeOptions.LoadedLibrary"/>), além de labels
/// legíveis em pt-BR para a UI e a categoria de backend (CPU/CUDA/Vulkan).
///
/// <see cref="RuntimeOptions.LoadedLibrary"/> é null até a primeira
/// <c>WhisperFactory</c> ser criada (ocorre na primeira transcrição); após isso
/// fica fixo pelo tempo de vida do processo — a lib nativa não é descarregada.
/// Por isso a UI de desenvolvedor mostra "ainda não carregado" até a primeira
/// transcrição (ou até um probe explícito via <see cref="ProbeRuntime"/>).
/// </summary>
public static class WhisperRuntimeInspector
{
    /// <summary>Runtime efetivamente carregado, ou null se nenhuma transcrição/probe ocorreu.</summary>
    public static RuntimeLibrary? LoadedRuntime => RuntimeOptions.LoadedLibrary;

    /// <summary>Backend categorizado (CPU/CUDA/Vulkan) do runtime carregado; null se nada carregado.</summary>
    public static string? LoadedBackend => GetBackend(LoadedRuntime);

    /// <summary>Label curto em pt-BR do runtime carregado; "Não carregado" se null.</summary>
    public static string LoadedRuntimeLabel =>
        LoadedRuntime is null ? "Não carregado" : GetRuntimeLabel(LoadedRuntime.Value);

    /// <summary>True se o runtime carregado é um backend de GPU (CUDA/CUDA 12/Vulkan).</summary>
    public static bool IsGpuRuntime => LoadedRuntime is RuntimeLibrary.Cuda
        or RuntimeLibrary.Cuda12
        or RuntimeLibrary.Vulkan;

    /// <summary>
    /// Label legível em pt-BR para um <see cref="RuntimeLibrary"/>.
    /// </summary>
    public static string GetRuntimeLabel(RuntimeLibrary runtime) => runtime switch
    {
        RuntimeLibrary.Cpu => "CPU",
        RuntimeLibrary.CpuNoAvx => "CPU (sem AVX)",
        RuntimeLibrary.Cuda => "CUDA",
        RuntimeLibrary.Cuda12 => "CUDA 12",
        RuntimeLibrary.Vulkan => "Vulkan",
        RuntimeLibrary.CoreML => "CoreML",
        RuntimeLibrary.OpenVino => "OpenVINO",
        _ => runtime.ToString(),
    };
    public static string? GetBackend(RuntimeLibrary? runtime) => runtime switch
    {
        RuntimeLibrary.Cpu or RuntimeLibrary.CpuNoAvx => "CPU",
        RuntimeLibrary.Cuda or RuntimeLibrary.Cuda12 => "CUDA",
        RuntimeLibrary.Vulkan => "Vulkan",
        RuntimeLibrary.CoreML => "CoreML",
        RuntimeLibrary.OpenVino => "OpenVINO",
        null => null,
        _ => runtime.ToString(),
    };

    /// <summary>
    /// Força o carregamento do runtime nativo para o <paramref name="device"/> escolhido
    /// sem rodar uma transcrição completa: cria e descarta imediatamente uma
    /// <c>WhisperFactory</c> standalone a partir de <paramref name="modelPath"/>, o que
    /// dispara a carga da lib nativa e popula <see cref="RuntimeOptions.LoadedLibrary"/>.
    ///
    /// Não usa o <see cref="IWhisperFactoryCache"/> compartilhado com o engine — cria uma
    /// fábrica efêmera e a descarta — para não evictar a fábrica real em uso.
    /// </summary>
    /// <returns>Runtime carregado após o probe, ou null se o modelo não existir.</returns>
    public static RuntimeLibrary? ProbeRuntime(string modelPath, ExecutionDevice device)
    {
        if (!File.Exists(modelPath))
        {
            return null;
        }

        WhisperRuntimeConfigurator.Configure(device);
        try
        {
            var gpuDevice = WhisperRuntimeConfigurator.CurrentGpuDevice;
            if (gpuDevice != 0)
            {
                using var factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { GpuDevice = gpuDevice });
                return RuntimeOptions.LoadedLibrary;
            }
            else
            {
                using var factory = WhisperFactory.FromPath(modelPath);
                return RuntimeOptions.LoadedLibrary;
            }
        }
        catch
        {
            // Se a carga falhar (lib ausente/driver), devolve o que estiver carregado.
            return RuntimeOptions.LoadedLibrary;
        }
    }
    /// <summary>String legível da ordem de preferência para um dispositivo (para a UI).</summary>
    public static string DescribeRuntimeOrder(ExecutionDevice device) =>
        string.Join(" → ", WhisperRuntimeConfigurator.ResolveRuntimeOrder(device).Select(GetRuntimeLabel));
}