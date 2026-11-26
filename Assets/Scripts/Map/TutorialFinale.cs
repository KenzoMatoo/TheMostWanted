using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

// Final del tutorial: el unico modo de terminarlo es ABSORBER al muneco final.
// Al absorberlo: la camara tiembla, el piso se agrieta y se desmorona en pedazos, el jugador cae
// por el hueco, la pantalla se funde a negro y se carga el HUB (donde el jugador aparece cayendo).
public class TutorialFinale : MonoBehaviour
{
    [Tooltip("El muneco que hay que absorber")]
    [SerializeField] private GameObject target;
    [SerializeField] private Tilemap ground;
    [Tooltip("Celdas del piso que se rompen (min y max, inclusive)")]
    [SerializeField] private Vector2Int breakMin;
    [SerializeField] private Vector2Int breakMax;
    [SerializeField] private string nextScene = "HUB";

    [Header("Tiempos")]
    [SerializeField] private float delayAfterAbsorb = 0.7f;
    [SerializeField] private float rumbleTime = 1.4f;
    [SerializeField] private float rowInterval = 0.06f;
    [SerializeField] private float fallBeforeFade = 0.6f;
    [SerializeField] private float fadeTime = 0.8f;

    [Header("Temblor")]
    [SerializeField] private float shakeStrength = 0.25f;

    private CharacterCombat combat;
    private bool started;
    private float shake;

    void Start()
    {
        combat = FindAnyObjectByType<CharacterCombat>();
        if (combat != null) combat.AbsorbedTarget += OnAbsorbed;
    }

    void OnDestroy()
    {
        if (combat != null) combat.AbsorbedTarget -= OnAbsorbed;
    }

    void OnAbsorbed(GameObject absorbed)
    {
        if (started || absorbed != target) return;
        started = true;
        StartCoroutine(Finale());
    }

    // Camera shake on top of whatever Cinemachine did this frame
    void LateUpdate()
    {
        if (shake <= 0f) return;
        var cam = Camera.main;
        if (cam != null) cam.transform.position += (Vector3)(Random.insideUnitCircle * shake);
    }

    IEnumerator Finale()
    {
        var player = FindAnyObjectByType<PlayerController>();
        var rb = player != null ? player.GetComponent<Rigidbody2D>() : null;

        yield return new WaitForSeconds(delayAfterAbsorb);
        if (player != null) player.canMove = false;
        if (rb != null) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        var hook = player != null ? player.GetComponent<GrapplingHook>() : null;
        if (hook != null) hook.ReleaseHook();

        GameAudio.Play("Retumbo");
        // Rumble: shake growing, the floor flickers darker (cracks) and dust falls from the ceiling
        var cells = new List<Vector3Int>();
        for (int y = breakMax.y; y >= breakMin.y; y--)
            for (int x = breakMin.x; x <= breakMax.x; x++)
            {
                var c = new Vector3Int(x, y, 0);
                if (ground.HasTile(c)) { cells.Add(c); ground.SetTileFlags(c, TileFlags.None); }
            }
        for (float t = 0f; t < rumbleTime; t += Time.deltaTime)
        {
            float k = t / rumbleTime;
            shake = shakeStrength * (0.3f + 0.7f * k);
            foreach (var c in cells)
                if (c.y >= breakMax.y - 1 && Random.value < 0.08f)
                    ground.SetColor(c, Color.Lerp(Color.white, new Color(0.45f, 0.4f, 0.4f), Random.value * k));
            if (Random.value < 0.25f) SpawnDust();
            yield return null;
        }

        GameAudio.Play("Derrumbe");
        // Crumble row by row from the top, throwing pieces of stone
        shake = shakeStrength * 1.3f;
        if (player != null) player.canMove = true;     // the player can wiggle while falling
        for (int y = breakMax.y; y >= breakMin.y; y--)
        {
            for (int x = breakMin.x; x <= breakMax.x; x++)
            {
                var c = new Vector3Int(x, y, 0);
                if (!ground.HasTile(c)) continue;
                SpawnDebris(c);
                ground.SetTile(c, null);
            }
            yield return new WaitForSeconds(rowInterval);
        }
        shake = shakeStrength * 0.5f;

        yield return new WaitForSeconds(fallBeforeFade);
        var fader = CameraRoom.GetFader();
        yield return CameraRoom.Fade(fader, 1f, fadeTime);
        shake = 0f;

        FallIntoHub.Pending = true;
        SceneManager.LoadScene(nextScene);
    }

    void SpawnDebris(Vector3Int cell)
    {
        Sprite sprite = ground.GetSprite(cell);
        Vector3 center = ground.GetCellCenterWorld(cell);
        int pieces = Random.Range(2, 4);
        for (int i = 0; i < pieces; i++)
        {
            var go = new GameObject("Escombro");
            go.transform.position = center + (Vector3)(Random.insideUnitCircle * 0.3f);
            go.transform.localScale = Vector3.one * Random.Range(0.3f, 0.55f);
            go.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            var tr = ground.GetComponent<TilemapRenderer>();
            if (tr != null) { sr.sortingLayerID = tr.sortingLayerID; sr.sortingOrder = tr.sortingOrder + 1; sr.sharedMaterial = tr.sharedMaterial; }
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 3f;
            body.linearVelocity = new Vector2(Random.Range(-3f, 3f), Random.Range(0.5f, 4f));
            body.angularVelocity = Random.Range(-360f, 360f);
            Destroy(go, 2.5f);
        }
    }

    void SpawnDust()
    {
        float x = Random.Range(breakMin.x, breakMax.x + 1);
        Vector3 top = ground.GetCellCenterWorld(new Vector3Int((int)x, breakMax.y + 12, 0));
        var go = new GameObject("Polvo");
        go.transform.position = top + new Vector3(Random.Range(-0.5f, 0.5f), 0f, 0f);
        go.transform.localScale = Vector3.one * Random.Range(0.08f, 0.16f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = DustSprite();
        sr.color = new Color(0.8f, 0.75f, 0.7f, 0.9f);
        sr.sortingOrder = 5;
        var body = go.AddComponent<Rigidbody2D>();
        body.gravityScale = 1.5f;
        Destroy(go, 1.5f);
    }

    static Sprite dust;
    static Sprite DustSprite()
    {
        if (dust != null) return dust;
        var tex = new Texture2D(4, 4) { filterMode = FilterMode.Point };
        var px = new Color[16];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px); tex.Apply();
        dust = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        return dust;
    }
}
