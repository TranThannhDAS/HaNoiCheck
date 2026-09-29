using System.Windows;

namespace HanoiCheck.Services;

public interface IApplicationControlService
{
    void CloseApplication();
}

public sealed class ApplicationControlService : IApplicationControlService
{
    public void CloseApplication() => Application.Current.Shutdown();
}
