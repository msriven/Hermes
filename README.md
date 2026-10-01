# Hermes

A WPF/MVVM command framework for .NET 8 and .NET 10. Hermes provides synchronous and asynchronous commands, cancellation, retries, timeout, progress reporting, debounce/throttle, composite pipelines, undo/redo, window commands, event-to-command bindings, and a `[Command]` source generator.

> All examples below describe the extended Hermes API. Add a reference to the library and, for XAML samples, use:
>
> ```xml
> xmlns:hermes="clr-namespace:Hermes;assembly=Hermes"
> xmlns:behaviors="clr-namespace:Hermes.Behaviors;assembly=Hermes"
> ```

## Contents

- [Choosing a command](#choosing-a-command)
- [Base command configuration](#base-command-configuration)
- [Synchronous commands](#synchronous-commands)
- [Asynchronous commands](#asynchronous-commands)
- [Progress commands](#progress-commands)
- [Debounce and throttle](#debounce-and-throttle)
- [Composite pipelines](#composite-pipelines)
- [Undo and redo](#undo-and-redo)
- [Window commands](#window-commands)
- [EventBinding](#eventbinding)
- [Source generator](#source-generator)
- [Disposal and diagnostics](#disposal-and-diagnostics)
- [Benchmarks](#benchmarks)

## Choosing a command

| Scenario | Class | Main behavior |
|---|---|---|
| Normal button/menu command | `DelegateCommand`, `DelegateCommand<T>` | Synchronous `ICommand` |
| Long operation | `AsyncDelegateCommand`, `AsyncDelegateCommand<T>` | `CancellationToken`, retries, timeout, `CancelCommand` |
| Long operation with progress | `AsyncProgressCommand<TProgress>` | `IProgress<T>`, bindable `Progress` |
| Run after text input stops | `DebouncedDelegateCommand` | Last call wins after pause |
| Async search after text input stops | `DebouncedAsyncDelegateCommand<T>` | Debounce plus cancellation of stale request |
| High-frequency events | `ThrottledDelegateCommand` | Leading and optional trailing execution |
| Several independent synchronous commands | `CompositeCommand` | Runs all available children |
| Ordered synchronous workflow | `SequentialCompositeCommand` | Stops or continues after failures |
| Ordered async workflow | `AsyncSequentialCommand` | Awaits each step |
| Parallel async workflow | `AsyncParallelCommand` | `Task.WhenAll`, optional concurrency cap |
| Single undoable action | `UndoableCommand` | Last operation only without history |
| Shared multi-step undo/redo | `UndoRedoManager` | History, redo, grouping, capacity |
| XAML window actions | `WindowCommands` | Close/minimize/maximize/dialog result |
| CLR/routed event to command | `EventBinding` | Event arguments, sender, filters, gestures |
| Less ViewModel boilerplate | `[Command]` | Source-generated command properties |

## Base command configuration

All `DelegateCommandBase` descendants implement:

- `ICommand` for WPF binding
- `IActiveAware` with `IsActive` and `IsActiveChanged`
- `INotifyPropertyChanged` for bindable command state
- `IDisposable` for cleanup of property and collection subscriptions

### Common fluent methods

The common fluent methods are extension methods and retain the concrete type of the command.

```csharp
SaveCommand = DelegateCommand.Create(Save)
    .UseCommandManager(false)                         // Use direct CanExecuteChanged notifications.
    .WithLogging(message => _logger.LogInformation(message))
    .WithDelay(TimeSpan.FromMilliseconds(100))        // Delay ICommand-triggered execution.
    .CatchExceptions(ShowError)                       // Local handler overrides the global one.
    .ObservesProperty(() => CanSave)                  // Raise CanExecuteChanged when CanSave changes.
    .ObservesCollection(Items);                       // Raise CanExecuteChanged when Items changes.
```

### `GlobalExceptionHandler`

Set a fallback handler once during application startup. It is used only when the individual command has no `CatchExceptions(...)` handler.

```csharp
// App.xaml.cs
DelegateCommandBase.GlobalExceptionHandler = ex =>
{
    _logger.LogError(ex, "Unhandled Hermes command error");

    MessageBox.Show(
        ex.Message,
        "Operation failed",
        MessageBoxButton.OK,
        MessageBoxImage.Error);
};
```

### `UseCommandManager`

```csharp
SaveCommand = new DelegateCommand(Save)
    .UseCommandManager(false) // Configure before the command is bound in XAML.
    .ObservesProperty(() => CanSave);
```

By default Hermes integrates with `CommandManager.RequerySuggested`. Use `false` when you want only explicit Hermes updates; this is generally more predictable for ViewModel commands. Do not switch this mode after WPF has subscribed to `CanExecuteChanged`.

### Disposal

```csharp
public void Dispose()
{
    SaveCommand.Dispose();
    SearchCommand.Dispose();
    RefreshAllCommand.Dispose();
}
```

Dispose commands that observe properties/collections or own cancellable timers when the ViewModel lifetime ends.

## Synchronous commands

### Minimal command

```csharp
public DelegateCommand SaveCommand { get; }

public EditorViewModel()
{
    SaveCommand = new DelegateCommand(Save);
}

private void Save()
{
    _repository.Save(Document);
}
```

```xml
<Button Content="Save"
        Command="{Binding SaveCommand}" />
```

### `CanExecute`

```csharp
public DelegateCommand SaveCommand { get; }

public EditorViewModel()
{
    SaveCommand = new DelegateCommand(
        executeMethod: Save,
        canExecuteMethod: () => CanSave)
        .ObservesProperty(() => CanSave);
}

private bool _canSave;
public bool CanSave
{
    get => _canSave;
    set => SetProperty(ref _canSave, value);
}
```

The button is disabled while `CanSave` is false. `ObservesProperty` eliminates manual `RaiseCanExecuteChanged()` calls.

### `ObservesCanExecute`

```csharp
SaveCommand = new DelegateCommand(Save)
    .ObservesCanExecute(() => CanSave);
```

This is shorthand for using `CanSave` as `CanExecute` and observing its property change notification.

### Command with a parameter

```csharp
public DelegateCommand<Customer> SelectCustomerCommand { get; }

public EditorViewModel()
{
    SelectCustomerCommand = new DelegateCommand<Customer>(
        customer => SelectedCustomer = customer,
        customer => customer is not null);
}
```

```xml
<Button Content="Select"
        Command="{Binding SelectCustomerCommand}"
        CommandParameter="{Binding}" />
```

When the button is inside a customer item template, `{Binding}` is that customer.

### Value-type parameter

Use a nullable value type for a synchronous generic command, because WPF can query `CanExecute(null)` during initialization.

```csharp
public DelegateCommand<int?> SelectTabCommand { get; }

public EditorViewModel()
{
    SelectTabCommand = new DelegateCommand<int?>(
        tab =>
        {
            if (tab is not null)
                SelectedTabIndex = tab.Value;
        },
        tab => tab is >= 0);
}
```

```xml
<Button Content="Tab 2"
        Command="{Binding SelectTabCommand}"
        CommandParameter="2" />
```

### Pipeline hooks, error handling, logging

```csharp
public DelegateCommand SaveCommand { get; }

public EditorViewModel()
{
    SaveCommand = DelegateCommand.Create(Save)
        .When(() => CanSave)
        .BeforeExecute(() =>
        {
            IsBusy = true;
            StatusText = "Saving...";
        })
        .AfterExecute(() =>
        {
            IsBusy = false;
            StatusText = "Saved";
        })
        .CatchExceptions(ex =>
        {
            IsBusy = false;
            StatusText = ex.Message;
            _logger.LogError(ex, "Save failed");
        })
        .WithLogging(message => _logger.LogInformation(message))
        .ObservesProperty(() => CanSave);
}
```

Successful order:

```text
logging → BeforeExecute → action → AfterExecute
```

When the action fails, `AfterExecute` is skipped and the exception reaches `CatchExceptions`.

### Manual execution

```csharp
if (SaveCommand.CanExecute())
    SaveCommand.Execute();
```

The public `Execute()` follows the same execution pipeline as a WPF `ICommand` invocation: hooks, logging, and exception handling are applied.

### Delay

```csharp
RefreshCommand = DelegateCommand.Create(Refresh)
    .WithDelay(TimeSpan.FromMilliseconds(250))
    .CatchExceptions(ShowError);
```

This delays execution after an `ICommand` trigger. For input where only the last event should execute, use debounce instead.

### Observe a nested property

```csharp
SaveCommand = new DelegateCommand(Save)
    .When(() => Document is { IsValid: true })
    .ObservesProperty(() => Document.IsValid);
```

When `Document` is replaced, Hermes follows the new object. Every object in the property chain must implement `INotifyPropertyChanged`.

### Observe a collection

```csharp
public ObservableCollection<OrderLine> Items { get; } = [];

public DelegateCommand ClearCommand { get; }

public EditorViewModel()
{
    ClearCommand = new DelegateCommand(
            () => Items.Clear(),
            () => Items.Count > 0)
        .ObservesCollection(Items);
}
```

The command availability is refreshed for add/remove/replace/move/reset notifications.

## Asynchronous commands

### Minimal async command

```csharp
public AsyncDelegateCommand LoadCommand { get; }

public EditorViewModel()
{
    LoadCommand = new AsyncDelegateCommand(LoadAsync);
}

private async Task LoadAsync()
{
    var items = await _api.GetItemsAsync();
    Items = new ObservableCollection<Item>(items);
}
```

```xml
<Button Content="Load"
        Command="{Binding LoadCommand}" />
```

From C#, await the operation:

```csharp
await LoadCommand.ExecuteAsync();
```

### `CancellationToken`, `Cancel`, and `CancelCommand`

```csharp
public AsyncDelegateCommand LoadCommand { get; }

public EditorViewModel()
{
    LoadCommand = new AsyncDelegateCommand(async ct =>
    {
        using var response = await _httpClient.GetAsync("api/orders", ct);
        response.EnsureSuccessStatusCode();

        Orders = await response.Content.ReadFromJsonAsync<List<Order>>(
            cancellationToken: ct) ?? [];
    });
}
```

```csharp
LoadCommand.Cancel(); // Cancels all running executions owned by this command.
```

```xml
<Button Content="Load"
        Command="{Binding LoadCommand}" />

<Button Content="Cancel"
        Command="{Binding LoadCommand.CancelCommand}" />
```

`CancelCommand` is enabled while `IsExecuting` is true.

### External cancellation token

```csharp
using var pageLifetime = new CancellationTokenSource();

await LoadCommand.ExecuteAsync(pageLifetime.Token);

// For example when the view is closed:
pageLifetime.Cancel();
```

The external token is linked to the token that Hermes creates internally.

### `IsExecuting` and `RunningCount`

```xml
<Grid>
    <Button Content="Refresh"
            Command="{Binding LoadCommand}" />

    <ProgressBar IsIndeterminate="True"
                 Visibility="{Binding LoadCommand.IsExecuting,
                                      Converter={StaticResource BooleanToVisibilityConverter}}" />

    <TextBlock Text="{Binding LoadCommand.RunningCount}" />
</Grid>
```

`RunningCount` is especially useful for `Queue` and `Parallel` modes.

### Async command with a parameter

```csharp
public AsyncDelegateCommand<string> SearchCommand { get; }

public EditorViewModel()
{
    SearchCommand = new AsyncDelegateCommand<string>(
        async (query, ct) =>
        {
            Results = await _searchService.SearchAsync(query, ct);
        },
        query => !string.IsNullOrWhiteSpace(query));
}
```

```xml
<Button Content="Search"
        Command="{Binding SearchCommand}"
        CommandParameter="{Binding SearchText}" />
```

```csharp
await SearchCommand.ExecuteAsync("WPF");
```

### Async hooks

```csharp
SaveCommand = new AsyncDelegateCommand(SaveAsync)
    .BeforeExecuteAsync(async () =>
    {
        IsBusy = true;
        await _telemetry.TrackAsync("save_started");
    })
    .AfterExecuteAsync(async () =>
    {
        IsBusy = false;
        await _telemetry.TrackAsync("save_finished");
    });
```

`BeforeExecuteAsync` / `AfterExecuteAsync` are readable aliases for the async `BeforeExecute(Func<Task>)` / `AfterExecute(Func<Task>)` hooks.

### Async exception handling

```csharp
LoadCommand = new AsyncDelegateCommand(LoadAsync)
    .CatchExceptions(ex =>
    {
        StatusText = ex.Message;
        _logger.LogError(ex, "Load failed");
    });
```

```csharp
LoadCommand = new AsyncDelegateCommand(LoadAsync)
    .CatchExceptionsAsync(async ex =>
    {
        _logger.LogError(ex, "Load failed");
        await _dialogService.ShowErrorAsync(ex.Message);
    });
```

`CatchExceptionsAsync` takes precedence over `CatchExceptions`.

### Timeout

```csharp
LoadCommand = new AsyncDelegateCommand(ct => _api.LoadAsync(ct))
    .WithTimeout(TimeSpan.FromSeconds(15))
    .CatchExceptions(ex =>
    {
        StatusText = ex is TimeoutException
            ? "The server did not respond within 15 seconds."
            : ex.Message;
    });
```

The operation token is cancelled at timeout; Hermes exposes the result as `TimeoutException`.

### Retry

```csharp
LoadCommand = new AsyncDelegateCommand(ct => _api.LoadAsync(ct))
    .WithRetry(
        retryCount: 3,
        delay: TimeSpan.FromSeconds(1),
        shouldRetry: ex => ex is HttpRequestException)
    .CatchExceptions(ShowError);
```

`retryCount: 3` means up to four total attempts: the initial attempt plus three retries. Cancellations are never retried.

### Concurrency modes

```csharp
LoadCommand = new AsyncDelegateCommand(LoadAsync)
    .WithConcurrency(ConcurrencyMode.DisableWhileRunning);
```

| Mode | Example | Behavior |
|---|---|---|
| `DisableWhileRunning` | Save, payment, load | Default. `CanExecute` becomes false while running |
| `CancelPrevious` | Search-as-you-type | New invocation cancels current invocation |
| `Queue` | Serial synchronization | Calls wait and run in invocation order |
| `Parallel` | Independent refreshes | Every call runs independently |

```csharp
// New search cancels stale server request.
SearchCommand = new AsyncDelegateCommand<string>(
        (query, ct) => _searchService.SearchAsync(query, ct))
    .WithConcurrency(ConcurrencyMode.CancelPrevious);

// Requests execute one by one.
SyncCommand = new AsyncDelegateCommand(ct => _syncService.SyncAsync(ct))
    .WithConcurrency(ConcurrencyMode.Queue);

// Multiple independent operations can coexist.
RefreshCommand = new AsyncDelegateCommand(ct => RefreshAsync(ct))
    .WithConcurrency(ConcurrencyMode.Parallel);
```

## Progress commands

### Numeric progress

```csharp
public AsyncProgressCommand<double> ImportCommand { get; }

public EditorViewModel()
{
    ImportCommand = new AsyncProgressCommand<double>(
        async (progress, ct) =>
        {
            const int total = 100;

            for (int current = 0; current <= total; current++)
            {
                ct.ThrowIfCancellationRequested();
                await ImportOneRecordAsync(current, ct);

                progress.Report(current * 100.0 / total);
            }
        })
        .OnProgress(percent => StatusText = $"Import: {percent:0}%");
}
```

```xml
<ProgressBar Minimum="0"
             Maximum="100"
             Value="{Binding ImportCommand.Progress}" />

<Button Content="Import"
        Command="{Binding ImportCommand}" />

<Button Content="Cancel import"
        Command="{Binding ImportCommand.CancelCommand}" />
```

`Progress` is bindable. `ProgressChanged` is raised for every report. `IProgress<T>.Report(...)` may be called from a background operation.

### Structured progress

```csharp
public sealed record ImportProgress(
    int Current,
    int Total,
    string FileName);

public AsyncProgressCommand<ImportProgress> ImportCommand { get; }

public EditorViewModel()
{
    ImportCommand = new AsyncProgressCommand<ImportProgress>(
        async (progress, ct) =>
        {
            var files = Directory.GetFiles(SourceDirectory);

            for (int i = 0; i < files.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                await ImportFileAsync(files[i], ct);

                progress.Report(new ImportProgress(
                    Current: i + 1,
                    Total: files.Length,
                    FileName: Path.GetFileName(files[i])));
            }
        })
        .OnProgress(p => StatusText = $"{p.Current}/{p.Total}: {p.FileName}");
}
```

```xml
<TextBlock Text="{Binding ImportCommand.Progress.FileName}" />
```

### Parameter plus progress

```csharp
public AsyncProgressCommand<string, double> UploadCommand { get; }

public EditorViewModel()
{
    UploadCommand = new AsyncProgressCommand<string, double>(
        (filePath, progress, ct) => _uploadService.UploadAsync(filePath, progress, ct),
        filePath => File.Exists(filePath));
}
```

```xml
<Button Content="Upload"
        Command="{Binding UploadCommand}"
        CommandParameter="{Binding SelectedFilePath}" />

<ProgressBar Minimum="0"
             Maximum="100"
             Value="{Binding UploadCommand.Progress}" />
```

## Debounce and throttle

### Synchronous debounce

```csharp
public DebouncedDelegateCommand<string> FilterCommand { get; }

public EditorViewModel()
{
    FilterCommand = new DebouncedDelegateCommand<string>(
        query => ApplyFilter(query),
        debounceInterval: TimeSpan.FromMilliseconds(300));
}
```

```xml
<TextBox Text="{Binding FilterText, UpdateSourceTrigger=PropertyChanged}"
         behaviors:EventBinding.EventName="TextChanged"
         behaviors:EventBinding.Command="{Binding FilterCommand}"
         behaviors:EventBinding.CommandParameter="{Binding Text,
               RelativeSource={RelativeSource Self}}" />
```

Every new call cancels the previous pending call. Only the final value after 300 ms of silence reaches `ApplyFilter`.

### Debounce with `maxWait`

```csharp
FilterCommand = new DebouncedDelegateCommand<string>(
    ApplyFilter,
    debounceInterval: TimeSpan.FromMilliseconds(300),
    maxWait: TimeSpan.FromSeconds(2));
```

If calls continue indefinitely, the command executes at least once every two seconds instead of waiting forever for silence.

### Cancel pending debounce

```csharp
FilterCommand.CancelPending();
```

Use it, for example, during ViewModel disposal or when a search panel closes.

### Async debounce

```csharp
public DebouncedAsyncDelegateCommand<string> SearchCommand { get; }

public EditorViewModel()
{
    SearchCommand = new DebouncedAsyncDelegateCommand<string>(
        async (query, ct) =>
        {
            Results = string.IsNullOrWhiteSpace(query)
                ? []
                : await _searchService.SearchAsync(query, ct);
        },
        debounceInterval: TimeSpan.FromMilliseconds(300),
        maxWait: TimeSpan.FromSeconds(2));
}
```

This command uses `CancelPrevious` by default. A new text value cancels both its pending debounce timer and an already-running stale request.

### Throttle: leading only

```csharp
public ThrottledDelegateCommand<double> ScrollCommand { get; }

public EditorViewModel()
{
    ScrollCommand = new ThrottledDelegateCommand<double>(
        offset => UpdateViewport(offset),
        throttleInterval: TimeSpan.FromMilliseconds(100));
}
```

The first call runs immediately; calls inside the next 100 ms are dropped.

### Throttle: leading plus trailing

```csharp
ScrollCommand = new ThrottledDelegateCommand<double>(
    offset => UpdateViewport(offset),
    throttleInterval: TimeSpan.FromMilliseconds(100),
    executeTrailing: true);
```

The first call runs immediately. During the interval Hermes remembers the latest value and executes it at the end of the interval.

## Composite pipelines

### `CompositeCommand`

```csharp
public CompositeCommand SaveAllCommand { get; }

public EditorViewModel()
{
    SaveAllCommand = new CompositeCommand(CanExecuteMode.Any)
        .CatchExceptions(ex => _logger.LogError(ex, "Save all failed"));

    SaveAllCommand.RegisterCommand(SaveSettingsCommand);
    SaveAllCommand.RegisterCommand(SaveDocumentCommand);
    SaveAllCommand.RegisterCommand(SaveLayoutCommand);
}
```

```xml
<Button Content="Save all"
        Command="{Binding SaveAllCommand}" />
```

`CanExecuteMode.Any`: enabled when any child can execute. `CanExecuteMode.All`: enabled only when every child can execute.

All executable children run. If one child fails, remaining children still run. Errors go to `CatchExceptions`; without a handler, Hermes throws after all children have been attempted.

### Respect active child commands

```csharp
GlobalSaveCommand = new CompositeCommand(CanExecuteMode.Any)
{
    RespectIsActive = true
};

GlobalSaveCommand.RegisterCommand(EditorTab.SaveCommand);
GlobalSaveCommand.RegisterCommand(SettingsTab.SaveCommand);

EditorTab.SaveCommand.IsActive = true;
SettingsTab.SaveCommand.IsActive = false;
```

Inactive `IActiveAware` child commands are ignored.

### Synchronous sequence

```csharp
public SequentialCompositeCommand PublishCommand { get; }

public EditorViewModel()
{
    PublishCommand = new SequentialCompositeCommand(stopOnFailure: true)
        .OnError((failedCommand, exception) =>
        {
            _logger.LogError(exception, "Publish step {Command} failed", failedCommand.GetType().Name);
            StatusText = exception.Message;
        });

    PublishCommand.RegisterCommand(ValidateCommand);
    PublishCommand.RegisterCommand(BuildPackageCommand);
    PublishCommand.RegisterCommand(SignPackageCommand);
    PublishCommand.RegisterCommand(OpenPublishDialogCommand);
}
```

`stopOnFailure: true` stops on unavailable or failing child. With `false`, unavailable/failing commands are skipped and later commands still run.

### Async sequence

```csharp
public AsyncSequentialCommand PublishCommand { get; }

public EditorViewModel()
{
    PublishCommand = new AsyncSequentialCommand(stopOnFailure: true)
        .WithTimeout(TimeSpan.FromMinutes(5))
        .WithLoadingIndicator(isBusy => IsPublishing = isBusy)
        .OnError((command, ex) => PublishError = ex.Message);

    PublishCommand.RegisterCommand(ValidateAsyncCommand);
    PublishCommand.RegisterCommand(BuildAsyncCommand);
    PublishCommand.RegisterCommand(UploadAsyncCommand);
    PublishCommand.RegisterCommand(NotifyAsyncCommand);
}
```

```csharp
await PublishCommand.ExecuteAsync();
PublishCommand.Cancel();
```

Unlike a normal `SequentialCompositeCommand`, this command waits for each `IAsyncCommand` before starting the next one.

### Async parallel execution

```csharp
public AsyncParallelCommand RefreshAllCommand { get; }

public EditorViewModel()
{
    RefreshAllCommand = new AsyncParallelCommand(
        mode: CanExecuteMode.Any,
        maxDegreeOfParallelism: 3);

    RefreshAllCommand.RegisterCommand(LoadUsersCommand);
    RefreshAllCommand.RegisterCommand(LoadOrdersCommand);
    RefreshAllCommand.RegisterCommand(LoadProductsCommand);
    RefreshAllCommand.RegisterCommand(LoadStatisticsCommand);
}
```

```csharp
await RefreshAllCommand.ExecuteAsync();
```

At most three children run concurrently. Errors from several children are reported as an `AggregateException` unless the parent has an exception handler.

## Undo and redo

### Single undoable command

```csharp
public UndoableCommand AddItemCommand { get; }

public EditorViewModel()
{
    var item = new ProductViewModel();

    AddItemCommand = new UndoableCommand(
        executeMethod: () => Items.Add(item),
        undoMethod: () => Items.Remove(item));
}
```

```csharp
AddItemCommand.Undo();
```

Without a manager this remembers only the last execution.

### Undoable parameterized command

```csharp
public UndoableCommand<Customer> DeleteCustomerCommand { get; }

public EditorViewModel()
{
    DeleteCustomerCommand = new UndoableCommand<Customer>(
        executeMethod: customer => Customers.Remove(customer),
        undoMethod: customer => Customers.Add(customer),
        canExecuteMethod: customer => customer is not null);
}
```

The last parameter supplied to `Execute` is stored for `Undo()`.

### `UndoRedoManager`

```csharp
public UndoRedoManager History { get; } = new(capacity: 100);

public UndoableCommand AddItemCommand { get; }

public EditorViewModel()
{
    AddItemCommand = new UndoableCommand(
            () => Items.Add(NewItem),
            () => Items.Remove(NewItem))
        .WithUndoManager(History);
}
```

```xml
<Button Content="Add"
        Command="{Binding AddItemCommand}" />

<Button Content="Undo"
        Command="{Binding History.UndoCommand}" />

<Button Content="Redo"
        Command="{Binding History.RedoCommand}" />
```

Bindable state: `CanUndo`, `CanRedo`, `UndoCount`, `RedoCount`, `UndoDescription`, `RedoDescription`.

### Direct history operation

```csharp
History.Execute(new DelegateOperation(
    execute: () => Settings.Theme = "Dark",
    undo: () => Settings.Theme = "Light",
    description: "Change theme"));
```

### Group / transaction

```csharp
using (History.BeginGroup("Move and rename"))
{
    History.Execute(new DelegateOperation(
        () => Shape.X = 200,
        () => Shape.X = 20,
        "Move"));

    History.Execute(new DelegateOperation(
        () => Shape.Name = "Logo",
        () => Shape.Name = "Rectangle",
        "Rename"));
}

// One call reverses both operations, undoing them in reverse order.
History.Undo();
```

## Window commands

### Basic commands

```xml
<Button Content="Close"
        Command="{x:Static hermes:WindowCommands.Close}" />

<Button Content="Minimize"
        Command="{x:Static hermes:WindowCommands.Minimize}" />

<Button Content="Maximize"
        Command="{x:Static hermes:WindowCommands.Maximize}" />

<Button Content="Restore"
        Command="{x:Static hermes:WindowCommands.Restore}" />

<Button Content="Hide"
        Command="{x:Static hermes:WindowCommands.Hide}" />

<Button Content="Show"
        Command="{x:Static hermes:WindowCommands.Show}" />

<Button Content="Activate"
        Command="{x:Static hermes:WindowCommands.Activate}" />

<ToggleButton Content="Topmost"
              Command="{x:Static hermes:WindowCommands.ToggleTopmost}" />
```

Hermes resolves the target window automatically. You may still explicitly provide it:

```xml
<Button Content="Close"
        Command="{x:Static hermes:WindowCommands.Close}"
        CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}" />
```

### Keyboard bindings

```xml
<Window.InputBindings>
    <KeyBinding Key="Escape"
                Command="{x:Static hermes:WindowCommands.Close}" />

    <KeyBinding Key="F11"
                Command="{x:Static hermes:WindowCommands.ToggleMaximize}" />
</Window.InputBindings>
```

### Close confirmation

```csharp
WindowCommands.CloseConfirmationTitle = "Unsaved changes";
WindowCommands.CloseConfirmationMessage = "Close without saving?";
```

```xml
<Button Content="Close with confirmation"
        Command="{x:Static hermes:WindowCommands.CloseWithConfirmation}" />
```

### Custom title bar drag

```xml
<Border Height="36"
        Background="#252525"
        behaviors:EventBinding.EventName="MouseLeftButtonDown"
        behaviors:EventBinding.Command="{x:Static hermes:WindowCommands.DragMove}"
        behaviors:EventBinding.CommandParameter="{Binding RelativeSource={RelativeSource Self}}">
    <TextBlock Margin="12,0"
               VerticalAlignment="Center"
               Text="My application" />
</Border>
```

### Dialog result

```csharp
public ICommand ConfirmCommand { get; } =
    WindowCommandFactory.CloseWithResult(true);

public ICommand CancelCommand { get; } =
    WindowCommandFactory.CloseWithResult(false);

public ICommand CloseWithoutResultCommand { get; } =
    WindowCommandFactory.CloseWithResult(null);
```

```xml
<Button Content="OK" Command="{Binding ConfirmCommand}" />
<Button Content="Cancel" Command="{Binding CancelCommand}" />
```

For a window shown with `ShowDialog()`, the command assigns `DialogResult`; for a normal window, it closes the window.

## EventBinding

`EventBinding` maps a CLR event or WPF routed event to an `ICommand` without code-behind.

### CLR event

```xml
<TextBox behaviors:EventBinding.EventName="TextChanged"
         behaviors:EventBinding.Command="{Binding SearchCommand}" />
```

### Normal parameter

```xml
<Button Content="Edit"
        behaviors:EventBinding.EventName="MouseDoubleClick"
        behaviors:EventBinding.Command="{Binding EditCommand}"
        behaviors:EventBinding.CommandParameter="{Binding SelectedItem}" />
```

### Pass `EventArgs`

```xml
<ListView behaviors:EventBinding.EventName="SelectionChanged"
          behaviors:EventBinding.Command="{Binding SelectionChangedCommand}"
          behaviors:EventBinding.PassEventArgs="True" />
```

```csharp
public DelegateCommand<SelectionChangedEventArgs> SelectionChangedCommand { get; }

SelectionChangedCommand = new DelegateCommand<SelectionChangedEventArgs>(args =>
{
    SelectedCustomer = args.AddedItems.Cast<Customer>().FirstOrDefault();
});
```

### Pass sender

```xml
<Button Content="Click"
        behaviors:EventBinding.EventName="Click"
        behaviors:EventBinding.Command="{Binding ClickCommand}"
        behaviors:EventBinding.PassSender="True" />
```

```csharp
public DelegateCommand<Button> ClickCommand { get; }

ClickCommand = new DelegateCommand<Button>(button =>
{
    button.Content = "Clicked";
});
```

### Pass complete context

```xml
<ListView behaviors:EventBinding.EventName="MouseDoubleClick"
          behaviors:EventBinding.Command="{Binding OpenCommand}"
          behaviors:EventBinding.CommandParameter="{Binding SelectedItem}"
          behaviors:EventBinding.PassContext="True" />
```

```csharp
public DelegateCommand<EventContext> OpenCommand { get; }

OpenCommand = new DelegateCommand<EventContext>(context =>
{
    var sender = context.Sender;
    var eventArgs = context.EventArgs;
    var selected = context.Parameter as DocumentViewModel;

    if (selected is not null)
        Open(selected);
});
```

`EventContext` contains `Sender`, `EventArgs`, and the regular `CommandParameter`.

### Convert event arguments

```csharp
public sealed class MouseEventToDataContextConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is MouseButtonEventArgs { OriginalSource: FrameworkElement element }
            ? element.DataContext
            : null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

```xml
<Window.Resources>
    <local:MouseEventToDataContextConverter x:Key="MouseEventToDataContextConverter" />
</Window.Resources>

<ListView behaviors:EventBinding.EventName="MouseDoubleClick"
          behaviors:EventBinding.Command="{Binding OpenDocumentCommand}"
          behaviors:EventBinding.EventArgsConverter="{StaticResource MouseEventToDataContextConverter}" />
```

The converter receives `EventArgs` as `value` and `CommandParameter` as its converter parameter.

### Routed event

```xml
<Grid behaviors:EventBinding.RoutedEvent="{x:Static Mouse.PreviewMouseDownEvent}"
      behaviors:EventBinding.Command="{Binding CanvasMouseDownCommand}"
      behaviors:EventBinding.PassEventArgs="True"
      behaviors:EventBinding.HandledEventsToo="True" />
```

### Mark routed event as handled

```xml
<TextBox behaviors:EventBinding.EventName="PreviewKeyDown"
         behaviors:EventBinding.Command="{Binding InterceptKeyCommand}"
         behaviors:EventBinding.PassEventArgs="True"
         behaviors:EventBinding.MarkHandled="True" />
```

If the command executes, `RoutedEventArgs.Handled` is set to true.

### Event filter

```csharp
public sealed class CtrlOnlyFilter : IEventFilter
{
    public bool ShouldHandle(object? sender, EventArgs args)
        => Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
}
```

```xml
<Window.Resources>
    <local:CtrlOnlyFilter x:Key="CtrlOnlyFilter" />
</Window.Resources>

<TextBox behaviors:EventBinding.EventName="KeyDown"
         behaviors:EventBinding.Command="{Binding ShortcutCommand}"
         behaviors:EventBinding.PassEventArgs="True"
         behaviors:EventBinding.Filter="{StaticResource CtrlOnlyFilter}" />
```

Or construct a filter in C#:

```csharp
var leftClickOnly = new DelegateEventFilter((sender, args) =>
    args is MouseButtonEventArgs mouse &&
    mouse.ChangedButton == MouseButton.Left);
```

### Key and mouse gestures

```xml
<TextBox behaviors:EventBinding.EventName="KeyDown"
         behaviors:EventBinding.Command="{Binding SaveCommand}"
         behaviors:EventBinding.MarkHandled="True">
    <behaviors:EventBinding.Gesture>
        <KeyGesture Key="S" Modifiers="Control" />
    </behaviors:EventBinding.Gesture>
</TextBox>
```

```xml
<ListView behaviors:EventBinding.EventName="MouseDown"
          behaviors:EventBinding.Command="{Binding OpenCommand}">
    <behaviors:EventBinding.Gesture>
        <MouseGesture MouseAction="LeftDoubleClick" />
    </behaviors:EventBinding.Gesture>
</ListView>
```

Parameter priority is:

```text
EventArgsConverter → PassContext → PassEventArgs → PassSender → CommandParameter
```

## Source generator

The generator project creates a command property from a method marked `[Command]`.

### Add generator package

```xml
<ItemGroup>
    <PackageReference Include="Hermes.Commands" Version="1.0.0" />
    <PackageReference Include="Hermes.Commands.Generators"
                      Version="1.0.0"
                      PrivateAssets="all" />
</ItemGroup>
```

For local solution development:

```xml
<ItemGroup>
    <ProjectReference Include="..\Hermes\Hermes.csproj" />

    <ProjectReference Include="..\Hermes.Generators\Hermes.Generators.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
</ItemGroup>
```

The containing class must be `partial`.

### `void` method

```csharp
public partial class MainViewModel
{
    [Command]
    private void Save()
    {
        _repository.Save();
    }
}
```

Generated conceptually:

```csharp
private DelegateCommand? _saveCommand;

public DelegateCommand SaveCommand =>
    _saveCommand ??= new DelegateCommand(Save);
```

### Parameterized method

```csharp
public partial class MainViewModel
{
    [Command]
    private void Remove(Customer customer)
    {
        Customers.Remove(customer);
    }
}
```

The generator creates `DelegateCommand<Customer> RemoveCommand`.

### Async method

```csharp
public partial class MainViewModel
{
    [Command]
    private async Task LoadAsync(CancellationToken ct)
    {
        Items = await _api.LoadAsync(ct);
    }
}
```

The generator creates `AsyncDelegateCommand LoadCommand`. The `Async` suffix is removed.

### Async method with parameter

```csharp
public partial class MainViewModel
{
    [Command]
    private async Task SearchAsync(string query, CancellationToken ct)
    {
        Results = await _api.SearchAsync(query, ct);
    }
}
```

The generator creates `AsyncDelegateCommand<string> SearchCommand`.

### `CanExecute` property

```csharp
public partial class MainViewModel : INotifyPropertyChanged
{
    public bool CanSave { get; set; }

    [Command(CanExecute = nameof(CanSave))]
    private void Save() { }

    public event PropertyChangedEventHandler? PropertyChanged;
}
```

When the class implements `INotifyPropertyChanged`, the generated command automatically observes the `CanSave` property.

### `CanExecute` method

```csharp
public partial class MainViewModel
{
    [Command(CanExecute = nameof(CanDelete))]
    private void Delete(Document document)
    {
        Documents.Remove(document);
    }

    private bool CanDelete(Document document)
        => document is { IsReadOnly: false };
}
```

### Options

```csharp
[Command(
    Name = "RunSearchCommand",
    CanExecute = nameof(CanSearch),
    Concurrency = ConcurrencyMode.CancelPrevious,
    DebounceMs = 300)]
private async Task SearchAsync(string query, CancellationToken ct)
{
    Results = await _api.SearchAsync(query, ct);
}
```

| Attribute property | Meaning |
|---|---|
| `Name` | Generated property name. Default: method name without `Async`, plus `Command` |
| `CanExecute` | Name of bool property or supported bool method |
| `Concurrency` | `AsyncDelegateCommand` concurrency mode |
| `DebounceMs` | Creates a debounced command |
| `ThrottleMs` | Creates a throttled synchronous command |

The generator validates signatures and reports diagnostics `HERMES001` through `HERMES007` for unsupported return types, non-partial classes, static methods, invalid can-execute members, invalid synchronous value-type parameters, invalid throttle use, and member-name collisions.

## Full ViewModel example

```csharp
public sealed class OrdersViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IOrdersApi _api;
    private string _searchText = string.Empty;
    private Order? _selectedOrder;
    private bool _canSave;
    private bool _isBusy;

    public ObservableCollection<Order> Orders { get; } = [];
    public UndoRedoManager History { get; } = new(capacity: 100);

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public Order? SelectedOrder
    {
        get => _selectedOrder;
        set => SetProperty(ref _selectedOrder, value);
    }

    public bool CanSave
    {
        get => _canSave;
        set => SetProperty(ref _canSave, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public AsyncDelegateCommand LoadCommand { get; }
    public AsyncDelegateCommand SaveCommand { get; }
    public DebouncedAsyncDelegateCommand<string> SearchCommand { get; }
    public UndoableCommand<Order> DeleteOrderCommand { get; }
    public DelegateCommand ClearSearchCommand { get; }

    public OrdersViewModel(IOrdersApi api)
    {
        _api = api;

        LoadCommand = new AsyncDelegateCommand(LoadAsync)
            .WithLoadingIndicator(isLoading => IsBusy = isLoading)
            .WithTimeout(TimeSpan.FromSeconds(20))
            .WithRetry(2, TimeSpan.FromSeconds(1), ex => ex is HttpRequestException)
            .CatchExceptions(ShowError);

        SaveCommand = new AsyncDelegateCommand(SaveAsync)
            .When(() => CanSave)
            .ObservesProperty(() => CanSave)
            .WithLoadingIndicator(isLoading => IsBusy = isLoading)
            .CatchExceptions(ShowError);

        SearchCommand = new DebouncedAsyncDelegateCommand<string>(
            SearchAsync,
            debounceInterval: TimeSpan.FromMilliseconds(300),
            maxWait: TimeSpan.FromSeconds(2))
            .CatchExceptions(ShowError);

        DeleteOrderCommand = new UndoableCommand<Order>(
                executeMethod: order => Orders.Remove(order),
                undoMethod: order => Orders.Add(order),
                canExecuteMethod: order => order is not null)
            .WithUndoManager(History);

        ClearSearchCommand = new DelegateCommand(
                () => SearchText = string.Empty,
                () => !string.IsNullOrWhiteSpace(SearchText))
            .ObservesProperty(() => SearchText);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var orders = await _api.GetOrdersAsync(ct);
        Orders.Clear();
        foreach (var order in orders)
            Orders.Add(order);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await _api.SaveAsync(Orders, ct);
        CanSave = false;
    }

    private async Task SearchAsync(string query, CancellationToken ct)
    {
        var filtered = string.IsNullOrWhiteSpace(query)
            ? []
            : await _api.SearchAsync(query, ct);

        Orders.Clear();
        foreach (var order in filtered)
            Orders.Add(order);
    }

    private void ShowError(Exception ex)
    {
        IsBusy = false;
        ErrorText = ex.Message;
    }

    public void Dispose()
    {
        LoadCommand.Dispose();
        SaveCommand.Dispose();
        SearchCommand.Dispose();
        DeleteOrderCommand.Dispose();
        ClearSearchCommand.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

```xml
<Grid Margin="16">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
        <RowDefinition Height="Auto" />
    </Grid.RowDefinitions>

    <DockPanel>
        <TextBox Width="260"
                 Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                 behaviors:EventBinding.EventName="TextChanged"
                 behaviors:EventBinding.Command="{Binding SearchCommand}"
                 behaviors:EventBinding.CommandParameter="{Binding Text,
                       RelativeSource={RelativeSource Self}}" />

        <Button Margin="8,0,0,0"
                Content="Clear"
                Command="{Binding ClearSearchCommand}" />

        <Button Margin="8,0,0,0"
                Content="Refresh"
                Command="{Binding LoadCommand}" />

        <Button Margin="8,0,0,0"
                Content="Cancel"
                Command="{Binding LoadCommand.CancelCommand}" />
    </DockPanel>

    <ListBox Grid.Row="1"
             Margin="0,12,0,12"
             ItemsSource="{Binding Orders}"
             SelectedItem="{Binding SelectedOrder}">
        <ListBox.ItemTemplate>
            <DataTemplate>
                <DockPanel>
                    <TextBlock Text="{Binding Number}" />
                    <Button DockPanel.Dock="Right"
                            Content="Delete"
                            Command="{Binding DataContext.DeleteOrderCommand,
                                              RelativeSource={RelativeSource AncestorType=ListBox}}"
                            CommandParameter="{Binding}" />
                </DockPanel>
            </DataTemplate>
        </ListBox.ItemTemplate>
    </ListBox>

    <StackPanel Grid.Row="2" Orientation="Horizontal">
        <Button Content="Save"
                Command="{Binding SaveCommand}" />

        <Button Margin="8,0,0,0"
                Content="Undo"
                Command="{Binding History.UndoCommand}" />

        <Button Margin="8,0,0,0"
                Content="Redo"
                Command="{Binding History.RedoCommand}" />

        <ProgressBar Width="120"
                     Height="16"
                     Margin="12,0,0,0"
                     IsIndeterminate="True"
                     Visibility="{Binding IsBusy,
                                          Converter={StaticResource BooleanToVisibilityConverter}}" />
    </StackPanel>
</Grid>
```

## Benchmarks

The benchmark project measures raw delegate overhead against command execution, composite commands with 1/10/100 children, and `PropertyObserver` subscription/notification cost.

```bash
dotnet run -c Release --project Hermes.Benchmarks -- --filter "*"
```

Run only command benchmarks:

```bash
dotnet run -c Release --project Hermes.Benchmarks -- --filter "*CommandBenchmarks*"
```

Run only property observer benchmarks:

```bash
dotnet run -c Release --project Hermes.Benchmarks -- --filter "*PropertyObserverBenchmarks*"
```

## Target frameworks and package metadata

The library targets:

```xml
<TargetFrameworks>net8.0-windows;net10.0-windows</TargetFrameworks>
```

For a package build with SourceLink and symbol package:

```bash
dotnet pack -c Release
```

## License

MIT
