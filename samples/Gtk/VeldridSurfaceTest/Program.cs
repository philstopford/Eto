using Eto.Forms;

namespace VeldridSurfaceTest;

static class Program
{
    public static void Main(string[] args) =>
        new Application(new Eto.GtkSharp.Platform()).Run(new MainForm());
}
