using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HandyControl.Tools.Extension;

namespace MaaWpfGui.Views.UI
{
    partial class VersionUpdateView
    {
        public VersionUpdateView()
        {
            InitializeComponent();

        }

        private UIElement scrollViewer;

        private void UpdateInfoMarkdownDocument_Loaded(object sender, RoutedEventArgs e)
        {
            var viewer = sender as FlowDocumentScrollViewer;
            scrollViewer = viewer.FindDescendantByName("PART_ContentHost");
        }

        private void UpdateInfoMarkdownDocument_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            var e2 = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta);
            e2.RoutedEvent = UIElement.MouseWheelEvent;
            scrollViewer.RaiseEvent(e2);
        }
    }
}
