using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BricsAI.Overlay
{
    public partial class MainWindow : Window
    {
        // ── Zoom / pan state ──────────────────────────────────────────────────
        private double _zoomLevel = 1.0;
        private const double ZoomStep = 0.25;
        private const double MinZoom  = 0.5;
        private const double MaxZoom  = 8.0;
        private bool _isDragging;
        private System.Windows.Point _lastMousePos;

        private ScaleTransform     ImageScale     => (ScaleTransform)    ((TransformGroup)PopupImageElement.RenderTransform).Children[0];
        private TranslateTransform ImageTranslate => (TranslateTransform)((TransformGroup)PopupImageElement.RenderTransform).Children[1];

        private void ResetPopupZoom()
        {
            _zoomLevel        = 1.0;
            ImageScale.ScaleX = 1.0;
            ImageScale.ScaleY = 1.0;
            ImageTranslate.X  = 0;
            ImageTranslate.Y  = 0;
            ZoomLabel.Text    = "100%";
            ImageViewport.Cursor = Cursors.Arrow;
        }

        private void ApplyZoom(double newZoom, double? pivotX = null, double? pivotY = null)
        {
            newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
            if (Math.Abs(newZoom - _zoomLevel) < 0.001) return;

            if (pivotX.HasValue && pivotY.HasValue)
            {
                double ratio = newZoom / _zoomLevel;
                ImageTranslate.X = pivotX.Value + (ImageTranslate.X - pivotX.Value) * ratio;
                ImageTranslate.Y = pivotY.Value + (ImageTranslate.Y - pivotY.Value) * ratio;
            }

            _zoomLevel        = newZoom;
            ImageScale.ScaleX = _zoomLevel;
            ImageScale.ScaleY = _zoomLevel;
            ZoomLabel.Text    = $"{(int)(_zoomLevel * 100)}%";

            if (_zoomLevel <= 1.0) { ImageTranslate.X = 0; ImageTranslate.Y = 0; }
            ImageViewport.Cursor = _zoomLevel > 1.0 ? Cursors.SizeAll : Cursors.Arrow;
        }

        // ── Zoom button handlers ──────────────────────────────────────────────
        private void ZoomIn_Click(object sender, RoutedEventArgs e)  => ApplyZoom(_zoomLevel + ZoomStep);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => ApplyZoom(_zoomLevel - ZoomStep);
        private void ZoomReset_Click(object sender, RoutedEventArgs e) => ResetPopupZoom();

        // ── Scroll wheel zoom (towards cursor) ────────────────────────────────
        private void ImageViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var pos = e.GetPosition(ImageViewport);
            ApplyZoom(_zoomLevel + (e.Delta > 0 ? ZoomStep : -ZoomStep), pos.X, pos.Y);
            e.Handled = true;
        }

        // ── Drag-to-pan ───────────────────────────────────────────────────────
        private void ImageViewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_zoomLevel <= 1.0) return;
            _isDragging = true;
            _lastMousePos = e.GetPosition(ImageViewport);
            ImageViewport.CaptureMouse();
            ImageViewport.Cursor = Cursors.Hand;
            e.Handled = true;
        }

        private void ImageViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var pos   = e.GetPosition(ImageViewport);
            var delta = pos - _lastMousePos;
            ImageTranslate.X += delta.X;
            ImageTranslate.Y += delta.Y;
            _lastMousePos = pos;
        }

        private void ImageViewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            ImageViewport.ReleaseMouseCapture();
            ImageViewport.Cursor = _zoomLevel > 1.0 ? Cursors.SizeAll : Cursors.Arrow;
        }

        // ── Constructor ───────────────────────────────────────────────────────
        public MainWindow()
        {
            try
            {
                System.IO.File.AppendAllText("debug_log.txt", "MainWindow constructor called\n");
                InitializeComponent();
                System.IO.File.AppendAllText("debug_log.txt", "MainWindow InitializeComponent finished\n");

                if (DataContext is ViewModels.MainViewModel vm)
                {
                    vm.Messages.CollectionChanged += (s, e) =>
                    {
                        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                        {
                            ChatScrollViewer.ScrollToBottom();
                        }
                    };

                    // Reset zoom whenever a new snapshot image is loaded into the popup
                    vm.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(ViewModels.MainViewModel.PopupImage) && vm.PopupImage != null)
                            ResetPopupZoom();
                    };
                }
            }
            catch (System.Exception ex)
            {
                System.IO.File.WriteAllText("constructor_error.txt", ex.ToString());
                throw;
            }
        }

        protected override void OnContentRendered(System.EventArgs e)
        {
            base.OnContentRendered(e);
            System.IO.File.AppendAllText("debug_log.txt", "MainWindow ContentRendered\n");
        }

        protected override void OnClosed(System.EventArgs e)
        {
            System.IO.File.AppendAllText("debug_log.txt", "MainWindow Closed\n");
            base.OnClosed(e);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (WindowState == WindowState.Normal)
                    WindowState = WindowState.Maximized;
                else
                    WindowState = WindowState.Normal;
            }
            else
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ImagePopup_BackgroundClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.IsPopupVisible = false;
                vm.PopupImage = null;
                vm.PopupStatusText = null;
                vm.IsPopupLoading = false;
            }
        }

        private void ImagePopup_InnerClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // Prevent click from bubbling to background and closing popup
        }

        private bool _isExpanded = false;
        private const double CollapsedWidth = 650;
        private const double ExpandedWidth = 1100;

        private void ToggleExpand_Click(object sender, MouseButtonEventArgs e)
        {
            _isExpanded = !_isExpanded;
            Width = _isExpanded ? ExpandedWidth : CollapsedWidth;
            ExpandArrow.Text = _isExpanded ? "❮" : "❯";
            ExpandStrip.ToolTip = _isExpanded ? "Collapse to normal width" : "Expand to show full table width";
        }

        private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
        {
             if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    // Allow the TextBox to handle the newline naturally
                    return;
                }

                var vm = (ViewModels.MainViewModel)DataContext;
                if (vm.SendCommand.CanExecute(null))
                {
                    vm.SendCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}
