using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using GenieClient;
using GenieClient.Genie;

namespace GenieClient.Desktop;

public class App : Application
{
    IHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        // Mirror FormMain.CreateGenieFolders() startup directory initialization
        LocalDirectory.CheckUserDirectory();
        string dataPath = LocalDirectory.Path;
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "Profiles"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "Layout"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "PluginKeys"));
        // Note: Utility.MoveLayoutFiles() intentionally omitted — Windows-only migration aid
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Help"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Icons"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Logs"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Scripts"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Sounds"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Plugins"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Maps"));

        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddSingleton<Globals>()
            .AddSingleton<Game>(sp =>
            {
                var globals = sp.GetRequiredService<Globals>();
                return new Game(ref globals);
            })
            .AddSingleton<MainWindow>();
        _host = builder.Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += async (_, _) => await _host!.StopAsync();
            await _host.StartAsync();
            desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
