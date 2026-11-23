using UnityEngine;

namespace InvizimalsRA.V8Final
{
    public sealed class InvizimalsMotion : MonoBehaviour
    {
        [Range(0f, 0.01f)] public float amplitud = 0f;
        [Range(0f, 45f)] public float giro = 0f;
        public float velocidad = 1.2f;
        private Vector3 posicion;
        private Quaternion rotacion;
        private void Awake() { posicion = transform.localPosition; rotacion = transform.localRotation; }
        private void Update()
        {
            float t = Time.time * velocidad;
            transform.localPosition = posicion + Vector3.up * (Mathf.Sin(t) * amplitud);
            transform.localRotation = rotacion * Quaternion.Euler(0, Mathf.Sin(t * .8f) * giro, 0);
        }
    }
}
