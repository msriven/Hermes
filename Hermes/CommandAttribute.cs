namespace Hermes;

/*
    public partial class MainViewModel : ObservableObject        // class MUST be partial
    {
        [Command]                                                // -> public DelegateCommand SaveCommand
        private void Save() { ... }

        [Command(CanExecute = nameof(CanDelete), ObservedProperties = new[] { nameof(SelectedItem) })]
        private void Delete(Item? item) { ... }                  // -> DelegateCommand<Item>
        private bool CanDelete(Item? item) => item != null;

        [Command]                                                // -> AsyncDelegateCommand LoadCommand ("Async" suffix is trimmed)
        private async Task LoadAsync(CancellationToken ct) { ... }

        [Command(Concurrency = ConcurrencyMode.CancelPrevious)]  // -> AsyncDelegateCommand<string> SearchCommand
        private Task SearchAsync(string query, CancellationToken ct) { ... }
    }

    Supported signatures:  void M() | void M(T p) | Task M() | Task M(CancellationToken) |
                           Task M(T p) | Task M(T p, CancellationToken)
    CanExecute: a bool property, a bool method (), or a bool method (T p).
    Sync commands with a non-nullable value-type parameter are not supported (use T? or an async command).

    Requires the Hermes.Generators analyzer:
    <ProjectReference Include="..\Hermes.Generators\Hermes.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
 */

/// <summary>
/// Marks a method for command generation by Hermes.Generators.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CommandAttribute : Attribute
{
    /// <summary>Name of a bool property or method (in the same class) used as the CanExecute condition.</summary>
    public string? CanExecute { get; set; }

    /// <summary>Command property name. Default: method name without the "Async" suffix + "Command".</summary>
    public string? Name { get; set; }

    /// <summary>Properties whose changes re-query CanExecute (generates ObservesProperty(() => X)).</summary>
    public string[]? ObservedProperties { get; set; }

    /// <summary>Concurrency mode of generated async commands.</summary>
    public ConcurrencyMode Concurrency { get; set; } = ConcurrencyMode.DisableWhileRunning;
}
