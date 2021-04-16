using Eto.Wpf.Forms.Controls;
using System;
using System.Windows.Media;

namespace Eto.Test.Wpf
{
	class Startup
	{
		[STAThread]
		static void Main(string[] args)
		{
			var platform = new Eto.Wpf.Platform();

			// optional - enables GDI text display mode
			/**
			Style.Add<Eto.Wpf.Forms.FormHandler>(null, handler => TextOptions.SetTextFormattingMode(handler.Control, TextFormattingMode.Display));
			Style.Add<Eto.Wpf.Forms.DialogHandler>(null, handler => TextOptions.SetTextFormattingMode(handler.Control, TextFormattingMode.Display));
			/**/

			var app = new TestApplication(platform);
			app.TestAssemblies.Add(typeof(Startup).Assembly);

			Style.Add<Eto.Forms.Window>(null, w => w.BackgroundColor = Eto.Drawing.Colors.Transparent);

			System.Windows.Application.Current.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("pack://application:,,,/DynamicAero2;component/Theme.xaml", UriKind.RelativeOrAbsolute) });
			System.Windows.Application.Current.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("pack://application:,,,/DynamicAero2;component/Brushes/Dark.xaml", UriKind.RelativeOrAbsolute) });

			app.Run();
		}

	}
}

