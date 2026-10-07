namespace Supermarket.AndroidApp;
public static class MauiProgram
{
 public static MauiApp CreateMauiApp()=>MauiApp.CreateBuilder().UseMauiApp<App>().Build();
}
public sealed class App : Application
{
 protected override Window CreateWindow(IActivationState? activationState)=>new(new RootPage());
}
