using UnityEngine;

// Temblor de camara para cualquier efecto: ScreenShake.Shake(fuerza, segundos).
// Se agrega solo a la camara principal y mueve la camara despues de Cinemachine (LateUpdate tardio),
// asi no pelea con el seguimiento. Varios temblores a la vez se quedan con el mas fuerte.
[DefaultExecutionOrder(10000)]
public class ScreenShake : MonoBehaviour
{
    private static ScreenShake instance;
    private float strength, timeLeft, duration;

    public static void Shake(float strength, float duration)
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (instance == null || instance.gameObject != cam.gameObject)
        {
            instance = cam.GetComponent<ScreenShake>();
            if (instance == null) instance = cam.gameObject.AddComponent<ScreenShake>();
        }
        // Keep the stronger of the current and the new shake
        float current = instance.timeLeft > 0f ? instance.strength * (instance.timeLeft / instance.duration) : 0f;
        if (strength >= current)
        {
            instance.strength = strength;
            instance.duration = instance.timeLeft = Mathf.Max(0.01f, duration);
        }
    }

    private void LateUpdate()
    {
        if (timeLeft <= 0f) return;
        timeLeft -= Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(timeLeft / duration);
        transform.position += (Vector3)(Random.insideUnitCircle * strength * k);
    }
}
