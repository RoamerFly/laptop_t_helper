using System.Windows;

namespace LaptopThermalHelper.App.Services;

public sealed class ThemeService
{
    private const string DarkThemePath = "Themes/Dark.xaml";
    private const string LightThemePath = "Themes/Light.xaml";

    public bool IsDark { get; private set; } = true;

    public void Toggle()
    {
        if (IsDark)
        {
            UseLight();
        }
        else
        {
            UseDark();
        }
    }

    public void UseDark()
    {
        IsDark = true;
        Apply(DarkThemePath);
    }

    public void UseLight()
    {
        IsDark = false;
        Apply(LightThemePath);
    }

    private static void Apply(string source)
    {
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        ResourceDictionary? existing = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) == true ||
            dictionary.Source?.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) == true);
        var replacement = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };

        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
            return;
        }

        int index = dictionaries.IndexOf(existing);
        dictionaries[index] = replacement;
    }
}
