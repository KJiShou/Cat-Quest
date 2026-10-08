using UnityEngine.UI;

// A page selection survives EventSystem focus moving to controls inside that page.
public sealed class SidebarNavigationButton : Button
{
    private bool pageSelectionManaged;
    private bool pageSelected;

    public void SetPageSelected(bool selected)
    {
        pageSelectionManaged = true;
        pageSelected = selected;
        DoStateTransition(currentSelectionState, false);
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        if (pageSelectionManaged && state != SelectionState.Disabled)
            state = pageSelected ? SelectionState.Selected
                : state == SelectionState.Selected ? SelectionState.Normal : state;
        base.DoStateTransition(state, instant);
    }
}
