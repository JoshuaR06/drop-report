using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DropReport.App.Controls;

/// <summary>
/// A TextBlock that counts up to its value ("1,149", "6.1%", "3.8") the way a scoreboard rolls, keeping the value's own
/// format (thousands separators, decimals, prefix/suffix). Text that isn't a number is shown as is.
/// </summary>
public sealed partial class Ticker : TextBlock
{
    public static readonly StyledProperty<string> ValueProperty = AvaloniaProperty.Register<Ticker, string>(nameof(Value));
    public string Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    protected override Type StyleKeyOverride => typeof(TextBlock);   // keep TextBlock.hero / .stat styles

    [GeneratedRegex(@"^(?<pre>[^\d]*)(?<num>\d[\d,]*(?:\.\d+)?)(?<post>.*)$")]
    private static partial Regex NumberParts();

    private DispatcherTimer _timer;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty) Start(change.GetNewValue<string>());
    }

    private void Start(string value)
    {
        _timer?.Stop();
        var m = value == null ? null : NumberParts().Match(value);
        if (m == null || !m.Success || !double.TryParse(m.Groups["num"].Value, NumberStyles.Number, CultureInfo.CurrentCulture, out var target))
        {
            Text = value;
            return;
        }
        var num = m.Groups["num"].Value;
        var decimals = num.Contains('.') ? num.Length - num.IndexOf('.') - 1 : 0;
        var format = (num.Contains(',') ? "N" : "F") + decimals;
        string Show(double v) => m.Groups["pre"].Value + v.ToString(format, CultureInfo.CurrentCulture) + m.Groups["post"].Value;
        var started = DateTime.UtcNow;
        const double ms = 650;
        Text = Show(0);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            var k = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / ms);
            var eased = 1 - Math.Pow(1 - k, 3);   // ease-out cubic: fast start, settles on the number
            Text = Show(k >= 1 ? target : target * eased);
            if (k >= 1) _timer.Stop();
        });
        _timer.Start();
    }
}
