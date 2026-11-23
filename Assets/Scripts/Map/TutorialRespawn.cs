using System.Collections;
using UnityEngine;

// Respawn del tutorial: cuando el jugador muere, la pantalla se va a negro, revive con la vida
// llena en el ultimo TutorialCheckpoint que toco (o donde empezo) y la pantalla vuelve.
// Va en cualquier objeto de la escena (solo uno).
public class TutorialRespawn : MonoBehaviour
{
    [SerializeField] private float delayBeforeFade = 0.8f;
    [SerializeField] private float fadeOutTime = 0.3f;
    [SerializeField] private float fadeInTime = 0.4f;

    private PlayerStats player;
    private Vector3 startPosition;

    void Start()
    {
        player = FindFirstObjectByType<PlayerStats>();
        if (player == null) { Debug.LogWarning("[TutorialRespawn] No hay PlayerStats en la escena."); return; }
        startPosition = player.transform.position;
        player.OnDeath.AddListener(OnPlayerDeath);
    }

    void OnDestroy()
    {
        if (player != null) player.OnDeath.RemoveListener(OnPlayerDeath);
    }

    void OnPlayerDeath()
    {
        StartCoroutine(Respawn());
    }

    IEnumerator Respawn()
    {
        yield return new WaitForSecondsRealtime(delayBeforeFade);
        var fader = CameraRoom.GetFader();
        yield return CameraRoom.Fade(fader, 1f, fadeOutTime);

        player.Revive();
        player.transform.position = TutorialCheckpoint.Current ?? startPosition;
        var rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;
        var controller = player.GetComponent<PlayerController>();
        if (controller != null) controller.canMove = true;

        yield return new WaitForSecondsRealtime(0.15f);
        yield return CameraRoom.Fade(fader, 0f, fadeInTime);
    }
}
