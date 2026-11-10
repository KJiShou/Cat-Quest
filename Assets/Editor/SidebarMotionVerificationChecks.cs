using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// Enumerated by an existing isolated Play runner; no automatic execution or asset writes.
public static class SidebarMotionVerificationChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    static T Read<T>(ResisesSideMenuInLayoutGroup sidebar, string field)
        => (T)typeof(ResisesSideMenuInLayoutGroup).GetField(field, Hidden).GetValue(sidebar);

    static IEnumerable<double> Observe(ResisesSideMenuInLayoutGroup sidebar, float target,
        float duration, string label, Action<bool, string> check)
    {
        var menu = Read<RectTransform>(sidebar, "sideMenuContainer");
        var content = Read<RectTransform>(sidebar, "contentContainer");
        var container = Read<RectTransform>(sidebar, "container");
        float previousDistance = Mathf.Abs(menu.rect.width - target);
        float minimum = Mathf.Min(menu.rect.width, target), maximum = Mathf.Max(menu.rect.width, target);
        float started = Time.unscaledTime;
        int frame = -1, samples = 0;
        bool continuous = true, contentFits = true;
        var trace = new StringBuilder();
        // Sample rendered frames, including those between the Editor runner's updates.
        Canvas.WillRenderCanvases sample = () => {
            if (!menu || !content || !container || Time.frameCount == frame) return;
            frame = Time.frameCount;
            float width = menu.rect.width;
            float distance = Mathf.Abs(width - target);
            continuous &= width >= minimum - .05f && width <= maximum + .05f && distance <= previousDistance + .05f;
            contentFits &= Mathf.Abs(content.sizeDelta.x + width - container.rect.width) < .1f;
            previousDistance = distance;
            if (samples++ < 128)
                trace.AppendFormat(CultureInfo.InvariantCulture, " {0}@{1:F3}s:{2:F2}", frame, Time.unscaledTime - started, width);
        };
        Canvas.willRenderCanvases += sample;
        try { yield return duration + .12; }
        finally { Canvas.willRenderCanvases -= sample; }
        check(samples >= 2, label + " sampled rendered motion frames");
        check(continuous && contentFits, label + " moves toward the latest target without reversal/overshoot or content width drift");
        check(Mathf.Abs(menu.rect.width - target) < .05f && Read<Coroutine>(sidebar, "_animationRoutine") == null,
            label + " reaches its exact authored width and releases the animation");
        check(true, label + " frame@unscaled-time:width trace:" + trace);
    }

    public static IEnumerable<double> Run(ResisesSideMenuInLayoutGroup sidebar, Action<bool, string> check)
    {
        check(sidebar && sidebar.isActiveAndEnabled, "Sidebar motion controller is active");
        var toggle = Read<Toggle>(sidebar, "expander");
        var menu = Read<RectTransform>(sidebar, "sideMenuContainer");
        float collapsed = Read<int>(sidebar, "collapsedWidth"), expanded = Read<int>(sidebar, "expandedWidth");
        float duration = Read<float>(sidebar, "animationDuration");
        check(toggle && menu && !Read<bool>(sidebar, "instantChange") && duration > 0f && expanded > collapsed,
            "Sidebar retains its authored animated toggle configuration");
        bool previousToggle = toggle.isOn, previousEnabled = sidebar.enabled;
        float previousTimeScale = Time.timeScale;
        var focus = typeof(ResisesSideMenuInLayoutGroup).GetMethod("OnApplicationFocus", Hidden);
        try
        {
            Time.timeScale = 1f;
            sidebar.enabled = false;
            toggle.isOn = false;
            sidebar.enabled = true;
            check(Mathf.Abs(menu.rect.width - collapsed) < .05f, "Sidebar enable aligns with the current collapsed toggle");

            toggle.isOn = true;
            check(Mathf.Abs(menu.rect.width - collapsed) < .05f, "Sidebar expansion starts without an endpoint jump");
            foreach (var wait in Observe(sidebar, expanded, duration, "Sidebar normal expansion", check)) yield return wait;
            toggle.isOn = false;
            foreach (var wait in Observe(sidebar, collapsed, duration, "Sidebar normal collapse", check)) yield return wait;

            toggle.isOn = true;
            yield return duration * .25;
            float interruptedWidth = menu.rect.width;
            check(interruptedWidth > collapsed + .05f && interruptedWidth < expanded - .05f,
                "Sidebar reversal occurs during an unfinished expansion");
            toggle.isOn = false;
            check(Mathf.Abs(menu.rect.width - interruptedWidth) < .05f,
                "Sidebar reversal resumes from the actual current width");
            foreach (var wait in Observe(sidebar, collapsed, duration, "Sidebar interrupted reversal", check)) yield return wait;

            for (int i = 0; i < 6; i++)
            {
                float before = menu.rect.width;
                toggle.isOn = !toggle.isOn;
                check(Mathf.Abs(menu.rect.width - before) < .05f, "Sidebar rapid toggle " + i + " remains continuous");
                yield return .03;
            }
            foreach (var wait in Observe(sidebar, collapsed, duration, "Sidebar rapid toggles", check)) yield return wait;

            Time.timeScale = 0f;
            toggle.isOn = true;
            foreach (var wait in Observe(sidebar, expanded, duration, "Sidebar while gameplay time is paused", check)) yield return wait;
            Time.timeScale = 1f;

            toggle.isOn = false;
            yield return duration * .25;
            sidebar.enabled = false;
            check(Mathf.Abs(menu.rect.width - collapsed) < .05f && Read<Coroutine>(sidebar, "_animationRoutine") == null,
                "Sidebar disable stops and settles the pending collapse");
            foreach (var wait in Observe(sidebar, collapsed, duration, "Sidebar disabled settles without stale writes", check)) yield return wait;
            toggle.isOn = true;
            check(Read<Coroutine>(sidebar, "_animationRoutine") == null, "Disabled sidebar ignores toggle animation requests");
            sidebar.enabled = true;
            check(Mathf.Abs(menu.rect.width - expanded) < .05f, "Sidebar re-enable adopts changes made while disabled");

            toggle.isOn = false;
            yield return duration * .25;
            focus.Invoke(sidebar, new object[] { false });
            check(Mathf.Abs(menu.rect.width - collapsed) < .05f && Read<Coroutine>(sidebar, "_animationRoutine") == null,
                "Sidebar focus loss stops and settles its pending transition");
            foreach (var wait in Observe(sidebar, collapsed, duration, "Sidebar focus-loss settle", check)) yield return wait;
            focus.Invoke(sidebar, new object[] { true });
            toggle.isOn = true;
            foreach (var wait in Observe(sidebar, expanded, duration, "Sidebar resumes after focus returns", check)) yield return wait;
        }
        finally
        {
            Time.timeScale = previousTimeScale;
            if (sidebar)
            {
                sidebar.enabled = false;
                if (toggle) toggle.isOn = previousToggle;
                sidebar.enabled = previousEnabled;
            }
        }
    }
}
