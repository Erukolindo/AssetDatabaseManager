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
    /// <summary>
    /// Interaction logic for SingleTagSelectionWindow.xaml
    /// </summary>
    public partial class SingleTagSelectionWindow : Window
    {
        public Tag result = null;

        public SingleTagSelectionWindow()
        {
            InitializeComponent();
            ttpSelector.TagToggled += TagButtonClick;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TagButtonClick(TagDisplay tagDisplay)
        {
            tagDisplay.IsSelected = !tagDisplay.IsSelected;
            result = tagDisplay.Tag;
            DialogResult = true;
        }
    }
}
