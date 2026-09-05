using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfControl = System.Windows.Controls.Control;
using WpfCursors = System.Windows.Input.Cursors;
using WpfPath = System.Windows.Shapes.Path;
using WpfSize = System.Windows.Size;

namespace OrbitalVue.Player.Controls;

internal static class UiUsabilityService
{
    internal const double MinimumReadableFontSize = 12d;
    private const double PasswordRevealButtonSpace = 46d;
    private static readonly ConditionalWeakTable<PasswordBox, PasswordRevealAdorner> PasswordAdorners = new();

    public static void Enable()
    {
        EventManager.RegisterClassHandler(
            typeof(WpfControl),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(Control_Loaded));
        EventManager.RegisterClassHandler(
            typeof(TextBlock),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(TextBlock_Loaded));
        EventManager.RegisterClassHandler(
            typeof(PasswordBox),
            FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(PasswordBox_Unloaded));
    }

    private static void Control_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfControl control) return;
        EnsureReadableFont(control);
        if (control is PasswordBox passwordBox) QueuePasswordReveal(passwordBox);
    }

    private static void TextBlock_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock textBlock && textBlock.FontSize < MinimumReadableFontSize)
            textBlock.SetCurrentValue(TextBlock.FontSizeProperty, MinimumReadableFontSize);
    }

    private static void EnsureReadableFont(WpfControl control)
    {
        if (control.FontSize < MinimumReadableFontSize)
            control.SetCurrentValue(WpfControl.FontSizeProperty, MinimumReadableFontSize);
    }

    private static void QueuePasswordReveal(PasswordBox passwordBox)
    {
        var padding = passwordBox.Padding;
        if (padding.Right < PasswordRevealButtonSpace)
        {
            passwordBox.SetCurrentValue(
                WpfControl.PaddingProperty,
                new Thickness(padding.Left, padding.Top, PasswordRevealButtonSpace, padding.Bottom));
        }

        passwordBox.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => AttachPasswordReveal(passwordBox)));
    }

    private static void AttachPasswordReveal(PasswordBox passwordBox)
    {
        if (!passwordBox.IsLoaded || PasswordAdorners.TryGetValue(passwordBox, out _)) return;
        var layer = AdornerLayer.GetAdornerLayer(passwordBox);
        if (layer is null) return;

        var adorner = new PasswordRevealAdorner(passwordBox);
        layer.Add(adorner);
        PasswordAdorners.Add(passwordBox, adorner);
    }

    private static void PasswordBox_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox ||
            !PasswordAdorners.TryGetValue(passwordBox, out var adorner)) return;

        AdornerLayer.GetAdornerLayer(passwordBox)?.Remove(adorner);
        adorner.Dispose();
        PasswordAdorners.Remove(passwordBox);
    }
}

internal sealed class PasswordRevealAdorner : Adorner, IDisposable
{
    private const double ToggleWidth = 38d;
    private readonly VisualCollection _visuals;
    private readonly PasswordBox _passwordBox;
    private readonly Border _revealSurface;
    private readonly TextBlock _revealedText;
    private readonly WpfButton _toggleButton;
    private bool _revealed;
    private bool _disposed;

    public PasswordRevealAdorner(PasswordBox passwordBox) : base(passwordBox)
    {
        _passwordBox = passwordBox;
        _visuals = new VisualCollection(this);

        _revealedText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontFamily = passwordBox.FontFamily,
            FontSize = passwordBox.FontSize,
            Foreground = passwordBox.Foreground
        };

        _revealSurface = new Border
        {
            Background = passwordBox.Background,
            Padding = new Thickness(Math.Max(8d, passwordBox.Padding.Left), 0, 6, 0),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = _revealedText
        };

        _toggleButton = new WpfButton
        {
            Width = 34,
            Height = 30,
            Padding = new Thickness(7),
            Background = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = WpfCursors.Hand,
            ToolTip = "Show password",
            Focusable = true,
            Content = CreateEyeGlyph()
        };

        if (WpfApplication.Current.TryFindResource("IconButton") is Style iconStyle)
            _toggleButton.Style = iconStyle;

        AutomationProperties.SetName(_toggleButton, "Show password");
        _toggleButton.Click += ToggleButton_Click;
        _passwordBox.PasswordChanged += PasswordBox_PasswordChanged;

        _visuals.Add(_revealSurface);
        _visuals.Add(_toggleButton);
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override WpfSize MeasureOverride(WpfSize constraint)
    {
        for (var index = 0; index < _visuals.Count; index++)
        {
            if (_visuals[index] is UIElement child) child.Measure(constraint);
        }
        return constraint;
    }

    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        var contentWidth = Math.Max(0, finalSize.Width - ToggleWidth - 2);
        _revealSurface.Arrange(new Rect(1, 1, contentWidth, Math.Max(0, finalSize.Height - 2)));
        _toggleButton.Arrange(new Rect(
            Math.Max(0, finalSize.Width - ToggleWidth),
            Math.Max(0, (finalSize.Height - _toggleButton.Height) / 2),
            _toggleButton.Width,
            _toggleButton.Height));
        return finalSize;
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _revealed = !_revealed;
        if (_revealed)
        {
            _revealedText.Text = _passwordBox.Password;
            _revealSurface.Visibility = Visibility.Visible;
            _toggleButton.ToolTip = "Hide password";
            AutomationProperties.SetName(_toggleButton, "Hide password");
        }
        else
        {
            HidePassword();
        }

        _passwordBox.Focus();
        e.Handled = true;
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_revealed) _revealedText.Text = _passwordBox.Password;
    }

    private void HidePassword()
    {
        _revealed = false;
        _revealedText.Text = string.Empty;
        _revealSurface.Visibility = Visibility.Collapsed;
        _toggleButton.ToolTip = "Show password";
        AutomationProperties.SetName(_toggleButton, "Show password");
    }

    public void Dispose()
    {
        if (_disposed) return;
        HidePassword();
        _toggleButton.Click -= ToggleButton_Click;
        _passwordBox.PasswordChanged -= PasswordBox_PasswordChanged;
        _disposed = true;
    }

    private static WpfPath CreateEyeGlyph()
    {
        var stroke = WpfApplication.Current.TryFindResource("TextMutedBrush") as WpfBrush ?? WpfBrushes.LightGray;
        return new WpfPath
        {
            Data = Geometry.Parse("M2,10 C5,5 9,3 14,3 C19,3 23,5 26,10 C23,15 19,17 14,17 C9,17 5,15 2,10 Z M14,7 C12.343,7 11,8.343 11,10 C11,11.657 12.343,13 14,13 C15.657,13 17,11.657 17,10 C17,8.343 15.657,7 14,7 Z"),
            Stroke = stroke,
            StrokeThickness = 1.5,
            Fill = WpfBrushes.Transparent,
            Stretch = Stretch.Uniform
        };
    }
}
