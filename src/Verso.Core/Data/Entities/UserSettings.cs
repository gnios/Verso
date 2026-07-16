namespace Verso.Core.Data.Entities;

public class UserSettings
{
    public int Id { get; set; } = 1;                 // singleton row
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Institution { get; set; } = "";
    public string DefaultLanguage { get; set; } = "pt";
    public bool IdentifySpeakersDefault { get; set; } = true;
    public bool LiveTranscriptionEnabled { get; set; } = true; // inerte no MVP
    public ExecutionDevice Device { get; set; } = ExecutionDevice.Auto;
    public ModelQuality DefaultQuality { get; set; } = ModelQuality.Standard;
    public bool DarkTheme { get; set; }
    public int MaxTranscriptionThreads { get; set; } // 0 = automático
}
