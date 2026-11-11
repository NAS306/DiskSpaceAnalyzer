using System.Windows.Data;
using System.Windows.Markup;
using DiskSpaceAnalyzer.Core;

namespace DiskSpaceAnalyzer.Controls;

[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Localization.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
