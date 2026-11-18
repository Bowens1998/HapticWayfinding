using System.Collections.Generic;
using HapticWayfinding.Guidance;
using HapticWayfinding.Haptics;
using HapticWayfinding.Logging;
using HapticWayfinding.Rig;
using HapticWayfinding.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace HapticWayfinding.EditorTools
{
    /// <summary>
    /// Builds the realistic demo scene: a 14 x 14 m lab hall (concrete floor, painted walls,
    /// acoustic ceiling with LED panels, windows with daylight, door, desks, shelves, whiteboard),
    /// a glass booth in the middle where the participant stands (the "room" from the original video),
    /// and a jointed, walking mannequin as the target figure. Lighting is baked (indirect + area lights)
    /// with a realtime sun, light probes for the moving figure and a box-projected reflection probe.
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/HapticWayfinding/Scenes/HapticWayfinding.unity";
        const float Half = 7f;          // room interior half size (m)
        const float Height = 3.2f;      // floor to ceiling (m)
        const float WallT = 0.2f;       // wall thickness (m)
        const float BoothHalf = 1.5f;   // glass booth half size (m)
        const float BoothH = 2.3f;

        static int figureLayer, expLayer, ceilingLayer;
        static Transform envRoot, propsRoot;

        // materials
        static Material mFloor, mWall, mCeiling, mBaseboard, mAlu, mGlass, mWood, mDoorFrame, mLightPanel, mExit,
            mDeskLeg, mScreen, mPlastic, mFabricChair, mWhiteboard, mRed, mBlack, mOutdoorGround, mBuilding,
            mRubberMat, mRadiator, mShirt, mPants, mShoes, mSkin, mHair, mParticipant, mLine, mSill;

        static readonly StaticEditorFlags EnvFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                                                     StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccluderStatic |
                                                     StaticEditorFlags.OccludeeStatic;

        public static void Build(bool bakeLighting)
        {
            AssetFactory.ClearMeshCache();
            AssetFactory.GenerateTextures();
            CreateMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            figureLayer = LayerMask.NameToLayer(ProjectSetup.FigureLayerName);
            expLayer = LayerMask.NameToLayer(ProjectSetup.ExperimenterLayerName);
            ceilingLayer = LayerMask.NameToLayer(ProjectSetup.CeilingLayerName);

            envRoot = new GameObject("Environment").transform;
            propsRoot = new GameObject("Props").transform;
            propsRoot.SetParent(envRoot);

            var sun = BuildSkyAndSun();
            BuildShell();
            BuildWindows();
            BuildDoor();
            BuildCeilingLights();
            BuildGlassBooth();
            BuildFurniture();
            BuildExterior();
            BuildProbes();

            // ---- participant rig
            var rig = new GameObject("XR Rig");
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(rig.transform);
            camGo.transform.localPosition = new Vector3(0, 1.65f, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.Skybox;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camGo.AddComponent<AudioListener>();
            var hmd = camGo.AddComponent<HmdPoseDriver>();
            rig.AddComponent<DesktopSimulator>().head = camGo.transform;

            // ---- participant marker (map only)
            var marker = new GameObject("ParticipantMarker");
            var disc = Prim(PrimitiveType.Cylinder, "Body", marker.transform, Vector3.zero, new Vector3(0.45f, 0.01f, 0.45f), mParticipant, dynamicObject: true);
            Prim(PrimitiveType.Cube, "FacingDirection", marker.transform, new Vector3(0, 0.01f, 0.35f), new Vector3(0.12f, 0.02f, 0.3f), mParticipant, dynamicObject: true);
            SetLayerRecursive(marker, expLayer);
            marker.AddComponent<ParticipantMarker>().head = camGo.transform;

            // ---- target figure (walking mannequin)
            var figure = BuildMannequin(new Vector3(2.5f, 0f, 2.5f));
            var walker = figure.GetComponent<ProceduralWalker>();
            walker.faceTarget = camGo.transform;

            // ---- experimenter top-down camera
            var expCamGo = new GameObject("ExperimenterCamera");
            expCamGo.transform.position = new Vector3(0, 25f, 0);
            expCamGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var expCam = expCamGo.AddComponent<Camera>();
            expCam.orthographic = true;
            expCam.orthographicSize = 7.8f;
            expCam.stereoTargetEye = StereoTargetEyeMask.None;
            expCam.depth = 10;
            expCam.clearFlags = CameraClearFlags.SolidColor;
            expCam.backgroundColor = new Color(0.12f, 0.12f, 0.13f);
            expCam.cullingMask = ceilingLayer >= 0 ? ~(1 << ceilingLayer) : ~0;
            expCamGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;

            // ---- post processing
            var volGo = new GameObject("PostProcessVolume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = RenderSetup.LoadVolumeProfile();

            // ---- direction line (map only)
            var lineGo = new GameObject("DirectionLine") { layer = expLayer };
            var lr = lineGo.AddComponent<LineRenderer>();
            lr.widthMultiplier = 0.05f;
            lr.sharedMaterial = mLine;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;

            // ---- haptic system
            var sys = new GameObject("HapticSystem");
            var output = sys.AddComponent<HapticVestOutput>();
            var guide = sys.AddComponent<DirectionalHapticGuide>();
            guide.body = camGo.transform;
            guide.target = figure.transform;
            guide.output = output;
            sys.AddComponent<VestCalibration>().output = output;
            var view = sys.AddComponent<ViewController>();
            view.headsetCamera = cam;
            view.experimenterCamera = expCam;
            var logger = sys.AddComponent<SessionLogger>();
            var vis = sys.AddComponent<HapticVestVisualizer>();
            vis.output = output;
            vis.guide = guide;
            sys.AddComponent<ExperimenterHUD>().hmd = hmd;
            lineGo.AddComponent<DirectionLine>().guide = guide;

            var fc = figure.AddComponent<TargetFigureController>();
            fc.participant = camGo.transform;
            fc.guide = guide;
            fc.experimenterCamera = expCam;
            fc.faceMovement = true;

            logger.guide = guide;
            logger.output = output;
            logger.figure = fc;
            logger.view = view;

            ConfigureLighting(sun);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            if (bakeLighting)
            {
                Debug.Log("[HapticWayfinding] Baking lighting...");
                var t0 = System.DateTime.Now;
                bool ok = Lightmapping.Bake();
                Debug.Log($"[HapticWayfinding] Bake {(ok ? "finished" : "FAILED")} in {(System.DateTime.Now - t0).TotalSeconds:0} s");
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            }
            Debug.Log("[HapticWayfinding] Scene saved to " + ScenePath);
        }

        // ================================================================== materials
        static void CreateMaterials()
        {
            mFloor = AssetFactory.Lit("Floor_PolishedConcrete", new Color(0.92f, 0.92f, 0.92f), "concrete", 2f, 0.62f, 0f, 0.35f);
            mWall = AssetFactory.Lit("Wall_Paint", new Color(0.97f, 0.96f, 0.93f), "plaster", 1f, 0.18f, 0f, 0.25f);
            mCeiling = AssetFactory.Lit("Ceiling_AcousticTile", Color.white, "ceiling", 0.6f, 0.08f, 0f, 0.6f);
            mBaseboard = AssetFactory.Lit("Baseboard", new Color(0.18f, 0.18f, 0.19f), null, 1f, 0.35f);
            mAlu = AssetFactory.Lit("Aluminium_Anodized", new Color(0.72f, 0.73f, 0.75f), "brushed", 0.5f, 0.55f, 0.85f, 0.3f);
            mGlass = AssetFactory.Glass("Glass", new Color(0.55f, 0.66f, 0.68f), 0.07f);
            mWood = AssetFactory.Lit("Wood_Oak", Color.white, "wood", 1f, 0.42f, 0f, 0.5f);
            mDoorFrame = AssetFactory.Lit("DoorFrame_Metal", new Color(0.32f, 0.33f, 0.35f), null, 1f, 0.45f, 0.6f);
            mLightPanel = AssetFactory.Lit("LED_Panel", new Color(0.95f, 0.95f, 0.95f), null, 1f, 0.5f, 0f, 1f, new Color(1f, 0.98f, 0.95f) * 6f);
            mExit = AssetFactory.Lit("ExitSign", new Color(0.1f, 0.6f, 0.25f), null, 1f, 0.6f, 0f, 1f, new Color(0.15f, 1f, 0.35f) * 2.5f);
            mDeskLeg = AssetFactory.Lit("Metal_Black", new Color(0.08f, 0.08f, 0.09f), null, 1f, 0.45f, 0.7f);
            mScreen = AssetFactory.Lit("Monitor_Screen", new Color(0.02f, 0.02f, 0.025f), null, 1f, 0.92f, 0f, 1f, new Color(0.05f, 0.08f, 0.12f) * 0.6f);
            mPlastic = AssetFactory.Lit("Plastic_Dark", new Color(0.06f, 0.06f, 0.065f), null, 1f, 0.4f);
            mFabricChair = AssetFactory.Lit("Fabric_Chair", new Color(0.16f, 0.17f, 0.19f), "fabric", 0.1f, 0.15f, 0f, 0.6f);
            mWhiteboard = AssetFactory.Lit("Whiteboard", new Color(0.97f, 0.97f, 0.97f), null, 1f, 0.88f);
            mRed = AssetFactory.Lit("Red_Paint", new Color(0.7f, 0.05f, 0.04f), null, 1f, 0.6f);
            mBlack = AssetFactory.Lit("Black_Matte", new Color(0.03f, 0.03f, 0.03f), null, 1f, 0.25f);
            mOutdoorGround = AssetFactory.Lit("Outdoor_Pavement", new Color(0.55f, 0.55f, 0.52f), "concrete", 4f, 0.25f, 0f, 0.6f);
            mBuilding = AssetFactory.Lit("Building_Facade", new Color(0.78f, 0.74f, 0.68f), "plaster", 3f, 0.2f);
            mRubberMat = AssetFactory.Lit("Rubber_Mat", new Color(0.09f, 0.09f, 0.1f), "fabric", 0.05f, 0.2f, 0f, 0.4f);
            mRadiator = AssetFactory.Lit("Radiator_White", new Color(0.92f, 0.92f, 0.9f), null, 1f, 0.55f, 0.2f);
            mSill = AssetFactory.Lit("WindowSill", new Color(0.9f, 0.9f, 0.88f), null, 1f, 0.5f);
            mShirt = AssetFactory.Lit("Cloth_Shirt", new Color(0.27f, 0.36f, 0.5f), "fabric", 0.08f, 0.15f, 0f, 0.5f);
            mPants = AssetFactory.Lit("Cloth_Pants", new Color(0.13f, 0.13f, 0.15f), "fabric", 0.08f, 0.15f, 0f, 0.5f);
            mShoes = AssetFactory.Lit("Shoes_Leather", new Color(0.07f, 0.055f, 0.045f), null, 1f, 0.55f);
            mSkin = AssetFactory.Lit("Mannequin_Skin", new Color(0.8f, 0.66f, 0.56f), null, 1f, 0.38f);
            mHair = AssetFactory.Lit("Hair_Dark", new Color(0.06f, 0.045f, 0.035f), "fabric", 0.03f, 0.3f, 0f, 0.8f);
            mParticipant = AssetFactory.Lit("Participant_Marker", new Color(0.85f, 0.12f, 0.1f), null, 1f, 0.4f, 0f, 1f, new Color(0.6f, 0.05f, 0.03f));
            mLine = AssetFactory.Unlit("Line_Cyan", new Color(0.1f, 0.8f, 1f));
        }

        // ================================================================== helpers
        static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat, int layer = 0, float lightmapScale = 1f, bool forceLightmap = false)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.AddComponent<MeshFilter>().sharedMesh = AssetFactory.BoxMesh(size);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            // Thin trim (frames, rails, baseboards) gets too few lightmap texels -> overlapping charts.
            // Light them from probes instead; they still contribute bounce light and shadows to the bake.
            var dims = new[] { size.x, size.y, size.z };
            System.Array.Sort(dims);
            bool thin = !forceLightmap && dims[0] < 0.12f && dims[1] < 1.5f; // thin AND narrow (not floor / ceiling / walls)
            r.receiveGI = thin ? ReceiveGI.LightProbes : ReceiveGI.Lightmaps;
            var so = new SerializedObject(r);
            var p = so.FindProperty("m_ScaleInLightmap");
            if (p != null) { p.floatValue = lightmapScale; so.ApplyModifiedPropertiesWithoutUndo(); }
            GameObjectUtility.SetStaticEditorFlags(go, EnvFlags);
            return go;
        }

        /// <summary>Small prop from a Unity primitive: contributes to GI but is lit by light probes (no lightmap UVs needed).</summary>
        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat,
            Vector3? euler = null, bool dynamicObject = false, bool castShadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (!dynamicObject)
            {
                r.receiveGI = ReceiveGI.LightProbes;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
            }
            return go;
        }

        static void SetLayerRecursive(GameObject root, int layer)
        {
            if (layer < 0) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        struct Opening
        {
            public float center, width, bottom, top;
            public Opening(float c, float w, float b, float t) { center = c; width = w; bottom = b; top = t; }
        }

        /// <summary>Wall along X (alongX) or Z at a fixed coordinate, with rectangular openings.</summary>
        static void Wall(string name, bool alongX, float fixedCoord, List<Opening> openings)
        {
            var parent = new GameObject(name).transform;
            parent.SetParent(envRoot, false);
            float from = -Half - WallT, to = Half + WallT;
            openings.Sort((a, b) => a.center.CompareTo(b.center));
            float cursor = from;
            int i = 0;
            void Seg(float a, float b, float y0, float y1)
            {
                if (b - a < 0.001f || y1 - y0 < 0.001f) return;
                float mid = (a + b) * 0.5f, len = b - a, h = y1 - y0;
                Vector3 c = alongX ? new Vector3(mid, (y0 + y1) * 0.5f, fixedCoord) : new Vector3(fixedCoord, (y0 + y1) * 0.5f, mid);
                Vector3 s = alongX ? new Vector3(len, h, WallT) : new Vector3(WallT, h, len);
                Box($"Seg{i++}", parent, c, s, mWall);
            }
            foreach (var o in openings)
            {
                float a = o.center - o.width * 0.5f, b = o.center + o.width * 0.5f;
                Seg(cursor, a, 0f, Height);
                Seg(a, b, 0f, o.bottom);
                Seg(a, b, o.top, Height);
                cursor = b;
            }
            Seg(cursor, to, 0f, Height);
        }

        // ================================================================== shell
        static readonly float[] WindowZ = { -4f, 0f, 4f };
        const float WinW = 2.2f, WinSill = 0.85f, WinTop = 2.45f;
        const float DoorX = -4.5f, DoorW = 1.0f, DoorH = 2.15f;

        static void BuildShell()
        {
            Box("Floor", envRoot, new Vector3(0, -0.05f, 0), new Vector3(2 * Half + 2 * WallT, 0.1f, 2 * Half + 2 * WallT), mFloor, 0, 1f);
            Box("Ceiling", envRoot, new Vector3(0, Height + 0.05f, 0), new Vector3(2 * Half + 2 * WallT, 0.1f, 2 * Half + 2 * WallT), mCeiling, ceilingLayer, 0.5f);

            var east = new List<Opening>();
            foreach (float z in WindowZ) east.Add(new Opening(z, WinW, WinSill, WinTop));
            Wall("Wall_East", false, Half + WallT * 0.5f, east);
            Wall("Wall_West", false, -Half - WallT * 0.5f, new List<Opening>());
            Wall("Wall_North", true, Half + WallT * 0.5f, new List<Opening>());
            Wall("Wall_South", true, -Half - WallT * 0.5f, new List<Opening> { new Opening(DoorX, DoorW + 0.1f, 0f, DoorH + 0.05f) });

            // Baseboards
            var bb = new GameObject("Baseboards").transform;
            bb.SetParent(envRoot, false);
            const float bh = 0.1f, bt = 0.015f;
            Box("BB_N", bb, new Vector3(0, bh / 2, Half - bt / 2), new Vector3(2 * Half, bh, bt), mBaseboard, 0, 0.2f);
            Box("BB_W", bb, new Vector3(-Half + bt / 2, bh / 2, 0), new Vector3(bt, bh, 2 * Half), mBaseboard, 0, 0.2f);
            Box("BB_E", bb, new Vector3(Half - bt / 2, bh / 2, 0), new Vector3(bt, bh, 2 * Half), mBaseboard, 0, 0.2f);
            float doorL = DoorX - DoorW / 2 - 0.05f, doorR = DoorX + DoorW / 2 + 0.05f;
            Box("BB_S1", bb, new Vector3((-Half + doorL) / 2, bh / 2, -Half + bt / 2), new Vector3(doorL + Half, bh, bt), mBaseboard, 0, 0.2f);
            Box("BB_S2", bb, new Vector3((doorR + Half) / 2, bh / 2, -Half + bt / 2), new Vector3(Half - doorR, bh, bt), mBaseboard, 0, 0.2f);

            // Two structural columns
            foreach (var p in new[] { new Vector2(-3f, 3f), new Vector2(3f, -3f) })
                Box("Column", envRoot, new Vector3(p.x, Height / 2, p.y), new Vector3(0.45f, Height, 0.45f), mWall, 0, 0.6f);
        }

        static void BuildWindows()
        {
            var root = new GameObject("Windows").transform;
            root.SetParent(envRoot, false);
            float x = Half + WallT * 0.5f;
            const float f = 0.06f; // frame profile
            foreach (float z in WindowZ)
            {
                var w = new GameObject("Window").transform;
                w.SetParent(root, false);
                float h = WinTop - WinSill, mid = (WinTop + WinSill) / 2;
                Box("Head", w, new Vector3(x, WinTop - f / 2, z), new Vector3(0.1f, f, WinW), mAlu, 0, 0.2f);
                Box("Sill", w, new Vector3(x, WinSill + f / 2, z), new Vector3(0.1f, f, WinW), mAlu, 0, 0.2f);
                Box("JambL", w, new Vector3(x, mid, z - WinW / 2 + f / 2), new Vector3(0.1f, h, f), mAlu, 0, 0.2f);
                Box("JambR", w, new Vector3(x, mid, z + WinW / 2 - f / 2), new Vector3(0.1f, h, f), mAlu, 0, 0.2f);
                Box("Mullion", w, new Vector3(x, mid, z), new Vector3(0.08f, h, 0.05f), mAlu, 0, 0.2f);
                var glass = Prim(PrimitiveType.Cube, "Glass", w, new Vector3(x, mid, z), new Vector3(0.012f, h - 2 * f, WinW - 2 * f), mGlass, castShadows: false);
                GameObjectUtility.SetStaticEditorFlags(glass, StaticEditorFlags.BatchingStatic);
                Box("InteriorSill", w, new Vector3(Half - 0.1f, WinSill - 0.015f, z), new Vector3(0.28f, 0.03f, WinW + 0.1f), mSill, 0, 0.3f);
                // radiator under the window
                Box("Radiator", w, new Vector3(Half - 0.08f, 0.45f, z), new Vector3(0.08f, 0.5f, 1.6f), mRadiator, 0, 0.3f);
            }
        }

        static void BuildDoor()
        {
            var d = new GameObject("Door").transform;
            d.SetParent(envRoot, false);
            float z = -Half - WallT * 0.5f;
            const float f = 0.05f;
            Box("FrameL", d, new Vector3(DoorX - DoorW / 2 - f / 2 + 0.05f, DoorH / 2, z), new Vector3(f, DoorH, WallT + 0.02f), mDoorFrame, 0, 0.2f);
            Box("FrameR", d, new Vector3(DoorX + DoorW / 2 + f / 2 - 0.05f, DoorH / 2, z), new Vector3(f, DoorH, WallT + 0.02f), mDoorFrame, 0, 0.2f);
            Box("FrameTop", d, new Vector3(DoorX, DoorH + 0.025f, z), new Vector3(DoorW + 0.1f, f, WallT + 0.02f), mDoorFrame, 0, 0.2f);
            Box("Leaf", d, new Vector3(DoorX, (DoorH - 0.01f) / 2, z + 0.03f), new Vector3(DoorW - 0.02f, DoorH - 0.01f, 0.045f), mWood, 0, 0.6f, forceLightmap: true);
            // lever handle + escutcheon
            Prim(PrimitiveType.Cube, "Escutcheon", d, new Vector3(DoorX + 0.38f, 1.05f, z + 0.06f), new Vector3(0.04f, 0.18f, 0.01f), mAlu);
            Prim(PrimitiveType.Cube, "Lever", d, new Vector3(DoorX + 0.33f, 1.08f, z + 0.085f), new Vector3(0.13f, 0.02f, 0.02f), mAlu);
            Prim(PrimitiveType.Cube, "KickPlate", d, new Vector3(DoorX, 0.12f, z + 0.055f), new Vector3(DoorW - 0.06f, 0.22f, 0.003f), mAlu);
            // exit sign
            Box("ExitSign", d, new Vector3(DoorX, DoorH + 0.28f, -Half + 0.03f), new Vector3(0.36f, 0.15f, 0.04f), mExit, 0, 0.2f);
        }

        static void BuildCeilingLights()
        {
            var root = new GameObject("CeilingLights").transform;
            root.SetParent(envRoot, false);
            float[] grid = { -4.5f, -1.5f, 1.5f, 4.5f };
            foreach (float x in grid)
            {
                foreach (float z in grid)
                {
                    Box("LEDPanel", root, new Vector3(x, Height - 0.012f, z), new Vector3(0.6f, 0.024f, 1.2f), mLightPanel, ceilingLayer, 0.2f);
                    var lgo = new GameObject("PanelLight");
                    lgo.transform.SetParent(root, false);
                    lgo.transform.localPosition = new Vector3(x, Height - 0.03f, z);
                    lgo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // emit downwards
                    var l = lgo.AddComponent<Light>();
                    l.type = LightType.Rectangle;
                    l.areaSize = new Vector2(0.6f, 1.2f);
                    l.lightmapBakeType = LightmapBakeType.Baked;
                    l.color = new Color(1f, 0.97f, 0.92f);
                    l.intensity = 3.2f;
                    l.range = 10f;
                }
            }
        }

        static void BuildGlassBooth()
        {
            var b = new GameObject("GlassBooth").transform;
            b.SetParent(envRoot, false);
            const float post = 0.05f;
            float[] pts = { -BoothHalf, 0f, BoothHalf };
            foreach (float px in pts)
                foreach (float pz in pts)
                {
                    bool edge = Mathf.Abs(px) > 0.01f || Mathf.Abs(pz) > 0.01f;
                    if (!edge) continue;
                    if (Mathf.Abs(px) < 0.01f && Mathf.Abs(pz) < BoothHalf - 0.01f) continue;
                    if (Mathf.Abs(pz) < 0.01f && Mathf.Abs(px) < BoothHalf - 0.01f) continue;
                    Box("Post", b, new Vector3(px, BoothH / 2, pz), new Vector3(post, BoothH, post), mAlu, 0, 0.2f);
                }
            float len = 2 * BoothHalf + post;
            foreach (int s in new[] { -1, 1 })
            {
                Box("TopRailX", b, new Vector3(0, BoothH - 0.025f, s * BoothHalf), new Vector3(len, 0.05f, post), mAlu, 0, 0.2f);
                Box("TopRailZ", b, new Vector3(s * BoothHalf, BoothH - 0.025f, 0), new Vector3(post, 0.05f, len), mAlu, 0, 0.2f);
                Box("BaseRailX", b, new Vector3(0, 0.04f, s * BoothHalf), new Vector3(len, 0.08f, post + 0.01f), mAlu, 0, 0.2f);
                Box("BaseRailZ", b, new Vector3(s * BoothHalf, 0.04f, 0), new Vector3(post + 0.01f, 0.08f, len), mAlu, 0, 0.2f);
                foreach (int k in new[] { -1, 1 })
                {
                    float c = k * BoothHalf / 2;
                    float gh = BoothH - 0.13f, gy = 0.08f + gh / 2;
                    var g1 = Prim(PrimitiveType.Cube, "Glass", b, new Vector3(c, gy, s * BoothHalf), new Vector3(BoothHalf - post, gh, 0.012f), mGlass, castShadows: false);
                    var g2 = Prim(PrimitiveType.Cube, "Glass", b, new Vector3(s * BoothHalf, gy, c), new Vector3(0.012f, gh, BoothHalf - post), mGlass, castShadows: false);
                    GameObjectUtility.SetStaticEditorFlags(g1, StaticEditorFlags.BatchingStatic);
                    GameObjectUtility.SetStaticEditorFlags(g2, StaticEditorFlags.BatchingStatic);
                }
            }
            // VR floor mat where the participant stands
            Box("VRMat", b, new Vector3(0, 0.006f, 0), new Vector3(1.4f, 0.012f, 1.4f), mRubberMat, 0, 0.5f);
        }

        // ================================================================== furniture
        static void BuildFurniture()
        {
            var rnd = new System.Random(7);
            // Desks with monitors along the west wall
            foreach (float z in new[] { -3.6f, -1.6f, 0.4f })
            {
                var desk = new GameObject("Workstation").transform;
                desk.SetParent(propsRoot, false);
                desk.localPosition = new Vector3(-Half + 0.4f, 0f, z);
                Box("Top", desk, new Vector3(0, 0.725f, 0), new Vector3(0.75f, 0.03f, 1.6f), mWood, 0, 0.4f);
                foreach (var lx in new[] { -0.33f, 0.33f })
                    foreach (var lz in new[] { -0.75f, 0.75f })
                        Prim(PrimitiveType.Cube, "Leg", desk, new Vector3(lx, 0.355f, lz), new Vector3(0.04f, 0.71f, 0.04f), mDeskLeg);
                Prim(PrimitiveType.Cube, "Modesty", desk, new Vector3(-0.3f, 0.5f, 0), new Vector3(0.015f, 0.35f, 1.45f), mDeskLeg);
                // monitor
                Prim(PrimitiveType.Cube, "MonitorBack", desk, new Vector3(-0.2f, 1.06f, 0), new Vector3(0.035f, 0.36f, 0.6f), mPlastic);
                Prim(PrimitiveType.Cube, "MonitorScreen", desk, new Vector3(-0.181f, 1.065f, 0), new Vector3(0.002f, 0.33f, 0.57f), mScreen);
                Prim(PrimitiveType.Cube, "MonitorNeck", desk, new Vector3(-0.23f, 0.83f, 0), new Vector3(0.03f, 0.2f, 0.06f), mPlastic);
                Prim(PrimitiveType.Cube, "MonitorBase", desk, new Vector3(-0.21f, 0.745f, 0), new Vector3(0.18f, 0.01f, 0.24f), mPlastic);
                Prim(PrimitiveType.Cube, "Keyboard", desk, new Vector3(0.08f, 0.748f, 0), new Vector3(0.15f, 0.018f, 0.44f), mPlastic);
                Prim(PrimitiveType.Cube, "Mouse", desk, new Vector3(0.1f, 0.75f, 0.33f), new Vector3(0.1f, 0.025f, 0.06f), mPlastic);
                // office chair
                var chair = new GameObject("Chair").transform;
                chair.SetParent(desk, false);
                chair.localPosition = new Vector3(0.75f, 0, (float)(rnd.NextDouble() - 0.5) * 0.3f);
                chair.localRotation = Quaternion.Euler(0, -90f + (float)(rnd.NextDouble() - 0.5) * 40f, 0);
                Prim(PrimitiveType.Cylinder, "Base", chair, new Vector3(0, 0.05f, 0), new Vector3(0.58f, 0.025f, 0.58f), mPlastic);
                Prim(PrimitiveType.Cylinder, "Lift", chair, new Vector3(0, 0.27f, 0), new Vector3(0.05f, 0.2f, 0.05f), mAlu);
                Prim(PrimitiveType.Cube, "Seat", chair, new Vector3(0, 0.48f, 0), new Vector3(0.48f, 0.07f, 0.48f), mFabricChair);
                Prim(PrimitiveType.Cube, "Back", chair, new Vector3(0, 0.82f, -0.23f), new Vector3(0.45f, 0.55f, 0.06f), mFabricChair, new Vector3(-8f, 0, 0));
                foreach (int s in new[] { -1, 1 })
                    Prim(PrimitiveType.Cube, "Armrest", chair, new Vector3(s * 0.26f, 0.66f, 0), new Vector3(0.04f, 0.03f, 0.3f), mPlastic);
            }
            // bin
            Prim(PrimitiveType.Cylinder, "Bin", propsRoot, new Vector3(-Half + 0.3f, 0.2f, 1.6f), new Vector3(0.3f, 0.2f, 0.3f), mPlastic);

            // Bookshelf on the north wall, filled with binders / books
            var shelf = new GameObject("Bookshelf").transform;
            shelf.SetParent(propsRoot, false);
            shelf.localPosition = new Vector3(4.2f, 0, Half - 0.2f);
            const float sw = 1.8f, sh = 2.0f, sd = 0.36f;
            Box("SideL", shelf, new Vector3(-sw / 2, sh / 2, 0), new Vector3(0.025f, sh, sd), mWood, 0, 0.3f);
            Box("SideR", shelf, new Vector3(sw / 2, sh / 2, 0), new Vector3(0.025f, sh, sd), mWood, 0, 0.3f);
            Box("Back", shelf, new Vector3(0, sh / 2, sd / 2 - 0.006f), new Vector3(sw, sh, 0.012f), mWood, 0, 0.3f);
            Color[] spines = { new Color(0.15f, 0.2f, 0.35f), new Color(0.45f, 0.12f, 0.1f), new Color(0.85f, 0.83f, 0.78f), new Color(0.1f, 0.3f, 0.2f), new Color(0.2f, 0.2f, 0.2f), new Color(0.6f, 0.5f, 0.3f) };
            var bookMats = new Material[spines.Length];
            for (int i = 0; i < spines.Length; i++) bookMats[i] = AssetFactory.Lit("Book_" + i, spines[i], null, 1f, 0.3f);
            for (int level = 0; level < 5; level++)
            {
                float y = 0.04f + level * 0.48f;
                Box("Shelf", shelf, new Vector3(0, y, 0), new Vector3(sw, 0.025f, sd), mWood, 0, 0.3f);
                if (level == 4) break;
                float x = -sw / 2 + 0.04f;
                while (x < sw / 2 - 0.12f)
                {
                    if (rnd.NextDouble() < 0.12) { x += 0.1f + (float)rnd.NextDouble() * 0.2f; continue; }
                    float bw = 0.025f + (float)rnd.NextDouble() * 0.05f;
                    float bh2 = 0.24f + (float)rnd.NextDouble() * 0.12f;
                    float bd = 0.2f + (float)rnd.NextDouble() * 0.08f;
                    Prim(PrimitiveType.Cube, "Book", shelf, new Vector3(x + bw / 2, y + 0.0125f + bh2 / 2, 0.02f), new Vector3(bw, bh2, bd), bookMats[rnd.Next(bookMats.Length)]);
                    x += bw + 0.002f;
                }
            }

            // Whiteboard on the north wall
            var wb = new GameObject("Whiteboard").transform;
            wb.SetParent(propsRoot, false);
            wb.localPosition = new Vector3(-1.2f, 1.45f, Half - 0.02f);
            Box("Board", wb, Vector3.zero, new Vector3(2.4f, 1.2f, 0.02f), mWhiteboard, 0, 0.6f, forceLightmap: true);
            Prim(PrimitiveType.Cube, "FrameTop", wb, new Vector3(0, 0.61f, -0.01f), new Vector3(2.42f, 0.025f, 0.03f), mAlu);
            Prim(PrimitiveType.Cube, "FrameBottom", wb, new Vector3(0, -0.61f, -0.01f), new Vector3(2.42f, 0.025f, 0.03f), mAlu);
            Prim(PrimitiveType.Cube, "FrameL", wb, new Vector3(-1.21f, 0, -0.01f), new Vector3(0.025f, 1.24f, 0.03f), mAlu);
            Prim(PrimitiveType.Cube, "FrameR", wb, new Vector3(1.21f, 0, -0.01f), new Vector3(0.025f, 1.24f, 0.03f), mAlu);
            Prim(PrimitiveType.Cube, "Tray", wb, new Vector3(0, -0.64f, -0.04f), new Vector3(0.8f, 0.02f, 0.07f), mAlu);
            Prim(PrimitiveType.Cylinder, "Marker", wb, new Vector3(-0.2f, -0.62f, -0.04f), new Vector3(0.018f, 0.06f, 0.018f), mBlack, new Vector3(0, 0, 90f));

            // Wall clock
            var clock = new GameObject("Clock").transform;
            clock.SetParent(propsRoot, false);
            clock.localPosition = new Vector3(1.6f, 2.45f, Half - 0.03f);
            Prim(PrimitiveType.Cylinder, "Rim", clock, Vector3.zero, new Vector3(0.32f, 0.02f, 0.32f), mBlack, new Vector3(90f, 0, 0));
            Prim(PrimitiveType.Cylinder, "Face", clock, new Vector3(0, 0, -0.021f), new Vector3(0.29f, 0.002f, 0.29f), mWhiteboard, new Vector3(90f, 0, 0));
            Prim(PrimitiveType.Cube, "HourHand", clock, new Vector3(0.03f, 0.02f, -0.025f), new Vector3(0.012f, 0.08f, 0.003f), mBlack, new Vector3(0, 0, -60f));
            Prim(PrimitiveType.Cube, "MinuteHand", clock, new Vector3(0f, -0.05f, -0.026f), new Vector3(0.008f, 0.11f, 0.003f), mBlack, new Vector3(0, 0, 175f));

            // Fire extinguisher + sign by the door
            var fe = new GameObject("FireExtinguisher").transform;
            fe.SetParent(propsRoot, false);
            fe.localPosition = new Vector3(DoorX + 1.2f, 0f, -Half + 0.12f);
            Prim(PrimitiveType.Cylinder, "Body", fe, new Vector3(0, 0.62f, 0), new Vector3(0.15f, 0.24f, 0.15f), mRed);
            Prim(PrimitiveType.Sphere, "Top", fe, new Vector3(0, 0.86f, 0), new Vector3(0.15f, 0.06f, 0.15f), mRed);
            Prim(PrimitiveType.Cube, "Valve", fe, new Vector3(0, 0.92f, 0), new Vector3(0.04f, 0.06f, 0.1f), mBlack);
            Prim(PrimitiveType.Cylinder, "Hose", fe, new Vector3(0.07f, 0.72f, 0), new Vector3(0.02f, 0.18f, 0.02f), mBlack);
            Prim(PrimitiveType.Cube, "Sign", fe, new Vector3(0, 1.35f, -0.1f), new Vector3(0.2f, 0.2f, 0.005f), mRed);

            // SteamVR base stations high in two corners (as in a real VR lab)
            foreach (var p in new[] { new Vector3(-Half + 0.12f, 2.7f, -Half + 0.12f), new Vector3(Half - 0.12f, 2.7f, Half - 0.12f) })
            {
                float yaw = p.x < 0 ? 45f : -135f;
                Prim(PrimitiveType.Cube, "BaseStation", propsRoot, p, new Vector3(0.1f, 0.1f, 0.08f), mBlack, new Vector3(25f, yaw, 0));
            }
        }

        // ================================================================== mannequin
        static GameObject BuildMannequin(Vector3 position)
        {
            var root = new GameObject("TargetFigure");
            root.transform.position = position;
            var w = root.AddComponent<ProceduralWalker>();

            Transform J(string name, Transform parent, Vector3 local)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent, false);
                t.localPosition = local;
                return t;
            }
            void Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, Vector3? euler = null)
                => Prim(type, name, parent, pos, scale, m, euler, dynamicObject: true);

            var hips = J("Hips", root.transform, new Vector3(0, 0.95f, 0));
            Part(PrimitiveType.Sphere, "Pelvis", hips, new Vector3(0, -0.02f, 0), new Vector3(0.34f, 0.22f, 0.23f), mPants);

            Transform Leg(string side, float x, out Transform knee, out Transform ankle)
            {
                var hip = J(side + "Hip", hips, new Vector3(x, -0.05f, 0));
                Part(PrimitiveType.Capsule, "Thigh", hip, new Vector3(0, -0.215f, 0), new Vector3(0.15f, 0.235f, 0.15f), mPants);
                knee = J(side + "Knee", hip, new Vector3(0, -0.43f, 0));
                Part(PrimitiveType.Capsule, "Shin", knee, new Vector3(0, -0.205f, 0), new Vector3(0.115f, 0.225f, 0.115f), mPants);
                ankle = J(side + "Ankle", knee, new Vector3(0, -0.41f, 0));
                Part(PrimitiveType.Capsule, "Shoe", ankle, new Vector3(0, -0.035f, 0.045f), new Vector3(0.1f, 0.135f, 0.08f), mShoes, new Vector3(90f, 0, 0));
                return hip;
            }
            w.leftHip = Leg("Left", -0.095f, out w.leftKnee, out w.leftAnkle);
            w.rightHip = Leg("Right", 0.095f, out w.rightKnee, out w.rightAnkle);

            var spine = J("Spine", hips, new Vector3(0, 0.05f, 0));
            Part(PrimitiveType.Capsule, "Abdomen", spine, new Vector3(0, 0.12f, 0), new Vector3(0.3f, 0.17f, 0.2f), mShirt);
            Part(PrimitiveType.Capsule, "Chest", spine, new Vector3(0, 0.32f, 0.005f), new Vector3(0.37f, 0.19f, 0.23f), mShirt);
            Part(PrimitiveType.Capsule, "Shoulders", spine, new Vector3(0, 0.42f, 0), new Vector3(0.14f, 0.21f, 0.14f), mShirt, new Vector3(0, 0, 90f));
            var neck = J("Neck", spine, new Vector3(0, 0.49f, 0));
            Part(PrimitiveType.Cylinder, "NeckMesh", neck, new Vector3(0, 0.035f, 0), new Vector3(0.1f, 0.05f, 0.1f), mSkin);
            var head = J("Head", neck, new Vector3(0, 0.07f, 0));
            Part(PrimitiveType.Sphere, "Skull", head, new Vector3(0, 0.1f, 0.01f), new Vector3(0.185f, 0.23f, 0.21f), mSkin);
            Part(PrimitiveType.Sphere, "Hair", head, new Vector3(0, 0.145f, -0.008f), new Vector3(0.198f, 0.165f, 0.222f), mHair);
            Part(PrimitiveType.Sphere, "Nose", head, new Vector3(0, 0.085f, 0.112f), new Vector3(0.03f, 0.045f, 0.035f), mSkin);
            Part(PrimitiveType.Sphere, "EarL", head, new Vector3(-0.093f, 0.09f, 0f), new Vector3(0.02f, 0.055f, 0.035f), mSkin);
            Part(PrimitiveType.Sphere, "EarR", head, new Vector3(0.093f, 0.09f, 0f), new Vector3(0.02f, 0.055f, 0.035f), mSkin);

            Transform Arm(string side, float x, out Transform elbow)
            {
                var sh = J(side + "Shoulder", spine, new Vector3(x, 0.43f, 0));
                Part(PrimitiveType.Capsule, "UpperArm", sh, new Vector3(0, -0.14f, 0), new Vector3(0.1f, 0.165f, 0.1f), mShirt);
                elbow = J(side + "Elbow", sh, new Vector3(0, -0.29f, 0));
                Part(PrimitiveType.Capsule, "Forearm", elbow, new Vector3(0, -0.125f, 0), new Vector3(0.082f, 0.145f, 0.082f), mShirt);
                var hand = J(side + "Hand", elbow, new Vector3(0, -0.265f, 0));
                Part(PrimitiveType.Capsule, "HandMesh", hand, new Vector3(0, -0.06f, 0), new Vector3(0.075f, 0.085f, 0.035f), mSkin);
                return sh;
            }
            w.leftShoulder = Arm("Left", -0.2f, out w.leftElbow);
            w.rightShoulder = Arm("Right", 0.2f, out w.rightElbow);

            w.hips = hips;
            w.spine = spine;
            w.head = head;
            SetLayerRecursive(root, figureLayer);

            // Big disc under the figure so the experimenter can see / grab it on the map
            var icon = Prim(PrimitiveType.Cylinder, "MapIcon", root.transform, new Vector3(0, 0.02f, 0), new Vector3(0.6f, 0.005f, 0.6f), mLine, dynamicObject: true, castShadows: false);
            icon.layer = expLayer;
            return root;
        }

        // ================================================================== exterior & sky
        static Light BuildSkyAndSun()
        {
            var sky = new Material(Shader.Find("Skybox/Procedural")) { name = "HW_Sky" };
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetColor("_SkyTint", new Color(0.5f, 0.55f, 0.62f));
            sky.SetColor("_GroundColor", new Color(0.42f, 0.4f, 0.37f));
            sky.SetFloat("_Exposure", 1.15f);
            AssetDatabase.CreateAsset(sky, AssetFactory.Root + "/Materials/HW_Sky.mat");
            RenderSettings.skybox = sky;

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.color = new Color(1f, 0.95f, 0.87f);
            sun.intensity = 2.4f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.92f;
            // low morning sun from the east so it falls through the windows onto the floor
            sunGo.transform.rotation = Quaternion.Euler(28f, -105f, 0f);
            RenderSettings.sun = sun;
            return sun;
        }

        static void BuildExterior()
        {
            var ext = new GameObject("Exterior").transform;
            ext.SetParent(envRoot, false);
            void Outside(string name, Vector3 c, Vector3 s, Material m)
            {
                var go = new GameObject(name);
                go.transform.SetParent(ext, false);
                go.transform.localPosition = c;
                go.AddComponent<MeshFilter>().sharedMesh = AssetFactory.BoxMesh(s);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.receiveGI = ReceiveGI.LightProbes;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
            }
            Outside("Ground", new Vector3(0, -0.12f, 0), new Vector3(240f, 0.1f, 240f), mOutdoorGround);
            var rnd = new System.Random(3);
            for (int i = 0; i < 7; i++)
            {
                float z = -60f + i * 20f + (float)rnd.NextDouble() * 6f;
                float h = 9f + (float)rnd.NextDouble() * 16f;
                Outside("Building", new Vector3(45f + (float)rnd.NextDouble() * 15f, h / 2, z), new Vector3(16f, h, 14f), mBuilding);
            }
            Outside("BuildingWest", new Vector3(-40f, 8f, 0), new Vector3(16f, 16f, 60f), mBuilding);
        }

        static void BuildProbes()
        {
            var lpGo = new GameObject("LightProbes");
            lpGo.transform.SetParent(envRoot, false);
            var lp = lpGo.AddComponent<LightProbeGroup>();
            var pos = new List<Vector3>();
            for (float x = -Half + 0.5f; x <= Half - 0.5f + 0.01f; x += 1.625f)
                for (float z = -Half + 0.5f; z <= Half - 0.5f + 0.01f; z += 1.625f)
                    foreach (float y in new[] { 0.25f, 1.2f, 2.4f })
                        pos.Add(new Vector3(x, y, z));
            lp.probePositions = pos.ToArray();

            var rpGo = new GameObject("ReflectionProbe");
            rpGo.transform.SetParent(envRoot, false);
            rpGo.transform.localPosition = new Vector3(0, 1.6f, 0);
            var rp = rpGo.AddComponent<ReflectionProbe>();
            rp.mode = ReflectionProbeMode.Baked;
            rp.boxProjection = true;
            rp.size = new Vector3(2 * Half, Height, 2 * Half);
            rp.center = new Vector3(0, 0, 0);
            rp.resolution = 256;
            rp.intensity = 1f;
        }

        static void ConfigureLighting(Light sun)
        {
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.fog = false;

            const string path = "Assets/HapticWayfinding/Rendering/HW_Lighting.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (ls == null)
            {
                ls = new LightingSettings { name = "HW_Lighting" };
                AssetDatabase.CreateAsset(ls, path);
            }
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.lightmapResolution = 14f;
            ls.lightmapPadding = 4;
            ls.lightmapMaxSize = 2048;
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 256;
            ls.environmentSampleCount = 256;
            ls.maxBounces = 3;
            ls.ao = true;
            ls.aoMaxDistance = 0.6f;
            ls.directionalityMode = LightmapsMode.NonDirectional;
            ls.lightmapCompression = LightmapCompression.HighQuality;
            ls.filteringMode = LightingSettings.FilterMode.Auto;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;
        }
    }
}
