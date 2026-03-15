using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using GenieClient.Genie;

namespace GenieClient.Desktop;

public class App : Application
{
    IHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
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
