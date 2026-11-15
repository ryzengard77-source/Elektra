/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using System;
using System.IO;
using System.Media;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Elektra.Audio;
using Elektra.Configuration;
using Elektra.Knowledge;
using Elektra.Knowledge.Caching;
using Elektra.Knowledge.Context;
using Elektra.Knowledge.Providers;
using Elektra.Platform;
using Elektra.Security;
using Elektra.UI;
using Elektra.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Elektra;

public partial class App : Application
{
    private IHost? _host;
    private TrayIconManager? _trayManager;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = e.Args,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Services.Configure<AssistantOptions>(builder.Configuration.GetSection(AssistantOptions.SectionName));
            builder.Services.AddSingleton<UserPreferencesService>();

            var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(16)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
            builder.Services.AddSingleton(channel.Writer);
            builder.Services.AddSingleton(channel.Reader);

            builder.Services.AddSingleton<AudioTelemetryBridge>();
            builder.Services.AddSingleton<AudioCaptureService>();
            builder.Services.AddSingleton<VoiceSynthesisService>();
            builder.Services.AddSingleton<DirectoryEncryptionService>();
            builder.Services.AddSingleton<WindowsToolLauncher>();

            builder.Services.AddMemoryCache(options => options.SizeLimit = 512);
            builder.Services.AddSingleton<TwoTierKnowledgeCache>();
            builder.Services.AddSingleton<ConversationContextTracker>();

            var retryPolicy = HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(2, attempt => TimeSpan.FromMilliseconds(200 * attempt));
            bool enableOnlineLookups = builder.Configuration
                .GetSection(AssistantOptions.SectionName)
                .Get<AssistantOptions>()?.EnableOnlineLookups ?? true;

            builder.Services.AddSingleton<MathApiProvider>();
            builder.Services.AddSingleton<WatervilleTimeWeatherProvider>();
            builder.Services.AddSingleton<IKnowledgeProvider>(sp => sp.GetRequiredService<WatervilleTimeWeatherProvider>());
            builder.Services.AddSingleton<IKnowledgeProvider>(sp => sp.GetRequiredService<MathApiProvider>());

            builder.Services.AddHttpClient<LocationSearchService>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(10);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("ElektraVoiceAssistant/1.0 (desktop location lookup)");
            });

            if (enableOnlineLookups)
            {
                builder.Services.AddHttpClient<DuckDuckGoProvider>(c =>
                {
                    c.Timeout = TimeSpan.FromSeconds(6);
                    c.DefaultRequestHeaders.UserAgent.ParseAdd("ElektraAssistant/1.0");
                }).AddPolicyHandler(retryPolicy);

                builder.Services.AddHttpClient<WikipediaProvider>(c =>
                {
                    c.Timeout = TimeSpan.FromSeconds(8);
                    c.DefaultRequestHeaders.UserAgent.ParseAdd("ElektraAssistant/1.0");
                }).AddPolicyHandler(retryPolicy);

                builder.Services.AddHttpClient("WatervilleWeather", c =>
                {
                    c.Timeout = TimeSpan.FromSeconds(8);
                    c.DefaultRequestHeaders.UserAgent.ParseAdd("ElektraAssistant/1.0");
                }).AddPolicyHandler(retryPolicy);

                builder.Services.AddSingleton<IKnowledgeProvider>(sp => sp.GetRequiredService<WikipediaProvider>());
                builder.Services.AddSingleton<IKnowledgeProvider>(sp => sp.GetRequiredService<DuckDuckGoProvider>());
            }
            builder.Services.AddSingleton<QueryOrchestrator>();

            builder.Services.AddSingleton<MainWindow>();
            builder.Services.AddSingleton<TrayIconManager>(sp => new TrayIconManager(
                sp.GetRequiredService<MainWindow>(),
                sp.GetRequiredService<AudioCaptureService>(),
                sp.GetRequiredService<AudioTelemetryBridge>(),
                Dispatcher,
                () => Dispatcher.Invoke(Shutdown)));

            builder.Services.AddHostedService<ElektraBackgroundService>();

            _host = builder.Build();
            _trayManager = _host.Services.GetRequiredService<TrayIconManager>();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            _ = new WindowInteropHelper(mainWindow).EnsureHandle();
            await PlayStartupSoundAsync(_host.Services.GetRequiredService<ILogger<App>>());

            var prefs = _host.Services.GetRequiredService<UserPreferencesService>().Current;
            if (!prefs.StartHiddenInTray)
                mainWindow.Show();

            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            var logger = _host?.Services.GetService<ILogger<App>>();
            logger?.LogCritical(ex, "Elektra failed to start.");
            MessageBox.Show(
                $"Elektra encountered a fatal startup error:\n\n{ex.Message}",
                "Elektra Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static async Task PlayStartupSoundAsync(ILogger<App> logger)
    {
        string soundPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Elektra.wav");
        if (!File.Exists(soundPath))
            return;

        try
        {
            await Task.Run(() =>
            {
                using var player = new SoundPlayer(soundPath);
                player.Load();
                player.PlaySync();
            });
            logger.LogInformation("Played startup sound from '{SoundPath}'.", soundPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "Could not play the optional startup sound at '{SoundPath}'. Elektra will continue starting.", soundPath);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logger = _host?.Services.GetService<ILogger<App>>();
        logger?.LogError(e.Exception, "Unhandled UI dispatcher exception.");
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayManager?.Dispose();
        if (_host != null)
        {
            try
            {
                _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Host shutdown error: {ex}");
            }
            finally
            {
                _host.Dispose();
                _host = null;
            }
        }
        base.OnExit(e);
    }
}