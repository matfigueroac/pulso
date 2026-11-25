using System.Windows;
using System.Windows.Controls;

namespace LumenPlayer;

public partial class MediaDetailsPanel : UserControl
{
    internal MediaDetailsPanel(string title, string location, string format, string duration, string image)
    {
        InitializeComponent();
        FileTitle.Text = title;
        Location.Text = location;
        Format.Text = $"{format}   ·   {duration}";
        Image.Text = image;
        Image.Visibility = image.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
