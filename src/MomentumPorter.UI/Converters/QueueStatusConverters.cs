namespace MomentumPorter.UI.Converters;

using Avalonia.Data.Converters;
using Avalonia.Media;
using Material.Icons;
using MomentumPorter.UI.ViewModels.Shared;

public static class QueueStatusConverters
{
    public static readonly IValueConverter Icon = new FuncValueConverter<QueueStatus, MaterialIconKind>(status => status switch
    {
        QueueStatus.Queued => MaterialIconKind.ClockOutline,
        QueueStatus.Running => MaterialIconKind.ProgressClock,
        QueueStatus.Done => MaterialIconKind.CheckCircle,
        QueueStatus.Failed => MaterialIconKind.AlertCircle,
        QueueStatus.Skipped => MaterialIconKind.SkipNextCircleOutline,
        _ => MaterialIconKind.HelpCircleOutline,
    });

    public static readonly IValueConverter Brush = new FuncValueConverter<QueueStatus, IBrush>(status => status switch
    {
        QueueStatus.Done => new SolidColorBrush(Color.Parse("#8BD99B")),
        QueueStatus.Failed => new SolidColorBrush(Color.Parse("#FF8A80")),
        QueueStatus.Skipped => new SolidColorBrush(Color.Parse("#FFD27D")),
        QueueStatus.Running => new SolidColorBrush(Color.Parse("#82B1FF")),
        _ => new SolidColorBrush(Color.Parse("#99FFFFFF")),
    });
}
