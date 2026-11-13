namespace HCPU;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 高 DPI 与默认视觉样式（csproj 中 ApplicationHighDpiMode = PerMonitorV2）
        ApplicationConfiguration.Initialize();

        // app.manifest 已声明 requireAdministrator，正常情况下启动即已提权。
        // 这里仅做兜底提示（例如有人手动去掉了 manifest），不影响运行。
        Application.Run(new UI.Form1());
    }
}
