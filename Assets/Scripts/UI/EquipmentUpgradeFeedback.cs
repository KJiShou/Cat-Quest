using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Presentation only; JuicyButton continues to own button motion.
public sealed class EquipmentUpgradeFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public TMP_Text caption;
    public RawImage effectView;
    public GameObject effectPrefab;
    Button button;
    string preview;
    bool hovered, selected;
    GameObject stage;
    RenderTexture texture;
    ParticleSystem particles;
    float replayAt;
    public void SetPreview(string value){Dismiss();preview=value;}
    void Awake(){button=GetComponent<Button>();}
    public void OnPointerEnter(PointerEventData e){hovered=true;Refresh();}
    public void OnPointerExit(PointerEventData e){hovered=false;selected=false;Refresh();}
    public void OnSelect(BaseEventData e){selected=true;Refresh();}
    public void OnDeselect(BaseEventData e){selected=false;Refresh();}
    public void Dismiss(){hovered=selected=false;Stop();}
    void OnDisable(){Dismiss();}
    void OnApplicationFocus(bool focus){if(!focus)Dismiss();}
    void Update(){
        if(stage&&(!button.IsInteractable()||!Application.isFocused)){Dismiss();return;}
        if(stage&&Time.unscaledTime>=replayAt){particles.Play(true);replayAt=Time.unscaledTime+1.5f;}
    }
    void Refresh(){
        if(!button)button=GetComponent<Button>();
        if(!(hovered||selected)||!button.IsInteractable()||string.IsNullOrEmpty(preview)){Stop();return;}
        caption.text=preview;caption.gameObject.SetActive(true);
        if(stage)return;
        stage=new GameObject("Equipment hover VFX");stage.transform.position=new Vector3(30000+GetInstanceID()%1000*20,30000,0);
        var cameraObject=new GameObject("Preview camera");cameraObject.transform.SetParent(stage.transform,false);cameraObject.transform.localPosition=new Vector3(0,0,-10);
        var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=2.5f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.cullingMask=1<<31;camera.nearClipPlane=.1f;camera.farClipPlane=30;
        texture=new RenderTexture(512,512,16,RenderTextureFormat.ARGB32);texture.Create();camera.targetTexture=texture;
        var effect=Instantiate(effectPrefab,stage.transform,false);
        foreach(var t in effect.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
        foreach(var p in effect.GetComponentsInChildren<ParticleSystem>(true)){var main=p.main;main.useUnscaledTime=true;}
        particles=effect.GetComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);particles.Play(true);replayAt=Time.unscaledTime+1.5f;
        effectView.texture=texture;effectView.gameObject.SetActive(true);
    }
    void Stop(){
        if(caption)caption.gameObject.SetActive(false);
        if(effectView){effectView.gameObject.SetActive(false);effectView.texture=null;}
        if(stage){stage.SetActive(false);Destroy(stage);stage=null;}
        if(texture){texture.Release();Destroy(texture);texture=null;}
    }
}
