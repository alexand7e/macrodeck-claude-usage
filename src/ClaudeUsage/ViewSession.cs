using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;

namespace ClaudeUsage;

/// <summary>
/// Adapts a <see cref="UiView"/> to the host's session contract. The view is disposed with the session so
/// its queued patches do not outlive the widget.
/// </summary>
public sealed class ViewSession : IUiSession
{
	private readonly UiView _view;

	public ViewSession(UiView view)
	{
		_view = view;
		_view.Changed += OnViewChanged;
		_view.HandlerFaulted += OnViewHandlerFaulted;
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

	public ValueTask DisposeAsync()
	{
		_view.Changed -= OnViewChanged;
		_view.HandlerFaulted -= OnViewHandlerFaulted;
		_view.Dispose();
		return ValueTask.CompletedTask;
	}

	private void OnViewChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

	private void OnViewHandlerFaulted(object? sender, UiHandlerFaultEventArgs e) =>
		Faulted?.Invoke(this, new UiSessionFaultedEventArgs(e.EventName, e.Exception));
}
