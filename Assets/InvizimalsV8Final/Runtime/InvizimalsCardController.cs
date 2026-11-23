using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace InvizimalsRA.V8Final
{
    /// <summary>
    /// Bestiario V8: modo Evaluacion sigue la carta con filtro; modo Vitrina fija en pantalla.
    /// La identidad viene del nombre de la imagen de referencia reconocida por ARCore;
    /// no interpreta los digitos impresos como OCR.
    /// </summary>
    public sealed class InvizimalsCardController : MonoBehaviour
    {
        [Serializable]
        public sealed class Card
        {
            public string id;
            public string nombre;
            public string fase;
            public string elemento;
            public string estadisticas;
            public int vida;
            public int dano;
            [TextArea(3, 8)] public string descripcion;
            public string reverso;
            public string frente;
            // Segunda imagen de reconocimiento del mismo frente; no cambia la ficha ni el modelo.
            public string frenteAlternativo;
            public Texture2D retrato;
            public GameObject modeloPrefab;
        }

        // El modo Evaluacion sigue la rubrica: modelo sobre la carta, boton oculto sin target.
        // El modo Vitrina conserva la exhibicion fijada a la pantalla que ya funciona.
        public enum ModoPresentacion { Evaluacion, Vitrina }
        [Header("Modo de presentacion")]
        public ModoPresentacion modoInicial = ModoPresentacion.Evaluacion;
        public Button botonModo;
        public Text textoBotonModo;
        [Tooltip("Tiempo de seguimiento perdido antes de ocultar el contenido en modo evaluacion")]
        [Range(.15f, 2f)] public float toleranciaPerdidaEvaluacion = 1.0f;
        [Header("Seguimiento visual sobre carta")]
        [Tooltip("Controla la velocidad del seguimiento suave. Alto: sigue rapido; bajo: reduce vibracion.")]
        [Range(5f, 35f)] public float velocidadSeguimientoCarta = 18f;
        [Tooltip("Movimientos menores a esta distancia no reposicionan la criatura (metros)")]
        [Range(0f, .008f)] public float zonaMuertaCarta = .0015f;
        [Tooltip("Desplazamiento anomalo que debe confirmarse antes de cambiar la posicion (metros)")]
        [Range(.04f, .2f)] public float umbralSaltoCarta = .095f;
        [Tooltip("Tiempo necesario para aceptar un cambio brusco del seguimiento AR (segundos)")]
        [Range(.15f, 1f)] public float confirmarSaltoCarta = .36f;
        [Tooltip("Compensacion para recentrar la criatura cuando la referencia es el reverso recortado")]
        [Range(0f, .025f)] public float correccionCentroReverso = .014f;
        [Tooltip("En Evaluacion no gira automaticamente. Vitrina conserva su giro")]
        public bool mantenerQuietoEnEvaluacion = true;

        [Header("Reconocimiento")]
        public ARTrackedImageManager gestorImagenes;
        public ARAnchorManager gestorAnclajes;
        public Card[] cartas;
        [Range(.3f, 3f)] public float confirmacionInicial = 0.95f;
        [Range(.6f, 4f)] public float confirmacionCambio = 1.55f;
        [Range(.15f, 1f)] public float tiempoAlPerder = .45f;
        public bool mostrarDiagnostico = true;

        [Header("Interfaz de fichas")]
        public Text estado;
        public Text tituloFicha1;
        public Text datosFicha1;
        public Text tituloFicha2;
        public Text descripcionFicha2;
        public RawImage imagenFicha;
        public RawImage imagenFicha2;
        public Text tipoFicha1;
        public Image tipoFondo;
        public Text vidaValor;
        public Text danoValor;
        public Image vidaRelleno;
        public Image danoRelleno;
        public GameObject panelFicha1;
        public GameObject panelFicha2;
        public GameObject barraBotones;
        public Button boton1;
        public Button boton2;
        public Button botonCerrar;
        public Button botonCambiar;
        public Button botonCorregirId;
        public Button botonAccesoManual;
        public GameObject panelSeleccionId;
        public Button[] botonesIds;
        public Button botonCancelarId;

        [Header("Vitrina en pantalla - estable al mover el celular")]
        [Tooltip("Distancia de la figura frente a la camara AR, en metros")]
        [Range(0.30f, 0.85f)] public float distanciaDeCamara = 0.47f;
        [Tooltip("Tamanio de exhibicion en la pantalla (sin modificar el tamanio de las cartas)")]
        [Range(1.0f, 3.0f)] public float escalaDeExhibicion = 2.15f;
        [Header("Vitrina 3D")]
        public GameObject panelGiro;
        public Button botonGiroIzq;
        public Button botonGiroDer;
        public Button botonCentrar;
        public Button botonGiroAuto;
        public Text textoGiroAuto;
        public float velocidadGiroAutomatico = 7f;
        [Range(10f, 90f)] public float gradosPorPulsacion = 35f;

        private Card cartaActiva;
        private ARTrackedImage imagenActiva;
        private GameObject modeloActual;
        private Transform pivoteGiro;
        private GameObject nodoVitrina;
        private float anguloManual;
        private float anguloAuto;
        private bool giroAutomatico = true;
        private bool modoManual;
        private bool seleccionVisible;
        private float inicioEscaneo;
        private float esperarEscaneoHasta;
        private bool frenteActivo;
        private int fichaActual;
        private float ultimoSeguimientoActual = -100f;
        private float proximaRevision;
        private string candidatoClave = "";
        private float candidatoDesde;
        private bool bloqueoDiagnostico;
        private bool modoEvaluacion;
        private ARTrackedImage targetEvaluacion;
        private float ultimaVistaEvaluacion = -100f;
        // El modelo sigue el marcador con filtro suave; un anclaje mundial NO sigue una carta.
        private bool modeloColocadoEvaluacion;
        private bool verificandoSaltoCarta;
        private Vector3 posicionSaltoCandidata;
        private float inicioSaltoCarta;
        private ARAnchor anclaEvaluacion;
        private int versionSolicitudAncla;

#if UNITY_EDITOR
        private int simulacion = -1;
        private bool simulacionFrente;
        private bool testVisible;
#endif

        private sealed class Observacion
        {
            public Card carta;
            public ARTrackedImage imagen;
            public bool esFrente;
            public int prioridad;
        }

        private sealed class Grupo
        {
            public Card carta;
            public Observacion mejor;
            public float puntaje;
            public int coincidencias;
        }

        private void Awake()
        {
            modoEvaluacion = modoInicial == ModoPresentacion.Evaluacion;
            inicioEscaneo = Time.unscaledTime;
            OcultarTodo();
            ActualizarModoUI();
            Mensaje(modoEvaluacion ? "MODO EVALUACION | apunta a una carta" :
                "MODO VITRINA | apunta a una carta");
        }

        private void Start()
        {
            if (boton1) boton1.onClick.AddListener(VerFicha1);
            if (boton2) boton2.onClick.AddListener(VerFicha2);
            if (botonCerrar) botonCerrar.onClick.AddListener(CerrarFicha);
            if (botonCambiar) botonCambiar.onClick.AddListener(OtraCarta);
            if (botonCorregirId) botonCorregirId.onClick.AddListener(MostrarSelectorId);
            if (botonAccesoManual) botonAccesoManual.onClick.AddListener(MostrarSelectorId);
            if (botonCancelarId) botonCancelarId.onClick.AddListener(CerrarSelectorId);
            if (botonesIds != null)
                for (int i=0; i<botonesIds.Length; i++)
                {
                    int index=i;
                    if (botonesIds[i]) botonesIds[i].onClick.AddListener(() => ElegirIdManual(index));
                }
            if (botonModo) botonModo.onClick.AddListener(CambiarModo);
            if (botonGiroIzq) botonGiroIzq.onClick.AddListener(GirarIzquierda);
            if (botonGiroDer) botonGiroDer.onClick.AddListener(GirarDerecha);
            if (botonCentrar) botonCentrar.onClick.AddListener(ReiniciarGiro);
            if (botonGiroAuto) botonGiroAuto.onClick.AddListener(AlternarGiroAutomatico);
            ActualizarTextoAuto();

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
                Permission.RequestUserPermission(Permission.Camera);
#endif
            // V7 requirio habilitar el componente manualmente. V8 CORREGIDA lo fuerza al iniciar.
            if (!gestorImagenes)
                Debug.LogError("INVIZIMALS V8 CORREGIDA: falta el ARTrackedImageManager.");
            else
            {
                if (!gestorImagenes.enabled)
                {
                    Debug.LogWarning("INVIZIMALS V8 CORREGIDA: activando automaticamente ARTrackedImageManager.");
                    gestorImagenes.enabled = true;
                }
                if (gestorImagenes.referenceLibrary == null || gestorImagenes.referenceLibrary.count != 9)
                    Debug.LogError("INVIZIMALS V8 CORREGIDA: biblioteca de imagenes incompleta (esperadas 9 referencias).");
            }
        }

        private Card Buscar(string nombre, out bool frente, out int prioridad)
        {
            frente = false;
            prioridad = 0;
            if (cartas == null) return null;
            foreach (Card c in cartas)
            {
                if (c == null) continue;
                if ((!string.IsNullOrEmpty(c.frente) && nombre == c.frente) ||
                    (!string.IsNullOrEmpty(c.frenteAlternativo) && nombre == c.frenteAlternativo))
                { frente = true; prioridad = 7; return c; }
                // En V8 CORREGIDA cada reverso tiene UNA sola referencia de patron + numero,
                // sin las copias simultaneas del mismo marcador que competian en V8.
                if (nombre == c.reverso)
                { prioridad = 6; return c; }
            }
            return null;
        }

        private bool EnCampoDeCamara(ARTrackedImage im)
        {
            Camera cam = Camera.main;
            if (cam == null) return true;
            Vector3 v = cam.WorldToViewportPoint(im.transform.position);
            return v.z > 0f && v.x > -.08f && v.x < 1.08f && v.y > -.08f && v.y < 1.08f;
        }

        private void Update()
        {
            // Durante la evaluacion mantener la criatura quieta: solo la vitrina gira sola.
            if (giroAutomatico && pivoteGiro != null && (!modoEvaluacion || !mantenerQuietoEnEvaluacion))
            {
                anguloAuto = Mathf.Repeat(anguloAuto + velocidadGiroAutomatico * Time.deltaTime, 360f);
                AplicarRotacionActual();
            }
            if (botonAccesoManual)
                botonAccesoManual.gameObject.SetActive(!modoEvaluacion && cartaActiva == null &&
                    !seleccionVisible && Time.unscaledTime - inicioEscaneo > 5f);
            if (seleccionVisible || Time.unscaledTime < esperarEscaneoHasta)
                return;
#if UNITY_EDITOR
            if (simulacion >= 0 && cartas != null && simulacion < cartas.Length)
            {
                Card c = cartas[simulacion];
                if (modoEvaluacion) SeleccionarEvaluacion(c, null);
                else Seleccionar(c, null, simulacionFrente);
                Mensaje("TEST LOCAL (no es deteccion AR) | " + c.nombre + " | ID " + c.id
                    + (simulacionFrente ? " | frente" : " | reverso"));
                return;
            }
#endif
            if (modoEvaluacion)
            {
                ActualizarEvaluacion();
                return;
            }
            if (modoManual && cartaActiva != null) return;
            if (!gestorImagenes)
            {
                Mensaje("ERROR: falta AR Tracked Image Manager");
                return;
            }

            Dictionary<string, Grupo> grupos = new Dictionary<string, Grupo>();
            int vistos = 0;
            int visibles = 0;
            bool actualObservada = false;
            Observacion actualMejor = null;
            string ultimo = "";
            foreach (ARTrackedImage im in gestorImagenes.trackables)
            {
                if (!im) continue;
                vistos++;
                string target = im.referenceImage.name;
                ultimo = target + ": " + im.trackingState;
                if (im.trackingState != TrackingState.Tracking || !EnCampoDeCamara(im)) continue;
                bool frente;
                int prioridad;
                Card c = Buscar(target, out frente, out prioridad);
                if (c == null) continue;
                visibles++;
                Observacion obs = new Observacion { carta = c, imagen = im,
                    esFrente = frente, prioridad = prioridad };
                if (c == cartaActiva && frente == frenteActivo)
                {
                    actualObservada = true;
                    if (actualMejor == null || obs.imagen == imagenActiva ||
                        (actualMejor.imagen != imagenActiva && obs.prioridad > actualMejor.prioridad))
                        actualMejor = obs;
                }
                Grupo grupo;
                if (!grupos.TryGetValue(c.id, out grupo))
                {
                    grupo = new Grupo { carta = c, mejor = obs, puntaje = prioridad,
                        coincidencias = 1 };
                    grupos.Add(c.id, grupo);
                }
                else
                {
                    grupo.coincidencias++;
                    if (prioridad > grupo.mejor.prioridad ||
                        (prioridad == grupo.mejor.prioridad && im == imagenActiva))
                        grupo.mejor = obs;
                    grupo.puntaje = Mathf.Max(grupo.puntaje, prioridad);
                }
            }

            Grupo ganador = null;
            Grupo segundo = null;
            foreach (Grupo g in grupos.Values)
            {
                g.puntaje += Mathf.Min(2, g.coincidencias - 1) * 1.5f;
                if (ganador == null || g.puntaje > ganador.puntaje)
                { segundo = ganador; ganador = g; }
                else if (segundo == null || g.puntaje > segundo.puntaje)
                    segundo = g;
            }
            // Una figura identificada queda bloqueada: ninguna deteccion ajena puede reemplazarla.
            // El usuario debe tocar OTRA CARTA para volver a identificar otra criatura.
            if (cartaActiva != null && ganador != null && ganador.carta != cartaActiva)
            {
                if (grupos.TryGetValue(cartaActiva.id, out Grupo vigente))
                    ganador = vigente;
                else
                    ganador = null;
                segundo = null;
            }
            bool ambiguo = segundo != null && ganador != null && ganador.puntaje - segundo.puntaje < 2f;
            if (actualObservada)
                ultimoSeguimientoActual = Time.unscaledTime;

            if (ambiguo)
            {
                // En caso de empate NO cambiar a una criatura incorrecta.
                candidatoClave = "";
                bloqueoDiagnostico = true;
                if (cartaActiva == null)
                    Mensaje("Reconocimiento ambiguo: aleja otras cartas y enfoca una sola");
            }
            else if (ganador != null)
            {
                bloqueoDiagnostico = false;
                Observacion obs = ganador.mejor;
                string clave = obs.carta.id + (obs.esFrente ? ":F" : ":R");
                if (cartaActiva == obs.carta && frenteActivo == obs.esFrente)
                {
                    candidatoClave = "";
                    ultimoSeguimientoActual = Time.unscaledTime;
                    // Si alterna entre la foto completa y su detalle, no reconstruir el prefab.
                    // La vitrina permanece en pantalla: NO reubicarla al cambiar la referencia. 
                    Mensaje(obs.carta.nombre + " | ID " + obs.carta.id +
                        (mostrarDiagnostico ? (obs.esFrente ? " | frente" : " | reverso") : ""));
                    ActualizarPanelGiro();
                }
                else
                {
                    if (clave != candidatoClave)
                    { candidatoClave = clave; candidatoDesde = Time.unscaledTime; }
                    float espera = cartaActiva == null ? confirmacionInicial :
                        (cartaActiva == obs.carta ? .65f : confirmacionCambio);
                    if (Time.unscaledTime - candidatoDesde >= espera)
                    {
                        Seleccionar(obs.carta, obs.imagen, obs.esFrente);
                        ultimoSeguimientoActual = Time.unscaledTime;
                        candidatoClave = "";
                        Mensaje(obs.carta.nombre + " | ID " + obs.carta.id +
                            (obs.esFrente ? " | frente reconocido" : " | reverso reconocido"));
                    }
                    else if (cartaActiva == null)
                        Mensaje("Confirmando carta... enfoca sin mover el celular");
                }
            }
            else
            {
                bloqueoDiagnostico = false;
                candidatoClave = "";
            }

            // La vitrina permanece visible en la pantalla al mover la camara o perder la carta.
            // Nunca desaparece ni cambia de identidad por un fallo momentaneo de ARCore.
            // OTRA CARTA restablece el escaneo de manera explicita.
            if (cartaActiva != null && !actualObservada && ganador == null)
                Mensaje("Vitrina fija en pantalla: " + cartaActiva.nombre + " | OTRA CARTA para cambiar");

            if (cartaActiva == null && !bloqueoDiagnostico && ganador == null &&
                Time.unscaledTime >= proximaRevision)
            {
                proximaRevision = Time.unscaledTime + .55f;
                Mensaje(EstadoReconocimiento(vistos, visibles, ultimo));
            }
        }

        /// <summary>
        /// MODO EVALUACION: solo se muestran modelos al reconocer imagenes con seguimiento.
        /// 3D colocado y actualizado sobre el marcador, sin anclajes fijos de mundo.
        /// Las dos caras de la misma criatura usan el mismo modelo y fichas.
        /// </summary>
        private void ActualizarEvaluacion()
        {
            if (!gestorImagenes)
            {
                Mensaje("ERROR: falta AR Tracked Image Manager");
                return;
            }
            Observacion preferida = null;
            int especiesVisibles = 0;
            string idVista = "";
            bool mantenerActual = false;
            foreach (ARTrackedImage im in gestorImagenes.trackables)
            {
                if (!im || im.trackingState != TrackingState.Tracking || !EnCampoDeCamara(im))
                    continue;
                bool esFrente;
                int prioridad;
                Card c = Buscar(im.referenceImage.name, out esFrente, out prioridad);
                if (c == null) continue;
                if (idVista != c.id) { idVista = c.id; especiesVisibles++; }
                Observacion obs = new Observacion { carta = c, imagen = im,
                    esFrente = esFrente, prioridad = prioridad };
                if (cartaActiva == c)
                {
                    // Preferir la misma referencia de imagen que ya se estaba siguiendo.
                    // Si ARCore alterna entre frente/reverso, evitamos saltos de posicion.
                    if (!mantenerActual || im == targetEvaluacion ||
                        (preferida != null && preferida.imagen != targetEvaluacion &&
                        obs.prioridad > preferida.prioridad))
                        preferida = obs;
                    mantenerActual = true;
                    continue;
                }
                if (!mantenerActual && (preferida == null || obs.prioridad > preferida.prioridad))
                    preferida = obs;
            }
            // Frente y reverso de dos cartas diferentes en la misma vista: no adivinar.
            if (!mantenerActual && especiesVisibles > 1)
            {
                candidatoClave = "";
                if (cartaActiva == null) Mensaje("Enfoca solo una carta para evitar confusiones");
                ComprobarPerdidaEvaluacion();
                return;
            }
            if (preferida != null)
            {
                if (cartaActiva == preferida.carta)
                {
                    // No saltar de un frente a un reverso (o a un falso positivo)
                    // mientras el target original siga en tracking estable.
                    bool originalVigente = targetEvaluacion != null &&
                        targetEvaluacion.trackingState == TrackingState.Tracking &&
                        EnCampoDeCamara(targetEvaluacion);
                    if (targetEvaluacion == null || preferida.imagen == targetEvaluacion ||
                        !originalVigente)
                    {
                        targetEvaluacion = preferida.imagen;
                        ultimaVistaEvaluacion = Time.unscaledTime;
                    }
                    else
                        ultimaVistaEvaluacion = Time.unscaledTime;
                    ColocarModeloSobreCarta(targetEvaluacion, false);
                    candidatoClave = "";
                    Mensaje("DETECTADO | " + cartaActiva.nombre + " | ID " + cartaActiva.id);
                    return;
                }
                string clave = preferida.carta.id;
                if (candidatoClave != clave)
                {
                    candidatoClave = clave;
                    candidatoDesde = Time.unscaledTime;
                }
                if (Time.unscaledTime - candidatoDesde >= (cartaActiva == null ?
                    confirmacionInicial : confirmacionCambio))
                {
                    SeleccionarEvaluacion(preferida.carta, preferida.imagen);
                    ultimaVistaEvaluacion = Time.unscaledTime;
                    candidatoClave = "";
                }
                else if (cartaActiva == null)
                    Mensaje("Confirmando carta... mantenla enfocada");
                if (cartaActiva != null && cartaActiva != preferida.carta)
                    ComprobarPerdidaEvaluacion();
            }
            else
            {
                candidatoClave = "";
                ComprobarPerdidaEvaluacion();
                if (cartaActiva == null && Time.unscaledTime >= proximaRevision)
                {
                    proximaRevision = Time.unscaledTime + .65f;
                    Mensaje(EstadoReconocimiento(0, 0, ""));
                }
            }
        }

        private void ComprobarPerdidaEvaluacion()
        {
            if (cartaActiva != null && Time.unscaledTime - ultimaVistaEvaluacion >= toleranciaPerdidaEvaluacion)
            {
                OcultarTodo();
                Mensaje("Carta fuera de camara | vuelve a enfocarla");
            }
        }

        private void SeleccionarEvaluacion(Card carta, ARTrackedImage im)
        {
            if (carta == null) return;
            if (cartaActiva != carta || modeloActual == null)
            {
                OcultarTodo();
                cartaActiva = carta;
                frenteActivo = false; // En evaluacion ambas caras muestran el modelo primero.
                imagenActiva = im;
                targetEvaluacion = im;
                RellenarFicha(carta);
                modeloColocadoEvaluacion = false;
                nodoVitrina = new GameObject("AR - Modelo sobre carta - " + carta.id);
                nodoVitrina.transform.localScale = Vector3.one;
                if (carta.modeloPrefab)
                {
                    modeloActual = Instantiate(carta.modeloPrefab, nodoVitrina.transform, false);
                    modeloActual.name = "Modelo " + carta.nombre + " sobre carta";
                    modeloActual.transform.localPosition = Vector3.zero;
                    modeloActual.transform.localRotation = Quaternion.identity;
                    modeloActual.transform.localScale = Vector3.one;
                    pivoteGiro = modeloActual.transform.Find("03 Giro manual - vitrina");
                }
                MostrarFicha(0);
            }
            targetEvaluacion = im;
            ColocarModeloSobreCarta(im, true);
            ultimaVistaEvaluacion = Time.unscaledTime;
            Mensaje("DETECTADO | " + carta.nombre + " | ID " + carta.id + " | VER FICHA");
        }

        // Los anclajes de mundo no sirven para mantener una figura sobre una CARTA.
        // La pose de ARTrackedImage cambia cuando ARCore recalcula la tarjeta; se sigue
        // esa pose con filtro, en vez de congelar un punto del mundo y dejar la carta atras.
        private Vector3 CentroFisicoCarta(ARTrackedImage im)
        {
            if (im == null) return Vector3.zero;
            Vector3 centro = im.transform.position;
            // Las referencias Detalle_ recortan la parte superior de los reversos.
            // ARCore devuelve el centro del RECORTE, que queda mas abajo en la carta.
            // Corregimos ese desfase usando el eje vertical LOCAL del marcador.
            if (im.referenceImage.name.StartsWith("Detalle_", StringComparison.Ordinal))
                centro += im.transform.up * correccionCentroReverso;
            // Base unos milimetros por encima de la carta para evitar parpadeos z-fighting.
            return centro + Vector3.up * .004f;
        }

        private void ColocarModeloSobreCarta(ARTrackedImage im, bool inmediato)
        {
            if (!nodoVitrina || modeloColocadoEvaluacion) return;
            Camera cam = Camera.main;
            Vector3 centro = im != null ? CentroFisicoCarta(im) :
                cam != null ? cam.transform.position + cam.transform.forward * .55f :
                new Vector3(0f, 0f, .55f);
            // El personaje siempre queda derecho, sin copiar la inclinacion de la camara.
            Quaternion orientacion = cam != null
                ? Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f)
                : Quaternion.identity;
            nodoVitrina.transform.SetPositionAndRotation(centro, orientacion);
            modeloColocadoEvaluacion = true;
            verificandoSaltoCarta = false;
        }

        // Se ejecuta despues de los cambios de seguimiento de AR Foundation.
        // En modo evaluacion el personaje sigue el CENTRO de la carta, nunca la camara.
        private void LateUpdate()
        {
            if (!modoEvaluacion || !modeloColocadoEvaluacion || nodoVitrina == null ||
                targetEvaluacion == null || cartaActiva == null) return;
            if (targetEvaluacion.trackingState != TrackingState.Tracking ||
                !EnCampoDeCamara(targetEvaluacion) ||
                Time.unscaledTime - ultimaVistaEvaluacion > .25f) return;

            Vector3 destino = CentroFisicoCarta(targetEvaluacion);
            Transform raiz = nodoVitrina.transform;
            float diferencia = Vector3.Distance(raiz.position, destino);
            if (diferencia <= zonaMuertaCarta) return;

            // Un salto enorme puede indicar reconocimiento inestable o correccion
            // brusca de ARCore: ignorar el primer resultado y confirmar persistencia.
            if (diferencia > umbralSaltoCarta)
            {
                if (!verificandoSaltoCarta ||
                    Vector3.Distance(destino, posicionSaltoCandidata) > .02f)
                {
                    verificandoSaltoCarta = true;
                    posicionSaltoCandidata = destino;
                    inicioSaltoCarta = Time.unscaledTime;
                    return;
                }
                if (Time.unscaledTime - inicioSaltoCarta < confirmarSaltoCarta) return;
            }
            else verificandoSaltoCarta = false;

            // Seguimiento adaptable: reduce temblores pequenos, pero acompana a la carta
            // cuando de verdad se desplaza. No se cambia la rotacion vertical del modelo.
            float frecuencia = diferencia < .015f ? velocidadSeguimientoCarta * .55f :
                velocidadSeguimientoCarta * 1.6f;
            float avance = 1f - Mathf.Exp(-frecuencia * Time.deltaTime);
            raiz.position = Vector3.Lerp(raiz.position, destino, avance);
        }

        public void CambiarModo()
        {
#if UNITY_EDITOR
            simulacion = -1;
#endif
            CerrarSelectorId();
            OcultarTodo();
            modoEvaluacion = !modoEvaluacion;
            candidatoClave = "";
            ultimaVistaEvaluacion = -100f;
            modeloColocadoEvaluacion = false;
            inicioEscaneo = Time.unscaledTime;
            esperarEscaneoHasta = Time.unscaledTime + .4f;
            ActualizarModoUI();
            Mensaje(modoEvaluacion ? "MODO EVALUACION | modelo sobre la carta" :
                "MODO VITRINA | modelo fijo en pantalla");
        }

        private void ActualizarModoUI()
        {
            if (textoBotonModo)
                textoBotonModo.text = modoEvaluacion ? "CAMBIAR A VITRINA" : "CAMBIAR A EVALUACION";
            ActualizarAcciones();
        }

        private void ActualizarAcciones()
        {
            bool activa = cartaActiva != null;
            if (barraBotones) barraBotones.SetActive(activa);
            if (boton1)
            {
                boton1.gameObject.SetActive(activa);
                Text label = boton1.GetComponentInChildren<Text>();
                if (label) label.text = modoEvaluacion ?
                    (fichaActual == 0 ? "VER FICHA" : "FICHA 1") : "PERFIL";
            }
            if (boton2)
            {
                boton2.gameObject.SetActive(activa && (!modoEvaluacion || fichaActual != 0));
                Text label = boton2.GetComponentInChildren<Text>();
                if (label) label.text = modoEvaluacion ? "FICHA 2" : "HISTORIA";
            }
            if (botonCerrar) botonCerrar.gameObject.SetActive(activa &&
                (!modoEvaluacion || fichaActual != 0));
            // Un boton ancho para el flujo de evaluacion; tabs al abrir la ficha.
            // Se restauran las anclas originales al cambiar a Vitrina.
            if (boton1)
            {
                RectTransform r = boton1.GetComponent<RectTransform>();
                if (r != null)
                {
                    r.anchorMin = modoEvaluacion && fichaActual == 0 ?
                        new Vector2(.18f, .148f) : new Vector2(.045f, .148f);
                    r.anchorMax = modoEvaluacion && fichaActual == 0 ?
                        new Vector2(.82f, .215f) : new Vector2(.483f, .215f);
                    r.offsetMin = r.offsetMax = Vector2.zero;
                }
            }
            if (botonCerrar)
            {
                RectTransform r = botonCerrar.GetComponent<RectTransform>();
                if (r != null)
                {
                    r.anchorMin = modoEvaluacion ?
                        new Vector2(.19f, .06f) : new Vector2(.045f, .06f);
                    r.anchorMax = modoEvaluacion ?
                        new Vector2(.81f, .128f) : new Vector2(.483f, .128f);
                    r.offsetMin = r.offsetMax = Vector2.zero;
                }
            }
            if (botonCambiar) botonCambiar.gameObject.SetActive(activa && !modoEvaluacion);
            if (botonCorregirId) botonCorregirId.gameObject.SetActive(activa && !modoEvaluacion);
            ActualizarPanelGiro();
        }

        private void Seleccionar(Card carta, ARTrackedImage target, bool frente)
        {
            if (carta == null) return;
            if (cartaActiva == carta && !modoManual && modeloActual != null && frenteActivo != frente)
            {
                // Frente y reverso del MISMO personaje no deben reconstruir la vitrina ni el anclaje.
                frenteActivo = frente;
                imagenActiva = target;
                RellenarFicha(carta);
                MostrarFicha(frente ? 1 : 0);
                return;
            }
            bool cambio = cartaActiva != carta || frenteActivo != frente;
            if (cambio)
            {
                DestruirVitrina();
                modeloActual = null;
                pivoteGiro = null;
                anguloManual = 0f;
                anguloAuto = 0f;
                cartaActiva = carta;
                imagenActiva = target;
                frenteActivo = frente;
                RellenarFicha(carta);
                if (!frente && carta.modeloPrefab)
                {
                    // La carta activa la vitrina, pero NO determina su posicion ni rotacion.
                    // El usuario puede mover, subir, bajar e inclinar el movil sin
                    // que el modelo se desplace fuera de la pantalla.
                    Camera camara = Camera.main;
                    if (camara != null)
                    {
                        nodoVitrina = new GameObject("Vitrina en pantalla - NO ligada a ARTrackedImage");
                        nodoVitrina.transform.SetParent(camara.transform, false);
                        nodoVitrina.transform.localPosition = new Vector3(0f, -0.018f, distanciaDeCamara);
                        nodoVitrina.transform.localRotation = Quaternion.identity;
                        nodoVitrina.transform.localScale = Vector3.one * escalaDeExhibicion;
                        modeloActual = Instantiate(carta.modeloPrefab, nodoVitrina.transform, false);
                        modeloActual.transform.localPosition = Vector3.zero;
                        modeloActual.transform.localRotation = Quaternion.identity;
                        modeloActual.transform.localScale = Vector3.one;
                    }
                    else
                    {
                        Debug.LogWarning("INVIZIMALS: no se encontro AR Camera con etiqueta MainCamera; no se puede crear vitrina fija.");
                        Mensaje("Camara AR no disponible; comprueba la etiqueta MainCamera");
                    }
                    if (modeloActual)
                    {
                        modeloActual.name = "InvizimalV8 CORREGIDA_" + carta.id;
                        pivoteGiro = modeloActual.transform.Find("03 Giro manual - vitrina");
                    }
                }
                MostrarFicha(frente ? 1 : 0);
                Debug.Log("INVIZIMALS V8 CORREGIDA: carta confirmada " + carta.id +
                    (frente ? " frente" : " reverso"));
            }
            // El modelo no se mueve al perder el marcador; queda fijo respecto de la pantalla.
            ActualizarAcciones();
        }

        // La vitrina permanece en la camara y no necesita anclajes AR.
        // Los anclajes son para objetos que deben quedar en el entorno fisico;
        // aqui el usuario pidio que la criatura NO se vaya de la pantalla.

        private void DestruirVitrina()
        {
            ++versionSolicitudAncla;
            modeloColocadoEvaluacion = false;
            if (nodoVitrina) Destroy(nodoVitrina);
            nodoVitrina = null;
            if (anclaEvaluacion) Destroy(anclaEvaluacion.gameObject);
            anclaEvaluacion = null;
            if (modeloActual) Destroy(modeloActual);
            modeloActual = null;
        }

        public void OtraCarta()
        {
#if UNITY_EDITOR
            simulacion = -1;
#endif
            CerrarSelectorId();
            OcultarTodo();
            inicioEscaneo = Time.unscaledTime;
            esperarEscaneoHasta = Time.unscaledTime + 1.25f;
            Mensaje("Retira la carta anterior y apunta a la siguiente");
        }

        public void MostrarSelectorId()
        {
            if (modoEvaluacion || cartas == null || cartas.Length == 0) return;
            seleccionVisible = true;
            if (panelSeleccionId) panelSeleccionId.SetActive(true);
            if (botonAccesoManual) botonAccesoManual.gameObject.SetActive(false);
            Mensaje("ELEGIR ID | correccion manual, no lectura OCR");
        }
        public void CerrarSelectorId()
        {
            seleccionVisible = false;
            if (panelSeleccionId) panelSeleccionId.SetActive(false);
        }
        public void ElegirIdManual(int index)
        {
            if (cartas == null || index < 0 || index >= cartas.Length) return;
#if UNITY_EDITOR
            simulacion = -1;
#endif
            CerrarSelectorId();
            OcultarTodo();
            modoManual = true;
            Seleccionar(cartas[index], null, false);
            Mensaje("MODO MANUAL | " + cartas[index].nombre + " | ID " + cartas[index].id);
            Debug.Log("INVIZIMALS: ID seleccionado manualmente " + cartas[index].id);
        }
        private void AplicarRotacionActual()
        {
            if (pivoteGiro != null)
                pivoteGiro.localRotation = Quaternion.Euler(0f, Mathf.Repeat(anguloManual + anguloAuto, 360f), 0f);
        }
        private void ActualizarTextoAuto()
        {
            if (textoGiroAuto) textoGiroAuto.text = giroAutomatico ? "AUTO ON" : "AUTO OFF";
        }
        public void AlternarGiroAutomatico()
        {
            giroAutomatico = !giroAutomatico;
            ActualizarTextoAuto();
        }

        private void RellenarFicha(Card c)
        {
            if (tituloFicha1) tituloFicha1.text = c.nombre.ToUpperInvariant();
            if (datosFicha1) datosFicha1.text = "CARTA " + c.id + "   /   " + c.fase;
            if (tipoFicha1) tipoFicha1.text = c.elemento.ToUpperInvariant();
            if (tipoFondo)
                tipoFondo.color = c.elemento == "Fuego" ? new Color(.70f,.26f,.10f,1f)
                    : c.elemento == "Selva" ? new Color(.12f,.40f,.28f,1f)
                    : new Color(.47f,.34f,.12f,1f);
            if (vidaValor) vidaValor.text = c.vida.ToString("N0") + " / 1000";
            if (danoValor) danoValor.text = c.dano.ToString("N0") + " / 1000";
            if (vidaRelleno) vidaRelleno.rectTransform.anchorMax =
                new Vector2(Mathf.Clamp01(c.vida / 1000f), 1f);
            if (danoRelleno) danoRelleno.rectTransform.anchorMax =
                new Vector2(Mathf.Clamp01(c.dano / 1000f), 1f);
            if (tituloFicha2) tituloFicha2.text = c.nombre.ToUpperInvariant();
            if (descripcionFicha2) descripcionFicha2.text = c.descripcion;
            ConfigurarRetrato(imagenFicha, c.retrato);
            ConfigurarRetrato(imagenFicha2, c.retrato);
        }

        private static void ConfigurarRetrato(RawImage img, Texture2D tex)
        {
            if (!img) return;
            img.texture = tex;
            img.enabled = tex != null;
            img.color = Color.white;
            AspectRatioFitter aspect = img.GetComponent<AspectRatioFitter>();
            if (aspect != null && tex != null && tex.height > 0)
            {
                aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                aspect.aspectRatio = (float)tex.width / tex.height;
            }
        }

        private void MostrarFicha(int numero)
        {
            if (cartaActiva == null) numero = 0;
            fichaActual = numero;
            if (panelFicha1) panelFicha1.SetActive(numero == 1);
            if (panelFicha2) panelFicha2.SetActive(numero == 2);
            ActualizarAcciones();
        }
        public void VerFicha1() { MostrarFicha(1); }
        public void VerFicha2() { MostrarFicha(2); }
        public void CerrarFicha() { MostrarFicha(0); }

        private void ActualizarPanelGiro()
        {
            if (panelGiro) panelGiro.SetActive(!modoEvaluacion && cartaActiva != null &&
                modeloActual != null && !frenteActivo && fichaActual == 0);
        }
        private void AplicarGiro(float delta)
        {
            if (!modeloActual || frenteActivo || !pivoteGiro) return;
            anguloManual = Mathf.Repeat(anguloManual + delta, 360f);
            AplicarRotacionActual();
        }
        public void GirarIzquierda() { AplicarGiro(-gradosPorPulsacion); }
        public void GirarDerecha() { AplicarGiro(gradosPorPulsacion); }
        public void ReiniciarGiro()
        {
            anguloManual = 0f;
            anguloAuto = 0f;
            AplicarRotacionActual();
        }

        private void OcultarTodo(bool limpiarCandidato = true)
        {
            cartaActiva = null;
            imagenActiva = null;
            frenteActivo = false;
            modoManual = false;
            fichaActual = 0;
            targetEvaluacion = null;
            modeloColocadoEvaluacion = false;
            verificandoSaltoCarta = false;
            ++versionSolicitudAncla;
            pivoteGiro = null;
            anguloManual = 0f;
            DestruirVitrina();
            if (panelFicha1) panelFicha1.SetActive(false);
            if (panelFicha2) panelFicha2.SetActive(false);
            if (barraBotones) barraBotones.SetActive(false);
            if (panelGiro) panelGiro.SetActive(false);
            if (imagenFicha) { imagenFicha.texture = null; imagenFicha.enabled = false; }
            if (imagenFicha2) { imagenFicha2.texture = null; imagenFicha2.enabled = false; }
            if (limpiarCandidato) candidatoClave = "";
        }
        private void Mensaje(string texto)
        { if (estado && estado.text != texto) estado.text = texto; }

        private string EstadoReconocimiento(int vistos, int siguiendo, string ultimo)
        {
#if UNITY_EDITOR
            return "Test local listo | 4 personajes | 9 referencias AR";
#else
            if (Application.platform == RuntimePlatform.Android &&
                !Permission.HasUserAuthorizedPermission(Permission.Camera))
                return "Autoriza el permiso de camara en Android";
            if (ARSession.state == ARSessionState.Unsupported)
                return "ARCore no esta disponible en el dispositivo";
            if (ARSession.state == ARSessionState.NeedsInstall)
                return "Instala Servicios de Google Play para RA";
            if (ARSession.state != ARSessionState.SessionTracking)
                return "ARCore: " + ARSession.state + " | esperando camara";
            if (gestorImagenes.referenceLibrary == null || gestorImagenes.referenceLibrary.count != 9)
                return "Faltan imagenes en BibliotecaInvizimalsV8Final";
            if (gestorImagenes.subsystem == null || !gestorImagenes.subsystem.running)
                return "ARCore iniciado, pero seguimiento de imagenes inactivo";
            if (vistos > 0 && siguiendo == 0)
                return "Imagen detectada sin seguimiento estable: " + ultimo;
            if (mostrarDiagnostico)
                return "ARCore listo | " + siguiendo + " objetivos visibles | enfoca una sola carta";
            return "Apunta a una carta completa y evita los reflejos";
#endif
        }

        private void OnDestroy()
        {
            if (boton1) boton1.onClick.RemoveListener(VerFicha1);
            if (boton2) boton2.onClick.RemoveListener(VerFicha2);
            if (botonCerrar) botonCerrar.onClick.RemoveListener(CerrarFicha);
            if (botonCambiar) botonCambiar.onClick.RemoveListener(OtraCarta);
            if (botonCorregirId) botonCorregirId.onClick.RemoveListener(MostrarSelectorId);
            if (botonAccesoManual) botonAccesoManual.onClick.RemoveListener(MostrarSelectorId);
            if (botonCancelarId) botonCancelarId.onClick.RemoveListener(CerrarSelectorId);
            if (botonModo) botonModo.onClick.RemoveListener(CambiarModo);
            if (botonGiroIzq) botonGiroIzq.onClick.RemoveListener(GirarIzquierda);
            if (botonGiroDer) botonGiroDer.onClick.RemoveListener(GirarDerecha);
            if (botonCentrar) botonCentrar.onClick.RemoveListener(ReiniciarGiro);
            if (botonGiroAuto) botonGiroAuto.onClick.RemoveListener(AlternarGiroAutomatico);
        }

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (GUI.Button(new Rect(12, 12, 134, 32), testVisible ? "OCULTAR TEST" : "TEST LOCAL"))
                testVisible = !testVisible;
            if (!testVisible) return;
            float disponible = Mathf.Min(710f, Screen.width - 24f);
            GUILayout.BeginArea(new Rect(12, 50, disponible, 150), GUI.skin.box);
            GUILayout.Label("TEST LOCAL - NO ES DETECCION REAL DE CAMARA | SIMULA CARTAS Y FICHAS");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Math.Min(4, cartas == null ? 0 : cartas.Length); i++)
            {
                int indice = i;
                if (GUILayout.Button(cartas[i].nombre, GUILayout.Height(30)))
                { simulacion = indice; simulacionFrente = false; }
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Simular FRENTE", GUILayout.Height(28)))
            { if (simulacion < 0) simulacion = 0; simulacionFrente = true; }
            if (GUILayout.Button("Simular REVERSO", GUILayout.Height(28)))
            { if (simulacion < 0) simulacion = 0; simulacionFrente = false; }
            if (GUILayout.Button("Limpiar", GUILayout.Height(28)))
            { simulacion = -1; OcultarTodo(); }
            GUILayout.EndHorizontal();
            GUILayout.Label(modoEvaluacion ? "EVALUACION: 3D sobre carta y VER FICHA al reconocer." :
                "VITRINA: GIRO IZQ. / GIRO DER. / CENTRAR con personaje fijo en pantalla.");
            GUILayout.EndArea();
        }
#endif
    }
}
