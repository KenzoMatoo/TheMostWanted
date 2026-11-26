using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Texto con la fuente pixel del juego (la misma letra del titulo MOST WANTED), para UI.
// Escribe con las letras de Sprites/UI/FuentePixel.png: mayusculas, numeros, acentos, Ñ y signos.
// Las minusculas se escriben como mayusculas. Hace salto de linea por palabras si tiene un ancho maximo.
// Se ve tambien en el editor (las letras son objetos temporales que no se guardan en la escena).
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class PixelText : MonoBehaviour
{
    public enum Align { Left, Center, Right }

    // Must match the order and widths of the generated atlas
    public const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,!?¡¿:-/'()ÁÉÍÓÚÑÜ";
    private static readonly int[] Widths =
        { 5,5,5,5,5,5,5,5,3,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,3,5,5,5,5,5,5,5,5,3,1,2,1,5,1,5,1,3,5,1,2,2,5,5,3,5,5,5,5 };
    private const int CellW = 9, CellH = 12, Cols = 16;

    [SerializeField] private Texture2D atlas;
    [SerializeField, TextArea(1, 4)] private string text = "TEXTO";
    [Tooltip("Pixeles de pantalla por cada pixel de la letra")]
    [SerializeField, Min(1)] private int scale = 3;
    [Tooltip("Ancho maximo antes de saltar de linea (0 = sin limite)")]
    [SerializeField] private float maxWidth = 0f;
    [SerializeField] private Align alignment = Align.Left;
    [SerializeField] private Color color = Color.white;
    [Tooltip("Espacio extra entre lineas, en pixeles de la letra")]
    [SerializeField] private int lineSpacing = 2;

    private static readonly Dictionary<Texture2D, Sprite[]> spriteCache = new Dictionary<Texture2D, Sprite[]>();
    private readonly List<Image> glyphs = new List<Image>();
    private Transform container;
    private bool dirty = true;

    public string Text { get => text; set { if (text == value) return; text = value; dirty = true; } }
    public Color Color { get => color; set { color = value; foreach (var g in glyphs) if (g != null) g.color = value; } }
    public int Scale { get => scale; set { scale = Mathf.Max(1, value); dirty = true; } }
    public float MaxWidth { get => maxWidth; set { maxWidth = value; dirty = true; } }
    public Texture2D Atlas { get => atlas; set { atlas = value; dirty = true; } }
    public Align Alignment { get => alignment; set { alignment = value; dirty = true; } }

    private void OnEnable() { dirty = true; Rebuild(); }
    private void OnValidate() { dirty = true; }
    private void OnDisable() { Clear(); }

    private void Update() { if (dirty) Rebuild(); }

    public void Rebuild()
    {
        dirty = false;
        Clear();
        if (atlas == null) return;
        Sprite[] sprites = GetSprites(atlas);

        // Lines (word wrap)
        string upper = (text ?? "").ToUpperInvariant();
        var lines = new List<string>();
        foreach (string paragraph in upper.Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                string attempt = line.Length == 0 ? word : line + " " + word;
                if (maxWidth > 0f && line.Length > 0 && Measure(attempt) > maxWidth) { lines.Add(line); line = word; }
                else line = attempt;
            }
            lines.Add(line);
        }

        float lineH = (CellH - 3 + lineSpacing) * scale;   // the cell has 3 rows on top for accents
        float width = 0f;
        foreach (string l in lines) width = Mathf.Max(width, Measure(l));
        float height = lines.Count * lineH + 3 * scale;

        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(width, height);
        if (container == null)
        {
            var go = new GameObject("Letras", typeof(RectTransform)) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            container = go.transform;
            container.SetParent(transform, false);
        }
        var crt = (RectTransform)container;
        crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f);
        crt.pivot = new Vector2(0f, 1f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = Vector2.zero;

        float y = 0f;
        foreach (string l in lines)
        {
            float lw = Measure(l);
            float x = alignment == Align.Left ? 0f : alignment == Align.Center ? (width - lw) / 2f : width - lw;
            foreach (char ch in l)
            {
                int k = Chars.IndexOf(ch);
                if (k < 0) { x += 4 * scale; continue; }
                if (ch != ' ')
                {
                    var g = new GameObject("g", typeof(RectTransform)) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
                    var r = (RectTransform)g.transform;
                    r.SetParent(container, false);
                    r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
                    r.pivot = new Vector2(0f, 1f);
                    r.sizeDelta = new Vector2(CellW * scale, CellH * scale);
                    r.anchoredPosition = new Vector2(Mathf.Round(x - scale), -Mathf.Round(y));
                    var img = g.AddComponent<Image>();
                    img.sprite = sprites[k];
                    img.color = color;
                    img.raycastTarget = false;
                    glyphs.Add(img);
                }
                x += (Widths[k] + 1) * scale;
            }
            y += lineH;
        }
    }

    private float Measure(string s)
    {
        float w = 0f;
        foreach (char ch in s)
        {
            int k = Chars.IndexOf(ch);
            w += k < 0 ? 4 * scale : (Widths[k] + 1) * scale;
        }
        return Mathf.Max(0f, w - scale) + 2 * scale;   // + outline/shadow
    }

    private void Clear()
    {
        glyphs.Clear();
        if (container != null)
        {
            if (Application.isPlaying) Destroy(container.gameObject); else DestroyImmediate(container.gameObject);
            container = null;
        }
        // Leftovers from a domain reload
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i);
            if (c.name == "Letras") { if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject); }
        }
    }

    private static Sprite[] GetSprites(Texture2D tex)
    {
        if (spriteCache.TryGetValue(tex, out var cached) && cached[0] != null) return cached;
        var sprites = new Sprite[Chars.Length];
        for (int i = 0; i < Chars.Length; i++)
        {
            int cx = (i % Cols) * CellW, cy = tex.height - (i / Cols + 1) * CellH;   // texture origin is bottom-left
            sprites[i] = Sprite.Create(tex, new Rect(cx, cy, CellW, CellH), new Vector2(0f, 1f), 16f);
            sprites[i].name = "Letra " + Chars[i];
            sprites[i].hideFlags = HideFlags.DontSave;
        }
        spriteCache[tex] = sprites;
        return sprites;
    }
}
