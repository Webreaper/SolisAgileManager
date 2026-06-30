using MudBlazor;

namespace SolisManager.Client.Constants;

public static class UIConstants
{ 
    public const Variant GlobalMudVariant = Variant.Outlined;
    
    public static readonly Dictionary<string, object> TextAttribs = new ()
    {
        { nameof(Variant), GlobalMudVariant },
        { nameof(Margin), Margin.Dense },
    };

    public static readonly Dictionary<string, object> SelectAttribs = new ()
    {
        { nameof(Margin), Margin.Dense },
        { "Dense", true },
    };

    public static readonly Dictionary<string, object> ButtonAttribs = new ()
    {
        { nameof(Variant), GlobalMudVariant },
        { "DropShadow", false }
    };
}
