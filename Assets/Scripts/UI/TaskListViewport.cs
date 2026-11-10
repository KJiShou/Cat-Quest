using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Fixed-height task rows and group headings share the authored vertical layout.
// Spacers retain the scroll extent while only nearby controls participate in layout.
internal sealed class TaskListViewport
{
    sealed class Entry
    {
        public string key, kind;
        public float top, height;
        public Func<GameObject> create;
        public Action<GameObject> bind;
    }
    readonly RectTransform content, viewport, poolRoot;
    readonly VerticalLayoutGroup layout;
    readonly List<Entry> entries = new List<Entry>();
    readonly Dictionary<string, GameObject> visible = new Dictionary<string, GameObject>();
    readonly Dictionary<string, string> kinds = new Dictionary<string, string>();
    readonly Dictionary<string, Stack<GameObject>> spare = new Dictionary<string, Stack<GameObject>>();
    readonly HashSet<string> wanted = new HashSet<string>();
    readonly List<string> retired = new List<string>();
    readonly RectTransform before, after;
    float extent, lastOffset, lastHeight, prefixHeight;
    int prefixCount, previousFirst = -1, previousLast = -1;
    GameObject lastSelection;
    public TaskListViewport(RectTransform content)
    {
        this.content = content;
        var scroll = content.GetComponentInParent<ScrollRect>();
        viewport = scroll && scroll.viewport ? scroll.viewport : content.parent as RectTransform;
        layout = content.GetComponent<VerticalLayoutGroup>();
        poolRoot = TaskUIBuilder.Rect(content.parent, "Recycled task rows");
        poolRoot.gameObject.SetActive(false);
        before = TaskUIBuilder.Rect(content, "Rows above");
        after = TaskUIBuilder.Rect(content, "Rows below");
        before.gameObject.SetActive(false); after.gameObject.SetActive(false);
    }
    float Spacing => layout ? layout.spacing : 0;
    public float ScrollOffset => Mathf.Max(0, viewport.InverseTransformPoint(
        content.TransformPoint(new Vector3(0, content.rect.yMax))).y - viewport.rect.yMax);
    public void RestoreScrollOffset(float offset)
    {
        var scroll = content.GetComponentInParent<ScrollRect>();
        if (!scroll || !viewport || !content.gameObject.activeInHierarchy) return;
        // Resolve the newly bound tab's extent before restoring its pixel offset.
        // This is only used on navigation, not on every ordinary data refresh.
        Canvas.ForceUpdateCanvases();
        float maximum = Mathf.Max(0, content.rect.height - viewport.rect.height);
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = maximum > 0 ? 1 - Mathf.Clamp(offset, 0, maximum) / maximum : 1;
        lastSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        Update(true);
    }
    public void Begin(int prefix = 0, float prefixPixels = 0)
    {
        entries.Clear(); extent = 0; prefixCount = prefix;
        prefixHeight = prefixPixels + (prefix == 0 ? 0 : Spacing);
    }
    public void Add(string key, string kind, float height, Func<GameObject> create, Action<GameObject> bind)
    {
        entries.Add(new Entry { key = key, kind = kind, top = extent, height = height, create = create, bind = bind });
        extent += height + Spacing;
    }
    public void Update(bool refresh = false)
    {
        if (!viewport || !content.gameObject.activeInHierarchy) return;
        float offset = viewport.InverseTransformPoint(content.TransformPoint(new Vector3(0, content.rect.yMax))).y - viewport.rect.yMax;
        float height = viewport.rect.height;
        if (!refresh && entries.Count > 32 && EventSystem.current) {
            var selected = EventSystem.current.currentSelectedGameObject;
            if (selected && selected != lastSelection && selected.transform.IsChildOf(content)) {
                var r = selected.transform as RectTransform;
                if (r) {
                    float y = viewport.InverseTransformPoint(r.TransformPoint(r.rect.center)).y;
                    if (y < viewport.rect.yMin || y > viewport.rect.yMax) {
                        float delta = Mathf.Clamp(y, viewport.rect.yMin + 32, viewport.rect.yMax - 32) - y;
                        content.anchoredPosition += Vector2.up * delta;
                        offset += delta;
                    }
                }
            }
            lastSelection = selected;
        }
        if (!refresh && Mathf.Abs(offset-lastOffset)<1 && Mathf.Abs(height-lastHeight)<1) return;
        lastOffset = offset; lastHeight = height;
        int first = 0, last = entries.Count;
        if (entries.Count > 32 && layout && height > 0) {
            float top = Mathf.Max(0, offset - prefixHeight - layout.padding.top - 160);
            float bottom = Mathf.Max(top + 160, offset + height - prefixHeight + 160);
            while (first < last-1 && entries[first].top + entries[first].height < top) first++;
            while (last > first+1 && entries[last-1].top > bottom) last--;
        }
        if (!refresh && first == previousFirst && last == previousLast) return;
        previousFirst = first; previousLast = last;
        wanted.Clear();
        for (int i = first; i < last; i++) wanted.Add(entries[i].key);
        retired.Clear();
        foreach (var pair in visible) if (!wanted.Contains(pair.Key)) retired.Add(pair.Key);
        foreach (var key in retired) {
            var item = visible[key];
            item.SetActive(false); item.transform.SetParent(poolRoot, false);
            spare[kinds[key]].Push(item); visible.Remove(key); kinds.Remove(key);
        }
        int index = prefixCount;
        PlaceSpacer(before, first > 0 ? entries[first].top - Spacing : 0, ref index);
        for (int i = first; i < last; i++) {
            var entry = entries[i];
            bool retained = visible.TryGetValue(entry.key, out var item);
            if (!retained) {
                if (!spare.TryGetValue(entry.kind, out var pool)) spare.Add(entry.kind, pool = new Stack<GameObject>());
                item = pool.Count > 0 ? pool.Pop() : entry.create();
                if (item.transform.parent != content) item.transform.SetParent(content, false);
                visible.Add(entry.key, item); kinds.Add(entry.key, entry.kind);
            }
            if (!item.activeSelf) item.SetActive(true);
            if (refresh || !retained) entry.bind(item);
            if (item.transform.GetSiblingIndex() != index) item.transform.SetSiblingIndex(index);
            index++;
        }
        PlaceSpacer(after, last < entries.Count ? extent - entries[last].top - Spacing : 0, ref index);
    }
    public GameObject RevealTask(string id)
    {
        string suffix = "/" + id;
        foreach (var entry in entries) {
            if (entry.kind != "task" || !entry.key.EndsWith(suffix, StringComparison.Ordinal)) continue;
            if (visible.TryGetValue(entry.key,out var current)) return current;
            var scroll = content.GetComponentInParent<ScrollRect>();
            if (!scroll || !layout) break;
            Canvas.ForceUpdateCanvases();
            float max = Mathf.Max(0, content.rect.height - viewport.rect.height);
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = max > 0 ? 1 - Mathf.Clamp01((entry.top + prefixHeight + layout.padding.top) / max) : 1;
            Update();
            visible.TryGetValue(entry.key, out current);
            return current;
        }
        return null;
    }
    void PlaceSpacer(RectTransform spacer, float height, ref int index)
    {
        // Keep inactive spacers out of authored row lookup and sibling ordering.
        if (height <= 0) { spacer.gameObject.SetActive(false); spacer.SetParent(poolRoot, false); return; }
        spacer.SetParent(content, false);
        TaskUIBuilder.Height(spacer, height); spacer.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        spacer.gameObject.SetActive(true); spacer.SetSiblingIndex(index++);
    }
}
