using Android.App;
using Android.Content.PM;
namespace Supermarket.AndroidApp;
[Activity(Theme="@style/Maui.SplashTheme",MainLauncher=true,ConfigurationChanges=ConfigChanges.ScreenSize|ConfigChanges.Orientation|ConfigChanges.UiMode|ConfigChanges.ScreenLayout|ConfigChanges.SmallestScreenSize|ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
 protected override void OnStop()
 {
  Madar.AI.LocalAssistant.CancelForeground();
  base.OnStop();
 }
 protected override void OnResume()
 {
  base.OnResume();
  if (Window is not { } window) return;
  // The bundled interface stays light, including when Android uses dark mode.
  var bars = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
  if (bars is null) return;
  bars.AppearanceLightStatusBars = true;
  bars.AppearanceLightNavigationBars = true;
 }
}
