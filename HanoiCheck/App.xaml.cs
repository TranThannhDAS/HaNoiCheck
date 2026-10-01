using System.Net.Http.Headers;
using System.Windows;
using HanoiCheck.Services;
using HanoiCheck.Services.Licensing;
using HanoiCheck.Services.Menu;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;

namespace HanoiCheck;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
        services.AddLogging();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        services.AddSingleton<AuthStateService>();
        services.AddHttpClient<IInternetTimeService, InternetTimeService>(client =>
        {
            client.BaseAddress = new Uri("https://ncc-api.hanoicheck.com.vn/");
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddSingleton<IDemoLicenseService, DemoLicenseService>();
        services.AddSingleton<IApplicationControlService, ApplicationControlService>();
        services.AddHttpClient<AuthService>(client =>
        {
            client.BaseAddress = new Uri("https://ncc-api.hanoicheck.com.vn/");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<IBatchApiService, BatchApiService>(client =>
        {
            client.BaseAddress = new Uri("https://ncc-api.hanoicheck.com.vn/");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromMinutes(2);
        });
        services.AddHttpClient<IMenuApiService, MenuApiService>(client =>
        {
            client.BaseAddress = new Uri("https://ncc-api.hanoicheck.com.vn/");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromMinutes(2);
        });
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<ExcelImportService>();
        services.AddSingleton<ImageResolverService>();
        services.AddSingleton<OptionResolverService>();
        services.AddSingleton<BatchPayloadBuilder>();
        services.AddSingleton<IBatchImportSaveService, BatchImportSaveService>();
        services.AddSingleton<MenuExcelImportService>();
        services.AddSingleton<MenuResolverService>();
        services.AddSingleton<IMenuSaveService, MenuSaveService>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
