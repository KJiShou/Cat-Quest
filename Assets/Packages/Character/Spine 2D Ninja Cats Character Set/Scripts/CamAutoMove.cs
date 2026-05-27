using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CamAutoMove : MonoBehaviour
{
    public float maxDist = 1f;
    public float speed;
    private float t_angle = 0f, t_depth = 0.25f, t_speed = 0.5f;
    private Vector3 startPos;
    public Vector3 s = new Vector3();

    private void Start()
    {
        startPos = transform.position;
    }

    private void Update()
    {

        s.x = maxDist * (Mathf.PerlinNoise(t_angle, t_depth) - 0.5f);
        s.y = maxDist * (Mathf.PerlinNoise(t_depth, t_speed) - 0.5f);
        s.z = 0;

        transform.position = s + startPos;
        GetComponent<Camera>().orthographicSize = Mathf.Lerp(4.5f, 5.5f, Mathf.PerlinNoise(t_angle, t_speed));

        t_angle += speed;
        t_depth += speed;
        t_speed += speed;
    }
}
