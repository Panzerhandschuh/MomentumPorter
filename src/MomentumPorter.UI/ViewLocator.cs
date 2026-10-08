namespace MomentumPorter.UI;

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MomentumPorter.UI.ViewModels;

/// <summary>
/// Builds the view for a view model shown in a ContentControl. Reflection-free (registrations in
/// <see cref="App"/>), so it survives trimming.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Registrations = [];

    public static void Register<TViewModel, TView>()
        where TViewModel : ViewModel
        where TView : Control, new()
    {
        Registrations[typeof(TViewModel)] = () => new TView();
    }

    public Control Build(object? param)
    {
        if (param is null)
            return new TextBlock { Text = "Nothing to show" };

        return Registrations.TryGetValue(param.GetType(), out Func<Control>? factory)
            ? factory()
            : new TextBlock { Text = "No view for " + param.GetType().Name };
    }

    public bool Match(object? data) => data is ViewModel;
}
