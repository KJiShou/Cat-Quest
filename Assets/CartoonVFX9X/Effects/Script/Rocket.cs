using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions.Must;
using UnityEngine.Jobs;
using UnityEngine.UI;

public class Rocket : MonoBehaviour {
    bool shooting;
    [HideInInspector]
    public float force;
    [HideInInspector]
    public float length;
    [HideInInspector]
    public float delayStartTime;
    [HideInInspector]
    public Transform startPoint;
    [HideInInspector]
    public Vector3 originEuler;
    public SpriteRenderer headSprite;
    public ParticleSystem headFx;
    public ParticleSystem trailFx;
    [HideInInspector]
    public Rigidbody2D rigid;
    // Start is called before the first frame update
    void Start() {
    }

    // Update is called once per frame
    void Update() {
        if (shooting) {
            // get the actual velocity
            Vector3 vel = rigid.linearVelocity;
            // calc the rotation from x and y velocity via a simple atan2
            float angleZ = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg;
            float angleY = Mathf.Atan2(vel.z, vel.x) * Mathf.Rad2Deg;
			// rotate the arrow according to the trajectory
			transform.eulerAngles = new Vector3(0, 0, angleZ - 90);
		}
    }

    ParticleSystem impactFx;
    public void Shoot(float length, ParticleSystem _impactFx) {
        if (shooting) return;
        impactFx = _impactFx;

        originEuler = transform.eulerAngles;
        rigid.isKinematic = false;
        Vector2 dir = Quaternion.AngleAxis(transform.eulerAngles.z + 90, Vector3.forward) * Vector3.right;
        Debug.Log(dir.normalized);
        shooting = true;
        rigid.AddForce(dir * force * length);
        if (trailFx) trailFx.Play();
    }


    void OnCollisionEnter2D(Collision2D other) {
        if (other.gameObject.tag == "Bound") {
            if (headSprite) headSprite.enabled = false;
            if (headFx) headFx.gameObject.SetActive(false);
            if (GetComponent<Collider2D>()) GetComponent<Collider2D>().enabled = false;
            rigid.isKinematic = true;
            rigid.linearVelocity = Vector2.zero;
            rigid.angularVelocity = 0;
            if (trailFx) trailFx.Stop();
            ParticleSystem fx = Instantiate(impactFx, transform.position, Quaternion.identity);
            StartCoroutine(DelayDestroyMuzzle(fx));
        }
    }

    IEnumerator DelayDestroyMuzzle(ParticleSystem fx) {
        yield return new WaitForSeconds(3);
        Destroy(fx.gameObject);
        
        Destroy(gameObject);
    }
}
