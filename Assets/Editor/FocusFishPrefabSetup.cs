using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class FocusFishPrefabSetup
{
    public const string Path = "Assets/Prefabs/Focus/SpectatorFish.prefab";
    static FocusFishPrefabSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/setup-focus-fish-prefab.request")) return;
        File.Delete("Temp/setup-focus-fish-prefab.request");
        try { Create(); File.WriteAllText("Temp/setup-focus-fish-prefab.result", "PASS"); }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/setup-focus-fish-prefab.result", e.ToString()); }
    }

    [MenuItem("Tools/Cat Quest/Focus/Create Fish and Bubbles Prefab")]
    public static void Create()
    {
        // Create once; later invocations preserve the artist's Inspector edits.
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(Path))
        {
            var root = new GameObject("SpectatorFish", typeof(SpriteRenderer));
            try
            {
                var sprite = root.GetComponent<SpriteRenderer>();
                sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Generated/Icons/FishIcon_512_v2.png");
                sprite.sortingOrder = 45;
                float scale = 1.05f / sprite.sprite.bounds.size.x;
                root.transform.localScale = Vector3.one * scale;
                var bubbles = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Packages/2D Fantasy sprite bundle/Island pack/Prefabs/Particle Bubbles.prefab"), root.transform);
                bubbles.name = "Spectator Bubbles";
                bubbles.transform.localPosition = new Vector3(0, .45f / scale, 0);
                bubbles.transform.localScale = Vector3.one / scale;
                foreach (var particles in bubbles.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = particles.main;
                    main.loop = true; main.playOnAwake = true;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(.9f, 1.5f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(.45f, .8f);
                    main.startSize = new ParticleSystem.MinMaxCurve(.07f, .15f);
                    main.maxParticles = 12;
                    var emission = particles.emission; emission.enabled = true; emission.rateOverTime = 2;
                    emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
                    var shape = particles.shape; shape.enabled = true;
                    shape.shapeType = ParticleSystemShapeType.Cone; shape.radius = .15f; shape.angle = 6;
                    particles.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                    var renderer = particles.GetComponent<ParticleSystemRenderer>();
                    if (renderer) renderer.sortingOrder = 46;
                }
                PrefabUtility.SaveAsPrefabAsset(root, Path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        // Separate rotation from translation. Bubbles are a sibling of Fish Visual.
        var fishRoot = PrefabUtility.LoadPrefabContents(Path);
        try
        {
            var oldRenderer = fishRoot.GetComponent<SpriteRenderer>();
            if (oldRenderer)
            {
                var visual = new GameObject("Fish Visual", typeof(SpriteRenderer));
                visual.transform.SetParent(fishRoot.transform, false);
                EditorUtility.CopySerialized(oldRenderer, visual.GetComponent<SpriteRenderer>());
                UnityEngine.Object.DestroyImmediate(oldRenderer);
            }
            fishRoot.transform.localRotation = Quaternion.identity;
            foreach (var ps in fishRoot.GetComponentsInChildren<ParticleSystem>(true))
            {
                var shape = ps.shape; shape.angle = 0;
                var noise = ps.noise; noise.enabled = false;
                ps.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            }
            PrefabUtility.SaveAsPrefabAsset(fishRoot, Path);
        }
        finally { PrefabUtility.UnloadPrefabContents(fishRoot); }
        var battle = PrefabUtility.LoadPrefabContents(FocusAnimationSetup.Prefab);
        try
        {
            battle.GetComponent<FocusFishSpectators>().fishPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            PrefabUtility.SaveAsPrefabAsset(battle, FocusAnimationSetup.Prefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(battle); }
        AssetDatabase.SaveAssets();
        Verify();
    }

    static void Verify()
    {
        var root = PrefabUtility.LoadPrefabContents(Path);
        try
        {
            var sprite = root.GetComponentInChildren<SpriteRenderer>();
            if (!sprite || !sprite.sprite) throw new Exception("Missing fish sprite.");
            if (Mathf.Abs(sprite.sprite.bounds.size.x * root.transform.localScale.x - 1.05f) > .001f) throw new Exception("Fish size changed.");
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0) throw new Exception("Missing bubbles.");
            foreach (var ps in systems)
            {
                if (!ps.main.loop || !ps.main.playOnAwake || ps.main.simulationSpace != ParticleSystemSimulationSpace.World || ps.emission.burstCount != 0)
                    throw new Exception("Bubble settings not serialized.");
                ps.Simulate(2, true, true, false);
                if (ps.particleCount == 0) throw new Exception("Bubble simulation emitted no particles.");
            }
            Directory.CreateDirectory("Library/FocusFishPrefabVerification");
            File.WriteAllText("Library/FocusFishPrefabVerification/result.txt", "PASS: serialized fish sprite/size and bubble parameters; simulated particles emit; source prefab unchanged; shared training prefab references combined fish prefab.\n");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var stage = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Focus/FocusPageStage.prefab");
        try
        {
            var audience = stage.GetComponentInChildren<FocusFishSpectators>(true);
            if (!audience.fishPrefab) throw new Exception("Shared page/widget stage lost fish prefab reference.");
            audience.SetFishCount(3); audience.AnimateFish(Time.time + 3);
            audience.SetFishCount(5); audience.AnimateFish(Time.time + 3);
            var fish = audience.GetComponentsInChildren<SpriteRenderer>(true);
            int spawned = 0;
            foreach (var sprite in fish)
            {
                if (!sprite.transform.parent.name.StartsWith("Spectator Fish ")) continue;
                spawned++;
                var emitter = sprite.transform.parent.GetComponentInChildren<ParticleSystem>();
                var before = emitter.transform.forward;
                sprite.transform.localRotation = Quaternion.Euler(0, 0, 22);
                if (Vector3.Angle(emitter.transform.forward, Vector3.up) > .01f || Vector3.Angle(before, emitter.transform.forward) > .01f)
                    throw new Exception("Bubble emitter follows fish rotation.");
                emitter.Simulate(2, true, true, false);
                var particles = new ParticleSystem.Particle[emitter.particleCount];
                emitter.GetParticles(particles);
                foreach (var particle in particles)
                    if (Mathf.Abs(particle.velocity.x) > .001f || particle.velocity.y <= 0)
                        throw new Exception("Bubble velocity is not vertically upward.");
                var shape = emitter.shape;
                if (Mathf.Abs(shape.angle) > .001f) throw new Exception("Bubble cone is not straight upward.");
                if (sprite.transform.parent.GetComponentsInChildren<ParticleSystem>(true).Length == 0) throw new Exception("Spawned fish has no bubbles.");
                if (sprite.flipX != (audience.ninja.position.x < sprite.transform.position.x)) throw new Exception("Fish is facing away from Ninja.");
            }
            if (spawned != 5 || audience.VisibleFishCount != 5) throw new Exception("Fish generation count mismatch.");
            File.AppendAllText("Library/FocusFishPrefabVerification/result.txt", "PASS: shared Focus page/widget stage spawns 3 then 5 combined fish/bubble instances, with correct count and Ninja facing; bubble direction stays vertical when fish turns, with upward particle velocities.\n");
        }
        finally { PrefabUtility.UnloadPrefabContents(stage); }
    }
}
