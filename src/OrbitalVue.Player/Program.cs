#if !ORBITALVUE_STORE_BUILD
using Velopack;
#endif
using OrbitalVue.Player.Services;

namespace OrbitalVue.Player;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if !ORBITALVUE_QA_BUILD && !ORBITALVUE_STORE_BUILD
        VelopackApp.Build().Run();
#endif

        OrbitalVueSingleInstance? singleInstance = null;
#if !ORBITALVUE_QA_BUILD
        var automationRun = args.Any(argument =>
            argument.StartsWith("--capture-", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("--smoke-", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("--startup-refresh-probe", StringComparison.OrdinalIgnoreCase));
        if (!automationRun)
        {
            singleInstance = new OrbitalVueSingleInstance(args.Contains("--wait-for-instance", StringComparer.OrdinalIgnoreCase));
            if (!singleInstance.IsPrimary)
            {
                singleInstance.SignalPrimary();
                singleInstance.Dispose();
                return;
            }
        }
#endif

        try
        {
            var application = new App();
            application.InitializeComponent();
            if (singleInstance is not null)
                singleInstance.ActivationRequested += (_, _) => application.Dispatcher.BeginInvoke(() =>
                {
                    if (application.MainWindow is MainWindow window) window.ActivateFromSecondaryInstance();
                });
            application.Run();
        }
        finally
        {
            singleInstance?.Dispose();
        }
    }
}
