namespace Transcriba.Core.Data.Entities;

public enum TranscriptionStatus { InProgress, Done, Error }

public enum ModelQuality { Standard, High }        // Padrão -> small, Alta -> large-v3

public enum SpeakerMode { Automatic, Off }

public enum ExecutionDevice { Auto, Cpu, Cuda, Vulkan }
