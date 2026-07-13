using System.Text;

namespace VisionGrfPicker;

static class Program
{
    [STAThread]
    static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);   // enable cp949 for GRF names
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
