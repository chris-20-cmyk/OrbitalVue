using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using VlcVideoView = LibVLCSharp.WinForms.VideoView;

namespace OrbitalVue.Player.Controls;

public sealed class MultiviewVideoHost : WindowsFormsHost
{
    private readonly VlcVideoView _videoView;

    public MultiviewVideoHost()
    {
        _videoView = new VlcVideoView
        {
            BackColor = Color.Black,
            Dock = DockStyle.Fill
        };
        Child = _videoView;
    }

    public static readonly DependencyProperty MediaPlayerProperty = DependencyProperty.Register(
        nameof(MediaPlayer),
        typeof(VlcMediaPlayer),
        typeof(MultiviewVideoHost),
        new PropertyMetadata(null, MediaPlayerChanged));

    public VlcMediaPlayer? MediaPlayer
    {
        get => (VlcMediaPlayer?)GetValue(MediaPlayerProperty);
        set => SetValue(MediaPlayerProperty, value);
    }

    private static void MediaPlayerChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is MultiviewVideoHost host)
            host._videoView.MediaPlayer = args.NewValue as VlcMediaPlayer;
    }
}
