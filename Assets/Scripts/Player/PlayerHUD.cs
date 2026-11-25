using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// HUD del jugador sobre un Canvas que ya existe en la escena (prefab "HUD Jugador"):
// cubitos de vida (sanos / agrietados / perdidos), barra de resistencia y anillo de "absorber".
// Todo se puede mover y editar en la escena; este script solo actualiza y anima.
// Lee la vida y la resistencia cada frame, asi siempre esta al dia (no depende de eventos).
public class PlayerHUD : MonoBehaviour
{
    [Header("Cubitos de vida (en orden, de izquierda a derecha)")]
    [SerializeField] private List<HUDCube> cubes = new List<HUDCube>();
    [SerializeField] private Sprite cubeSprite;
    [SerializeField] private Sprite crackedSprite;
    [SerializeField] private Sprite emptySprite;

    [Header("Ojos")]
    [SerializeField] private bool moveEyes = true;
    [Tooltip("Cada cuantos segundos (min, max) un cubito mueve los ojos.")]
    [SerializeField] private Vector2 eyeLookInterval = new Vector2(3f, 7f);
    [Tooltip("Que tanto se mueve la pupila dentro del ojo (0 = nada, 0.3 = hasta el borde).")]
    [SerializeField][Range(0f, 0.3f)] private float eyeLookRange = 0.12f;
    [SerializeField] private float eyeSpeed = 5f;
    [Tooltip("Que tanto 'respiran' los cubitos sanos (0 = quietos).")]
    [SerializeField][Range(0f, 0.05f)] private float breatheAmount = 0.015f;

    [Header("Resistencia")]
    [SerializeField] private Image staminaBack;
    [SerializeField] private Image staminaFill;
    [SerializeField] private Image staminaTrail;

    [Header("Absorber")]
    [SerializeField] private RectTransform absorbGroup;
    [SerializeField] private Image absorbRing;
    private Transform absorbHint;

    [Header("Colores")]
    [SerializeField] private Color staminaColor = new Color(0.36f, 0.79f, 0.65f);
    [SerializeField] private Color staminaBackColor = new Color(0.27f, 0.27f, 0.25f, 0.9f);
    [SerializeField] private Color failColor = new Color(0.89f, 0.29f, 0.29f);

    private enum CubeState { Full, Cracked, Empty }

    private class CubeAnim
    {
        public CubeState state = CubeState.Full;
        public float punch;
        public float phase;
        public float nextLook;
        public Vector2[] target = new Vector2[2];
        public Vector2 basePos;
    }

    private PlayerStats player;
    private PlayerStamina stamina;
    private CharacterCombat combat;
    private Camera cam;
    private readonly List<CubeAnim> anims = new List<CubeAnim>();
    private float trailValue = 1f, trailHoldUntil, lastStamina = 1f;

    void Start()
    {
        player = FindFirstObjectByType<PlayerStats>();
        if (player == null) { Debug.LogWarning("[PlayerHUD] No hay PlayerStats en la escena."); enabled = false; return; }
        stamina = PlayerStamina.Get(player.gameObject);
        combat = player.GetComponent<CharacterCombat>();
        cam = Camera.main;

        for (int i = 0; i < cubes.Count; i++)
        {
            var a = new CubeAnim { phase = i * 0.9f, nextLook = Time.unscaledTime + Random.Range(eyeLookInterval.x, eyeLookInterval.y) };
            a.basePos = cubes[i] != null ? ((RectTransform)cubes[i].transform).anchoredPosition : Vector2.zero;
            anims.Add(a);
        }
        UpdateCubes(true);
        if (absorbGroup != null) absorbGroup.gameObject.SetActive(false);
    }

    void Update()
    {
        if (player == null) return;
        UpdateCubes(false);
        UpdateStamina();
        UpdateAbsorb();
    }

    // ---------------------------------------------------------------- vida

    void UpdateCubes(bool instant)
    {
        int healthy = player.GetCurrentHealth();
        int cracked = player.CrackedMasks;
        float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;

        for (int i = 0; i < cubes.Count; i++)
        {
            var c = cubes[i];
            if (c == null) continue;
            var a = anims[i];
            bool exists = i < player.GetMaxHealth();
            if (c.gameObject.activeSelf != exists) c.gameObject.SetActive(exists);
            if (!exists) continue;

            var state = i < healthy ? CubeState.Full : i < healthy + cracked ? CubeState.Cracked : CubeState.Empty;
            if (state != a.state || instant)
            {
                if (!instant) a.punch = 1f;
                a.state = state;
                c.body.sprite = state == CubeState.Full ? cubeSprite : state == CubeState.Cracked ? crackedSprite : emptySprite;
                if (c.eyes != null) c.eyes.gameObject.SetActive(state == CubeState.Full);
                if (c.timer != null) c.timer.SetActive(state == CubeState.Cracked);
            }
            Animate(c, a, t, dt);
        }
    }

