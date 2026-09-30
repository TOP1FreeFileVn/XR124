using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace XR124.Combat.EditorTools
{
    // Dựng art cho Zeru và kiếm katana: cấu hình import FBX, tạo Animator Controller, chuyển material sang URP,
    // gắn model vào PetDefinition và thay hình tạm của SummonSword trong scene.
    public static class CombatArtSetup
    {
        private const string ZeruFbx = "Assets/Art/Pets/Zeru/Zeru.fbx";
        private const string ZeruController = "Assets/Art/Pets/Zeru/Zeru.controller";
        private const string ZeruMaterial = "Assets/Art/Pets/Zeru/Zeru_Body.mat";
        private const string ZeruTexture = "Assets/Art/Pets/Zeru/Zeru_Albedo.jpg";
        // Chó nhỏ: model Rigify cũ (đã dọn còn 174 xương, 12 clip) với texture riêng; dùng làm quái Onea.
        private const string DogSmallFbx = "Assets/Art/Pets/DogSmall/DogSmall.fbx";
        private const string DogSmallController = "Assets/Art/Pets/DogSmall/DogSmall.controller";
        private const string DogSmallMaterial = "Assets/Art/Pets/DogSmall/DogSmall_Body.mat";
        private const string DogSmallTexture = "Assets/Art/Pets/DogSmall/DogSmall_Albedo.png";
        // Rồng lửa: model Tripo đã giảm còn 45k tam giác, rig 29 xương tự dựng + 11 clip cùng tên với Zeru; dùng làm Tiwo (hệ Lửa).
        private const string DragonFbx = "Assets/Art/Pets/Dragon/Dragon.fbx";
        private const string DragonController = "Assets/Art/Pets/Dragon/Dragon.controller";
        private const string DragonMaterial = "Assets/Art/Pets/Dragon/Dragon_Body.mat";
        private const string DragonTexture = "Assets/Art/Pets/Dragon/Dragon_Albedo.jpg";
        // Normal map bake lại từ bản gốc 1,9 triệu mặt sang lưới 18k (tangent space khớp lưới thấp).
        private const string DragonNormal = "Assets/Art/Pets/Dragon/Dragon_Normal.png";
        // Mask URP: R = metallic, A = độ bóng (1 - roughness), gộp từ map metallic/roughness của Tripo.
        private const string DragonMetallicSmooth = "Assets/Art/Pets/Dragon/Dragon_MetallicSmooth.png";
        // Texture rồng nhiều chi tiết nhỏ (vảy, gai, hoa văn) nên nén ASTC 4x4 thay vì 6x6 như các pet khác.
        private const TextureImporterFormat DragonTextureFormat = TextureImporterFormat.ASTC_4x4;
        private const int PetTextureSize = 2048;
        private const string KatanaFbx = "Assets/Art/Weapons/Katana/Katana.fbx";
        private const string KatanaBladeMat = "Assets/Art/Weapons/Katana/Katana_Blade.mat";
        private const string KatanaHandleMat = "Assets/Art/Weapons/Katana/Katana_Handle.mat";
        private const string ZeruDefinition = "Assets/Combat/Data/Pet_Zeru.asset";
        private const string UrpLitShader = "Universal Render Pipeline/Lit";
        private const float KatanaLength = 0.95f;
        private const int QuestTextureSize = 1024;

        // falling lặp vì thời gian rơi do BattleSummoner quyết định, không cố định theo độ dài clip.
        private static readonly HashSet<string> LoopClips = new HashSet<string> { "idle", "walk", "walk_long", "run", "falling" };

        // Chạy toàn bộ các bước theo thứ tự; mỗi bước ghi log để debug khi có lỗi.
        [MenuItem("XR124/Combat/Setup Zeru & Katana Art", priority = 120)]
        public static void SetupAll()
        {
            // File được chép từ ngoài vào nên import trước để có importer/asset.
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(ZeruFbx, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(DogSmallFbx, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(KatanaFbx, ImportAssetOptions.ForceUpdate);

            Material zeruBody = EnsureTexturedMaterial(ZeruMaterial, ZeruTexture, "Zeru_Body", Color.white);
            float yaw = ConfigurePetModel(ZeruFbx, zeruBody);
            AnimatorController controller = BuildController(ZeruFbx, ZeruController);
            AssignZeruDefinition(controller, yaw);

            // Chó nhỏ chỉ dựng khi file đã có trong project.
            AnimatorController smallController = null;
            float smallYaw = 0f;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(DogSmallFbx) != null)
            {
                Material smallBody = EnsureTexturedMaterial(DogSmallMaterial, DogSmallTexture, "DogSmall_Body", Color.white);
                smallYaw = ConfigurePetModel(DogSmallFbx, smallBody);
                smallController = BuildController(DogSmallFbx, DogSmallController);
            }

            // Rồng chỉ dựng khi file đã có trong project.
            AnimatorController dragonController = null;
            float dragonYaw = 0f;
            AssetDatabase.ImportAsset(DragonFbx, ImportAssetOptions.ForceUpdate);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(DragonFbx) != null)
            {
                Material dragonBody = EnsureTexturedMaterial(DragonMaterial, DragonTexture, "Dragon_Body", Color.white, DragonTextureFormat);
                ApplyNormalMap(dragonBody, DragonNormal);
                ApplyMetallicSmoothness(dragonBody, DragonMetallicSmooth);
                dragonYaw = ConfigurePetModel(DragonFbx, dragonBody);
                dragonController = BuildController(DragonFbx, DragonController);
            }

            AssignMonsterVariants(controller, yaw, smallController, smallYaw, dragonController, dragonYaw);
            ConfigureKatana();
            ReplaceSwordVisual();
            EnsureSkybox();
            AttachHudToScenePets(EnsureElementIconLibrary());
            AssetDatabase.SaveAssets();
            Debug.Log("[Art] Hoàn tất dựng art: Zeru, quái theo hệ, katana, skybox, HUD.");
        }

        private const string SkyTexture = "Assets/Art/Sky/Sky_Arena.png";
        private const string SkyMaterial = "Assets/Art/Sky/Sky_Arena.mat";
        private const string IconFolder = "Assets/Art/UI/Elements";
        private const string IconLibraryPath = "Assets/Art/UI/ElementIconLibrary.asset";
        private const string MonsterFolder = "Assets/Art/Pets/Monsters";

        // Skybox panorama tự sinh: texture không mipmap (tránh đường nối ở cực), lặp ngang và kẹp dọc, nén ASTC cho Quest;
        // material Skybox/Panoramic gán vào RenderSettings và làm nguồn ánh sáng môi trường.
        private static void EnsureSkybox()
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(SkyTexture);
            if (importer == null)
            {
                Debug.LogWarning($"[Art] Không có {SkyTexture}.");
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.maxTextureSize = 4096;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 4096;
            android.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();

            Material material = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterial);
            if (material == null)
            {
                material = new Material(Shader.Find("Skybox/Panoramic")) { name = "Sky_Arena" };
                AssetDatabase.CreateAsset(material, SkyMaterial);
            }

            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SkyTexture));
            material.SetFloat("_Mapping", 1f);   // Latitude-Longitude
            material.SetFloat("_ImageType", 0f); // 360°
            material.SetFloat("_Exposure", 1.05f);
            EditorUtility.SetDirty(material);

            RenderSettings.skybox = material;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            DynamicGI.UpdateEnvironment();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        // Import 8 ảnh hệ thành Sprite và tạo/cập nhật ElementIconLibrary kèm màu nhận diện từng hệ.
        public static ElementIconLibrary EnsureElementIconLibrary()
        {
            (ElementType element, string file, Color color)[] table =
            {
                (ElementType.Fire, "Fire", new Color(1f, 0.45f, 0.2f)),
                (ElementType.Grass, "Grass", new Color(0.4f, 0.85f, 0.35f)),
                (ElementType.Water, "Water", new Color(0.3f, 0.65f, 1f)),
                (ElementType.Electric, "Electric", new Color(1f, 0.85f, 0.2f)),
                (ElementType.Ice, "Ice", new Color(0.6f, 0.9f, 1f)),
                (ElementType.Ground, "Ground", new Color(0.8f, 0.58f, 0.35f)),
                (ElementType.Normal, "Normal", new Color(0.85f, 0.85f, 0.85f)),
                (ElementType.Myth, "Myth", new Color(0.72f, 0.5f, 1f))
            };

            ElementIconLibrary.Entry[] entries = new ElementIconLibrary.Entry[table.Length];
            for (int i = 0; i < table.Length; i++)
            {
                string path = $"{IconFolder}/Element_{table[i].file}.png";
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer != null && importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = true;
                    importer.maxTextureSize = 256;
                    importer.SaveAndReimport();
                }

                entries[i] = new ElementIconLibrary.Entry
                {
                    element = table[i].element,
                    icon = AssetDatabase.LoadAssetAtPath<Sprite>(path),
                    color = table[i].color
                };
            }

            ElementIconLibrary library = AssetDatabase.LoadAssetAtPath<ElementIconLibrary>(IconLibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<ElementIconLibrary>();
                AssetDatabase.CreateAsset(library, IconLibraryPath);
            }

            library.SetEntries(entries);
            EditorUtility.SetDirty(library);
            return library;
        }

        // Onea = chó nhỏ (model + texture riêng). Tiwo = rồng lửa (model + rig + clip riêng).
        // Nếu chưa có chó nhỏ thì Onea tạm dùng mesh chó lớn nhuộm màu đất; chưa có rồng thì Tiwo tạm dùng mesh chó lớn nhuộm đỏ.
        private static void AssignMonsterVariants(RuntimeAnimatorController bigController, float bigYaw, RuntimeAnimatorController smallController, float smallYaw,
            RuntimeAnimatorController dragonController, float dragonYaw)
        {
            if (!AssetDatabase.IsValidFolder(MonsterFolder))
            {
                AssetDatabase.CreateFolder("Assets/Art/Pets", "Monsters");
            }

            if (smallController != null)
            {
                AssignVariant("Assets/Combat/Data/Pet_Onea.asset", DogSmallFbx, null, 1f, 0.4f, 0.75f, smallController, smallYaw);
            }
            else
            {
                Material earth = EnsureTexturedMaterial($"{MonsterFolder}/Onea_Body.mat", ZeruTexture, "Onea_Body", new Color(0.78f, 0.6f, 0.45f));
                AssignVariant("Assets/Combat/Data/Pet_Onea.asset", ZeruFbx, earth, 1.12f, 0.45f, 0.95f, bigController, bigYaw);
            }

            if (dragonController != null)
            {
                // Rồng cao ~0.9 m (tính cả cánh), thân rộng ~0.4 m.
                AssignVariant("Assets/Combat/Data/Pet_Tiwo.asset", DragonFbx, null, 1f, 0.4f, 0.9f, dragonController, dragonYaw);
            }
            else
            {
                Material fire = EnsureTexturedMaterial($"{MonsterFolder}/Tiwo_Body.mat", ZeruTexture, "Tiwo_Body", new Color(1f, 0.55f, 0.45f));
                AssignVariant("Assets/Combat/Data/Pet_Tiwo.asset", ZeruFbx, fire, 0.9f, 0.36f, 0.78f, bigController, bigYaw);
            }
        }

        // Gắn normal map (import dạng NormalMap, giới hạn 2048 + ASTC cho Quest) vào material URP/Lit và bật keyword _NORMALMAP.
        private static void ApplyNormalMap(Material material, string normalPath)
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            if (importer == null)
            {
                return;
            }

            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            if (importer.textureType != TextureImporterType.NormalMap || importer.maxTextureSize != PetTextureSize || !android.overridden
                || android.format != DragonTextureFormat)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.maxTextureSize = PetTextureSize;
                android.overridden = true;
                android.maxTextureSize = PetTextureSize;
                android.format = DragonTextureFormat;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }

            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
        }

        // Gắn map kim loại/độ bóng (R = metallic, A = smoothness; tuyến tính, không sRGB) vào URP/Lit.
        // _Smoothness = 1 vì URP nhân kênh A với giá trị này; không có map thì giữ nguyên material.
        private static void ApplyMetallicSmoothness(Material material, string maskPath)
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(maskPath);
            if (importer == null)
            {
                return;
            }

            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            if (importer.sRGBTexture || importer.maxTextureSize != PetTextureSize || !android.overridden || android.format != DragonTextureFormat)
            {
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.maxTextureSize = PetTextureSize;
                android.overridden = true;
                android.maxTextureSize = PetTextureSize;
                android.format = DragonTextureFormat;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }

            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath));
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material);
        }

        // Gán model + controller + material (null = giữ material của model) và kích thước thân cho một PetDefinition.
        private static void AssignVariant(string definitionPath, string modelPath, Material overrideMaterial, float scale, float radius, float height,
            RuntimeAnimatorController controller, float yaw)
        {
            PetDefinition definition = AssetDatabase.LoadAssetAtPath<PetDefinition>(definitionPath);
            if (definition == null)
            {
                return;
            }

            definition.modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            definition.animatorController = controller;
            definition.overrideMaterial = overrideMaterial;
            definition.modelYawOffset = yaw;
            definition.modelScale = scale;
            definition.bodyRadius = radius;
            definition.bodyHeight = height;
            EditorUtility.SetDirty(definition);
        }

        // Tạo/cập nhật material URP/Lit dùng texture albedo (nhân màu tint để làm biến thể theo hệ); texture giới hạn 2048 + ASTC cho Quest.
        // androidFormat: định dạng nén trên Quest (mặc định ASTC 6x6; rồng dùng 4x4 để giữ chi tiết vảy/gai).
        private static Material EnsureTexturedMaterial(string materialPath, string texturePath, string materialName, Color tint,
            TextureImporterFormat androidFormat = TextureImporterFormat.ASTC_6x6)
        {
            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            TextureImporter texImporter = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            if (texImporter != null)
            {
                TextureImporterPlatformSettings android = texImporter.GetPlatformTextureSettings("Android");
                if (texImporter.maxTextureSize != PetTextureSize || !android.overridden || android.format != androidFormat)
                {
                    texImporter.maxTextureSize = PetTextureSize;
                    android.overridden = true;
                    android.maxTextureSize = PetTextureSize;
                    android.format = androidFormat;
                    texImporter.SetPlatformTextureSettings(android);
                    texImporter.SaveAndReimport();
                }
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find(UrpLitShader)) { name = materialName };
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.shader = Shader.Find(UrpLitShader);
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", albedo != null ? tint : new Color(0.86f, 0.76f, 0.62f));
            material.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // Thay nhãn chữ cũ bằng HUD (biểu tượng hệ + tên + thanh máu) cho mọi pet trong scene.
        private static void AttachHudToScenePets(ElementIconLibrary icons)
        {
            PetCombatant[] pets = Object.FindObjectsByType<PetCombatant>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < pets.Length; i++)
            {
                PetHudBuilder.Build(pets[i], icons);
            }
        }

        // Cấu hình import một model pet: Generic rig, tên clip gọn, loop idle/walk/run, material URP. Đo hướng đầu trước khi bật
        // Optimize Game Objects (vì sau khi bật, cây xương bị ẩn khỏi prefab). Trả về góc xoay Y để mặt pet nhìn +Z.
        private static float ConfigurePetModel(string fbxPath, Material body)
        {
            ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            importer.optimizeGameObjects = false;
            importer.SaveAndReimport();

            // Gắn material URP cho mọi material nhúng trong FBX (tên material đổi theo từng bản xuất, ví dụ tripo_mat_*).
            Object[] embedded = AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath);
            for (int i = 0; i < embedded.Length; i++)
            {
                if (embedded[i] is Material sourceMaterial)
                {
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), sourceMaterial.name), body);
                }
            }

            importer.SaveAndReimport();

            float yaw = MeasureYaw(fbxPath);

            // Đổi tên take "ZeruArmature|idle" → "idle"; clip vận động lặp lại, clip hành động chạy một lần.
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                string clipName = clips[i].takeName;
                int bar = clipName.LastIndexOf('|');
                clipName = bar >= 0 ? clipName.Substring(bar + 1) : clipName;
                clips[i].name = clipName;
                clips[i].loopTime = LoopClips.Contains(clipName);
            }

            importer.clipAnimations = clips;
            // Chỉ giữ Transform cần thiết để Quest không phải cập nhật cả cây xương.
            importer.optimizeGameObjects = true;
            importer.SaveAndReimport();
            Debug.Log($"[Art] {System.IO.Path.GetFileNameWithoutExtension(fbxPath)}: {clips.Length} clip, xoay model {yaw:0}°.");
            return yaw;
        }

        // Đo hướng từ hông tới đầu trên mặt phẳng ngang, trả về góc xoay Y để hướng đó thành +Z.
        private static float MeasureYaw(string fbxPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                // Hỗ trợ rig Auto-Rig Pro (head.x, root.x), rig Rigify cũ (DEF-nose, DEF-pelvis.L) và rig rồng tự dựng (head, hips).
                Transform nose = FindDeep(instance.transform, "head.x") ?? FindDeep(instance.transform, "DEF-nose") ?? FindDeep(instance.transform, "head");
                Transform pelvis = FindDeep(instance.transform, "root.x") ?? FindDeep(instance.transform, "DEF-pelvis.L") ?? FindDeep(instance.transform, "hips");
                if (nose == null || pelvis == null)
                {
                    Debug.LogWarning("[Art] Không tìm thấy xương đầu/hông để đo hướng Zeru; giữ góc 0°.");
                    return 0f;
                }

                Vector3 facing = nose.position - pelvis.position;
                facing.y = 0f;
                // Góc cần xoay để vector facing trùng +Z.
                return -Vector3.SignedAngle(Vector3.forward, facing, Vector3.up);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // Tạo Animator Controller với tham số khớp PetAutoBattler: Speed, Attack, Cast, Ultimate, Hit, Die, Summon, Land.
        // Chuỗi Summon: start_jumping → jump → falling (lặp tới khi có Land) → landing → roar → idle.
        private static AnimatorController BuildController(string fbxPath, string controllerPath)
        {
            Dictionary<string, AnimationClip> clips = LoadClips(fbxPath);
            AssetDatabase.DeleteAsset(controllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Cast", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Ultimate", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Summon", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Land", AnimatorControllerParameterType.Trigger);
            // Cờ đã chết: chặn mọi chuyển trạng thái Any State khác để Death là trạng thái cuối (không bị Hit/Attack kéo về Idle).
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            AnimatorState idle = AddState(sm, "Idle", clips, "idle", new Vector3(300, 0));
            AnimatorState walk = AddState(sm, "Walk", clips, "walk", new Vector3(550, 0));
            AnimatorState run = AddState(sm, "Run", clips, "run", new Vector3(800, 0));
            AnimatorState attack = AddState(sm, "Attack", clips, "atk", new Vector3(300, 150));
            AnimatorState hurt = AddState(sm, "Hurt", clips, "hurt", new Vector3(550, 150));
            AnimatorState ultimate = AddState(sm, "UltimateRoar", clips, "roar", new Vector3(800, 150));
            AnimatorState death = AddState(sm, "Death", clips, "death", new Vector3(1050, 150));
            AnimatorState summonStart = AddState(sm, "SummonStart", clips, "start_jumping", new Vector3(300, 300));
            AnimatorState summonJump = AddState(sm, "SummonJump", clips, "jump", new Vector3(550, 300));
            AnimatorState summonFall = AddState(sm, "SummonFall", clips, "falling", new Vector3(800, 300));
            AnimatorState summonLand = AddState(sm, "SummonLand", clips, "landing", new Vector3(1050, 300));
            AnimatorState summonRoar = AddState(sm, "SummonRoar", clips, "roar", new Vector3(1300, 300));
            sm.defaultState = idle;

            // Di chuyển theo Speed (m/s): > 0.05 là đi, > 1.2 là chạy.
            AddCondition(idle, walk, AnimatorConditionMode.Greater, "Speed", 0.05f);
            AddCondition(walk, idle, AnimatorConditionMode.Less, "Speed", 0.05f);
            AddCondition(walk, run, AnimatorConditionMode.Greater, "Speed", 1.2f);
            AddCondition(run, walk, AnimatorConditionMode.Less, "Speed", 1.2f);

            // Hành động một lần: vào từ Any State bằng trigger, chạy hết clip rồi về Idle. Khi Dead = true chỉ còn Summon
            // (PetAutoBattler tắt Dead trước khi triệu hồi lại); Death không có đường ra nên pet nằm gục tới hết trận.
            AddAnyTrigger(sm, attack, "Attack", true);
            AddAnyTrigger(sm, attack, "Cast", true);
            AddAnyTrigger(sm, hurt, "Hit", true);
            AddAnyTrigger(sm, ultimate, "Ultimate", true);
            AddAnyTrigger(sm, death, "Die", false);
            AddAnyTrigger(sm, summonStart, "Summon", false);
            AddExit(attack, idle);
            AddExit(hurt, idle);
            AddExit(ultimate, idle);
            AddExit(summonStart, summonJump);
            AddExit(summonJump, summonFall);
            // Rơi cho tới khi chạm sàn; BattleSummoner bắn Land đúng lúc pet chạm đất.
            AnimatorStateTransition touchDown = summonFall.AddTransition(summonLand);
            touchDown.hasExitTime = false;
            touchDown.duration = 0.08f;
            touchDown.AddCondition(AnimatorConditionMode.If, 0f, "Land");
            AddExit(summonLand, summonRoar);
            AddExit(summonRoar, idle);

            EditorUtility.SetDirty(controller);
            Debug.Log($"[Art] Tạo {controllerPath} với {clips.Count} clip.");
            return controller;
        }

        // Nạp mọi AnimationClip (bỏ clip preview nội bộ) trong FBX theo tên.
        private static Dictionary<string, AnimationClip> LoadClips(string path)
        {
            Dictionary<string, AnimationClip> result = new Dictionary<string, AnimationClip>();
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    result[clip.name] = clip;
                }
            }

            return result;
        }

        // Thêm state với clip tương ứng; thiếu clip thì báo cảnh báo nhưng vẫn tạo state rỗng để controller không lỗi.
        private static AnimatorState AddState(AnimatorStateMachine sm, string stateName, Dictionary<string, AnimationClip> clips, string clipName, Vector3 position)
        {
            AnimatorState state = sm.AddState(stateName, position);
            if (clips.TryGetValue(clipName, out AnimationClip clip))
            {
                state.motion = clip;
            }
            else
            {
                Debug.LogWarning($"[Art] Thiếu clip '{clipName}' cho state {stateName}.");
            }

            return state;
        }

        // Chuyển trạng thái theo điều kiện số (Speed), không chờ hết clip.
        private static void AddCondition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, string parameter, float threshold)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = 0.15f;
            t.AddCondition(mode, threshold, parameter);
        }

        // Chuyển từ Any State bằng trigger; không cho tự chuyển về chính nó để trigger lặp không giật animation.
        // Chuyển từ Any State bằng trigger; requireAlive = true thì thêm điều kiện Dead == false để không kéo pet ra khỏi Death.
        private static void AddAnyTrigger(AnimatorStateMachine sm, AnimatorState to, string trigger, bool requireAlive)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(to);
            t.hasExitTime = false;
            t.duration = 0.1f;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            if (requireAlive)
            {
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            }
        }

        // Chuyển tiếp khi clip chạy xong (exit time gần 1).
        private static void AddExit(AnimatorState from, AnimatorState to)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = 0.95f;
            t.duration = 0.1f;
        }

        // Gán model, controller và góc xoay vào PetDefinition của Zeru.
        private static void AssignZeruDefinition(RuntimeAnimatorController controller, float yaw)
        {
            PetDefinition definition = AssetDatabase.LoadAssetAtPath<PetDefinition>(ZeruDefinition);
            if (definition == null)
            {
                Debug.LogWarning($"[Art] Không có {ZeruDefinition}; chạy 'Create Default Data' trước.");
                return;
            }

            definition.modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ZeruFbx);
            definition.animatorController = controller;
            definition.modelYawOffset = yaw;
            definition.modelScale = 1f;
            // Model Zeru bản mới dài ~1.06 m, cao ~0.81 m.
            definition.bodyRadius = 0.4f;
            definition.bodyHeight = 0.85f;
            EditorUtility.SetDirty(definition);
        }

        // Chuyển 2 material kiếm sang URP/Lit (giữ texture), lưỡi phát sáng xanh theo concept; giới hạn texture 1024 cho Quest.
        private static void ConfigureKatana()
        {
            ConvertToUrp(KatanaBladeMat, new Color(0.35f, 0.6f, 1f) * 1.5f);
            ConvertToUrp(KatanaHandleMat, Color.black);
            // UV của chuôi được chiếu từ ảnh concept: màu chuôi (xanh tím than, ngọc xanh) nằm trong ảnh concept,
            // còn normal_handle.1001.png chỉ là mặt nạ trắng/vàng/đỏ (đã kiểm tra bằng lấy mẫu UV), không phải màu.
            const string handleColor = "Assets/Art/Weapons/Katana/Katana_HandleColor.jpeg";
            Material handle = AssetDatabase.LoadAssetAtPath<Material>(KatanaHandleMat);
            Texture2D handleTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(handleColor);
            if (handle != null && handleTexture != null)
            {
                handle.SetTexture("_BaseMap", handleTexture);
                handle.SetColor("_BaseColor", Color.white);
                EditorUtility.SetDirty(handle);
            }

            LimitTexture("Assets/Art/Weapons/Katana/luoidao.1001.png");
            LimitTexture(handleColor);
        }

        // Đổi shader Standard (Built-in, hiện hồng trong URP) sang URP/Lit và chuyển _MainTex → _BaseMap.
        private static void ConvertToUrp(string path, Color emission)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Debug.LogWarning($"[Art] Không có material {path}.");
                return;
            }

            // Đồng bộ tên bên trong với tên file (file đã được đổi tên khi chép từ gói katana).
            material.name = System.IO.Path.GetFileNameWithoutExtension(path);
            // Chạy lại menu khi material đã là URP thì giữ _BaseMap hiện có; lần đầu thì lấy _MainTex của Standard.
            Texture albedo = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
            if (albedo == null && material.HasProperty("_MainTex"))
            {
                albedo = material.GetTexture("_MainTex");
            }
            material.shader = Shader.Find(UrpLitShader);
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", Color.white);
            if (emission.maxColorComponent > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", emission);
                material.SetTexture("_EmissionMap", albedo);
            }

            EditorUtility.SetDirty(material);
        }

        // Giới hạn kích thước texture 4096 → 1024 để tiết kiệm bộ nhớ và băng thông trên Quest.
        private static void LimitTexture(string path)
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null || importer.maxTextureSize == QuestTextureSize)
            {
                return;
            }

            importer.maxTextureSize = QuestTextureSize;
            importer.SaveAndReimport();
        }

        // Thay hình tạm Handle/Guard/Blade của SummonSword bằng model katana: xoay để lưỡi chỉ xuống −Y, chuôi ở gốc,
        // scale về KatanaLength; dời điểm Grip vào giữa chuôi và Tip ra mũi kiếm theo kích thước thật.
        private static void ReplaceSwordVisual()
        {
            SummonSword sword = Object.FindFirstObjectByType<SummonSword>(FindObjectsInactive.Include);
            GameObject katanaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(KatanaFbx);
            if (sword == null || katanaPrefab == null)
            {
                Debug.LogWarning("[Art] Không tìm thấy SummonSword trong scene hoặc Katana.fbx.");
                return;
            }

            Transform swordRoot = sword.transform;
            string[] placeholders = { "Handle", "Guard", "Blade", "KatanaModel" };
            for (int i = 0; i < placeholders.Length; i++)
            {
                Transform old = swordRoot.Find(placeholders[i]);
                if (old != null)
                {
                    Undo.DestroyObjectImmediate(old.gameObject);
                }
            }

            GameObject katana = (GameObject)PrefabUtility.InstantiatePrefab(katanaPrefab, swordRoot);
            katana.name = "KatanaModel";
            Undo.RegisterCreatedObjectUndo(katana, "Katana model");

            MeshFilter meshFilter = katana.GetComponentInChildren<MeshFilter>();
            MeshRenderer meshRenderer = katana.GetComponentInChildren<MeshRenderer>();
            Mesh mesh = meshFilter.sharedMesh;

            // Trục dài nhất của mesh là trục kiếm; phía xa pivot hơn là lưỡi (pivot nằm ở chắn kiếm).
            Bounds bounds = mesh.bounds;
            int axis = bounds.size.x >= bounds.size.y && bounds.size.x >= bounds.size.z ? 0 : bounds.size.y >= bounds.size.z ? 1 : 2;
            float min = bounds.min[axis];
            float max = bounds.max[axis];
            bool bladeTowardPositive = Mathf.Abs(max) >= Mathf.Abs(min);
            Vector3 bladeDirLocal = Vector3.zero;
            bladeDirLocal[axis] = bladeTowardPositive ? 1f : -1f;

            // Hướng lưỡi (trong không gian mesh, đã tính cả xoay của node con) phải trùng −Y của SummonSword.
            Transform meshTransform = meshFilter.transform;
            katana.transform.localPosition = Vector3.zero;
            katana.transform.localRotation = Quaternion.identity;
            katana.transform.localScale = Vector3.one;
            Vector3 bladeDirInSword = swordRoot.InverseTransformDirection(meshTransform.TransformDirection(bladeDirLocal)).normalized;
            katana.transform.localRotation = Quaternion.FromToRotation(bladeDirInSword, Vector3.down);

            // Đo chiều dài thật giữa hai đầu mesh trong không gian SummonSword (đã tính mọi scale/xoay của node FBX).
            Vector3 endMin = bounds.center;
            Vector3 endMax = bounds.center;
            endMin[axis] = min;
            endMax[axis] = max;
            float rawLength = (swordRoot.InverseTransformPoint(meshTransform.TransformPoint(endMax))
                               - swordRoot.InverseTransformPoint(meshTransform.TransformPoint(endMin))).magnitude;
            float scale = KatanaLength / Mathf.Max(0.0001f, rawLength);
            katana.transform.localScale = Vector3.one * scale;

            // Đặt lại pivot về gốc SummonSword, rồi đo chuôi/mũi thực tế sau khi xoay + scale.
            Vector3 pivotInSword = swordRoot.InverseTransformPoint(meshTransform.position);
            katana.transform.localPosition -= pivotInSword;
            float handleLength = Mathf.Min(Mathf.Abs(min), Mathf.Abs(max)) / (max - min) * KatanaLength;
            float bladeLength = KatanaLength - handleLength;

            Transform grip = swordRoot.Find("Grip");
            Transform tip = swordRoot.Find("Tip");
            if (grip != null) grip.localPosition = new Vector3(0f, handleLength * 0.5f, 0f);
            if (tip != null) tip.localPosition = new Vector3(0f, -bladeLength, 0f);

            // Vùng cầm (trigger cho Interaction SDK) bao đúng chuôi katana thật.
            if (swordRoot.TryGetComponent(out BoxCollider grabVolume))
            {
                grabVolume.center = new Vector3(0f, handleLength * 0.5f, 0f);
                grabVolume.size = new Vector3(0.07f, handleLength, 0.07f);
            }

            // Gán material theo khe: khe có submesh kéo dài về phía lưỡi xa hơn là lưỡi, còn lại là chuôi.
            Material blade = AssetDatabase.LoadAssetAtPath<Material>(KatanaBladeMat);
            Material handle = AssetDatabase.LoadAssetAtPath<Material>(KatanaHandleMat);
            Material[] slots = new Material[mesh.subMeshCount];
            int bladeSlot = 0;
            float bestReach = float.MinValue;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                Bounds sub = mesh.GetSubMesh(i).bounds;
                float reach = bladeTowardPositive ? sub.max[axis] : -sub.min[axis];
                if (reach > bestReach)
                {
                    bestReach = reach;
                    bladeSlot = i;
                }
            }

            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = i == bladeSlot ? blade : handle;
            }

            meshRenderer.sharedMaterials = slots;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            EditorUtility.SetDirty(sword.gameObject);
            Debug.Log($"[Art] Katana: trục {axis}, scale {scale:0.0000}, chuôi {handleLength:0.00} m, lưỡi {bladeLength:0.00} m, khe lưỡi = {bladeSlot}.");
        }

        // Tìm Transform theo tên trong toàn bộ cây con.
        private static Transform FindDeep(Transform root, string targetName)
        {
            if (root.name == targetName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), targetName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
