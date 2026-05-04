using System;
using System.Windows;
using System.Windows.Input;

namespace ACVN
{
    public partial class FullscreenWindow : Window
    {
        public FullscreenWindow(Uri mediaSource)
        {
            InitializeComponent();
            media.Source = mediaSource;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.F11 || e.Key == Key.Space)
                Close();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Close();
        }
    }
}
