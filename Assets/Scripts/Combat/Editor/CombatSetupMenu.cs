using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace XR124.Combat.EditorTools
{
    // Menu Editor tạo dữ liệu mặc định theo GDD, chuyển scene sang VR và dựng luồng triệu hồi trên rig tương tác của scene.
    public static class CombatSetupMenu
    {
        private const string RootFolder = "Assets/Combat";
        private const string DataFolder = "Assets/Combat/Data";

        private struct DefaultData
        {
            public ElementChart chart;
            public CombatRules rules;
            public PetDefinition zeru;
            public PetDefinition onea;
            public PetDefinition tiwo;
        }

        // Tạo (hoặc giữ nguyên nếu đã có) ElementChart, CombatRules và 3 PetDefinition trong Assets/Combat/Data.
        [MenuItem("XR124/Combat/Create Default Data", priority = 100)]
        public static void CreateDefaultDataMenu()
        {
            DefaultData data = EnsureDefaultData();
            Selection.activeObject = data.rules;
            EditorGUIUtility.PingObject(data.rules);
        }

        // Dựng toàn bộ luồng triệu hồi trong scene: đấu trường VR, đọc tay + ấn kí, kiếm, 2 pháp trận, pet người chơi (Zeru)
        // và quái AI (Onea) tự di chuyển/đánh thường, trận đấu và bảng debug. Pet ẩn cho tới khi cắm kiếm; mọi vị trí spawn đi qua ArenaPlacement.
        [MenuItem("XR124/Combat/Create Summon Battle Setup", priority = 102)]
        public static void CreateSummonBattleSetup()
        {
            DefaultData data = EnsureDefaultData();
            Collider arenaFloor = EnsureArena(out Transform arenaRoot);

            GameObject root = new GameObject("SummonBattle");
            Undo.RegisterCreatedObjectUndo(root, "Create Summon Battle Setup");
            ArenaPlacement placement = root.AddComponent<ArenaPlacement>();
            placement.Configure(arenaFloor, arenaRoot, ArenaRadius);

            // Hai tay: bộ đọc xương + nhận diện ấn kí; tự gán OVRSkeleton theo SkeletonType nếu tìm thấy trong scene.
            FindHandSkeletons(out OVRSkeleton leftSkeleton, out OVRSkeleton rightSkeleton);
            HandSealRecognizer leftSeal = CreateHand(root.transform, "LeftHandSeals", leftSkeleton);
            HandSealRecognizer rightSeal = CreateHand(root.transform, "RightHandSeals", rightSkeleton);

            // Pet người chơi là Zeru vì đây là model đã có đủ animation (idle/walk/run/atk/hurt/death/jump/roar...).
            PetCombatant player = CreatePet(root.transform, data.zeru, typeof(ZeruAbility), new Vector3(-0.5f, 0f, 2f));
            PetCombatant enemy = CreatePet(root.transform, data.onea, typeof(OneaAbility), new Vector3(0.5f, 0f, 2f));
            player.gameObject.AddComponent<PetAutoBattler>().Configure(placement);
            enemy.gameObject.AddComponent<PetAutoBattler>().Configure(placement);
            enemy.gameObject.AddComponent<PetCombatDummyAI>().Configure(enemy);

            CombatMatch match = root.AddComponent<CombatMatch>();
            match.Configure(data.rules, data.chart, player, enemy, false);

            SealComboCaster caster = root.AddComponent<SealComboCaster>();
            caster.Configure(leftSeal, rightSeal, player);

            SummonSword sword = CreateSword(root.transform, new Vector3(0.25f, 0.9f, 0.45f), placement, leftSeal, rightSeal);

            // Chỉ pet người chơi được triệu hồi qua pháp trận; quái địch tự xuất hiện nên không có cổng riêng.
            SummonPortal playerPortal = CreatePortal(root.transform, "PlayerPortal", PortalDiameter);

            BattleSummoner summoner = root.AddComponent<BattleSummoner>();
            summoner.Configure(placement, sword, match, caster, player, enemy, playerPortal);

            GameObject debugObject = new GameObject("BattleDebugLabel");
            debugObject.transform.SetParent(root.transform, false);
            debugObject.transform.localPosition = new Vector3(0.35f, 1.4f, 0.8f);
            TextMeshPro debugText = debugObject.AddComponent<TextMeshPro>();
            debugText.fontSize = 0.25f;
            debugText.alignment = TextAlignmentOptions.TopLeft;
            debugText.rectTransform.sizeDelta = new Vector2(0.6f, 0.4f);
            debugObject.AddComponent<BattleDebugLabel>().Configure(debugText, placement, sword, summoner, caster);

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;

            if (leftSkeleton == null || rightSkeleton == null)
            {
                Debug.LogWarning("[Combat] Không tìm thấy đủ OVRSkeleton trái/phải trong scene. Hãy gán Skeleton cho HandSkeletonReader trong LeftHandSeals/RightHandSeals.", root);
            }

            Debug.Log("[Combat] Đã tạo SummonBattle. Cầm kiếm bằng grip tay cầm hoặc nắm tay, đâm xuống sàn để triệu hồi. Tay cầm: A/B/X = ấn A/D/H, Y = Ultimate.", root);
        }

        // Chuyển scene từ MR sang VR: bỏ passthrough (OVRManager + OVRPassthroughLayer + nền camera trong suốt, theo
        // OVRPassthroughHelper của Meta SDK), bỏ các object MRUK, đặt nền camera là Skybox và dựng đấu trường ảo.
        [MenuItem("XR124/Combat/Convert Scene To VR Arena", priority = 101)]
        public static void ConvertSceneToVr()
        {
            OVRManager manager = UnityEngine.Object.FindFirstObjectByType<OVRManager>(FindObjectsInactive.Include);
            if (manager != null && manager.isInsightPassthroughEnabled)
            {
                Undo.RecordObject(manager, "Disable Passthrough");
                manager.isInsightPassthroughEnabled = false;
                EditorUtility.SetDirty(manager);
            }

            OVRPassthroughLayer[] layers = UnityEngine.Object.FindObjectsByType<OVRPassthroughLayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < layers.Length; i++)
            {
                GameObject owner = layers[i].gameObject;
                // Object riêng của building block thì xóa hẳn; nếu layer nằm trên rig thì chỉ gỡ component.
                if (owner.name.Contains("Passthrough"))
                {
                    Undo.DestroyObjectImmediate(owner);
                }
                else
                {
                    Undo.DestroyObjectImmediate(layers[i]);
                }
            }

            GameObject[] roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].name.StartsWith("[MRUK]"))
                {
                    Undo.DestroyObjectImmediate(roots[i]);
                }
            }

            OVRCameraRig rig = UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            Camera center = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor.GetComponent<Camera>() : null;
            if (center != null)
            {
                Undo.RecordObject(center, "Skybox background");
                center.clearFlags = CameraClearFlags.Skybox;
                center.backgroundColor = new Color(0.19f, 0.3f, 0.47f, 1f);
            }

            EnsureArena(out _);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[Combat] Đã chuyển scene sang VR: tắt passthrough, bỏ MRUK, camera dùng Skybox, dựng VRArena.");
        }

        // Ẩn mọi mesh tay cầm và luôn hiện bàn tay: bật Controller Driven Hand Poses = Natural (Capsense) trên OVRManager
        // để khi cầm tay cầm runtime vẫn cấp dữ liệu bàn tay (OVRSkeleton) với tư thế ngón tự nhiên; tắt hình tay cầm
        // của rig ISDK (OVRControllerVisual*) và model tay cầm của building block Controller Tracking.
        [MenuItem("XR124/Combat/Use Hands Instead Of Controllers", priority = 103)]
        public static void UseHandsInsteadOfControllers()
        {
            OVRManager manager = UnityEngine.Object.FindFirstObjectByType<OVRManager>(FindObjectsInactive.Include);
            if (manager != null)
            {
                Undo.RecordObject(manager, "Controller driven hands");
                manager.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.Natural;
                EditorUtility.SetDirty(manager);
            }

            int hidden = 0;
            string[] visualNames = { "OVRControllerVisualLeft", "OVRControllerVisualRight" };
            foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (Array.IndexOf(visualNames, t.name) >= 0 && t.gameObject.activeSelf)
                {
                    Undo.RecordObject(t.gameObject, "Hide controller visual");
                    t.gameObject.SetActive(false);
                    hidden++;
                }
            }

            // Model tay cầm của building block Controller Tracking (OVRControllerHelper) chỉ để hiển thị.
            foreach (OVRControllerHelper helper in UnityEngine.Object.FindObjectsByType<OVRControllerHelper>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (helper.gameObject.activeSelf)
                {
                    Undo.RecordObject(helper.gameObject, "Hide controller model");
                    helper.gameObject.SetActive(false);
                    hidden++;
                }
            }

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log($"[Combat] Đã ẩn {hidden} hình tay cầm và bật tay điều khiển bằng tay cầm (Natural). Bàn tay luôn hiện để kết ấn.");
        }

        private const string ArenaModelPath = "Assets/Art/Arena/Arena.fbx";
        // Bán kính vùng đứng được bên trong lan can của model đấu trường (mép sàn 4.95 m).
        private const float ArenaModelWalkRadius = 4.9f;

        // Thay đấu trường tạm bằng model Arena.fbx dựng trong Blender: material URP theo tên (Arena_Stone/StoneShade/Blue/Gold),
        // MeshCollider cho mặt sàn "ArenaFloor" và nối vào ArenaPlacement. Phần kiến trúc còn lại không có collider.
        [MenuItem("XR124/Combat/Use Arena Model", priority = 104)]
        public static void UseArenaModel()
        {
            ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(ArenaModelPath);
            if (importer == null)
            {
                Debug.LogError($"[Combat] Không có {ArenaModelPath}.");
                return;
            }

            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            (string name, Color color, float smooth, float metal)[] palette =
            {
                ("Arena_Stone", new Color(0.86f, 0.88f, 0.92f), 0.3f, 0f),
                ("Arena_StoneShade", new Color(0.62f, 0.66f, 0.75f), 0.25f, 0f),
                ("Arena_Blue", new Color(0.28f, 0.58f, 0.95f), 0.5f, 0f),
                ("Arena_Gold", new Color(0.95f, 0.72f, 0.25f), 0.65f, 0.8f)
            };
            foreach (var entry in palette)
            {
                string path = $"{ArenaFolder}/{entry.name}.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = entry.name };
                    AssetDatabase.CreateAsset(material, path);
                }

                material.SetColor("_BaseColor", entry.color);
                material.SetFloat("_Smoothness", entry.smooth);
                material.SetFloat("_Metallic", entry.metal);
                EditorUtility.SetDirty(material);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.name), material);
            }

            importer.SaveAndReimport();

            GameObject root = GameObject.Find("VRArena");
            if (root == null)
            {
                root = new GameObject("VRArena");
                Undo.RegisterCreatedObjectUndo(root, "Create VR Arena");
            }

            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ArenaModelPath), root.transform);
            Undo.RegisterCreatedObjectUndo(model, "Arena model");
            model.name = "ArenaModel";
            Transform floorTransform = FindDeepChild(model.transform, "ArenaFloor");
            if (floorTransform == null)
            {
                Debug.LogError("[Combat] Model đấu trường không có object ArenaFloor.");
                return;
            }

            // TryGetComponent thay cho "??" vì GetComponent trong Editor có thể trả null giả của Unity.
            if (!floorTransform.TryGetComponent(out MeshCollider floorCollider))
            {
                floorCollider = floorTransform.gameObject.AddComponent<MeshCollider>();
            }
            ArenaPlacement placement = UnityEngine.Object.FindFirstObjectByType<ArenaPlacement>(FindObjectsInactive.Include);
            if (placement != null)
            {
                Undo.RecordObject(placement, "Arena floor");
                placement.Configure(floorCollider, root.transform, ArenaModelWalkRadius);
                EditorUtility.SetDirty(placement);
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[Combat] Đã thay đấu trường bằng Arena.fbx (sàn có MeshCollider, bán kính đứng 4.9 m).", root);
        }

        // Tìm Transform con theo tên ở mọi cấp.
        private static Transform FindDeepChild(Transform parent, string childName)
        {
            if (parent.name == childName)
            {
                return parent;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindDeepChild(parent.GetChild(i), childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private const float ArenaRadius = 5f;
        private const string ArenaFolder = "Assets/Art/Arena";

        // Tìm hoặc tạo đấu trường VR: sàn tròn bán kính ArenaRadius có MeshCollider (dùng cho ArenaPlacement),
        // viền và vòng cột đá bên ngoài để người chơi thấy ranh giới. Trả về collider sàn.
        private static Collider EnsureArena(out Transform arenaRoot)
        {
            GameObject existing = GameObject.Find("VRArena");
            if (existing != null)
            {
                arenaRoot = existing.transform;
                Transform floorTransform = arenaRoot.Find("Floor");
                if (floorTransform != null && floorTransform.TryGetComponent(out Collider existingFloor))
                {
                    return existingFloor;
                }
            }

            EnsureFolder("Assets", "Art");
            EnsureFolder("Assets/Art", "Arena");
            Material floorMat = LoadOrCreateMaterial("Arena_Floor", new Color(0.36f, 0.33f, 0.3f), 0.15f);
            Material edgeMat = LoadOrCreateMaterial("Arena_Edge", new Color(0.55f, 0.42f, 0.25f), 0.3f);
            Material pillarMat = LoadOrCreateMaterial("Arena_Pillar", new Color(0.45f, 0.45f, 0.5f), 0.2f);

            GameObject arena = existing != null ? existing : new GameObject("VRArena");
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(arena, "Create VR Arena");
            }

            arenaRoot = arena.transform;

            // Sàn: cylinder dẹt; thay CapsuleCollider mặc định bằng MeshCollider để raycast đúng mặt phẳng tròn.
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Floor";
            floor.transform.SetParent(arenaRoot, false);
            floor.transform.localPosition = new Vector3(0f, -0.01f, 0f);
            floor.transform.localScale = new Vector3(ArenaRadius * 2f, 0.01f, ArenaRadius * 2f);
            UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
            MeshCollider floorCollider = floor.AddComponent<MeshCollider>();
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            // Viền đấu trường: đĩa rộng hơn một chút, thấp hơn sàn, không collider.
            GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            edge.name = "Edge";
            edge.transform.SetParent(arenaRoot, false);
            edge.transform.localPosition = new Vector3(0f, -0.03f, 0f);
            edge.transform.localScale = new Vector3(ArenaRadius * 2f + 0.6f, 0.02f, ArenaRadius * 2f + 0.6f);
            UnityEngine.Object.DestroyImmediate(edge.GetComponent<Collider>());
            edge.GetComponent<MeshRenderer>().sharedMaterial = edgeMat;

            // Vòng 12 cột đá bên ngoài bán kính đấu trường.
            const int pillarCount = 12;
            for (int i = 0; i < pillarCount; i++)
            {
                float angle = i * Mathf.PI * 2f / pillarCount;
                GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = $"Pillar_{i:00}";
                pillar.transform.SetParent(arenaRoot, false);
                pillar.transform.localPosition = new Vector3(Mathf.Cos(angle) * (ArenaRadius + 0.8f), 0.6f, Mathf.Sin(angle) * (ArenaRadius + 0.8f));
                pillar.transform.localScale = new Vector3(0.4f, 0.6f, 0.4f);
                pillar.GetComponent<MeshRenderer>().sharedMaterial = pillarMat;
            }

            return floorCollider;
        }

        // Nạp hoặc tạo material URP/Lit màu trơn trong thư mục Arena.
        private static Material LoadOrCreateMaterial(string materialName, Color color, float smoothness)
        {
            string path = $"{ArenaFolder}/{materialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = materialName };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // Tìm OVRSkeleton tay trái/phải trong scene theo SkeletonType (hỗ trợ cả HandLeft/Right và XRHandLeft/Right).
        private static void FindHandSkeletons(out OVRSkeleton left, out OVRSkeleton right)
        {
            left = null;
            right = null;
            OVRSkeleton[] skeletons = UnityEngine.Object.FindObjectsByType<OVRSkeleton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < skeletons.Length; i++)
            {
                OVRSkeleton.SkeletonType type = skeletons[i].GetSkeletonType();
                if (left == null && (type == OVRSkeleton.SkeletonType.HandLeft || type == OVRSkeleton.SkeletonType.XRHandLeft))
                {
                    left = skeletons[i];
                }
                else if (right == null && (type == OVRSkeleton.SkeletonType.HandRight || type == OVRSkeleton.SkeletonType.XRHandRight))
                {
                    right = skeletons[i];
                }
            }
        }

        // Tạo object một tay gồm HandSkeletonReader + HandSealRecognizer.
        private static HandSealRecognizer CreateHand(Transform parent, string objectName, OVRSkeleton skeleton)
        {
            GameObject handObject = new GameObject(objectName);
            handObject.transform.SetParent(parent, false);
            HandSkeletonReader reader = handObject.AddComponent<HandSkeletonReader>();
            reader.Configure(skeleton);
            HandSealRecognizer recognizer = handObject.AddComponent<HandSealRecognizer>();
            recognizer.Configure(reader);
            return recognizer;
        }

        // Tạo kiếm cầm được bằng rig Interaction SDK của scene: Rigidbody kinematic + BoxCollider trigger bao chuôi,
        // Grabbable (không ném khi thả), GrabInteractable cho tay cầm Touch và HandGrabInteractable cho tay.
        // Hình placeholder: gốc ở chắn kiếm, lưỡi hướng xuống (−Y), điểm Tip ở mũi.
        private static SummonSword CreateSword(Transform parent, Vector3 position, ArenaPlacement placement,
            HandSealRecognizer leftSeal, HandSealRecognizer rightSeal)
        {
            GameObject swordObject = new GameObject("SummonSword");
            swordObject.transform.SetParent(parent, false);
            swordObject.transform.localPosition = position;

            CreatePart(swordObject.transform, PrimitiveType.Cylinder, "Handle", new Vector3(0f, 0.07f, 0f), new Vector3(0.03f, 0.07f, 0.03f));
            CreatePart(swordObject.transform, PrimitiveType.Cube, "Guard", Vector3.zero, new Vector3(0.14f, 0.015f, 0.03f));
            CreatePart(swordObject.transform, PrimitiveType.Cube, "Blade", new Vector3(0f, -0.38f, 0f), new Vector3(0.04f, 0.75f, 0.008f));

            GameObject grip = new GameObject("Grip");
            grip.transform.SetParent(swordObject.transform, false);
            grip.transform.localPosition = new Vector3(0f, 0.07f, 0f);

            GameObject tip = new GameObject("Tip");
            tip.transform.SetParent(swordObject.transform, false);
            tip.transform.localPosition = new Vector3(0f, -0.76f, 0f);

            // Vật lý: kinematic để kiếm không rơi; collider trigger chỉ dùng để rig phát hiện cầm, không đẩy vật khác.
            Rigidbody body = swordObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            BoxCollider grabVolume = swordObject.AddComponent<BoxCollider>();
            grabVolume.isTrigger = true;
            grabVolume.center = new Vector3(0f, 0.07f, 0f);
            grabVolume.size = new Vector3(0.08f, 0.2f, 0.08f);

            Oculus.Interaction.Grabbable grabbable = swordObject.AddComponent<Oculus.Interaction.Grabbable>();
            grabbable.InjectOptionalTargetTransform(swordObject.transform);
            grabbable.InjectOptionalRigidbody(body);
            grabbable.InjectOptionalThrowWhenUnselected(false);
            grabbable.InjectOptionalKinematicWhileSelected(true);

            GameObject controllerGrab = new GameObject("ControllerGrabInteractable");
            controllerGrab.transform.SetParent(swordObject.transform, false);
            Oculus.Interaction.GrabInteractable grabInteractable = controllerGrab.AddComponent<Oculus.Interaction.GrabInteractable>();
            grabInteractable.InjectAllGrabInteractable(body);
            grabInteractable.InjectOptionalPointableElement(grabbable);

            GameObject handGrab = new GameObject("HandGrabInteractable");
            handGrab.transform.SetParent(swordObject.transform, false);
            Oculus.Interaction.HandGrab.HandGrabInteractable handGrabInteractable = handGrab.AddComponent<Oculus.Interaction.HandGrab.HandGrabInteractable>();
            handGrabInteractable.InjectRigidbody(body);
            handGrabInteractable.InjectOptionalPointableElement(grabbable);

            SummonSword sword = swordObject.AddComponent<SummonSword>();
            sword.Configure(placement, grabbable, new Behaviour[] { grabInteractable, handGrabInteractable }, leftSeal, rightSeal, tip.transform);
            return sword;
        }

        private const string MagicCircleShader = "XR124/MagicCircle";
        private const string MagicCircleMaterialPath = "Assets/Art/VFX/MagicCircle.mat";
        // Đường kính pháp trận (m): đủ rộng để thân pet lớn nhất (bán kính ~0.45 m) nằm gọn trong vòng.
        private const float PortalDiameter = 1.4f;

        // Tạo pháp trận ở trạng thái tắt: quad nằm phẳng dùng shader HLSL XR124/MagicCircle.
        private static SummonPortal CreatePortal(Transform parent, string objectName, float diameter)
        {
            GameObject portalObject = new GameObject(objectName);
            portalObject.transform.SetParent(parent, false);
            SummonPortal portal = portalObject.AddComponent<SummonPortal>();
            portal.Configure(CreateMagicCircleVisual(portalObject.transform, diameter));
            portalObject.SetActive(false);
            return portal;
        }

        // Quad xoay 90° quanh X để nằm ngửa trên sàn, gắn material MagicCircle; không đổ/nhận bóng vì là ánh sáng cộng.
        private static Transform CreateMagicCircleVisual(Transform parent, float diameter)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            visual.name = "MagicCircle";
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(parent, false);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visual.transform.localScale = new Vector3(diameter, diameter, 1f);
            MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = EnsureMagicCircleMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return visual.transform;
        }

        // Tạo (hoặc lấy lại) material dùng shader XR124/MagicCircle; báo lỗi nếu shader chưa biên dịch được.
        private static Material EnsureMagicCircleMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MagicCircleMaterialPath);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find(MagicCircleShader);
            if (shader == null)
            {
                Debug.LogError($"[Combat] Không tìm thấy shader {MagicCircleShader}; kiểm tra Assets/Art/VFX/MagicCircle.shader.");
                return null;
            }

            material = new Material(shader) { name = "MagicCircle" };
            AssetDatabase.CreateAsset(material, MagicCircleMaterialPath);
            return material;
        }

        // Nâng cấp scene đã dựng: thay đĩa xám của PlayerPortal bằng pháp trận shader, xóa EnemyPortal (địch tự xuất hiện)
        // và nối lại BattleSummoner. Không dựng lại pet/kiếm nên giữ nguyên art đã gán.
        [MenuItem("XR124/Combat/Upgrade Summon Portal (Magic Circle)", priority = 105)]
        public static void UpgradeSummonPortal()
        {
            BattleSummoner summoner = UnityEngine.Object.FindFirstObjectByType<BattleSummoner>(FindObjectsInactive.Include);
            if (summoner == null)
            {
                Debug.LogError("[Combat] Chưa có BattleSummoner trong scene; chạy Create Summon Battle Setup trước.");
                return;
            }

            SerializedObject so = new SerializedObject(summoner);
            SummonPortal portal = so.FindProperty("playerPortal").objectReferenceValue as SummonPortal;
            if (portal == null)
            {
                portal = CreatePortal(summoner.transform, "PlayerPortal", PortalDiameter);
            }
            else
            {
                // Xóa hình cũ (đĩa Cylinder hoặc pháp trận cũ) rồi tạo pháp trận mới.
                for (int i = portal.transform.childCount - 1; i >= 0; i--)
                {
                    Undo.DestroyObjectImmediate(portal.transform.GetChild(i).gameObject);
                }

                portal.Configure(CreateMagicCircleVisual(portal.transform, PortalDiameter));
                EditorUtility.SetDirty(portal);
            }

            Transform enemyPortal = summoner.transform.Find("EnemyPortal");
            if (enemyPortal != null)
            {
                Undo.DestroyObjectImmediate(enemyPortal.gameObject);
            }

            so.FindProperty("playerPortal").objectReferenceValue = portal;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(summoner.gameObject.scene);
            Debug.Log("[Combat] Đã thay pháp trận bằng shader MagicCircle và bỏ cổng của quái địch.");
        }

        // Tạo một khối primitive không có collider làm hình tạm.
        private static GameObject CreatePart(Transform parent, PrimitiveType type, string partName, Vector3 localPosition, Vector3 localScale)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = partName;
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            return part;
        }

        // Tạo một pet: PetCombatant + ability, ModelRoot chứa model từ PetDefinition, capsule tạm (ẩn khi có model) và HUD máu.
        private static PetCombatant CreatePet(Transform parent, PetDefinition definition, Type abilityType, Vector3 localPosition)
        {
            GameObject petObject = new GameObject(definition.displayName);
            petObject.transform.SetParent(parent, false);
            petObject.transform.localPosition = localPosition;

            GameObject modelRoot = new GameObject("ModelRoot");
            modelRoot.transform.SetParent(petObject.transform, false);

            GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            placeholder.name = "Placeholder";
            placeholder.transform.SetParent(petObject.transform, false);
            placeholder.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            placeholder.transform.localScale = new Vector3(0.3f, 0.25f, 0.3f);

            PetCombatant combatant = petObject.AddComponent<PetCombatant>();
            combatant.SetDefinition(definition);
            combatant.ConfigureVisuals(modelRoot.transform, placeholder);
            petObject.AddComponent(abilityType);

            // HUD: biểu tượng hệ + tên phía trên thanh máu.
            PetHudBuilder.Build(combatant, CombatArtSetup.EnsureElementIconLibrary());
            return combatant;
        }

        // Đảm bảo thư mục và toàn bộ asset mặc định tồn tại; asset đã có thì giữ nguyên để không ghi đè chỉnh sửa của người dùng.
        private static DefaultData EnsureDefaultData()
        {
            EnsureFolder("Assets", "Combat");
            EnsureFolder(RootFolder, "Data");

            DefaultData data = new DefaultData
            {
                chart = LoadOrCreate<ElementChart>("ElementChart", null),
                rules = LoadOrCreate<CombatRules>("CombatRules", null),
                zeru = LoadOrCreate<PetDefinition>("Pet_Zeru", pet =>
                {
                    // Chỉ số Zeru là đề xuất trong GDD mục 7.
                    pet.petId = "PET-002";
                    pet.displayName = "Zeru";
                    pet.primaryElement = ElementType.Grass;
                    pet.canChooseElement = true;
                    pet.maxHp = 1350f;
                    pet.attack = 150f;
                    pet.defense = 100f;
                    pet.speed = 105f;
                    pet.ultimateEnergyCost = 17;
                    pet.ultimateMaxCharges = 1;
                    // Đánh thường / di chuyển: đề xuất, chưa có trong sheet.
                    pet.moveSpeed = 0.8f;
                    pet.attackRange = 1.5f;
                    pet.attacksPerSecond = 1f;
                    pet.basicAttackRatio = 0.2f;
                    // Model Zeru dài ~1.0 m, cao ~0.7 m: bán kính 0.4 m bao gần hết thân khi kiểm tra chỗ đứng trên đấu trường.
                    pet.bodyRadius = 0.4f;
                    pet.bodyHeight = 0.75f;
                }),
                onea = LoadOrCreate<PetDefinition>("Pet_Onea", pet =>
                {
                    // Chỉ số và hệ Đất của Onea là đề xuất trong GDD mục 7.
                    pet.petId = "PET-003";
                    pet.displayName = "Onea";
                    pet.primaryElement = ElementType.Ground;
                    pet.maxHp = 1500f;
                    pet.attack = 140f;
                    pet.defense = 110f;
                    pet.speed = 95f;
                    pet.ultimateEnergyCost = 15;
                    pet.ultimateMaxCharges = 1;
                    // Đánh thường / di chuyển: đề xuất, chưa có trong sheet.
                    pet.moveSpeed = 0.7f;
                    pet.attackRange = 1.2f;
                    pet.attacksPerSecond = 0.9f;
                    pet.basicAttackRatio = 0.2f;
                    pet.bodyRadius = 0.35f;
                    pet.bodyHeight = 1f;
                }),
                tiwo = LoadOrCreate<PetDefinition>("Pet_Tiwo", pet =>
                {
                    // Chỉ số Tiwo lấy từ sheet gốc.
                    pet.petId = "PET-001";
                    pet.displayName = "Tiwo";
                    pet.primaryElement = ElementType.Fire;
                    pet.maxHp = 1280f;
                    pet.attack = 165f;
                    pet.defense = 88f;
                    pet.speed = 115f;
                    pet.ultimateEnergyCost = 9;
                    pet.ultimateMaxCharges = 2;
                    // Tầm đánh 1.8 m, 1.25 đòn/s và kích thước 0.85 m lấy từ sheet; tốc độ chạy và hệ số đánh thường là đề xuất.
                    pet.moveSpeed = 0.9f;
                    pet.attackRange = 1.8f;
                    pet.attacksPerSecond = 1.25f;
                    pet.basicAttackRatio = 0.2f;
                    pet.bodyRadius = 0.3f;
                    pet.bodyHeight = 0.85f;
                })
            };

            AssetDatabase.SaveAssets();
            return data;
        }

        // Nạp asset theo tên trong DataFolder; nếu chưa có thì tạo mới, chạy hàm khởi tạo giá trị rồi lưu.
        private static T LoadOrCreate<T>(string assetName, Action<T> initialize) where T : ScriptableObject
        {
            string path = $"{DataFolder}/{assetName}.asset";
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            initialize?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // Tạo thư mục con nếu chưa tồn tại.
        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