    void Animate(HUDCube c, CubeAnim a, float t, float dt)
    {
        var rt = (RectTransform)c.transform;
        a.punch = Mathf.MoveTowards(a.punch, 0f, dt * 3f);
        float pop = 1f + 0.3f * a.punch * a.punch;
        Vector2 offset = Vector2.zero;

        if (a.state == CubeState.Full)
        {
            float breathe = Mathf.Sin(t * 1.6f + a.phase) * breatheAmount;
            rt.localScale = new Vector3(pop * (1f - breathe), pop * (1f + breathe), 1f);
            if (moveEyes && t >= a.nextLook)
            {
                a.nextLook = t + Random.Range(eyeLookInterval.x, eyeLookInterval.y);
                for (int k = 0; k < 2; k++) a.target[k] = Random.insideUnitCircle;
            }
            for (int k = 0; k < c.pupils.Length && k < 2; k++)
            {
                var pupil = c.pupils[k];
                if (pupil == null) continue;
                var eye = (RectTransform)pupil.parent;
                Vector2 goal = moveEyes ? a.target[k] * eye.rect.width * eyeLookRange : Vector2.zero;
                pupil.anchoredPosition = Vector2.Lerp(pupil.anchoredPosition, goal, dt * eyeSpeed);
            }
        }
        else
        {
            rt.localScale = new Vector3(pop, pop, 1f);
            if (a.state == CubeState.Cracked)
            {
                offset = new Vector2(Mathf.Sin(t * 30f + a.phase) * 1.2f, 0f); // tiembla
                if (c.timerFill != null) c.timerFill.fillAmount = player.CrackTimeLeft01;
            }
        }

        rt.anchoredPosition = a.basePos + offset;
        c.body.color = Color.Lerp(Color.white, failColor, a.state == CubeState.Full ? 0f : a.punch * 0.7f);
    }

    // ---------------------------------------------------------------- resistencia

    void UpdateStamina()
    {
        if (staminaFill == null) return;
        float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        float value = stamina != null ? stamina.Current01 : 1f;
        staminaFill.fillAmount = value;

        // la parte tenue muestra lo que se esta gastando: se queda atras mientras baja
        if (value >= trailValue) trailValue = value;
        else if (value < lastStamina - 0.0001f) trailHoldUntil = t + 0.35f;
        else if (t > trailHoldUntil) trailValue = Mathf.MoveTowards(trailValue, value, dt * 0.8f);
        lastStamina = value;
        if (staminaTrail != null) staminaTrail.fillAmount = trailValue;

        var col = staminaColor;
        if (value < 0.25f) col.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * 8f)); // poca: parpadea
        staminaFill.color = col;
        if (staminaBack != null)
        {
            bool failing = stamina != null && Time.time - stamina.LastFailTime < 0.35f;
            staminaBack.color = failing ? failColor : staminaBackColor;
        }
    }

    // ---------------------------------------------------------------- absorber

    void UpdateAbsorb()
    {
        if (absorbGroup == null) return;
        bool show = combat != null && (combat.CanAbsorbNow || combat.AbsorbProgress01 > 0f);
        if (absorbGroup.gameObject.activeSelf != show) absorbGroup.gameObject.SetActive(show);
        if (!show) return;

        // El texto "Manten la RUEDA" solo sale hasta la primera vez que absorbes
        if (absorbHint == null) absorbHint = absorbGroup.Find("Aviso");
        if (absorbHint != null)
        {
            bool hint = !CharacterCombat.AbsorbHintSeen;
            if (absorbHint.gameObject.activeSelf != hint) absorbHint.gameObject.SetActive(hint);
        }

        if (absorbRing != null) absorbRing.fillAmount = combat.AbsorbProgress01;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        Vector2 screen = cam.WorldToScreenPoint(player.transform.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)absorbGroup.parent, screen, null, out Vector2 local);
        absorbGroup.anchoredPosition = local;
    }
}
