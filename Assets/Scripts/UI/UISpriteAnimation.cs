using UnityEngine;
using UnityEngine.UI;

public class UISpriteAnimation : MonoBehaviour
{
    [SerializeField] private Image targetImage;
    [SerializeField] private Sprite[] frames;
    [SerializeField] private float framesPerSecond = 12f;
    [SerializeField] private bool loop = true;

    private int currentFrame;
    private float timer;

    private void Reset()
    {
        targetImage = GetComponent<Image>();
    }

    private void Awake()
    {
        if (targetImage == null)
        {
            targetImage = GetComponent<Image>();
        }
    }

    private void OnEnable()
    {
        currentFrame = 0;
        timer = 0f;
        ApplyFrame();
    }

    private void Update()
    {
        if (targetImage == null || frames == null || frames.Length == 0 || framesPerSecond <= 0f)
        {
            return;
        }

        timer += Time.unscaledDeltaTime;
        float frameDuration = 1f / framesPerSecond;

        while (timer >= frameDuration)
        {
            timer -= frameDuration;
            currentFrame++;

            if (currentFrame >= frames.Length)
            {
                currentFrame = loop ? 0 : frames.Length - 1;
            }

            ApplyFrame();
        }
    }

    private void ApplyFrame()
    {
        if (targetImage != null && frames != null && frames.Length > 0)
        {
            targetImage.sprite = frames[currentFrame];
        }
    }
}
