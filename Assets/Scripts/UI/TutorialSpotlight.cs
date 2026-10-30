using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// One authored overlay: draws the dimmed area and keeps pointer/keyboard input in the current step.
[DefaultExecutionOrder(-200)]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TutorialSpotlight : MaskableGraphic
{
    RectTransform target;
    Button next, skip;
    bool allowTarget;
    Rect hole;
    bool hasHole;
    GameObject previousSelection;
    readonly List<Selectable> active = new List<Selectable>();
    readonly List<Selectable> controls = new List<Selectable>();
    readonly Dictionary<Selectable, Navigation> navigation = new Dictionary<Selectable, Navigation>();
    readonly Vector3[] corners = new Vector3[4];

    public void Show(RectTransform anchor, bool interactive, Button continueButton, Button skipButton)
    {
        bool changed = target != anchor || allowTarget != interactive || next != continueButton;
        if(!gameObject.activeSelf) {
            previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            gameObject.SetActive(true); changed = true;
        }
        target = anchor; allowTarget = interactive; next = continueButton; skip = skipButton;
        if(changed) RebuildControls();
        UpdateHole();
    }
    public void Dismiss() { gameObject.SetActive(false); }
    void RestoreNavigation()
    {
        foreach(var pair in navigation) if(pair.Key) pair.Key.navigation = pair.Value;
        navigation.Clear(); controls.Clear();
    }
    void RebuildControls()
    {
        RestoreNavigation();
        if(allowTarget && target) controls.AddRange(target.GetComponentsInChildren<Selectable>(false));
        if(next) controls.Add(next); if(skip) controls.Add(skip);
        foreach(var control in controls) if(control && !navigation.ContainsKey(control)) navigation.Add(control,control.navigation);
    }
    void Update()
    {
        if(!EventSystem.current) return;
        // Explicit links stop arrow/submit navigation escaping through an underlying control.
        active.Clear();
        foreach(var c in controls) if(c && c.IsActive() && c.IsInteractable()) active.Add(c);
        if(active.Count == 0) return;
        for(int i=0;i<active.Count;i++) {
            var before=active[(i+active.Count-1)%active.Count];var after=active[(i+1)%active.Count];
            active[i].navigation=new Navigation { mode=Navigation.Mode.Explicit,selectOnUp=before,selectOnLeft=before,selectOnDown=after,selectOnRight=after };
        }
        int index=active.FindIndex(c=>c.gameObject==EventSystem.current.currentSelectedGameObject);
        var keyboard=Keyboard.current;
        if(keyboard!=null && keyboard.tabKey.wasPressedThisFrame) {
            int direction=keyboard.shiftKey.isPressed?-1:1;
            index=index<0?0:(index+direction+active.Count)%active.Count;
            EventSystem.current.SetSelectedGameObject(active[index].gameObject);
        }
        else if(index<0) EventSystem.current.SetSelectedGameObject(active[0].gameObject);
    }
    void LateUpdate() { UpdateHole(); }
    void UpdateHole()
    {
        bool visible=target && target.gameObject.activeInHierarchy;
        Rect updated=default;
        if(visible) {
            target.GetWorldCorners(corners);
            Vector2 min=rectTransform.InverseTransformPoint(corners[0]),max=rectTransform.InverseTransformPoint(corners[2]);
            var bounds=rectTransform.rect;
            updated=Rect.MinMaxRect(Mathf.Clamp(min.x-5,bounds.xMin,bounds.xMax),Mathf.Clamp(min.y-5,bounds.yMin,bounds.yMax),Mathf.Clamp(max.x+5,bounds.xMin,bounds.xMax),Mathf.Clamp(max.y+5,bounds.yMin,bounds.yMax));
        }
        if(hasHole!=visible || hole!=updated) {hasHole=visible;hole=updated;SetVerticesDirty();}
    }
    public override bool Raycast(Vector2 screenPoint, Camera eventCamera)
    {
        if(!base.Raycast(screenPoint,eventCamera)) return false;
        return !(allowTarget && hasHole && target && RectTransformUtility.RectangleContainsScreenPoint(target,screenPoint,eventCamera));
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();var r=rectTransform.rect;
        if(!hasHole) {Quad(mesh,r);return;}
        Quad(mesh,Rect.MinMaxRect(r.xMin,r.yMin,r.xMax,hole.yMin));
        Quad(mesh,Rect.MinMaxRect(r.xMin,hole.yMax,r.xMax,r.yMax));
        Quad(mesh,Rect.MinMaxRect(r.xMin,hole.yMin,hole.xMin,hole.yMax));
        Quad(mesh,Rect.MinMaxRect(hole.xMax,hole.yMin,r.xMax,hole.yMax));
    }
    void Quad(VertexHelper mesh,Rect r)
    {
        if(r.width<=0 || r.height<=0)return;
        int i=mesh.currentVertCount;
        mesh.AddVert(new Vector3(r.xMin,r.yMin),color,Vector2.zero);mesh.AddVert(new Vector3(r.xMin,r.yMax),color,Vector2.zero);
        mesh.AddVert(new Vector3(r.xMax,r.yMax),color,Vector2.zero);mesh.AddVert(new Vector3(r.xMax,r.yMin),color,Vector2.zero);
        mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
    }
    protected override void OnDisable()
    {
        RestoreNavigation();
        var events=EventSystem.current;
        if(events) events.SetSelectedGameObject(previousSelection && previousSelection.activeInHierarchy ? previousSelection : null);
        previousSelection=null;target=null;hasHole=false;base.OnDisable();
    }
}
