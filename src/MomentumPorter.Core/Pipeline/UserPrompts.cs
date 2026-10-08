namespace MomentumPorter.Pipeline;

/// <summary>
/// Questions a step may ask the user while a job runs, e.g. whether to overwrite an existing map.
/// Store one in the job's context with <c>context.Set&lt;IUserPrompts&gt;(...)</c>; without one, steps take the
/// cautious answer (<see cref="DeclinePrompts"/>).
/// </summary>
public interface IUserPrompts
{
    /// <summary>Asks a yes/no question. May be called from a worker thread.</summary>
    Task<bool> ConfirmAsync(string question);
}

/// <summary>
/// Answers every question "no", for unattended runs such as the CLI.
/// </summary>
public sealed class DeclinePrompts : IUserPrompts
{
    public static readonly DeclinePrompts Instance = new();

    public Task<bool> ConfirmAsync(string question) => Task.FromResult(false);
}

/// <summary>
/// Forwards questions to a callback.
/// </summary>
public sealed class DelegatePrompts(Func<string, Task<bool>> confirm) : IUserPrompts
{
    public Task<bool> ConfirmAsync(string question) => confirm(question);
}

public static class UserPromptsExtensions
{
    public static IUserPrompts Prompts(this PortJobContext context) => context.TryGet<IUserPrompts>() ?? DeclinePrompts.Instance;
}
