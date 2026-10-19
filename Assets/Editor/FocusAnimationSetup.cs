using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Spine.Unity;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Rendering the Spine mesh captures deformations, tint and draw order as well as bones.
[InitializeOnLoad]
public static class FocusAnimationSetup
{
    public const string Folder = "Assets/Animations/Focus";
    public const string Prefab = "Assets/Prefabs/Focus/FocusTrainingBattle.prefab";
    const string Source = "Assets/Layer Lab/2D Characters-PetPack2/Export/Acorn_SkeletonData.asset";
    const string NinjaFolder = "Assets/Packages/Character/Spine 2D Ninja Cats Character Set/Characters/Animations/Ninja/";
    const int FPS = 60, Resolution = 512;
    static FocusAnimationSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists("Temp/verify-focus-animation.request"))
        {
            File.Delete("Temp/verify-focus-animation.request");
            try { VerifyGenerated(); File.WriteAllText("Temp/verify-focus-animation.result", "PASS"); }
            catch (Exception e) { File.WriteAllText("Temp/verify-focus-animation.result", e.ToString()); Debug.LogException(e); }
        }
        if (!File.Exists("Temp/bake-focus-animation.request")) return;
        File.Delete("Temp/bake-focus-animation.request");
        try { Generate(); File.WriteAllText("Temp/bake-focus-animation.result", "PASS"); }
        catch (Exception e) { File.WriteAllText("Temp/bake-focus-animation.result", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Tools/Cat Quest/Animations/Bake Acorn and Generate Focus Clips")]
    public static void Generate()
    {
        Directory.CreateDirectory(Folder + "/AcornFrames");
        Directory.CreateDirectory("Library/FocusAnimationVerification");
        var frames = Bake();
        var ninjaIdle = AssetDatabase.LoadAssetAtPath<AnimationClip>(NinjaFolder + "ninja-idle.anim");
        var ninjaSlash = AssetDatabase.LoadAssetAtPath<AnimationClip>(NinjaFolder + "ninja-slash.anim");
        var idle = MakeClip("FocusIdle", 4f, t => NinjaFrame(ninjaIdle, Mathf.Repeat(t, 1f)),
            t => AcornFrame(frames["Idle"], t));
        var training = MakeClip("FocusTrainingLoop", 2.2f,
            t => t >= .2f && t < .7f ? NinjaFrame(ninjaSlash, (t - .2f) / .5f) : NinjaFrame(ninjaIdle, Mathf.Repeat(t, 1f)),
            t => t >= .45f && t < .85f ? AcornFrame(frames["Walk"], t - .45f) : AcornFrame(frames["Idle"], t < .45f ? t : t - .85f));
        var controllerPath = Folder + "/FocusTraining.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.parameters = new[] { new AnimatorControllerParameter { name = "IsTraining", type = AnimatorControllerParameterType.Bool } };
        var machine = controller.layers[0].stateMachine;
        foreach (var child in machine.states) machine.RemoveState(child.state);
        var idleState = machine.AddState("Idle", new Vector3(200, 100)); idleState.motion = idle;
        var trainingState = machine.AddState("Training", new Vector3(460, 100)); trainingState.motion = training;
        machine.defaultState = idleState;
        Transition(idleState, trainingState, AnimatorConditionMode.If);
        Transition(trainingState, idleState, AnimatorConditionMode.IfNot);
        EditorUtility.SetDirty(controller);
        var root = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var ninja = root.transform.Find("Training Ninja");
            var acorn = root.transform.Find("Acorn");
            foreach (var childAnimator in root.GetComponentsInChildren<Animator>(true))
                if (childAnimator.gameObject != root) UnityEngine.Object.DestroyImmediate(childAnimator);
            foreach (var spine in acorn.GetComponents<SkeletonAnimation>()) UnityEngine.Object.DestroyImmediate(spine);
            foreach (var component in acorn.GetComponents<Component>())
                if (!(component is Transform) && !(component is SpriteRenderer)) UnityEngine.Object.DestroyImmediate(component);
            var renderer = acorn.GetComponent<SpriteRenderer>();
            if (!renderer) renderer = acorn.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = frames["Idle"][0]; renderer.sortingOrder = 41; renderer.color = Color.white;
            // Facing was baked with Skeleton.ScaleX = -1, preserving the original origin/pivot.
            renderer.flipX = renderer.flipY = false;
            ninja.GetComponent<SpriteRenderer>().sprite = NinjaFrame(ninjaIdle, 0);
            var animator = root.GetComponent<Animator>();
            if (!animator) animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            root.GetComponent<FocusTrainingBattle>().animator = animator;
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Verify(idle, training, frames);
        VerifyUsage();
        Debug.Log("Focus animation generated: one root Animator, 2.2 second loop, Acorn Walk once at 0.45-0.85s.");
    }

    [MenuItem("Tools/Cat Quest/Animations/Verify Generated Focus Animation")]
    public static void VerifyGenerated()
    {
        var frames = new Dictionary<string, Sprite[]>();
        foreach (var name in new[] { "Idle", "Walk" })
            frames[name] = Directory.GetFiles(Folder + "/AcornFrames", "Acorn_" + name + "_*.png")
                .OrderBy(p => p).Select(p => AssetDatabase.LoadAssetAtPath<Sprite>(p.Replace('\\', '/'))).ToArray();
        Verify(AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/FocusIdle.anim"),
            AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/FocusTrainingLoop.anim"), frames);
        VerifyUsage();
    }
    static void VerifyUsage()
    {
        var stage = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Focus/FocusPageStage.prefab");
        try { VerifyBattle(stage.GetComponentInChildren<FocusTrainingBattle>(true), "Focus page / companion shared stage"); }
        finally { PrefabUtility.UnloadPrefabContents(stage); }
        var original = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/FocusScene.unity");
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/FocusScene.unity", OpenSceneMode.Additive);
        try
        {
            var battle = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FocusTrainingBattle>(true)).Single();
            VerifyBattle(battle, "Standalone Focus Scene");
            File.WriteAllText("Library/FocusAnimationVerification/usage.txt", "PASS: standalone Focus Scene and page/companion stage inherit one root Animator, both SpriteRenderers, and no Spine component.\n");
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); if (original.IsValid()) SceneManager.SetActiveScene(original); }
    }
    static void VerifyBattle(FocusTrainingBattle battle, string context)
    {
        if (!battle || !battle.animator || battle.animator.gameObject != battle.gameObject ||
            battle.GetComponentsInChildren<Animator>(true).Length != 1 ||
            battle.GetComponentsInChildren<SkeletonAnimation>(true).Length != 0 ||
            !battle.transform.Find("Training Ninja").GetComponent<SpriteRenderer>() ||
            !battle.transform.Find("Acorn").GetComponent<SpriteRenderer>())
            throw new Exception(context + " has an outdated training instance.");
    }

    static void Transition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode)
    {
        var transition = from.AddTransition(to); transition.hasExitTime = false;
        transition.duration = 0; transition.hasFixedDuration = true;
        transition.AddCondition(mode, 0, "IsTraining");
    }

    static Dictionary<string, Sprite[]> Bake()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null; Texture2D texture = null;
        var previous = RenderTexture.active;
        try
        {
            var go = new GameObject("Acorn Bake", typeof(MeshFilter), typeof(MeshRenderer));
            SceneManager.MoveGameObjectToScene(go, scene);
            var skeleton = go.AddComponent<SkeletonAnimation>();
            skeleton.skeletonDataAsset = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(Source);
            skeleton.Initialize(true); skeleton.UpdateTiming = UpdateTiming.ManualUpdate;
            skeleton.Skeleton.ScaleX = -1;
            var animations = new[] { "Idle", "Walk" };
            var counts = animations.ToDictionary(a => a, a => Mathf.RoundToInt(skeleton.Skeleton.Data.FindAnimation(a).Duration * FPS));
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var name in animations)
                for (int i = 0; i < counts[name]; i++) { Pose(skeleton, name, i / (float)FPS); bounds.Encapsulate(go.GetComponent<Renderer>().bounds); }
            float size = Mathf.Max(bounds.size.x, bounds.size.y) * 1.1f;
            var center = bounds.center;
            var pivot = new Vector2(.5f - center.x / size, .5f - center.y / size);
            var cameraGo = new GameObject("Bake Camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraGo, scene);
            var camera = cameraGo.GetComponent<Camera>(); camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = size / 2;
            camera.transform.position = new Vector3(center.x, center.y, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.nearClipPlane = .1f; camera.farClipPlane = 20; camera.enabled = false; camera.allowHDR = false;
            target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32); target.antiAliasing = 1; target.Create();
            camera.targetTexture = target;
            texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            var paths = new Dictionary<string, string[]>();
            foreach (var name in animations)
            {
                paths[name] = new string[counts[name]];
                for (int i = 0; i < counts[name]; i++)
                {
                    Pose(skeleton, name, i / (float)FPS);
                    camera.Render(); RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0); texture.Apply();
                    // Spine's PMA blend writes premultiplied color to a transparent target.
                    // Unity Sprites/Default expects straight alpha textures.
                    var pixels = texture.GetPixels32(); int visible = 0;
                    for (int j = 0; j < pixels.Length; j++)
                    {
                        var p = pixels[j];
                        if (p.a == 0) { pixels[j] = new Color32(0, 0, 0, 0); continue; }
                        visible++;
                        float multiplier = 255f / p.a;
                        pixels[j] = new Color32((byte)Mathf.Min(255, Mathf.RoundToInt(p.r * multiplier)),
                            (byte)Mathf.Min(255, Mathf.RoundToInt(p.g * multiplier)), (byte)Mathf.Min(255, Mathf.RoundToInt(p.b * multiplier)), p.a);
                    }
                    if (visible == 0 || pixels[0].a != 0) throw new Exception("Bake failed: empty image or nontransparent background.");
                    texture.SetPixels32(pixels); texture.Apply();
                    var path = Folder + "/AcornFrames/Acorn_" + name + "_" + i.ToString("000") + ".png";
                    File.WriteAllBytes(path, texture.EncodeToPNG()); paths[name][i] = path;
                }
            }
            AssetDatabase.Refresh();
            var result = new Dictionary<string, Sprite[]>();
            foreach (var name in animations)
            {
                var sprites = new List<Sprite>();
                foreach (var path in paths[name])
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = Resolution / size; importer.spritePivot = pivot;
                    var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                    settings.spriteAlignment = (int)SpriteAlignment.Custom; settings.spritePivot = pivot;
                    settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
                    importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
                    importer.filterMode = FilterMode.Bilinear; importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.maxTextureSize = Resolution; importer.SaveAndReimport();
                    sprites.Add(AssetDatabase.LoadAssetAtPath<Sprite>(path));
                }
                result[name] = sprites.ToArray();
            }
            File.WriteAllText("Library/FocusAnimationVerification/bake.txt", $"512x512 RGBA; 60 FPS; Idle {counts["Idle"]} frames; Walk {counts["Walk"]} frames\nFrame world size {size}; pivot {pivot}; facing left\n");
            return result;
        }
        finally
        {
            RenderTexture.active = previous;
            if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (texture) UnityEngine.Object.DestroyImmediate(texture);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static void Pose(SkeletonAnimation skeleton, string animation, float time)
    {
        skeleton.Skeleton.SetToSetupPose(); skeleton.Skeleton.ScaleX = -1;
        skeleton.AnimationState.ClearTracks(); var track = skeleton.AnimationState.SetAnimation(0, animation, true);
        track.TrackTime = time; skeleton.Update(0); skeleton.LateUpdate();
    }
    static Sprite AcornFrame(Sprite[] sprites, float seconds) => sprites[Mathf.FloorToInt((seconds + .00001f) * FPS) % sprites.Length];
    static Sprite NinjaFrame(AnimationClip source, float normalized)
    {
        var binding = AnimationUtility.GetObjectReferenceCurveBindings(source).First(b => b.type == typeof(SpriteRenderer));
        var keys = AnimationUtility.GetObjectReferenceCurve(source, binding);
        float time = Mathf.Clamp01(normalized) * source.length;
        return (Sprite)keys.Last(k => k.time <= time + .00001f).value;
    }
    static AnimationClip MakeClip(string name, float duration, Func<float, Sprite> ninja, Func<float, Sprite> acorn)
    {
        var path = Folder + "/" + name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (!clip) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves(); clip.name = name; clip.frameRate = FPS;
        var samples = Mathf.RoundToInt(duration * FPS);
        foreach (var child in new[] { "Training Ninja", "Acorn" })
        {
            // Unity object-reference tracks include the final key for one frame.
            // Last key at duration - 1/FPS makes clip.length exactly duration.
            var keys = new ObjectReferenceKeyframe[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i == samples - 1 ? 0 : i / (float)FPS;
                keys[i] = new ObjectReferenceKeyframe { time = i / (float)FPS, value = child == "Acorn" ? acorn(t) : ninja(t) };
            }
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve(child, typeof(SpriteRenderer), "m_Sprite"), keys);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true; settings.stopTime = duration;
        AnimationUtility.SetAnimationClipSettings(clip, settings); EditorUtility.SetDirty(clip);
        return clip;
    }

    static void Verify(AnimationClip idle, AnimationClip training, Dictionary<string, Sprite[]> frames)
    {
        var root = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            if (Mathf.Abs(training.length - 2.2f) > .0001f || Mathf.Abs(idle.length - 4f) > .0001f)
                throw new Exception("Unexpected clip duration.");
            var acorn = root.transform.Find("Acorn").GetComponent<SpriteRenderer>();
            var ninja = root.transform.Find("Training Ninja").GetComponent<SpriteRenderer>();
            var position = acorn.transform.localPosition;
            if (root.GetComponentsInChildren<Animator>().Length != 1 || root.GetComponentsInChildren<SkeletonAnimation>().Length != 0)
                throw new Exception("Expected one root Animator and no remaining Acorn Spine component.");
            var changes = new HashSet<Sprite>();
            for (int loop = 0; loop < 10; loop++)
                for (int frame = 0; frame < 132; frame++)
                {
                    float t = frame / 60f; training.SampleAnimation(root, t); changes.Add(acorn.sprite);
                    bool reacting = frame >= 27 && frame < 51;
                    if (reacting != acorn.sprite.name.StartsWith("Acorn_Walk_")) throw new Exception("Reaction timing mismatch at " + t);
                    if (reacting && acorn.sprite != frames["Walk"][frame - 27]) throw new Exception("Walk must play exactly once.");
                    if (acorn.transform.localPosition != position) throw new Exception("Acorn origin drifted.");
                    if (!ninja.sprite) throw new Exception("Missing Ninja frame.");
                }
            training.SampleAnimation(root, 0); var seam = acorn.sprite;
            training.SampleAnimation(root, training.length); if (seam != acorn.sprite) throw new Exception("Loop endpoint mismatch.");
            idle.SampleAnimation(root, 0); if (!acorn.sprite.name.StartsWith("Acorn_Idle_")) throw new Exception("Idle mismatch.");
            File.WriteAllText("Library/FocusAnimationVerification/clips.txt", $"PASS: sampled 10 loops / 1320 frames; one 24-frame Walk per loop at .45-.85; stable transforms; matching endpoint; {changes.Count} Acorn sprites; one root Animator.\nTraining length {training.length}; Idle length {idle.length}.\n");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
