namespace Verso.App.ViewModels;



public sealed class TranscriptionCardTagViewModel(string name, string colorName)

{

    public string Name { get; } = name;

    public string ColorName { get; } = colorName;

    public bool IsBlue => colorName == "blue";

    public bool IsGreen => colorName == "green";

    public bool IsYellow => colorName == "yellow";

    public bool IsRed => colorName == "red";

    public bool IsPurple => colorName == "purple";

    public bool IsOrange => colorName == "orange";

    public bool IsPink => colorName == "pink";

    public bool IsTeal => colorName == "teal";

}

