using Microsoft.Extensions.Logging;
using Immons.Tools.Maui.Inspector;
using Immons.Tools.Maui.Inspector.Persistency;

namespace SampleApp;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder
			.UseMauiInspector(options =>
			{
				options.LongPressDuration = TimeSpan.FromMilliseconds(800);
				options.EnableWebServer = true;
				options.ShakeToOpen = true;
				options.SeedRulesAsset = "inspector-rules.json";
				// options.Cookbook.IncludedControls.Add("SampleApp.Controls.");                 // render only the design system (namespace or XAML folder prefix)
				// options.Cookbook.ExcludedControls.Add("SampleApp.Controls.CameraPreview");   // keep hardware-driving controls out of the cookbook
				// options.Cookbook.ExcludedResources.Add("colors:Gray");                       // resources by key / dictionary file / image name, optionally per section
				// options.Cookbook.BindingContext = () => new DesignTimeViewModel();          // what the screens give the controls: texts, theme colors
				// options.Cookbook.LightBackground = Color.FromArgb("#F4F1FA");              // the backdrop the app's pages use (per theme, or Background for a brush)
			})
			.UseMauiInspectorPersistency()   // mock rules and scenarios in SQLite instead of Preferences
			.Logging.AddDebug();
		builder.Logging.AddMauiInspector();
#endif

		return builder.Build();
	}
}
