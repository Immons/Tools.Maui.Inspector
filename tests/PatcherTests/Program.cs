using Immons.Tools.Maui.Inspector.Sync;

string dir = Path.Combine(Path.GetTempPath(), "patchertest-" + Environment.ProcessId);
Directory.CreateDirectory(dir);
string file = Path.Combine(dir, "MainPage.xaml");
string contents = "<ContentPage xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\"\n             x:Class=\"Demo.MainPage\">\n    <Grid x:Name=\"Root\">\n        <VerticalStackLayout>\n            <Label Text=\"Hello\" />\n            <Border Stroke=\"Red\" />\n        </VerticalStackLayout>\n        <BoxView Color=\"Blue\" />\n    </Grid>\n</ContentPage>";
File.WriteAllText(file, contents);
XamlPatcher patcher = new XamlPatcher(dir, dryRun: false);
int failures = 0;
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 4, 10, "VerticalStackLayout", "op1", "{\"type\":\"Microsoft.Maui.Controls.Label\",\"asm\":\"Microsoft.Maui.Controls\",\"name\":\"Label\",\"attrs\":{\"Text\":\"New\"}}", Remove: false, "insert"));
Check("insert into stack", Text().Contains("<Label Text=\"New\" />") && Text().IndexOf("<Label Text=\"New\" />") > Text().IndexOf("<Border"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 4, 10, "VerticalStackLayout", "op1", "{\"type\":\"Microsoft.Maui.Controls.Label\",\"asm\":\"Microsoft.Maui.Controls\",\"name\":\"Label\",\"attrs\":{\"Text\":\"Newer\",\"FontSize\":\"20\"}}", Remove: false, "insert"));
Check("upsert replaces", !Text().Contains("Text=\"New\" ") && Text().Contains("<Label Text=\"Newer\" FontSize=\"20\" />") && Text().Split("Newer").Length == 2);
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 8, 10, "BoxView", "Color", "Green"));
Check("attr below shifted insert", Text().Contains("<BoxView Color=\"Green\" />"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 6, 14, "Border", "rmop1", "", Remove: true, "remove-el"));
Check("remove element", !Text().Contains("<Border"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 8, 10, "BoxView", "Opacity", "0.5"));
Check("attr below removal", Text().Contains("Opacity=\"0.5\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 6, 14, "Border", "rmop1", "", Remove: false, "remove-el"));
Check("restore element", Text().Contains("<Border Stroke=\"Red\" />"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 6, 14, "Border", "op2", "{\"type\":\"Demo.Controls.FancyBadge\",\"asm\":\"Demo\",\"name\":\"FancyBadge\",\"attrs\":{}}", Remove: false, "insert"));
Check("self-closing expand + xmlns", Text().Contains("xmlns:ctl=\"clr-namespace:Demo.Controls;assembly=Demo\"") && Text().Contains("<ctl:FancyBadge  />".Replace("  ", " ")) && Text().Contains("</Border>"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 6, 14, "Border", "op2", "", Remove: true, "insert"));
Check("cancel insert restores tag", !Text().Contains("FancyBadge") && !Text().Contains("</Border>") && Text().Contains("<Border Stroke=\"Red\" />"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 4, 10, "VerticalStackLayout", "op1", "", Remove: true, "insert"));
Check("cancel first insert", !Text().Contains("Newer"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 6, 14, "Border", "mv1", "{\"sibLine\":5,\"sibColumn\":14,\"sibElement\":\"Label\",\"before\":true}", Remove: false, "move-el"));
Check("move before", Text().IndexOf("<Border") < Text().IndexOf("<Label Text=\"Hello\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 8, 10, "BoxView", "IsVisible", "False"));
Check("attr below move", Text().Contains("IsVisible=\"False\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 6, 14, "Border", "mv2", "{\"sibLine\":5,\"sibColumn\":14,\"sibElement\":\"Label\",\"before\":false}", Remove: false, "move-el"));
Check("move after", Text().IndexOf("<Label Text=\"Hello\"") < Text().IndexOf("<Border"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 4, 10, "VerticalStackLayout", "op3", "{\"type\":\"Microsoft.Maui.Controls.Button\",\"asm\":\"m\",\"name\":\"Button\",\"attrs\":{\"Text\":\"B1\"},\"sibLine\":5,\"sibColumn\":14,\"sibElement\":\"Label\",\"before\":true}", Remove: false, "insert"));
Check("anchored insert before sibling", Text().IndexOf("<Button Text=\"B1\" />") < Text().IndexOf("<Label Text=\"Hello\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 4, 10, "VerticalStackLayout", "op4", "{\"type\":\"Microsoft.Maui.Controls.Button\",\"asm\":\"m\",\"name\":\"Button\",\"attrs\":{\"Text\":\"B2\"},\"anchorOp\":\"op3\",\"before\":false}", Remove: false, "insert"));
int num = Text().IndexOf("<Button Text=\"B1\" />");
int num2 = Text().IndexOf("<Button Text=\"B2\" />");
Check("insert anchored to insert", num >= 0 && num2 > num && num2 < Text().IndexOf("<Label Text=\"Hello\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 8, 10, "BoxView", "rp1", "{\"parLine\":4,\"parColumn\":10,\"parElement\":\"VerticalStackLayout\",\"before\":false}", Remove: false, "move-el"));
Check("reparent into stack", Text().IndexOf("<BoxView") > Text().IndexOf("<Label Text=\"Hello\"") && Text().IndexOf("<BoxView") < Text().IndexOf("</VerticalStackLayout>") && Text().Contains("\n            <BoxView"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 8, 10, "BoxView", "CornerRadius", "4"));
Check("attr after reparent", Text().Contains("CornerRadius=\"4\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 8, 10, "BoxView", "rp2", "{\"parLine\":6,\"parColumn\":14,\"parElement\":\"Border\",\"before\":false}", Remove: false, "move-el"));
Check("reparent into self-closing", Text().Contains("</Border>") && Text().IndexOf("<BoxView") > Text().IndexOf("<Border") && Text().IndexOf("<BoxView") < Text().IndexOf("</Border>"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 5, 14, "Label", "wr1", "{\"type\":\"Microsoft.Maui.Controls.Grid\",\"asm\":\"m\",\"name\":\"Grid\",\"attrs\":{}}", Remove: false, "wrap-el"));
int num3 = Text().IndexOf("<Grid>");
Check("wrap in grid", num3 >= 0 && num3 < Text().IndexOf("<Label Text=\"Hello\"") && Text().IndexOf("<Label Text=\"Hello\"") < Text().IndexOf("</Grid>") && Text().Contains("\n                <Label Text=\"Hello\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 5, 14, "Label", "wr1", "{\"type\":\"Microsoft.Maui.Controls.Grid\",\"asm\":\"m\",\"name\":\"Grid\",\"attrs\":{\"Padding\":\"8\"}}", Remove: false, "wrap-el"));
Check("wrapper attr upsert", Text().Contains("<Grid Padding=\"8\">") && !Text().Contains("<Grid>"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 5, 14, "Label", "TextColor", "Red"));
Check("attr on wrapped element", Text().Contains("<Label TextColor=\"Red\" Text=\"Hello\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 5, 14, "Label", "wr1", "", Remove: true, "wrap-el"));
Check("cancel wrap", !Text().Contains("<Grid Padding") && Text().Contains("\n            <Label TextColor=\"Red\" Text=\"Hello\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 5, 14, "Label", "FontSize", "22"));
Check("attr after unwrap", Text().Contains("FontSize=\"22\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 4, 10, "VerticalStackLayout", "uw1", "", Remove: true, "unwrap-el"));
Check("unwrap container", !Text().Contains("VerticalStackLayout") && Text().Contains("\n        <Button Text=\"B1\" />") && Text().IndexOf("<Button Text=\"B1\"") < Text().IndexOf("<Label"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 5, 14, "Label", "LineHeight", "1.2"));
Check("attr on promoted child", Text().Contains("LineHeight=\"1.2\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 8, 10, "BoxView", "ZIndex", "3"));
Check("attr deep after unwrap", Text().Contains("ZIndex=\"3\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 4, 10, "VerticalStackLayout", "uw1", "", Remove: false, "unwrap-el"));
Check("undo unwrap", Text().Contains("<VerticalStackLayout>") && Text().Contains("</VerticalStackLayout>") && Text().Contains("\n            <Button Text=\"B1\" />") && Text().Contains("LineHeight=\"1.2\"") && Text().Contains("ZIndex=\"3\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 5, 14, "Label", "MaxLines", "2"));
Check("attr after undo unwrap", Text().Contains("MaxLines=\"2\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 4, 10, "VerticalStackLayout", "op9", "{\"type\":\"Microsoft.Maui.Controls.Border\",\"asm\":\"m\",\"name\":\"Border\",\"attrs\":{\"Stroke\":\"Blue\"},\"childrenXml\":\"<VerticalStackLayout Spacing=\\\"4\\\">\\n    <Label Text=\\\"Copy\\\" />\\n</VerticalStackLayout>\"}", Remove: false, "insert"));
Check("paste with subtree", Text().Contains("<Border Stroke=\"Blue\">") && Text().IndexOf("<Label Text=\"Copy\" />") > Text().IndexOf("<Border Stroke=\"Blue\">") && Text().Contains("\n                    <Label Text=\"Copy\" />") && Text().IndexOf("</Border>", Text().IndexOf("<Border Stroke=\"Blue\">")) > 0);
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 6, 14, "Border", "StrokeThickness", "2"));
Check("attr below subtree insert", Text().Contains("StrokeThickness=\"2\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 4, 10, "VerticalStackLayout", "op10", "{\"type\":\"Microsoft.Maui.Controls.Grid\",\"asm\":\"m\",\"name\":\"Grid\",\"attrs\":{},\"childrenXml\":\"<p1:FancyBadge Glow=\\\"True\\\" />\\n<p2:EspButton />\",\"xmlns\":{\"p1\":\"clr-namespace:Demo.Controls;assembly=Demo\",\"p2\":\"clr-namespace:Esp.Widgets;assembly=Esp\"}}", Remove: false, "insert"));
Check("paste custom xmlns", Text().Contains("<ctl:FancyBadge Glow=\"True\" />") && Text().Contains("xmlns:ctl2=\"clr-namespace:Esp.Widgets;assembly=Esp\"") && Text().Contains("<ctl2:EspButton />") && !Text().Contains("p1:") && !Text().Contains("p2:"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 1, 2, "ContentPage", "sty1", "{\"xml\":\"<Style x:Key=\\\"CardStyle\\\" TargetType=\\\"Label\\\">\\n    <Setter Property=\\\"TextColor\\\" Value=\\\"#FF112233\\\" />\\n</Style>\"}", Remove: false, "style-res"));
Check("style into new resources", Text().Contains("<ContentPage.Resources>") && Text().IndexOf("<Style x:Key=\"CardStyle\"") > Text().IndexOf("<ContentPage.Resources>") && Text().IndexOf("</ContentPage.Resources>") > Text().IndexOf("</Style>"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 1, 2, "ContentPage", "sty2", "{\"xml\":\"<Style x:Key=\\\"PillStyle\\\" TargetType=\\\"Button\\\">\\n    <Setter Property=\\\"Padding\\\" Value=\\\"8\\\" />\\n</Style>\"}", Remove: false, "style-res"));
Check("style into existing resources", Text().Split("<ContentPage.Resources>").Length == 2 && Text().Contains("PillStyle"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 0, 0, "Style", "TextColor", "{\"key\":\"CardStyle\",\"targetType\":\"Label\",\"property\":\"TextColor\",\"value\":\"#FF445566\"}", Remove: false, "setter"));
Check("setter patched", Text().Contains("Value=\"#FF445566\"") && !Text().Contains("#FF112233"));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 0, 0, "Style", "FontSize", "{\"key\":\"CardStyle\",\"targetType\":\"Label\",\"property\":\"FontSize\",\"value\":\"20\"}", Remove: false, "setter"));
int num4 = Text().IndexOf("<Style x:Key=\"CardStyle\"");
int num5 = Text().IndexOf("</Style>", num4);
string text2 = Text();
int num6 = num4;
Check("setter appended", text2.Substring(num6, num5 - num6).Contains("Property=\"FontSize\" Value=\"20\""));
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 1, 2, "ContentPage", "sty2", "", Remove: true, "style-res"));
Check("cancel style", !Text().Contains("PillStyle") && Text().Contains("CardStyle"));
string dictFile = Path.Combine(dir, "Colors.xaml");
File.WriteAllText(dictFile, "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\"\n                    xmlns:x=\"http://schemas.microsoft.com/winfx/2009/xaml\">\n    <x:Double x:Key=\"TitleSize\">24</x:Double>\n    <Color x:Key=\"Primary\">#FF112233</Color>\n</ResourceDictionary>");
patcher.Apply(new XamlChange("Colors.xaml;assembly=Demo", 0, 0, "Resource", "TitleSize", "{\"key\":\"TitleSize\",\"value\":\"32\"}", Remove: false, "res-val"));
Check("res-val double", Dict().Contains(">32</x:Double>") && !Dict().Contains(">24<"));
patcher.Apply(new XamlChange("Colors.xaml;assembly=Demo", 0, 0, "Resource", "Primary", "{\"key\":\"Primary\",\"value\":\"#FF445566\"}", Remove: false, "res-val"));
Check("res-val color", Dict().Contains(">#FF445566</Color>") && Dict().Contains(">32</x:Double>"));
patcher.Apply(new XamlChange("Colors.xaml;assembly=Demo", 0, 0, "Resource", "TitleSize", "{\"key\":\"TitleSize\",\"value\":\"48\"}", Remove: false, "res-val"));
Check("res-val re-edit", Dict().Contains(">48</x:Double>") && Dict().Contains(">#FF445566</Color>"));
// 39. "{inspector:Adaptive}" placeholder: xmlns for the Extensions namespace is declared
// on the root and the placeholder prefix rewritten to the file's actual one.
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 8, 10, "BoxView", "Margin",
    "{inspector:Adaptive Phone='1,2,3,4', Default='5,6,7,8'}"));
var extPrefix = System.Text.RegularExpressions.Regex.Match(Text(),
    "xmlns:(\\w+)=\"clr-namespace:Immons.Tools.Maui.Inspector.Extensions").Groups[1].Value;
Check("adaptive prefix rewritten", extPrefix.Length > 0
    && Text().Contains($"Margin=\"{{{extPrefix}:Adaptive Phone='1,2,3,4', Default='5,6,7,8'}}\"")
    && !Text().Contains("{inspector:"));

// 40. A second Adaptive value reuses the declared prefix instead of re-declaring.
patcher.Apply(new XamlChange("MainPage.xaml;assembly=Demo", 8, 10, "BoxView", "Opacity",
    "{inspector:Adaptive Phone=0.5, Default=1}"));
Check("adaptive reuses xmlns", Text().Split("clr-namespace:Immons.Tools.Maui.Inspector.Extensions").Length == 2
    && Text().Contains($"Opacity=\"{{{extPrefix}:Adaptive Phone=0.5, Default=1}}\""));

Console.WriteLine();
Console.WriteLine(Text());
return failures == 0 ? 0 : 1;
void Check(string name, bool condition)
{
	Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
	if (!condition)
	{
		failures++;
	}
}
string Dict()
{
	return File.ReadAllText(dictFile);
}
string Text()
{
	return File.ReadAllText(file);
}
