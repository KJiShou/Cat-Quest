using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class BossPageSetup
{
    public const string SourceScene = "Assets/Scenes/BossBattle1.unity";
    const string Folder = "Assets/Prefabs/Boss";
    const string StagePath = Folder + "/ForestBossStage.prefab";
    const string Poster = "Assets/Generated/Previews/ForestBossArena.png";
    static BossPageSetup()
    {
        EditorApplication.update += Poll;
        EditorSceneManager.sceneSaved += scene => {
            if (scene.path == SourceScene) EditorApplication.delayCall += RefreshScene;
        };
    }
    [MenuItem("Tools/Cat Quest/Refresh Boss Scene")]
    public static void RefreshScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        BuildStage();
        AssetDatabase.SaveAssets();
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists("Temp/refresh-boss-scene.request"))
        {
            File.Delete("Temp/refresh-boss-scene.request");
            try { RefreshScene(); File.WriteAllText("Temp/refresh-boss-scene.result", "PASS: BossBattle1 scene synchronized."); }
            catch (Exception e) { File.WriteAllText("Temp/refresh-boss-scene.result",e.ToString());Debug.LogException(e); }
        }
    }
    static void BuildStage()
    {
        Directory.CreateDirectory(Folder);
        var scene = EditorSceneManager.OpenPreviewScene(SourceScene);
        GameObject root = null;
        try
        {
            var sources = scene.GetRootGameObjects();
            root = new GameObject("BossBattle1 Stage");
            SceneManager.MoveGameObjectToScene(root, scene);
            foreach (var source in sources)
            {
                var clone = UnityEngine.Object.Instantiate(source, root.transform, false);
                clone.name = source.name;
                source.SetActive(false);
            }
            var cameras = root.GetComponentsInChildren<Camera>(true);
            var camera = cameras.FirstOrDefault(c => c.CompareTag("MainCamera")) ?? cameras.FirstOrDefault();
            if (!camera) throw new InvalidOperationException("BossBattle1 needs a preview camera.");
            foreach (var c in cameras) { c.enabled = false; c.tag = "Untagged"; }
            camera.name = "Arena Camera";
            // Use the composition saved by the scene author, not the asset demo's camera tour.
            var tour = camera.GetComponent<Animator>();
            if (tour) tour.enabled = false;
            foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                if (animator.enabled) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var actors = new[] { root.transform.Find("Ninja Cat"), root.transform.Find("Cyclope") };
            var renderers = actors.Where(a=>a).SelectMany(a=>a.GetComponentsInChildren<SpriteRenderer>()).ToArray();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                camera.aspect = 1.5f;
                if (camera.WorldToViewportPoint(bounds.min).x < .04f || camera.WorldToViewportPoint(bounds.max).x > .96f)
                {
                    var position = camera.transform.position;
                    position.x = bounds.center.x;
                    camera.transform.position = position;
                }
            }
            PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            var rt = new RenderTexture(1200,800,24);
            var previous = RenderTexture.active;
            var texture = new Texture2D(1200,800,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=rt;camera.aspect=1.5f;camera.Render();
                RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1200,800),0,0);texture.Apply();
                File.WriteAllBytes(Poster,texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=null;RenderTexture.active=previous;rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(Poster,ImportAssetOptions.ForceUpdate);
        }
        finally
        {
            if(root) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
