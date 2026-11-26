using UnityEngine;
using UnityEngine.Rendering.Universal;

// Hace parpadear suave una luz 2D (farol, fuego). 'Boost' permite un destello temporal encima.
[RequireComponent(typeof(Light2D))]
public class LightFlicker : MonoBehaviour
{
    public float baseIntensity = 1f;
    [Range(0f, 1f)] public float amount = 0.15f;
    public float speed = 6f;

    private Light2D light2D;
    private float seed, boost;

    private void Awake()
    {
        light2D = GetComponent<Light2D>();
        seed = Random.Range(0f, 100f);
    }

    /// <summary>Short extra flash on top of the flicker (decays by itself).</summary>
    public void Boost(float extra) { boost = Mathf.Max(boost, extra); }

    private void Update()
    {
        float n = Mathf.PerlinNoise(seed, Time.time * speed) * 2f - 1f;       // slow wobble
        float pop = Mathf.PerlinNoise(seed + 7f, Time.time * speed * 3f) > 0.82f ? -0.5f : 0f;   // rare dips, like a flame
        boost = Mathf.MoveTowards(boost, 0f, Time.deltaTime * 3f);
        light2D.intensity = Mathf.Max(0f, baseIntensity * (1f + amount * (n + pop)) + boost);
    }
}
