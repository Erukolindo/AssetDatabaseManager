using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AssetDatabaseManager.WindowElements
{
    /// <summary>
    /// Interaction logic for TagTogglePanel.xaml
    /// </summary>
    public partial class TagTogglePanel : UserControl
    {
        public TagTogglePanel()
        {
            InitializeComponent();
        }

        public event Action<TagDisplay> TagToggled;

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle && toggle.DataContext is TagDisplay tag && TagToggled != null)
            {
                TagToggled(tag);
            }
        }
    }
}
