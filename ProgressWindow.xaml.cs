using System.Windows;

namespace AULGK
{
    public partial class ProgressWindow : Window
    {
        public ProgressWindow(string message)
        {
            InitializeComponent();
            StatusText.Text = message;
        }
    }
}