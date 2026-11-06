using System.ComponentModel;
using System.Windows.Media;
using Sortopia.WPF.Rendering;

namespace Sortopia.WPF.ViewModels.Shared.Controls;

public interface IParameterValue : INotifyPropertyChanged
{
    double Value { get; set; }
    double SliderValue { get => Value; set => Value = value; }
}

/// <summary>A labeled, rectangular view of editable parameters in row-major order.</summary>
public sealed class ParameterArrayViewModel
{
    public ParameterArrayViewModel(double[,] values, double minimum = 0, double maximum = 1,
        string[]? rowHeaders = null, string[]? columnHeaders = null)
        : this(Headers(rowHeaders, (values ?? throw new ArgumentNullException(nameof(values))).GetLength(0), "Row"),
            Headers(columnHeaders, values.GetLength(1), "Column"),
            (row, column) => new ArrayValue(values, row, column, minimum, maximum), minimum, maximum) { }

    internal ParameterArrayViewModel(string[] rowHeaders, string[] columnHeaders, Func<int, int, IParameterValue> parameterAt,
        double minimum = 0, double maximum = 1)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum - minimum) || maximum <= minimum)
            throw new ArgumentException("The slider range must have a finite, positive span.");

        Minimum = minimum;
        Maximum = maximum;
        SmallChange = (maximum - minimum) / 100;
        LargeChange = (maximum - minimum) / 10;
        RowHeaders = Array.AsReadOnly(rowHeaders.ToArray());
        ColumnHeaders = Array.AsReadOnly(columnHeaders.ToArray());
        var cells = new List<ParameterCell>();
        for (int row = 0; row < RowHeaders.Count; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                var parameter = parameterAt(row, column);
                if (!double.IsFinite(parameter.SliderValue) || parameter.SliderValue < minimum || parameter.SliderValue > maximum)
                    throw new ArgumentOutOfRangeException(nameof(parameterAt), $"Parameter [{row}, {column}] is outside the specified range.");
                cells.Add(new ParameterCell(this, $"{RowHeaders[row]}, {ColumnHeaders[column]}", parameter));
            }
        }
        Cells = cells.AsReadOnly();
    }

    public IReadOnlyList<string> RowHeaders { get; }
    public IReadOnlyList<string> ColumnHeaders { get; }
    public int Columns => ColumnHeaders.Count;
    public double Minimum { get; }
    public double Maximum { get; }
    public double SmallChange { get; init; }
    public double LargeChange { get; init; }
    public IReadOnlyList<ParameterCell> Cells { get; }

    private static string[] Headers(string[]? headers, int count, string axis)
    {
        if (headers is null) return Enumerable.Range(1, count).Select(i => $"{axis} {i}").ToArray();
        if (headers.Length != count) throw new ArgumentException($"{axis} header count must match the array.", nameof(headers));
        return headers;
    }

    private sealed class ArrayValue(double[,] values, int row, int column, double minimum, double maximum)
        : ViewModelBase, IParameterValue
    {
        public double Value
        {
            get => values[row, column];
            set
            {
                if (!double.IsFinite(value) || value < minimum || value > maximum)
                    throw new ArgumentOutOfRangeException(nameof(value));
                SetProperty(ref values[row, column], value);
            }
        }
    }
}

public sealed class ParameterCell : ViewModelBase
{
    private readonly ParameterArrayViewModel array;
    private readonly IParameterValue parameter;

    internal ParameterCell(ParameterArrayViewModel array, string label, IParameterValue parameter)
    {
        this.array = array;
        this.parameter = parameter;
        Label = label;
        PropertyChangedEventManager.AddHandler(parameter, ValueChanged, nameof(parameter.Value));
    }

    public string Label { get; }
    public double Value { get => parameter.Value; set => parameter.Value = value; }
    public double SliderValue { get => parameter.SliderValue; set => parameter.SliderValue = value; }

    private void ValueChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(SliderValue));
        OnPropertyChanged(nameof(Brush));
        OnPropertyChanged(nameof(TextBrush));
    }

    public SolidColorBrush Brush
    {
        get
        {
            var fraction = (SliderValue - array.Minimum) / (array.Maximum - array.Minimum);
            return SpectralPalette.Shades[(int)Math.Round(fraction * (SpectralPalette.Shades.Count - 1))];
        }
    }

    public SolidColorBrush TextBrush
    {
        get
        {
            static double Linear(byte channel)
            {
                var value = channel / 255.0;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            var color = Brush.Color;
            var luminance = 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
            return luminance > 0.179 ? Brushes.Black : Brushes.White;
        }
    }
}
