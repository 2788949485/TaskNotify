using System.Globalization;
using System.Windows.Data;

namespace TaskNotify.Desktop.Converters;

/// <summary>
/// Converts an enum value to/from a boolean for RadioButton two-way binding.
/// Pass the target enum value as <c>ConverterParameter</c>. Returns true when
/// the bound value equals the parameter; setting to true writes the parameter back.
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        => value is not null && parameter is not null && value.Equals(parameter);

    public object? ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        => value is true ? parameter : System.Windows.Data.Binding.DoNothing;
}
