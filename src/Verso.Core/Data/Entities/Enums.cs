namespace Verso.Core.Data.Entities;

public enum TranscriptionStatus { InProgress, Done, Error }

public enum ModelQuality
{
    Standard,      // alias histórico -> Small (compat com DB existente: valor 0)
    High,          // alias histórico -> LargeV3 (compat com DB existente: valor 1)
    Tiny,          // ~75 MB  — mais rápido, menor precisão
    Base,          // ~142 MB
    Medium,        // ~1.5 GB
    LargeV2,       // ~3 GB
    LargeV3Turbo,  // ~1.2 GB — grande qualidade bem mais rápido que LargeV3
    LargeV1,       // ~3 GB — versão antiga do large
    TinyEn,        // inglês apenas
    BaseEn,        // inglês apenas
    SmallEn,       // inglês apenas
    MediumEn,      // inglês apenas
    PtBrTurbo,     // distil-whisper-large-v3 fine-tuned pt-BR (GGML Q5_0, ~538 MB) — força idioma pt
}

public enum SpeakerMode { Automatic, Off }

public enum ExecutionDevice { Auto, Cpu, Cuda, Vulkan, CoreMl }
