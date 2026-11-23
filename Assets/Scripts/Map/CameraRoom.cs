using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

// One room of a hand-made level. The BoxCollider2D is the room's area: the camera stays inside it
// while the player is in the room. When the player crosses into another room the screen fades to
// black, the camera cuts to the new room, and the screen fades back in (no camera slide between
// rooms, so the empty space outside them is never shown).
// Create rooms with Tools > Tutorial > Nuevo cuarto de camara. If the room has no camera yet,
// it makes one in Play mode by copying the scene's CinemachineCamera.
[RequireComponent(typeof(BoxCollider2D))]
public class CameraRoom : MonoBehaviour
{
    [Tooltip("Camera that only sees this room. Leave empty and it is created when you press Play.")]
    public CinemachineCamera roomCamera;

    [Tooltip("Seconds to fade to black when entering this room.")]
    [SerializeField] float fadeOutTime = 0.12f;
    [Tooltip("Seconds to fade back in once the camera is in this room.")]
    [SerializeField] float fadeInTime = 0.2f;

    static CameraRoom current;
    static CameraRoom pending; // room we're fading into
    static CanvasGroup fader;
    static bool brainSetToCut;

    BoxCollider2D area;
    Transform player;

    // Statics survive between Play sessions when domain reload is off, so reset them on every start.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        current = null;
        pending = null;
        fader = null;
        brainSetToCut = false;
    }

    void Awake()
    {
        area = GetComponent<BoxCollider2D>();
        area.isTrigger = true;
        if (roomCamera == null) roomCamera = CreateCamera();
        if (roomCamera != null) roomCamera.gameObject.SetActive(false);
        UseCutsBetweenRooms();
    }

    // The fade hides the change, so the camera should jump straight to the new room instead of sliding.
    static void UseCutsBetweenRooms()
    {
        if (brainSetToCut || Camera.main == null) return;
        var brain = Camera.main.GetComponent<CinemachineBrain>();
        if (brain == null) return;
        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        brainSetToCut = true;
    }

    // Copy of the main camera (the one that isn't inside a room), kept inside this room's box.
    // The main camera is turned off so only room cameras are used.
    CinemachineCamera CreateCamera()
    {
        CinemachineCamera template = null;
        foreach (var c in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.GetComponentInParent<CameraRoom>(true) == null) { template = c; break; }
        if (template == null)
        {
            Debug.LogWarning($"[CameraRoom] {name}: no CinemachineCamera in the scene to copy.", this);
            return null;
        }

        var go = Instantiate(template.gameObject, transform);
        go.name = "Camara " + name;
        var confiner = go.GetComponent<CinemachineConfiner2D>();
        if (confiner == null) confiner = go.AddComponent<CinemachineConfiner2D>();
        confiner.BoundingShape2D = area;
        confiner.InvalidateBoundingShapeCache();
        template.gameObject.SetActive(false);
        return go.GetComponent<CinemachineCamera>();
    }

    void Start()
    {
        // Pick the room the player starts in (no fade).
        var pc = FindFirstObjectByType<PlayerController>();
        if (pc != null) player = pc.transform;
        if (player != null && area.OverlapPoint(player.position)) Enter();
    }

    // Switch only once the player's center has LEFT the current room and is inside this one.
    // Neighbouring room boxes overlap a tile or two at the doors; without this check, standing in
    // that overlap made both rooms steal the camera from each other every frame (shaking + repeated fades).
    void OnTriggerStay2D(Collider2D other)
    {
        if (current == this || pending != null) return; // already here, or a fade is in progress
        var pc = other.GetComponentInParent<PlayerController>();
        if (pc == null) return;
        Vector2 pos = pc.transform.position;
        if (!area.OverlapPoint(pos)) return;
        if (current != null && current.area != null && current.area.OverlapPoint(pos)) return; // still in the old room
        player = pc.transform;

        if (current == null) { Enter(); return; }
        pending = this;
        StartCoroutine(FadeInto());
    }

    IEnumerator FadeInto()
    {
        var group = GetFader();
        yield return Fade(group, 1f, fadeOutTime);

        // If the player stepped back out while the screen was going dark, stay in the old room.
        if (player != null && area.OverlapPoint(player.position)) Enter();

        yield return null; // let Cinemachine cut to the new camera before showing the screen again
        yield return Fade(group, 0f, fadeInTime);
        pending = null; // only now can another room start a transition (no overlapping fades)
    }

    // Also used by FallRespawn to hide the teleport.
    public static IEnumerator Fade(CanvasGroup group, float target, float time)
    {
        float start = group.alpha;
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(start, target, t / time);
            yield return null;
        }
        group.alpha = target;
    }

    // Full-screen black image on its own overlay canvas, created the first time it's needed.
    public static CanvasGroup GetFader()
    {
        if (fader != null) return fader;
        var go = new GameObject("Fundido Cuartos");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        fader = go.AddComponent<CanvasGroup>();
        fader.alpha = 0f;
        fader.blocksRaycasts = false;
        fader.interactable = false;

        var img = new GameObject("Negro").AddComponent<Image>();
        img.transform.SetParent(go.transform, false);
        img.color = Color.black;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return fader;
    }

    void Enter()
    {
        if (current != null && current.roomCamera != null) current.roomCamera.gameObject.SetActive(false);
        current = this;
        if (roomCamera != null) roomCamera.gameObject.SetActive(true);
    }

    void OnDestroy()
    {
        if (current == this) current = null;
        if (pending == this) pending = null;
    }
}
