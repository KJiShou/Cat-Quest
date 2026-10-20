using UnityEngine;
using UnityEngine.UI;

// Uses the 4 x 4 sheets from Square_Effect_1 / 3 / 4 without spawning demo particles.
[RequireComponent(typeof(RawImage))]
public sealed class EquipmentGradeEffect : MonoBehaviour
{
    RawImage target;
    void Awake(){target=GetComponent<RawImage>();}
    void Update(){int frame=(int)(Time.unscaledTime*16)%16;// Crop the empty margin of each frame so the lightning follows the tile edge.
        target.uvRect=new Rect(frame%4*.25f+.05f,(3-frame/4)*.25f+.05f,.15f,.15f);}
}
