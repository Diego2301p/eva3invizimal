using UnityEngine;

namespace InvizimalsRA.V8Final
{
    // Efecto visual discreto de color en el aro: no mueve, inclina ni escala la criatura.
    [RequireComponent(typeof(Renderer))]
    public sealed class InvizimalsHaloPulse : MonoBehaviour
    {
        [Range(0.1f, 3f)] public float velocidad = 0.9f;
        private Renderer aro;
        private MaterialPropertyBlock bloque;
        private Color colorInicial;

        private void Awake()
        {
            aro = GetComponent<Renderer>();
            bloque = new MaterialPropertyBlock();
            colorInicial = aro.sharedMaterial != null && aro.sharedMaterial.HasProperty("_Color")
                ? aro.sharedMaterial.color : Color.white;
        }

        private void Update()
        {
            if (aro == null) return;
            float factor = 0.13f + 0.24f * (0.5f + 0.5f * Mathf.Sin(Time.time * velocidad));
            Color suave = Color.Lerp(colorInicial, Color.white, factor);
            aro.GetPropertyBlock(bloque);
            bloque.SetColor("_Color", suave);
            aro.SetPropertyBlock(bloque);
        }
    }
}
