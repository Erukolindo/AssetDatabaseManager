using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AssetDatabaseManager
{
    public partial class InputWindow : Window
    {
        public string Result { get; private set; }
        public string ResultColor { get; private set; } = "ffffff";

        public InputWindow()
        {
            InitializeComponent();
            InputTextBox.Focus();
        }

        public InputWindow(bool isColorPickerEnabled)
        {
            InitializeComponent();
            InputTextBox.Focus();
            ColorPickerButton.Visibility = isColorPickerEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Result = InputTextBox.Text;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Enter)
            {
                Ok_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                Cancel_Click(sender, e);
            }
        }

        private void ColorPickerButton_Click(object sender, RoutedEventArgs e)
        {
            // Open a color picker dialog and set result color to hex value
            var colorDialog = new System.Windows.Forms.ColorDialog();
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var selectedColor = colorDialog.Color;
                ResultColor = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}"; // Convert to hex format
                ColorPickerButton.Background = new SolidColorBrush(Color.FromRgb(selectedColor.R, selectedColor.G, selectedColor.B));
                double selectedLuminance = (0.299 * selectedColor.R + 0.587 * selectedColor.G + 0.114 * selectedColor.B) / 255;
                ColorPickerButton.Foreground = selectedLuminance > 0.5 ? Brushes.Black : Brushes.White;
            }
        }

        public void SetDisplayedColor(string hexColor)
        {
            ResultColor = hexColor;
            Color color = (Color)ColorConverter.ConvertFromString(hexColor);
            if (ColorPickerButton != null)
            {
                ColorPickerButton.Background = (Brush)new BrushConverter().ConvertFromString(hexColor);
                double selectedLuminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
                ColorPickerButton.Foreground = selectedLuminance > 0.5 ? Brushes.Black : Brushes.White;
            }
        }
    }
}
