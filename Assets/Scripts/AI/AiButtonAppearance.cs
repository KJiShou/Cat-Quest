using UnityEngine;
using UnityEngine.UI;
// Applies a neutral disabled appearance; JuicyButton remains the sole motion owner.
[RequireComponent(typeof(Button))]
public sealed class AiButtonAppearance : MonoBehaviour
{
    public Sprite disabledSprite;
    Button button; Sprite normalSprite; Color normalColor; bool? last;
    void Awake(){button=GetComponent<Button>();normalSprite=button.image.sprite;normalColor=button.image.color;button.transition=Selectable.Transition.None;}
    void LateUpdate(){bool active=button.IsInteractable();if(last==active)return;last=active;button.image.canvasRenderer.SetColor(Color.white);button.image.sprite=active?normalSprite:disabledSprite;button.image.color=active?normalColor:new Color32(166,174,185,255);}
    void OnDisable(){last=null;}
}
