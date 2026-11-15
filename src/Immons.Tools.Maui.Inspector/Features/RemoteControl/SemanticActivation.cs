using System.Reflection;
using System.Windows.Input;

namespace Immons.Tools.Maui.Inspector.Features.RemoteControl;

/// <summary>
/// The MAUI-level stand-in for a finger when touches cannot be injected: from the element up,
/// the first thing that reacts to a tap gets it — gesture recognizers, buttons, toggles, inputs —
/// and an item of a selectable CollectionView becomes its selection.
/// </summary>
internal static class SemanticActivation
{
    public static bool Tap(VisualElement element)
    {
        for (Element? current = element; current != null; current = current.Parent)
        {
            if (SemanticTap.TryInvoke(current))
                return true;

            if (current is VisualElement { BindingContext: { } item } cell
                && current.Parent is SelectableItemsView { SelectionMode: not SelectionMode.None } items
                && !ReferenceEquals(item, items.BindingContext))
            {
                Select(items, item);
                return true;
            }
        }
        return false;
    }

    static void Select(SelectableItemsView items, object item)
    {
        if (items.SelectionMode == SelectionMode.Single)
        {
            items.SelectedItem = item;
            return;
        }

        if (items.SelectedItems.Contains(item))
            items.SelectedItems.Remove(item);
        else
            items.SelectedItems.Add(item);
    }

    /// <summary>
    /// Long press without touches: CommunityToolkit's TouchBehavior (LongPressCommand) on the
    /// element or a parent — looked up by name, the toolkit is not a dependency.
    /// </summary>
    public static bool LongPress(VisualElement element)
    {
        for (Element? current = element; current != null; current = current.Parent)
        {
            if (current is not VisualElement visual)
                continue;
            foreach (var behavior in visual.Behaviors)
            {
                var type = behavior.GetType();
                if (type.Name != "TouchBehavior")
                    continue;
                if (type.GetProperty("LongPressCommand", BindingFlags.Public | BindingFlags.Instance)?.GetValue(behavior) is not ICommand command)
                    continue;
                var parameter = type.GetProperty("LongPressCommandParameter")?.GetValue(behavior);
                if (!command.CanExecute(parameter))
                    continue;
                command.Execute(parameter);
                return true;
            }
        }
        return false;
    }

    /// <summary>Raises Completed (Entry) or the search action (SearchBar) — what the return key does.</summary>
    public static void Submit(InputView input)
    {
        try
        {
            switch (input)
            {
                case SearchBar searchBar:
                    // Runs SearchCommand and raises SearchButtonPressed, like the keyboard's search key.
                    ((Microsoft.Maui.ISearchBar)searchBar).SearchButtonPressed();
                    break;
                default:
                    input.GetType().GetMethod("SendCompleted", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.Invoke(input, null);
                    break;
            }
        }
        catch
        {
            // Completed stays unraised — the text itself is already in place
        }
    }
}
