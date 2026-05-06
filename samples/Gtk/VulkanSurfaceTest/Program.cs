using Eto.Forms;

namespace VulkanSurfaceTest;

static class Program
{
    public static void Main(string[] args)
    {
        new Application(new Eto.GtkSharp.Platform()).Run(new MainForm());
    }
}
