#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.ARSubsystems;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;
using InvizimalsRA.V8Final;

/// <summary>
/// Generador que construye una escena nueva y aislada, sin referencias heredadas de V1-V3.
/// Proyecto autonomo para Unity Hub. Se activa despues de importar los paquetes XR.
/// </summary>
[InitializeOnLoad]
public static class InvizimalsV8FinalBuilder
{
    private const string Root = "Assets/InvizimalsV8Final";
    private const string Scene = Root + "/Scenes/InvizimalsV8Final.unity";
    private const string Library = Root + "/BibliotecaInvizimalsV8Final.asset";
    private const string TargetDir = Root + "/Targets/";
    private static bool autoIntentado;

    // Nueve targets: 4 reversos, 4 frentes y recorte artistico adicional de Uber Jackal.
    private static readonly string[] Targets = {
        "Detalle_FireDragon", "Frente_FireDragon",
        "Detalle_Chupacabras", "Frente_Chupacabras",
        "Detalle_DarkTiger", "Frente_DarkTiger",
        "Detalle_UberJackal", "Frente_UberJackal", "Frente_UberJackal_Arte"
    };

    static InvizimalsV8FinalBuilder()
    {
        EditorApplication.update += AutoMontaje;
    }
    private static void AutoMontaje()
    {
        // Esperar a Unity Package Manager para que todos los tipos de AR Foundation esten listos.
        if (autoIntentado) { EditorApplication.update -= AutoMontaje; return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists(Root + "/Models/firedragon3/firedragon3.obj") ||
            !File.Exists(Root + "/Models/chupacabra3/chupacabra3.obj") ||
            !File.Exists(Root + "/Models/dark_tiger/DarkTiger.fbx") ||
            !File.Exists(Root + "/Models/jackal3/jackal3.obj") ||
            !File.Exists(TargetDir + "Frente_Chupacabras.png")) return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/dark_tiger/DarkTiger.fbx") == null) return;
        autoIntentado = true;
        EditorApplication.update -= AutoMontaje;
        if (File.Exists(Scene)) return;
        try { Generar(); }
        catch (Exception ex)
        {
            Debug.LogError("INVIZIMALS V8 CORREGIDA: no se pudo completar el montaje automatico. " +
                "Prueba el menu Invizimals V8 Corregida > 2 - Generar escena limpia y configuracion AR. " + ex);
        }
    }

