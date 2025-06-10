using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

public class TagDisplay : INotifyPropertyChanged
{
    public Tag Tag;

    public string Name
    {
        get
        {
            return Tag.TagName;
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public Brush BackgroundBrush => (Brush)new BrushConverter().ConvertFromString(Tag.Color);
    public Brush ForegroundBrush
    {
        get
        {
            var color = (Color)ColorConverter.ConvertFromString(Tag.Color);
            double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            return luminance > 0.5 ? Brushes.Black : Brushes.White;
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
