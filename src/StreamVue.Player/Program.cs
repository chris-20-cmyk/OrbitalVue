using Velopack;

namespace StreamVue.Player;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if !STREAMVUE_QA_BUILD
        VelopackApp.Build().Run();
#endif

        var application = new App();
        application.InitializeComponent();
        application.Run();
    }
}
