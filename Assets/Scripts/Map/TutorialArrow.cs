using UnityEngine;

// Flecha del tutorial que apunta a la salida del cuarto. Se mueve de ida y vuelta
// en la direccion a la que apunta (su eje X local) para llamar la atencion.
public class TutorialArrow : MonoBehaviour
{
    [SerializeField] private float distance = 0.35f;
    [SerializeField] private float speed = 4f;

    private Vector3 start;

    void Start()
    {
        start = transform.position;
    }

    void Update()
    {
        float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;
        transform.position = start + transform.right * (t * distance);
    }
}
