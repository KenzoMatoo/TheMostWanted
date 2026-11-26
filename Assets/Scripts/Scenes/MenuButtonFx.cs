using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Efecto de los botones del menu: al pasar el mouse o seleccionarlos con teclado/control
// crecen un poco, el texto (imagen pixel o TMP) cambia de color y aparece el cubito marcador a la izquierda.
public class MenuButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [Tooltip("Texto del boton (imagen pixel o TextMeshPro): se tine con los colores de abajo")]
    [SerializeField] private Graphic label;
    [Tooltip("Opcional: texto con la fuente pixel (PixelText), se tine igual que el label")]
    [SerializeField] private PixelText pixelLabel;
    [SerializeField] private Graphic marker;
    [SerializeField] private Color normalText = new Color(0.92f, 0.9f, 0.85f, 1f);
    [SerializeField] private Color highlightText = new Color(0.36f, 0.79f, 0.65f, 1f);
    [SerializeField] private float highlightScale = 1.07f;
    [SerializeField] private float speed = 14f;

    private bool hovered, selected;

    /// <summary>For UI built from code.</summary>
    public void Setup(Graphic labelGraphic, PixelText pixel, Graphic markerGraphic, Color normal, Color highlight)
    {
        label = labelGraphic; pixelLabel = pixel; marker = markerGraphic;
        normalText = normal; highlightText = highlight;
        if (marker != null) marker.canvasRenderer.SetAlpha(0f);
    }
    private Vector3 baseScale;

    private float enabledAt;

    private void Awake()
    {
        var button = GetComponent<UnityEngine.UI.Button>();
        if (button != null) button.onClick.AddListener(() => GameAudio.PlayUI("UI_Click"));
        baseScale = transform.localScale;
        if (marker != null) marker.canvasRenderer.SetAlpha(0f);
    }

    private void OnEnable() { hovered = selected = false; enabledAt = Time.unscaledTime; }

    public void OnPointerEnter(PointerEventData e)
    {
        hovered = true;
        // The mouse also moves the keyboard selection, so only one button is lit at a time
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject);
    }
    public void OnPointerExit(PointerEventData e) { hovered = false; }
    public void OnSelect(BaseEventData e)
    {
        selected = true;
        // No blip for the automatic selection when a menu opens
        if (Time.unscaledTime - enabledAt > 0.2f) GameAudio.PlayUI("UI_Hover", 0.6f);
    }
    public void OnDeselect(BaseEventData e) { selected = false; }

    private void Update()
    {
        bool on = hovered || selected;
        float k = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        transform.localScale = Vector3.Lerp(transform.localScale, baseScale * (on ? highlightScale : 1f), k);
        if (label != null) label.color = Color.Lerp(label.color, on ? highlightText : normalText, k);
        if (pixelLabel != null) pixelLabel.Color = Color.Lerp(pixelLabel.Color, on ? highlightText : normalText, k);
        if (marker != null)
        {
            float a = Mathf.Lerp(marker.canvasRenderer.GetAlpha(), on ? 1f : 0f, k);
            marker.canvasRenderer.SetAlpha(a);
        }
    }
}
