using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace FModel.Android;

[Activity(
    Label = "FModel",
    Theme = "@style/FModelTheme.NoActionBar",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity<App>
{
}
