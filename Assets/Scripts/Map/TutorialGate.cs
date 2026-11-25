using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

// Puerta del tutorial hecha de tiles: esta cerrada (tiles puestos en Ground) hasta que el jugador
// hace la accion pedida; entonces se quitan los tiles. Opcional: objetos que se encienden al abrir
// (por ejemplo un texto de "Bien!").
public class TutorialGate : MonoBehaviour
{
    public enum Condition { Capture, Absorb }

    [SerializeField] private Condition condition = Condition.Capture;
    [SerializeField] private Tilemap ground;
    [SerializeField] private TileBase gateTile;
    [SerializeField] private Vector3Int[] gateCells;
    [Tooltip("Se activan al abrir la puerta.")]
    [SerializeField] private GameObject[] showOnOpen;
    [Tooltip("Se desactivan al abrir la puerta.")]
    [SerializeField] private GameObject[] hideOnOpen;

    private CharacterCombat combat;
    private bool open;

    void Start()
    {
        combat = FindFirstObjectByType<CharacterCombat>();
        if (combat != null)
        {
            if (condition == Condition.Capture) combat.CaptureCompleted += Trigger;
            else combat.Absorbed += Trigger;
        }
        SetGate(true);
        foreach (var go in showOnOpen) if (go != null) go.SetActive(false);
    }

    void OnDestroy()
    {
        if (combat == null) return;
        combat.CaptureCompleted -= Trigger;
        combat.Absorbed -= Trigger;
    }

    void Trigger()
    {
        if (open) return;
        open = true;
        StartCoroutine(Open());
    }

    IEnumerator Open()
    {
        yield return new WaitForSeconds(0.3f);
        SetGate(false);
        foreach (var go in showOnOpen) if (go != null) go.SetActive(true);
        foreach (var go in hideOnOpen) if (go != null) go.SetActive(false);
    }

    void SetGate(bool closed)
    {
        if (ground == null || gateCells == null) return;
        foreach (var c in gateCells) ground.SetTile(c, closed ? gateTile : null);
    }
}
