using UnityEngine;

// Lampara colgante que solo sirve como punto de enganche del gancho.
// Vive en la capa "Grapple": esa capa esta dentro de GrappleLayer (el CircleCast del gancho la
// detecta) pero no choca con nada (matriz de colisiones 2D + collider trigger), asi que el jugador
// la atraviesa sin pararse encima ni chocar con ella.
// Se apaga cuando el jugador esta fuera de rango y se enciende (con brillo) cuando la puede alcanzar,
// para que el jugador entienda solo que "eso" es para el gancho.
public class GrappleLamp : MonoBehaviour
{
    [SerializeField] private SpriteRenderer lamp;
    [SerializeField] private SpriteRenderer glow;
    [SerializeField] private Sprite unlitSprite;
    [SerializeField] private Sprite litSprite;
    [Tooltip("Color del brillo; el alpha es la intensidad cuando el jugador esta en rango.")]
    [SerializeField] private Color glowColor = new Color(1f, 0.75f, 0.35f, 0.35f);
    [SerializeField] private float pulseSpeed = 3f;

    private GrapplingHook hook;
    private bool hooked;
    private float punch; // 1 justo al engancharse, baja a 0: pequeño "golpe" visual
    private Vector3 lampScale, glowScale;

    // El gancho se pega al centro de la lampara (no al borde del collider) para que el columpio sea igual siempre.
    public Vector2 HookPoint => transform.position;

    void Awake()
    {
        lampScale = lamp.transform.localScale;
        glowScale = glow.transform.localScale;
    }

    void Start()
    {
        hook = FindFirstObjectByType<GrapplingHook>();
    }

    public void SetHooked(bool value)
    {
        hooked = value;
        if (value) punch = 1f;
    }

    void Update()
    {
        bool inRange = hook != null && hook.canUseHook &&
                       Vector2.Distance(hook.transform.position, transform.position) <= hook.MaxDistance;

        lamp.sprite = hooked || inRange ? litSprite : unlitSprite;

        float targetAlpha = 0f;
        if (hooked) targetAlpha = glowColor.a * 1.6f;
        else if (inRange) targetAlpha = glowColor.a * (0.7f + 0.3f * Mathf.Sin(Time.time * pulseSpeed));
        Color c = glowColor;
        c.a = Mathf.MoveTowards(glow.color.a, targetAlpha, Time.deltaTime * 2f);
        glow.color = c;

        punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 4f);
        lamp.transform.localScale = lampScale * (1f + 0.25f * punch);
        glow.transform.localScale = glowScale * (1f + 0.4f * punch);
    }
}
