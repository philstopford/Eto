using Eto.GtkSharp;

namespace NodeShapeBuilder;

/// <summary>
/// Entry point.  Platform handlers are loaded here.
///
/// To switch platform, replace the GtkSharp initialization with either:
///   new Eto.WinForms.Platform()    — Windows Forms
///   new Eto.Wpf.Platform()         — WPF
///   new Eto.Mac.Platform()         — macOS
/// </summary>
class Program
{
	[STAThread]
	static void Main(string[] args)
	{
		new Application(new Platform()).Run(new MainForm());
	}
}
