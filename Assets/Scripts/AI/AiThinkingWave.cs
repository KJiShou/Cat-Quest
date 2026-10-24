using TMPro;
using UnityEngine;
// Only the small pending label animates. Layout and hit areas never move.
[RequireComponent(typeof(TMP_Text))]
public sealed class AiThinkingWave : MonoBehaviour
{
    TMP_Text label;
    public float Offset { get; private set; }
    void Awake() { label=GetComponent<TMP_Text>(); }
    void LateUpdate()
    {
        label.ForceMeshUpdate(); var info=label.textInfo;
        Offset=Mathf.Sin(Time.unscaledTime*5)*3;
        for(int i=0;i<info.characterCount;i++) {
            var c=info.characterInfo[i]; if(!c.isVisible)continue;
            var vertices=info.meshInfo[c.materialReferenceIndex].vertices;
            float y=Mathf.Sin(Time.unscaledTime*5-i*.55f)*3;
            for(int j=0;j<4;j++) vertices[c.vertexIndex+j].y+=y;
        }
        label.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }
    void OnDisable() { Offset=0; if(label)label.ForceMeshUpdate(); }
}