    [MenuItem("Invizimals V8 Corregida/2 - Generar escena limpia y configuracion AR")]
    public static void Generar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogWarning("INVIZIMALS V8 CORREGIDA: sal de Play antes de regenerar la escena."); return; }
        Directory.CreateDirectory(Root + "/Scenes");
        Directory.CreateDirectory(Root + "/Prefabs");
        Directory.CreateDirectory(Root + "/Materials");
        AssetDatabase.Refresh();

        XRReferenceImageLibrary biblioteca = GenerarBiblioteca();
        var prefabs = new GameObject[4];
        for (int i=0; i<4; i++) prefabs[i] = PrefabPersonaje(i);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var arSession = new GameObject("AR Session");
        arSession.AddComponent<ARSession>();
        arSession.AddComponent<ARInputManager>();

        var arOrigin = new GameObject("XR Origin (Mobile AR)");
        var origin = arOrigin.AddComponent<XROrigin>();
        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(arOrigin.transform, false);
        var goCamera = new GameObject("AR Camera");
        goCamera.tag = "MainCamera";
        goCamera.transform.SetParent(offset.transform, false);
        var camera = goCamera.AddComponent<Camera>();
        camera.nearClipPlane = .01f;
        camera.farClipPlane = 30f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        goCamera.AddComponent<AudioListener>();
        goCamera.AddComponent<ARCameraManager>();
        goCamera.AddComponent<ARCameraBackground>();
        // Iluminar siempre el modelo en la vitrina, incluso al girar el telefono.
        var lightVitrina = new GameObject("Luz suave de la vitrina");
        lightVitrina.transform.SetParent(goCamera.transform, false);
        lightVitrina.transform.localPosition = new Vector3(-.12f, .13f, .18f);
        var luzVitrina = lightVitrina.AddComponent<Light>();
        luzVitrina.type = LightType.Point;
        luzVitrina.range = 1.35f;
        luzVitrina.intensity = 1.3f;
        var driver = goCamera.AddComponent<TrackedPoseDriver>();
        var pos = new InputAction("AR Device Position",InputActionType.Value,"<HandheldARInputDevice>/devicePosition");
        pos.expectedControlType="Vector3";
        var rot = new InputAction("AR Device Rotation",InputActionType.Value,"<HandheldARInputDevice>/deviceRotation");
        rot.expectedControlType="Quaternion";
        driver.positionInput = new InputActionProperty(pos);
        driver.rotationInput = new InputActionProperty(rot);
        origin.Camera = camera;
        origin.CameraFloorOffsetObject = offset;

        var anchorManager = arOrigin.AddComponent<ARAnchorManager>();
        var manager = arOrigin.AddComponent<ARTrackedImageManager>();
        manager.referenceLibrary = biblioteca;
        manager.requestedMaxNumberOfMovingImages = 1;
        // Nueve referencias: 4 reversos, 4 frentes y un segundo target para el frente brillante de Uber Jackal.
        manager.enabled = biblioteca != null && biblioteca.count == 9;

        var light = new GameObject("Luz de exhibicion 3D");
        light.transform.rotation = Quaternion.Euler(50,-27,0);
        light.AddComponent<Light>().intensity = 1.2f;

        var ctrl = arOrigin.AddComponent<InvizimalsCardController>();
        ctrl.gestorImagenes = manager;
        ctrl.gestorAnclajes = anchorManager;
        ctrl.mostrarDiagnostico = true;
        // Criterio 3.1.2: modo evaluacion inicia con el boton Ver ficha oculto.
        // La vitrina sigue disponible con el conmutador en pantalla.
        ctrl.modoInicial = InvizimalsCardController.ModoPresentacion.Evaluacion;
        // La carta se sigue con filtro; los anclajes mundiales no acompanian al papel.
        ctrl.toleranciaPerdidaEvaluacion = 1.0f;
        ctrl.velocidadSeguimientoCarta = 18f;
        ctrl.zonaMuertaCarta = .0015f;
        ctrl.umbralSaltoCarta = .095f;
        ctrl.confirmarSaltoCarta = .36f;
        ctrl.correccionCentroReverso = .014f;
        ctrl.mantenerQuietoEnEvaluacion = true;
        ctrl.distanciaDeCamara = 0.47f;
        ctrl.escalaDeExhibicion = 2.15f;
        // IDs, textos y cifras: cartas reales proporcionadas por el usuario.
        // Tres modelos OBJ y un FBX fanart aportados por el usuario.
        // Vitrina 3D permanece centrada en el campo de vision despues de escanear.
        ctrl.cartas = new [] {
            Carta("048", "Fire Dragon", "MAX", "Fuego", "900 / 750", 900, 750,
                "¿Para qué molestarte en cambiar de táctica, si eres un Fire Dragon? ¡Es infinitamente más entretenido ver cómo tus rivales huyen despavoridos cuando pisas la arena de combate!",
                "Detalle_FireDragon", "Frente_FireDragon", "Frente_FireDragon", prefabs[0]),
            Carta("081", "Chupacabras", "MAX", "Arena", "700 / 650", 700, 650,
                "Chupacabra se las apaña contra muchos Max. Cuando se enfrenta a Xtractors o a Legendarios, equípalo con el Vector Lombrices Subterráneas. Es repugnante, pero funciona.",
                "Detalle_Chupacabras", "Frente_Chupacabras", "Frente_Chupacabras", prefabs[1]),
            Carta("132", "Dark Tigershark", "MAX / Plata", "Selva", "850 / 700", 850, 700,
                "La fuerza de Dark Tigershark Max es tal que algunos cazadores dicen haberle visto derrotar al Dragón de Acero Xtractor. Quizás con algún Vector de por medio, ¡pero qué victoria!",
                "Detalle_DarkTiger", "Frente_DarkTiger", "Frente_DarkTiger", prefabs[2]),
            Carta("099", "Uber Jackal", "MAX / Oro", "Arena", "800 / 700", 800, 700,
                "Sin duda el Invizimal Legendario de Desierto más poderoso y temido del mundo Invizimal. Es el guardián del Templo de la Luz, y allí sus oponentes caerán uno tras otro sin esfuerzo.",
                "Detalle_UberJackal", "Frente_UberJackal", "Frente_UberJackal", prefabs[3])
        };
        // El arte interior de Uber Jackal se reconoce como otro frente del MISMO personaje.
        ctrl.cartas[3].frenteAlternativo = "Frente_UberJackal_Arte";
        CrearUI(ctrl);
        EditorSceneManager.SaveScene(scene,Scene);
        EditorBuildSettings.scenes=new [] { new EditorBuildSettingsScene(Scene,true) };
        ConfigurarAndroid();
        AssetDatabase.SaveAssets();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(Scene);
        Debug.Log("INVIZIMALS: escena preparada con MODO EVALUACION (3D sobre carta) y MODO VITRINA (fijo en pantalla), 9 targets y 4 modelos | " + Scene);
    }

    private static InvizimalsCardController.Card Carta(string id,string nombre,string fase,string elemento,
        string stats,int vida,int dano,string descripcion,string reverso,string frente,string retrato,GameObject modelo)
    {
        string rutaRetrato=TargetDir + retrato + ".png";
        var imagen=AssetDatabase.LoadAssetAtPath<Texture2D>(rutaRetrato);
        // No sustituir nunca un frente por el reverso.
        return new InvizimalsCardController.Card {
            id=id,nombre=nombre,fase=fase,elemento=elemento,estadisticas=stats,vida=vida,dano=dano,descripcion=descripcion,
            reverso=reverso,frente=AssetDatabase.LoadAssetAtPath<Texture2D>(TargetDir+frente+".png")?frente:"",
            retrato=imagen,modeloPrefab=modelo
        };
    }

    private static XRReferenceImageLibrary GenerarBiblioteca()
    {
        var biblioteca=AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(Library);
        if (!biblioteca) {
            biblioteca=ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
            AssetDatabase.CreateAsset(biblioteca,Library);
        }
        foreach(string target in Targets)
        {
            string ruta=TargetDir+target+".png";
            if (!File.Exists(ruta)) throw new Exception("Falta imagen AR: " + ruta);
            var importer=AssetImporter.GetAtPath(ruta) as TextureImporter;
            if (importer != null && (importer.npotScale!=TextureImporterNPOTScale.None ||
                importer.textureCompression!=TextureImporterCompression.Uncompressed || importer.mipmapEnabled))
            {
                importer.npotScale=TextureImporterNPOTScale.None;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled=false;
                importer.SaveAndReimport();
            }
            var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
            if (!tex) throw new Exception("No se pudo importar Target: " + ruta);
            int index=-1;
            for(int i=0;i<biblioteca.count;i++) if(biblioteca[i].name==target) {index=i;break;}
            if(index<0) {biblioteca.Add();index=biblioteca.count-1;}
            biblioteca.SetName(index,target);
            biblioteca.SetTexture(index,tex,true);
            // Foto completa: ancho fisico de la carta 6.4 cm, no del cuadrado de colores.
            const float ancho = .064f;
            biblioteca.SetSpecifySize(index, true);
            float anchoObjetivo = target.StartsWith("Detalle_", StringComparison.Ordinal)
                ? .064f * (875f / 960f)
                : target == "Frente_UberJackal_Arte"
                    ? .064f * (833f / 960f) : ancho;
            biblioteca.SetSize(index,new Vector2(anchoObjetivo,anchoObjetivo*tex.height/tex.width));
        }
        EditorUtility.SetDirty(biblioteca);
        AssetDatabase.SaveAssets();
        return biblioteca;
    }

    [MenuItem("Invizimals V8 Corregida/3 - Estado de fotos de las cartas")]
    public static void EstadoFotos()
    {
        int cantidad = 0;
        foreach(var t in Targets) if (File.Exists(TargetDir + t + ".png")) cantidad++;
        EditorUtility.DisplayDialog("Invizimals V8 Corregida: revision de fotografias",
            "Targets disponibles: " + cantidad + " de 9.\n" +
            "Se esperan 9 referencias: 4 frentes, 4 reversos distintivos con ID y arte interior de Uber Jackal, IDs 048, 081, 132, 099.", "Entendido");
    }

    private static void ConfigurarAndroid()
    {
        PlayerSettings.companyName="Trabajo academico";
        PlayerSettings.productName="Invizimals Bestiario AR V8 Corregida";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"cl.inacap.invizimals.bestiariov8final");
        PlayerSettings.defaultInterfaceOrientation=UIOrientation.Portrait;
        PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel26;
        try {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3});
        } catch(Exception e) { Debug.LogWarning("INVIZIMALS V8 CORREGIDA: revisa Player Android: "+e.Message); }
        try {
            var general=XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (general!=null && general.Manager!=null)
            {
                if(!XRPackageMetadataStore.AssignLoader(general.Manager,"UnityEngine.XR.ARCore.ARCoreLoader",BuildTargetGroup.Android))
                    Debug.LogWarning("INVIZIMALS V8 CORREGIDA: habilita Google ARCore en XR Plug-in Management > Android.");
            }
            else Debug.LogWarning("INVIZIMALS V8 CORREGIDA: habilita Google ARCore en XR Plug-in Management > Android.");
        } catch(Exception ex) { Debug.LogWarning("INVIZIMALS V8 CORREGIDA: comprobacion ARCore: " + ex.Message); }
    }

    private static Material Mat(string name,Color color)
    {
        string path=Root+"/Materials/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m) {
            Shader shader=Shader.Find("Standard");if(!shader) shader=Shader.Find("Unlit/Color");
            m=new Material(shader);m.color=color;AssetDatabase.CreateAsset(m,path);
        }
        return m;
    }
    private static Transform Forma(PrimitiveType tipo,string nombre,Transform parent,Vector3 pos,Vector3 escala,
        Material mat,Vector3 rot=default(Vector3))
    {
        var obj=GameObject.CreatePrimitive(tipo);
        obj.name=nombre;
        obj.transform.SetParent(parent,false);
        obj.transform.localPosition=pos;
        obj.transform.localEulerAngles=rot;
        obj.transform.localScale=escala;
        var rend=obj.GetComponent<Renderer>();if(rend)rend.sharedMaterial=mat;
        var col=obj.GetComponent<Collider>();if(col)UnityEngine.Object.DestroyImmediate(col);
        return obj.transform;
    }

    // Materiales Standard compatibles con Built-In, livianos para Android.
    private static Material MaterialPersonaje(int index)
    {
        string[] names = {"FireDragon", "Chupacabras", "DarkTiger", "UberJackal"};
        string[] diffuse = {
            "firedragon3/firedragon3_diffuse_1024.png",
            "chupacabra3/chupacabra3_diffuse_1024.png",
            "dark_tiger/DarkTiger_Albedo_2K.png",
            "jackal3/jackal3_diffuse_1024.png"
        };
        string materialPath=Root+"/Materials/Personaje_"+names[index]+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(!material)
        {
            var shader=Shader.Find("Standard");
            if(!shader) shader=Shader.Find("Unlit/Texture");
            material=new Material(shader);
            AssetDatabase.CreateAsset(material,materialPath);
        }
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Models/"+diffuse[index]);
        if (texture) { material.mainTexture=texture; material.color=Color.white; }
        material.SetFloat("_Glossiness", index==2 ? 0.36f : 0.16f);
        if (index == 2)
        {
            string normalPath=Root+"/Models/dark_tiger/DarkTiger_Normal_2K.png";
            var importer=AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if(importer!=null && importer.textureType!=TextureImporterType.NormalMap)
            { importer.textureType=TextureImporterType.NormalMap;importer.SaveAndReimport(); }
            var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if(normal) {material.SetTexture("_BumpMap",normal);material.EnableKeyword("_NORMALMAP");}
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static GameObject PrefabPersonaje(int tipo)
    {
        string[] labels={"FireDragon","Chupacabras","DarkTiger","UberJackal"};
        string[] models={
            "firedragon3/firedragon3.obj",
            "chupacabra3/chupacabra3.obj",
            "dark_tiger/DarkTiger.fbx",
            "jackal3/jackal3.obj"
        };
        string path=Root+"/Prefabs/ARContentRoot_"+labels[tipo]+".prefab";
        var root=new GameObject("ARContentRoot_"+labels[tipo]);
        root.transform.localScale=Vector3.one;
        Material dark=Mat("PedestalOscuro",new Color(.032f,.056f,.095f,1));
        Material edge=Mat("PedestalDorado",new Color(.81f,.61f,.26f,1));
        var base3d=Forma(PrimitiveType.Cylinder,"01 Base de exhibicion",root.transform,
            new Vector3(0,.003f,0),new Vector3(.059f,.003f,.059f),dark);
        var aroExhibicion = Forma(PrimitiveType.Cylinder,"02 Aro de exhibicion",root.transform,
            new Vector3(0,.0068f,0),new Vector3(.057f,.0008f,.057f),edge);
        aroExhibicion.gameObject.AddComponent<InvizimalsHaloPulse>(); // Efecto visual SIN mover el personaje.

        var giroManual = new GameObject("03 Giro manual - vitrina");
        giroManual.transform.SetParent(root.transform, false);
        var rotating=new GameObject("04 Criatura - balanceo sutil");rotating.transform.SetParent(giroManual.transform,false);
        rotating.transform.localPosition=new Vector3(0,.009f,0);
        var movimiento = rotating.AddComponent<InvizimalsMotion>();
        movimiento.amplitud = 0f; // No perder estabilidad en el modo evaluacion.
        movimiento.giro = 0f; // El controlador hace el giro automatico suave.
        var imported=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Models/"+models[tipo]);
        if (!imported) {UnityEngine.Object.DestroyImmediate(root);throw new Exception("Modelo faltante: "+models[tipo]);}
        var instance=UnityEngine.Object.Instantiate(imported);
        instance.name="Modelo_"+labels[tipo];
        instance.transform.SetParent(rotating.transform,false);
        instance.transform.localPosition=Vector3.zero;
        instance.transform.localRotation=Quaternion.identity;
        instance.transform.localScale=Vector3.one;
        var mat=MaterialPersonaje(tipo);
        var renderers=instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length==0) {UnityEngine.Object.DestroyImmediate(root); throw new Exception("Modelo sin geometria visible: "+models[tipo]);}
        Bounds bounds=renderers[0].bounds;
        for(int i=1;i<renderers.Length;i++) bounds.Encapsulate(renderers[i].bounds);
        float maxExtent=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
        // Los OBJ usan coordenadas de modelado grandes. Normalizar al ancho de la carta (6,4 cm).
        float s=maxExtent>0.000001f ? (0.061f/maxExtent):1f;
        instance.transform.localScale=Vector3.one*s;
        instance.transform.localPosition=new Vector3(-bounds.center.x*s, -bounds.min.y*s, -bounds.center.z*s);
        foreach(var renderer in renderers)
        {
            var ms=renderer.sharedMaterials;
            for(int j=0;j<ms.Length;j++) ms[j]=mat;
            renderer.sharedMaterials=ms;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows=true;
        }
        // Sin colliders ni piezas auxiliares que bloqueen la camara AR.
        foreach(var col in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
        var prefab=PrefabUtility.SaveAsPrefabAsset(root,path);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject Panel(Transform parent, string name, Color color,
        Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        go.GetComponent<Image>().color = color;
        return go;
    }

    private static Text Texto(Transform parent, string nombre, string content, int size,
        Color color, TextAnchor alignment, Vector2 min, Vector2 max,
        int minSize = 18)
    {
        var g = new GameObject(nombre, typeof(RectTransform), typeof(Text));
        g.transform.SetParent(parent, false);
        var rt = g.GetComponent<RectTransform>();
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var t = g.GetComponent<Text>();
        t.text = content;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size;
        t.color = color;
        t.alignment = alignment;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = minSize;
        t.resizeTextMaxSize = size;
        return t;
    }

    private static Button Boton(Transform parent, string name, string caption,
        Vector2 min, Vector2 max, Color color)
    {
        var go = Panel(parent, name, color, min, max, Vector2.zero, Vector2.zero);
        var b = go.AddComponent<Button>();
        var nav = b.navigation;
        nav.mode = Navigation.Mode.None;
        b.navigation = nav;
        Texto(go.transform, "Etiqueta", caption, 27, Color.white, TextAnchor.MiddleCenter,
            new Vector2(.03f,.05f), new Vector2(.97f,.95f));
        return b;
    }

    private static RawImage MarcoImagen(Transform ficha, string nombre,
        Vector2 min, Vector2 max, Color fondo)
    {
        var marco = Panel(ficha, "Marco " + nombre, fondo, min, max,
            Vector2.zero, Vector2.zero);
        // La fotografia NO ocupa todo el panel y nunca se deforma.
        var go = new GameObject(nombre, typeof(RectTransform), typeof(RawImage),
            typeof(AspectRatioFitter));
        go.transform.SetParent(marco.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(.055f, .048f);
        rt.anchorMax = new Vector2(.945f, .952f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var aspect = go.GetComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = .72f;
        var image = go.GetComponent<RawImage>();
        image.enabled = false;
        image.raycastTarget = false;
        return image;
    }

    private static Image Barra(Transform panel, string name, Color fill,
        Vector2 min, Vector2 max)
    {
        var track = Panel(panel, name + " Fondo", new Color(.105f, .137f, .18f, 1),
            min, max, Vector2.zero, Vector2.zero);
        var bar = Panel(track.transform, name + " Relleno", fill,
            new Vector2(0,0), new Vector2(1,1), Vector2.zero, Vector2.zero);
        var image = bar.GetComponent<Image>();
        // Solid-color Image without an external Sprite: runtime scales rectTransform.anchorMax.x.
        image.type = Image.Type.Simple;
        image.rectTransform.anchorMax = new Vector2(0f, 1f);
        image.raycastTarget = false;
        return image;
    }

    private static void CrearUI(InvizimalsCardController c)
    {
        // Paleta inspirada en una bestiario digital oscuro: legible, sobria y sin paneles superpuestos.
        var fondo = new Color(.033f,.055f,.093f,.965f);
        var superficie = new Color(.082f,.119f,.174f,.98f);
        var marcoFoto = new Color(.13f,.17f,.23f,1f);
        var oro = new Color(.98f,.79f,.36f,1f);
        var blanco = new Color(.955f,.965f,.985f,1f);
        var gris = new Color(.74f,.81f,.88f,1f);
        var verde = new Color(.18f,.72f,.43f,1f);
        var rojo = new Color(.87f,.30f,.34f,1f);

        var canvasGo = new GameObject("Canvas - Bestiario V8 CORREGIDA", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080,1920);
        scaler.matchWidthOrHeight = .5f;

        var header = Panel(canvasGo.transform,"Encabezado",fondo,
            new Vector2(0,.895f),Vector2.one,Vector2.zero,Vector2.zero);
        Texto(header.transform,"Titulo","INVIZIMALS   /   BESTIARIO AR",38,oro,
            TextAnchor.MiddleCenter,new Vector2(.025f,.51f),new Vector2(.975f,.98f));
        c.estado = Texto(header.transform,"Estado","Apunta a una carta para explorar su ficha",23,
            gris,TextAnchor.MiddleCenter,new Vector2(.035f,.04f),new Vector2(.965f,.49f));
        // Un selector siempre disponible; no cuenta como boton Ver ficha.
        c.botonModo = Boton(canvasGo.transform, "Modo evaluacion / vitrina",
            "CAMBIAR A VITRINA", new Vector2(.565f,.865f), new Vector2(.965f,.894f),
            new Color(.21f,.31f,.43f,.98f));
        c.textoBotonModo = c.botonModo.GetComponentInChildren<Text>();

        // Ambas fichas ocupan el mismo area, pero el controlador activa solamente una.
        c.panelFicha1 = Panel(canvasGo.transform,"Ficha 1 - Perfil",fondo,
            new Vector2(.035f,.255f),new Vector2(.965f,.865f),Vector2.zero,Vector2.zero);
        c.panelFicha2 = Panel(canvasGo.transform,"Ficha 2 - Historia",fondo,
            new Vector2(.035f,.255f),new Vector2(.965f,.865f),Vector2.zero,Vector2.zero);

        Texto(c.panelFicha1.transform,"Titulo perfil","FICHA 1   /   PERFIL DE LA CARTA",28,
            oro,TextAnchor.MiddleLeft,new Vector2(.045f,.885f),new Vector2(.955f,.98f));
        c.imagenFicha = MarcoImagen(c.panelFicha1.transform,"Fotografia frontal ficha 1",
            new Vector2(.04f,.165f),new Vector2(.442f,.86f),marcoFoto);
        var datos = Panel(c.panelFicha1.transform,"Datos y graficos",superficie,
            new Vector2(.465f,.165f),new Vector2(.962f,.86f),Vector2.zero,Vector2.zero);
        c.tituloFicha1 = Texto(datos.transform,"Nombre","",36,oro,TextAnchor.MiddleLeft,
            new Vector2(.06f,.79f),new Vector2(.955f,.975f),21);
        c.datosFicha1 = Texto(datos.transform,"Identificador","",23,gris,
            TextAnchor.MiddleLeft,new Vector2(.06f,.695f),new Vector2(.94f,.797f));
        Texto(datos.transform,"Etiqueta tipo","TIPO",20,gris,TextAnchor.MiddleLeft,
            new Vector2(.06f,.608f),new Vector2(.94f,.69f));
        var placaTipo = Panel(datos.transform, "Insignia de tipo", new Color(.47f,.34f,.12f,1f),
            new Vector2(.06f,.525f), new Vector2(.80f,.615f), Vector2.zero, Vector2.zero);
        c.tipoFondo = placaTipo.GetComponent<Image>();
        c.tipoFicha1 = Texto(placaTipo.transform,"Elemento","",28,blanco,TextAnchor.MiddleCenter,
            new Vector2(.04f,.015f),new Vector2(.96f,.985f));
        Texto(datos.transform,"Vida","VIDA",23,verde,TextAnchor.MiddleLeft,
            new Vector2(.06f,.402f),new Vector2(.38f,.49f));
        c.vidaValor = Texto(datos.transform,"Numero vida","",22,blanco,TextAnchor.MiddleRight,
            new Vector2(.40f,.402f),new Vector2(.945f,.49f));
        c.vidaRelleno = Barra(datos.transform,"Barra vida",verde,
            new Vector2(.06f,.348f),new Vector2(.945f,.390f));
        Texto(datos.transform,"Dano","DAÑO",23,rojo,TextAnchor.MiddleLeft,
            new Vector2(.06f,.225f),new Vector2(.38f,.313f));
        c.danoValor = Texto(datos.transform,"Numero dano","",22,blanco,TextAnchor.MiddleRight,
            new Vector2(.40f,.225f),new Vector2(.945f,.313f));
        c.danoRelleno = Barra(datos.transform,"Barra dano",rojo,
            new Vector2(.06f,.169f),new Vector2(.945f,.213f));
        Texto(datos.transform,"Escala","ESCALA 0 - 1000",18,gris,
            TextAnchor.MiddleRight,new Vector2(.06f,.025f),new Vector2(.945f,.12f));
        Texto(c.panelFicha1.transform,"Nota","Imagen original  ·  Estadisticas de la carta",19,gris,
            TextAnchor.MiddleCenter,new Vector2(.05f,.015f),new Vector2(.95f,.117f));

        Texto(c.panelFicha2.transform,"Titulo historia","FICHA 2   /   HISTORIA",28,oro,
            TextAnchor.MiddleLeft,new Vector2(.045f,.885f),new Vector2(.955f,.98f));
        c.imagenFicha2 = MarcoImagen(c.panelFicha2.transform,"Fotografia frontal ficha 2",
            new Vector2(.04f,.165f),new Vector2(.442f,.86f),marcoFoto);
        var historia = Panel(c.panelFicha2.transform,"Descripcion legible",superficie,
            new Vector2(.465f,.165f),new Vector2(.962f,.86f),Vector2.zero,Vector2.zero);
        c.tituloFicha2 = Texto(historia.transform,"Nombre historia","",34,oro,
            TextAnchor.MiddleLeft,new Vector2(.07f,.785f),new Vector2(.945f,.975f),19);
        Texto(historia.transform,"Subtitulo","DESCRIPCION DE LA CARTA",19,gris,
            TextAnchor.MiddleLeft,new Vector2(.07f,.690f),new Vector2(.945f,.778f));
        c.descripcionFicha2 = Texto(historia.transform,"Texto historia","",29,blanco,
            TextAnchor.UpperLeft,new Vector2(.07f,.088f),new Vector2(.93f,.681f),17);
        Texto(c.panelFicha2.transform,"Fuente","Texto transcrito de la carta original",19,gris,
            TextAnchor.MiddleCenter,new Vector2(.04f,.015f),new Vector2(.96f,.117f));

        c.barraBotones = new GameObject("Controles contextuales",typeof(RectTransform));
        c.barraBotones.transform.SetParent(canvasGo.transform,false);
        var br = c.barraBotones.GetComponent<RectTransform>();
        br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one;
        br.offsetMin = br.offsetMax = Vector2.zero;
        c.boton1 = Boton(c.barraBotones.transform,"Boton perfil","PERFIL",
            new Vector2(.045f,.148f),new Vector2(.483f,.215f),new Color(.105f,.39f,.35f,1));
        c.boton2 = Boton(c.barraBotones.transform,"Boton historia","HISTORIA",
            new Vector2(.517f,.148f),new Vector2(.955f,.215f),new Color(.17f,.34f,.55f,1));
        c.botonCerrar = Boton(c.barraBotones.transform,"Boton cerrar","CERRAR FICHA",
            new Vector2(.045f,.06f),new Vector2(.483f,.128f),new Color(.48f,.18f,.23f,1));
        c.botonCambiar = Boton(c.barraBotones.transform,"Volver a escanear","OTRA CARTA",
            new Vector2(.517f,.06f),new Vector2(.955f,.128f),new Color(.30f,.27f,.46f,1));
        c.botonCorregirId = Boton(c.barraBotones.transform, "Corregir personaje por ID", "CORREGIR ID",
            new Vector2(.26f,.006f),new Vector2(.74f,.052f),new Color(.24f,.31f,.41f,1));
        // Se muestra solamente tras cinco segundos sin detectar ninguna carta.
        c.botonAccesoManual = Boton(canvasGo.transform,"Ayuda si no reconoce la carta",
            "¿NO RECONOCE? ELEGIR ID", new Vector2(.20f,.075f),new Vector2(.80f,.124f),
            new Color(.22f,.27f,.40f,.96f));
        c.botonAccesoManual.gameObject.SetActive(false);
        c.panelSeleccionId = Panel(canvasGo.transform, "Selector de ID (manual)",
            new Color(.018f,.029f,.047f,.985f),new Vector2(.07f,.28f),new Vector2(.93f,.75f),Vector2.zero,Vector2.zero);
        Texto(c.panelSeleccionId.transform,"Encabezado selector","ELEGIR PERSONAJE / ID",30,oro,
            TextAnchor.MiddleCenter,new Vector2(.04f,.82f),new Vector2(.96f,.96f));
        Texto(c.panelSeleccionId.transform,"Descripcion selector",
            "Usa esta opción solo si ARCore identifica mal una carta. Selección manual (no OCR).",
            22,gris,TextAnchor.MiddleCenter,new Vector2(.06f,.70f),new Vector2(.94f,.82f));
        c.botonesIds = new Button[4];
        string[] labelsId = {"048  FIRE DRAGON", "081  CHUPACABRAS", "132  DARK TIGERSHARK", "099  UBER JACKAL"};
        for(int k=0;k<4;k++)
        {
            float y=.54f-k*.155f;
            c.botonesIds[k] = Boton(c.panelSeleccionId.transform,"ID "+labelsId[k],labelsId[k],
                new Vector2(.085f,y),new Vector2(.915f,y+.14f),new Color(.10f,.22f,.32f,1));
        }
        c.botonCancelarId=Boton(c.panelSeleccionId.transform,"Cerrar selector","VOLVER",
            new Vector2(.24f,.015f),new Vector2(.76f,.095f),new Color(.43f,.18f,.24f,1));
        c.panelSeleccionId.SetActive(false);
        // Controles de vitrina: solo visibles si el reverso muestra un modelo y ninguna ficha tapa la vista.
        c.panelGiro = new GameObject("Vitrina - girar personaje", typeof(RectTransform));
        c.panelGiro.transform.SetParent(canvasGo.transform, false);
        var rotRt = c.panelGiro.GetComponent<RectTransform>();
        rotRt.anchorMin=Vector2.zero;rotRt.anchorMax=Vector2.one;
        rotRt.offsetMin=rotRt.offsetMax=Vector2.zero;
        var rotBg = Panel(c.panelGiro.transform, "Fondo giro", new Color(.035f,.07f,.115f,.88f),
            new Vector2(.11f,.223f),new Vector2(.89f,.296f),Vector2.zero,Vector2.zero);
        c.botonGiroIzq=Boton(rotBg.transform,"Girar hacia la izquierda", "GIRAR IZQ.",
            new Vector2(.015f,.09f),new Vector2(.235f,.91f),new Color(.13f,.28f,.38f,1));
        c.botonCentrar=Boton(rotBg.transform,"Restablecer vista", "CENTRAR",
            new Vector2(.245f,.09f),new Vector2(.48f,.91f),new Color(.42f,.31f,.18f,1));
        c.botonGiroDer=Boton(rotBg.transform,"Girar hacia la derecha", "GIRAR DER.",
            new Vector2(.49f,.09f),new Vector2(.72f,.91f),new Color(.13f,.28f,.38f,1));
        // Giro automático opcional: conserva la vitrina vertical y estable.
        c.botonGiroAuto=Boton(rotBg.transform,"Giro suave automatico", "AUTO OFF",
            new Vector2(.735f,.09f),new Vector2(.985f,.91f),new Color(.20f,.35f,.30f,1));
        c.textoGiroAuto=c.botonGiroAuto.GetComponentInChildren<Text>();
        c.panelFicha1.SetActive(false);
        c.panelFicha2.SetActive(false);
        c.barraBotones.SetActive(false);
        c.panelGiro.SetActive(false);
        if (c.boton1) c.boton1.gameObject.SetActive(false);
        if (c.boton2) c.boton2.gameObject.SetActive(false);
        if (c.botonCerrar) c.botonCerrar.gameObject.SetActive(false);
        if (c.botonCambiar) c.botonCambiar.gameObject.SetActive(false);
        if (c.botonCorregirId) c.botonCorregirId.gameObject.SetActive(false);

        var eventSys = new GameObject("EventSystem - pantalla tactil",typeof(EventSystem),
            typeof(InputSystemUIInputModule));
        var module = eventSys.GetComponent<InputSystemUIInputModule>();
        if (module != null) module.AssignDefaultActions();
    }

    [MenuItem("Invizimals V8 Corregida/4 - Abrir escena V8 Corregida")]
    public static void Abrir() { EditorSceneManager.OpenScene(Scene); }

    [MenuItem("Invizimals V8 Corregida/6 - Verificar componentes y modelos")]
    public static void ValidarV8Corregida()
    {
        if (!File.Exists(Scene)) { Debug.LogError("V8 CORREGIDA: aun no se ha generado la escena."); return; }
        EditorSceneManager.OpenScene(Scene);
        var gestor = UnityEngine.Object.FindFirstObjectByType<ARTrackedImageManager>(FindObjectsInactive.Include);
        var ctrl = UnityEngine.Object.FindFirstObjectByType<InvizimalsCardController>(FindObjectsInactive.Include);
        bool ok = gestor != null && gestor.enabled && gestor.referenceLibrary != null &&
            gestor.referenceLibrary.count == 9 && ctrl != null && ctrl.cartas != null &&
            ctrl.cartas.Length == 4 && ctrl.botonGiroIzq != null &&
            ctrl.botonGiroDer != null && ctrl.botonCentrar != null && ctrl.botonCambiar != null && ctrl.panelGiro != null &&
            ctrl.botonCorregirId != null && ctrl.panelSeleccionId != null && ctrl.botonGiroAuto != null &&
            ctrl.botonModo != null && ctrl.textoBotonModo != null &&
            ctrl.modoInicial == InvizimalsCardController.ModoPresentacion.Evaluacion;
        var nombresBiblioteca = new System.Collections.Generic.HashSet<string>();
        if (gestor != null && gestor.referenceLibrary != null)
            for (int i = 0; i < gestor.referenceLibrary.count; i++)
                nombresBiblioteca.Add(gestor.referenceLibrary[i].name);
        for (int i = 0; i < 4 && ctrl != null && ctrl.cartas != null && i < ctrl.cartas.Length; i++)
        {
            var carta = ctrl.cartas[i];
            var prefab = carta.modeloPrefab;
            ok &= prefab != null && prefab.transform.Find("03 Giro manual - vitrina") != null;
            ok &= !string.IsNullOrEmpty(carta.id) && !string.IsNullOrEmpty(carta.descripcion) &&
                carta.retrato != null && nombresBiblioteca.Contains(carta.reverso) &&
                nombresBiblioteca.Contains(carta.frente);
        }
        if (!ok)
            Debug.LogError("INVIZIMALS V8 CORREGIDA: validacion fallida. Reejecuta el menu 2 para regenerar escena y biblioteca.");
        else
            Debug.Log("INVIZIMALS: OK. 9 referencias, 4 prefabs, AR manager activado, modo EVALUACION + VITRINA y controles enlazados. Comprobar deteccion real en Android.");
    }

    [MenuItem("Invizimals V8 Corregida/5 - Generar APK Android")]
    public static void APK()
    {
        if (!File.Exists(Scene)) { Generar(); }
        EditorSceneManager.OpenScene(Scene);
        var gestorBuild = UnityEngine.Object.FindFirstObjectByType<ARTrackedImageManager>(FindObjectsInactive.Include);
        if (gestorBuild == null || gestorBuild.referenceLibrary == null || gestorBuild.referenceLibrary.count != 9)
        { Debug.LogError("INVIZIMALS V8 CORREGIDA: imagenes AR incompletas, no se generara APK."); return; }
        if (!gestorBuild.enabled)
        {
            gestorBuild.enabled = true;
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
        }
        if (EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)
        {
            bool ok=EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android,BuildTarget.Android);
            Debug.Log(ok ? "Android activado; ejecuta el menu APK nuevamente." : "Instala Android Build Support/SDK/NDK/OpenJDK desde Unity Hub.");
            return;
        }
        Directory.CreateDirectory("Builds");
        var opt=new BuildPlayerOptions {scenes=new[]{Scene},locationPathName="Builds/InvizimalsRA_V8_Corregida.apk",target=BuildTarget.Android,options=BuildOptions.None};
        var result=BuildPipeline.BuildPlayer(opt);
        Debug.Log("INVIZIMALS V8 CORREGIDA BUILD: "+result.summary.result+" | "+result.summary.outputPath);
    }
}
#endif
