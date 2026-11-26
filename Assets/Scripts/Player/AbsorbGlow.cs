using UnityEngine;

// Silueta blanca encima de un enemigo capturado que se puede absorber.
// - Listo para absorber: la silueta palpita suave ("se ve comestible").
// - Absorbiendo: la silueta se vuelve cada vez mas blanca y el enemigo tiembla un poco.
// Copia el sprite del enemigo cada frame y lo dibuja como un color solido (shader GUI/Text).
public class AbsorbGlow : MonoBehaviour
{
    private SpriteRenderer source;
    private SpriteRenderer overlay;
    private float progress;      // 0..1 mientras se absorbe
    private float pulseSpeed = 5f;
    private static Material silhouette;

    public static AbsorbGlow Attach(Transform target)
    {
        SpriteRenderer src = target.GetComponentInChildren<SpriteRenderer>();
        if (src == null) return null;

        var go = new GameObject("Brillo absorber");
        go.transform.SetParent(src.transform, false);
        var glow = go.AddComponent<AbsorbGlow>();
        glow.source = src;
        glow.overlay = go.AddComponent<SpriteRenderer>();
        glow.overlay.sharedMaterial = GetMaterial();
        glow.overlay.sortingLayerID = src.sortingLayerID;
        glow.overlay.sortingOrder = src.sortingOrder + 1;
        glow.overlay.color = new Color(1f, 1f, 1f, 0f);
        return glow;
    }

    public void SetProgress(float value) => progress = Mathf.Clamp01(value);

    private void LateUpdate()
    {
        if (source == null) { Destroy(gameObject); return; }

        overlay.sprite = source.sprite;
        overlay.flipX = source.flipX;
        overlay.flipY = source.flipY;
        overlay.sortingOrder = source.sortingOrder + 1;

        float alpha;
        Vector3 shake = Vector3.zero;
        if (progress > 0f)
        {
            alpha = Mathf.Lerp(0.35f, 0.9f, progress);
            float s = 0.04f + 0.06f * progress;
            shake = new Vector3(Random.Range(-s, s), Random.Range(-s, s), 0f);
        }
        else
        {
            alpha = 0.12f + 0.18f * (0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed));
        }
        overlay.color = new Color(1f, 1f, 1f, alpha);
        transform.localPosition = shake;
    }

    private static Material GetMaterial()
    {
        if (silhouette != null) return silhouette;
        Shader shader = Shader.Find("Custom/SpriteSilhouette");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        silhouette = new Material(shader) { name = "AbsorbSilhouette" };
        return silhouette;
    }
}
