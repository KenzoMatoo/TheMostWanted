using UnityEngine;
using UnityEngine.UI;

// Un cubito de vida del HUD. Solo guarda referencias a sus partes; PlayerHUD lo anima.
// Para tener mas vida, duplica un cubito dentro de "Vida" y agregalo a la lista de PlayerHUD.
public class HUDCube : MonoBehaviour
{
    public Image body;
    public RectTransform eyes;
    public RectTransform[] pupils = new RectTransform[2];
    public GameObject timer;
    public Image timerFill;
}
