using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class ShootManager : MonoBehaviour
{
    public ParticleSystem[] muzzleFxs;
    public Rocket[] rockets;
    public ParticleSystem[] impactFxs;
    public int currentRocketIndex;
    public float force;
    public float minAngle;
    public float maxAngle;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.D)) {
            currentRocketIndex++;
            if (currentRocketIndex >= muzzleFxs.Length) {
                currentRocketIndex = 0;
			} 
		}

        if (Input.GetKeyDown(KeyCode.A)) {
            currentRocketIndex--;
            if (currentRocketIndex < 0) {
                currentRocketIndex = muzzleFxs.Length - 1;
            }
        }

        if (Input.GetMouseButtonDown(0)) {
            Vector2 pos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Rocket rocket = Instantiate(rockets[currentRocketIndex], pos, Quaternion.Euler(0,0, Random.Range(minAngle, maxAngle)));
            rocket.Shoot(force, impactFxs[currentRocketIndex]);
            ParticleSystem muzzleFx = Instantiate(muzzleFxs[currentRocketIndex], pos, Quaternion.identity);
            StartCoroutine(DelayDestroyMuzzle(muzzleFx));
        }
    }

    IEnumerator DelayDestroyMuzzle(ParticleSystem fx) {
        yield return new WaitForSeconds(3);
        Destroy(fx.gameObject);
	}
}
