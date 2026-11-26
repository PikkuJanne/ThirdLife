using System.ComponentModel;
using System.IO;
using System.Security;
using System.Windows;
using Microsoft.Win32;

namespace ThirdLife.UI.Themes;

internal enum TraceTheme
{
    Dark,
    Light,
}

internal sealed class TraceAppearance
{
    private const string PreferenceKey = @"Software\ThirdLife\SetupCore";
    private const string PreferenceName = "Appearance";
    private readonly Window _window;
    private readonly ResourceDictionary _resources;
    private readonly bool _persistPreference;
    private ResourceDictionary _palette;
    private bool _disposed;

    public TraceAppearance(Window window, bool persistPreference = false)
    {
        _window = window;
        _persistPreference = persistPreference;
        Preference = persistPreference ? ReadPreference() : TraceTheme.Dark;

        var applicationResources = (Application.Current as App)?.Resources;
        var applicationPalette = applicationResources?.MergedDictionaries.FirstOrDefault(dictionary =>
            dictionary.Contains("Trace.BgCanvasBrush"));
        if (applicationResources is not null && applicationPalette is not null)
        {
            _resources = applicationResources;
            _palette = applicationPalette;
        }
        else
        {
            _resources = window.Resources;
            _resources.MergedDictionaries.Add(new ResourceDictionary { Source = ThemeUri("Metrics") });
            _resources.MergedDictionaries.Add(new ResourceDictionary { Source = ThemeUri("Controls") });
            _palette = new ResourceDictionary { Source = ThemeUri("Dark") };
            _resources.MergedDictionaries.Add(_palette);
        }
        Apply();
        SystemParameters.StaticPropertyChanged += OnSystemPropertyChanged;
        _window.Closed += OnClosed;
    }

    public TraceTheme Preference { get; private set; }

    public void SetPreference(TraceTheme preference)
    {
        if (Preference == preference)
        {
            return;
        }

        Preference = preference;
        Apply();
        if (_persistPreference)
        {
            try
            {
                Registry.SetValue($@"HKEY_CURRENT_USER\{PreferenceKey}", PreferenceName, preference.ToString());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                // A Windows account may disallow preference storage. Keep the live selection for this session.
            }
        }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemParameters.StaticPropertyChanged -= OnSystemPropertyChanged;
        _window.Closed -= OnClosed;
    }

    private void OnSystemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SystemParameters.HighContrast))
        {
            return;
        }

        if (_window.Dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            _ = _window.Dispatcher.InvokeAsync(Apply);
        }
    }

    private void Apply()
    {
        if (_disposed)
        {
            return;
        }

        var name = SystemParameters.HighContrast ? "HighContrast" : Preference.ToString();
        var source = ThemeUri(name);
        if (_palette.Source?.OriginalString.EndsWith($"/Trace.{name}.xaml", StringComparison.Ordinal) == true
            || _palette.Source?.OriginalString == $"Trace.{name}.xaml")
        {
            return;
        }

        var next = new ResourceDictionary { Source = source };
        _resources.MergedDictionaries[_resources.MergedDictionaries.IndexOf(_palette)] = next;
        _palette = next;
    }

    private static TraceTheme ReadPreference()
    {
        try
        {
            return Registry.GetValue($@"HKEY_CURRENT_USER\{PreferenceKey}", PreferenceName, null) is "Light"
                ? TraceTheme.Light
                : TraceTheme.Dark;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return TraceTheme.Dark;
        }
    }

    private static Uri ThemeUri(string name) => new($"/ThirdLife.UI;component/Themes/Trace.{name}.xaml", UriKind.Relative);
}
