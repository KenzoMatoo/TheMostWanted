using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

// Puerta del tutorial que se abre cuando el jugador hace cierto numero de pogos.
// Los tiles de "gateCells" se ponen en el tilemap Ground al empezar (cerrado) y se quitan al abrir.
// Opcional: un TextMeshPro para mostrar el contador "Pogos 1/3".
public class PogoGate : MonoBehaviour
{
    [SerializeField] private int pogosNeeded = 3;
    [SerializeField] private Tilemap ground;
    [SerializeField] private TileBase gateTile;
    [Tooltip("Celdas del tilemap que forman la puerta.")]
    [SerializeField] private Vector3Int[] gateCells;
    [SerializeField] private TextMeshPro counterText;
    [SerializeField] private string doneMessage = "Bien! Sube por el hueco";

    private PlayerController player;
    private int pogos;
    private bool open;

    void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
        if (player != null) player.Pogoed += OnPogo;
        SetGate(true);
        UpdateText();
    }

    void OnDestroy()
    {
        if (player != null) player.Pogoed -= OnPogo;
    }

    void OnPogo()
    {
        if (open) return;
        pogos++;
        UpdateText();
        if (pogos >= pogosNeeded) StartCoroutine(Open());
    }

    IEnumerator Open()
    {
        open = true;
        yield return new WaitForSeconds(0.25f);
        SetGate(false);
        if (counterText != null) counterText.text = doneMessage;
    }

    void SetGate(bool closed)
    {
        if (ground == null || gateCells == null) return;
        foreach (var c in gateCells) ground.SetTile(c, closed ? gateTile : null);
    }

    void UpdateText()
    {
        if (counterText != null && !open) counterText.text = $"Haz {pogosNeeded} pogos para abrir el paso\nPogos: {Mathf.Min(pogos, pogosNeeded)}/{pogosNeeded}";
    }
}
