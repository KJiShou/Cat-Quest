using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.U2D.Animation;

// 2D Animation 13.0.4 creates its fallback ComputeBuffer after scene load even
// without SpriteSkin instances. In that case no deformation system owns cleanup.
// Use the package's idempotent release method while Unity's graphics device is alive.
// Remove this workaround once the package registers its own shutdown cleanup.
internal static class SpriteSkinShutdown
{
    static Action clearFallbackBuffer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        var type = typeof(SpriteSkin).Assembly.GetType("UnityEngine.U2D.Animation.GpuDeformationSystem");
        var clear = type?.GetMethod("ClearFallbackBuffer", BindingFlags.Static | BindingFlags.NonPublic);
        clearFallbackBuffer = clear == null ? null : (Action)Delegate.CreateDelegate(typeof(Action), clear);
        Application.quitting -= Release;
        Application.quitting += Release;
    }

    static void Release()
    {
        clearFallbackBuffer?.Invoke();
    }
}
