using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FPS.EditorTools
{
    /// <summary>
    /// 编辑器工具：菜单 FPS > 一键配置场景。
    /// 在当前打开的场景中补全 FPS 所需的物体、组件和引用（玩家组件、武器弹药与特效、
    /// 盾牌与装备切换、导航网格构建器、敌人、HUD、游戏管理器）。
    /// 最后会调用 FPSModelSwap，把枪、盾、敌人和场景换成 Assets/FPS/Models 下导入的模型。
    /// 可以重复执行：已经存在的物体和组件不会重复创建，数值参数不会被改动。
    /// 每次执行都会重新确认的只有：玩家 Tag、玩家 Health 不随死亡销毁、HUD 的三个数据来源引用。
    /// 本脚本只在编辑器中运行，不会进入最终的游戏包。
    /// </summary>
    public static class FPSSceneSetup
    {
        private const string MenuPath = "FPS/一键配置场景";
        private const string ShopMenuPath = "FPS/配置商城与陈列柜";
        private const string AmmoMenuPath = "FPS/放置弹药补给箱";
        private const string EnemyMaterialPath = "Assets/FPS/Materials/Enemy.mat";
        private const string ShieldMaterialPath = "Assets/FPS/Materials/Shield.mat";
        private const string AmmoCrateMaterialPath = "Assets/FPS/Materials/AmmoCrate.mat";
        private const string AmmoAccentMaterialPath = "Assets/FPS/Materials/AmmoCrateAccent.mat";
        private const string BlasterModelPath = "Assets/FPS/Models/BlasterKit/blaster-d.fbx";
        private const string BlasterMaterialPath = "Assets/FPS/Materials/Blaster.mat";
        private const int UILayer = 5;

        [MenuItem(MenuPath, true)]
        [MenuItem(ShopMenuPath, true)]
        [MenuItem(AmmoMenuPath, true)]
        private static bool CanSetupScene()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem(AmmoMenuPath)]
        private static void PlaceAmmoPickupsFromMenu()
        {
            PlayerMovement movement = FindInScene<PlayerMovement>();
            if (movement == null)
            {
                EditorUtility.DisplayDialog("FPS 弹药补给",
                    "场景中没有找到玩家物体，请先执行 FPS > 一键配置场景。", "确定");
                return;
            }

            Undo.SetCurrentGroupName("FPS 放置弹药补给箱");
            var log = new List<string>();
            int created = PlaceAmmoPickups(movement.transform, 3, log);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            string summary = created > 0
                ? "已放置弹药补给箱：\n\n- " + string.Join("\n- ", log) + "\n\n走近弹药箱即可拾取补给（不超过最大备弹上限），请按 Ctrl+S 保存场景。"
                : "没有成功放置弹药补给箱，请检查场景地面碰撞体。";
            Debug.Log("[FPS 弹药补给] " + summary);
            EditorUtility.DisplayDialog("FPS 弹药补给", summary, "确定");
        }

        [MenuItem(ShopMenuPath)]
        private static void SetupShopFromMenu()
        {
            var log = new List<string>();
            PlayerMovement movement = FindInScene<PlayerMovement>();
            if (movement == null)
            {
                EditorUtility.DisplayDialog("FPS 商城配置",
                    "场景中没有找到玩家物体，请先执行 FPS > 一键配置场景。", "确定");
                return;
            }

            Camera playerCamera = movement.GetComponentInChildren<Camera>(true);
            PlayerEquipment equipment = movement.GetComponent<PlayerEquipment>();
            GameManager gameManager = FindInScene<GameManager>();
            HUDController hud = FindInScene<HUDController>();
            if (playerCamera == null || equipment == null || gameManager == null || hud == null)
            {
                EditorUtility.DisplayDialog("FPS 商城配置",
                    "场景缺少玩家相机、装备切换、GameManager 或 HUD，请先执行 FPS > 一键配置场景。", "确定");
                return;
            }

            Undo.SetCurrentGroupName("FPS 配置商城与陈列柜");

            CoinManager coinManager = FindInScene<CoinManager>();
            if (coinManager == null)
            {
                coinManager = Ensure<CoinManager>(gameManager.gameObject, log);
            }

            SetRef(hud, "coinManager", coinManager);
            if (GetRef(hud, "coinCountText") == null || GetRef(hud, "coinGainText") == null)
            {
                CreateCoinHudElements(hud);
                log.Add("HUD：添加右上角金币总数与左下角击杀金币提示");
            }

            SetupShop(playerCamera, equipment, gameManager, coinManager, hud, log);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            string summary = log.Count > 0
                ? "已完成商城与陈列柜配置：\n\n- " + string.Join("\n- ", log) + "\n\n运行游戏后按 B 键即可打开商城，请按 Ctrl+S 保存场景。"
                : "商城与陈列柜已配置完成，运行游戏后按 B 键即可打开商城。";
            Debug.Log("[FPS 商城配置] " + summary);
            EditorUtility.DisplayDialog("FPS 商城配置", summary, "确定");
        }

        [MenuItem(MenuPath)]
        private static void SetupScene()
        {
            var log = new List<string>();

            // ---------- 玩家 ----------
            PlayerMovement movement = FindInScene<PlayerMovement>();
            if (movement == null)
            {
                EditorUtility.DisplayDialog("FPS 场景配置",
                    "场景中没有找到挂有 PlayerMovement 的玩家物体。\n请先按模块 2 的步骤创建 Player。", "确定");
                return;
            }

            GameObject player = movement.gameObject;
            Camera playerCamera = player.GetComponentInChildren<Camera>(true);
            if (playerCamera == null)
            {
                EditorUtility.DisplayDialog("FPS 场景配置",
                    "Player 下面没有找到相机。\n请先按模块 2 的步骤创建子物体 PlayerCamera。", "确定");
                return;
            }

            Undo.SetCurrentGroupName("FPS 一键配置场景");

            if (!player.CompareTag("Player"))
            {
                Undo.RecordObject(player, "Set Player Tag");
                player.tag = "Player";
                log.Add("Player：Tag 设为 Player");
            }

            PlayerInputReader input = Ensure<PlayerInputReader>(player, log);
            PlayerLook look = Ensure<PlayerLook>(player, log);
            if (GetRef(look, "cameraPivot") == null)
            {
                SetRef(look, "cameraPivot", playerCamera.transform);
                log.Add("PlayerLook：Camera Pivot 指向 " + playerCamera.name);
            }

            Health playerHealth = Ensure<Health>(player, log);
            SetBool(playerHealth, "destroyOnDeath", false);
            PlayerWeaponController weaponController = Ensure<PlayerWeaponController>(player, log);

            // ---------- 武器 ----------
            Weapon weapon = player.GetComponentInChildren<Weapon>(true);
            if (weapon == null)
            {
                weapon = CreateWeapon(playerCamera.transform);
                log.Add("创建武器物体 Weapon");
            }

            GameObject weaponObject = weapon.gameObject;
            Ensure<WeaponAmmo>(weaponObject, log);
            WeaponEffects effects = Ensure<WeaponEffects>(weaponObject, log);

            if (GetRef(effects, "muzzleLight") == null)
            {
                SetRef(effects, "muzzleLight", CreateMuzzleLight(weaponObject.transform));
                log.Add("创建枪口闪光灯 MuzzleFlash");
            }

            if (GetRef(effects, "audioSource") == null)
            {
                AudioSource source = weaponObject.GetComponent<AudioSource>();
                if (source == null)
                {
                    source = Undo.AddComponent<AudioSource>(weaponObject);
                    source.playOnAwake = false;
                    log.Add("Weapon：添加 AudioSource");
                }

                SetRef(effects, "audioSource", source);
            }

            // ---------- 盾牌与装备切换 ----------
            PlayerEquipment equipment = Ensure<PlayerEquipment>(player, log);
            PlayerShield shield = Ensure<PlayerShield>(player, log);

            GameObject shieldObject = GetRef(shield, "shieldObject") as GameObject;
            if (shieldObject == null)
            {
                Transform existing = playerCamera.transform.Find("Shield");
                if (existing != null)
                {
                    shieldObject = existing.gameObject;
                }
                else
                {
                    shieldObject = CreateShieldObject(playerCamera.transform);
                    log.Add("创建盾牌物体 Shield");
                }

                SetRef(shield, "shieldObject", shieldObject);
            }

            if (GetArraySize(equipment, "slots") == 0)
            {
                SetRefArray(equipment, "slots", weaponObject, shieldObject);
                log.Add("PlayerEquipment：装备槽设为 [Weapon, Shield]");
            }

            // ---------- 导航网格 ----------
            if (FindInScene<RuntimeNavMeshBaker>() == null)
            {
                var baker = new GameObject("NavMeshBaker");
                Undo.RegisterCreatedObjectUndo(baker, "Create NavMeshBaker");
                baker.AddComponent<RuntimeNavMeshBaker>();
                log.Add("创建 NavMeshBaker");
            }

            // ---------- 敌人 ----------
            if (FindInScene<EnemyAI>() == null)
            {
                int created = CreateEnemies(player.transform, 3);
                log.Add($"创建 {created} 个敌人（Enemies 下）");
            }

            // ---------- 游戏管理器 ----------
            GameManager gameManager = FindInScene<GameManager>();
            if (gameManager == null)
            {
                var managerObject = new GameObject("GameManager");
                Undo.RegisterCreatedObjectUndo(managerObject, "Create GameManager");
                gameManager = managerObject.AddComponent<GameManager>();

                SetRef(gameManager, "playerHealth", playerHealth);
                SetRef(gameManager, "playerInput", input);
                SetRefArray(gameManager, "disableOnGameEnd", movement, look, weaponController);
                log.Add("创建 GameManager");
            }

            // 游戏结束后不应再能切换装备
            if (AppendRefToArray(gameManager, "disableOnGameEnd", equipment))
            {
                log.Add("GameManager：结束时禁用的组件中加入 PlayerEquipment");
            }

            // ---------- 金币奖励 ----------
            CoinManager coinManager = FindInScene<CoinManager>();
            if (coinManager == null)
            {
                coinManager = Ensure<CoinManager>(gameManager.gameObject, log);
            }

            // ---------- HUD ----------
            HUDController hud = FindInScene<HUDController>();
            if (hud == null)
            {
                hud = CreateHud();
                log.Add("创建 HUD（准星、血条、弹药、敌人数量、金币、结算面板）");
            }

            SetRef(hud, "playerHealth", playerHealth);
            SetRef(hud, "weaponController", weaponController);
            SetRef(hud, "gameManager", gameManager);
            SetRef(hud, "playerShield", shield);
            SetRef(hud, "coinManager", coinManager);

            if (GetRef(hud, "shieldFill") == null)
            {
                CreateShieldBar(hud);
                log.Add("HUD：添加盾值条");
            }

            if (GetRef(hud, "coinCountText") == null || GetRef(hud, "coinGainText") == null)
            {
                CreateCoinHudElements(hud);
                log.Add("HUD：添加右上角金币总数与左下角击杀金币提示");
            }

            // ---------- 导入的模型 ----------
            // 把枪、盾、敌人和场景换成 Assets/FPS/Models 下导入的模型，并把角色摆到新场景里
            FPSModelSwap.ApplyImportedModels(log);

            // ---------- 商城、3D 陈列柜与可购买武器 Blaster ----------
            SetupShop(playerCamera, equipment, gameManager, coinManager, hud, log);

            // ---------- 带骨骼的敌人 ----------
            // Assets/FPS/Animations 下有带骨骼的模型、且场景里还没有带骨骼的敌人时，添加一个
            FPSRiggedEnemy.EnsureRiggedEnemy(log);

            // ---------- 弹药补给箱 ----------
            if (FindInScene<AmmoPickup>() == null)
            {
                PlaceAmmoPickups(player.transform, 3, log);
            }

            // ---------- 收尾 ----------
            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = player;

            string summary = log.Count > 0
                ? "已完成以下配置：\n\n- " + string.Join("\n- ", log) + "\n\n请按 Ctrl+S 保存场景。"
                : "场景已经配置完整，没有需要补充的内容。";

            Debug.Log("[FPS 场景配置] " + summary);
            EditorUtility.DisplayDialog("FPS 场景配置", summary, "确定");
        }

        // =====================================================================
        // 武器
        // =====================================================================

        private static Weapon CreateWeapon(Transform cameraTransform)
        {
            GameObject weaponObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            weaponObject.name = "Weapon";
            Undo.RegisterCreatedObjectUndo(weaponObject, "Create Weapon");

            weaponObject.transform.SetParent(cameraTransform, false);
            weaponObject.transform.localPosition = new Vector3(0.3f, -0.25f, 0.6f);
            weaponObject.transform.localRotation = Quaternion.identity;
            weaponObject.transform.localScale = new Vector3(0.1f, 0.1f, 0.5f);

            Collider collider = weaponObject.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            return weaponObject.AddComponent<Weapon>();
        }

        private static Light CreateMuzzleLight(Transform weaponTransform)
        {
            var lightObject = new GameObject("MuzzleFlash");
            Undo.RegisterCreatedObjectUndo(lightObject, "Create MuzzleFlash");

            lightObject.transform.SetParent(weaponTransform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0f, 0.7f);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.8f, 0.4f);
            light.intensity = 3f;
            light.range = 6f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
        }

        // =====================================================================
        // 盾牌
        // =====================================================================

        private static GameObject CreateShieldObject(Transform cameraTransform)
        {
            GameObject shieldObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shieldObject.name = "Shield";
            Undo.RegisterCreatedObjectUndo(shieldObject, "Create Shield");

            // 放在视野下半部分的正前方，不挡住准星
            shieldObject.transform.SetParent(cameraTransform, false);
            shieldObject.transform.localPosition = new Vector3(0f, -0.3f, 0.7f);
            shieldObject.transform.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            shieldObject.transform.localScale = new Vector3(0.9f, 0.5f, 0.04f);

            // 盾牌只是手持模型，保护效果由 PlayerShield 的盾值实现，不需要碰撞体
            Collider collider = shieldObject.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            Renderer renderer = shieldObject.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Material material = GetOrCreateShieldMaterial();
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }

            return shieldObject;
        }

        private static Material GetOrCreateShieldMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(ShieldMaterialPath);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                return null;
            }

            if (!AssetDatabase.IsValidFolder("Assets/FPS/Materials"))
            {
                AssetDatabase.CreateFolder("Assets/FPS", "Materials");
            }

            // 半透明蓝色，持盾时仍能看到前方
            material = new Material(shader) { color = new Color(0.25f, 0.6f, 1f, 0.35f) };
            MakeTransparent(material);

            AssetDatabase.CreateAsset(material, ShieldMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        /// <summary>把 URP Lit 材质切换为半透明（Alpha 混合）模式。</summary>
        internal static void MakeTransparent(Material material)
        {
            SetFloatIfExists(material, "_Surface", 1f);
            SetFloatIfExists(material, "_Blend", 0f);
            SetFloatIfExists(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetFloatIfExists(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetFloatIfExists(material, "_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            SetFloatIfExists(material, "_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetFloatIfExists(material, "_ZWrite", 0f);

            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static void SetFloatIfExists(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void CreateShieldBar(HUDController hud)
        {
            Transform root = hud.transform;

            // 放在血条正上方
            Image bar = CreateImage("ShieldBar", root, new Color(0f, 0f, 0f, 0.6f));
            Undo.RegisterCreatedObjectUndo(bar.gameObject, "Create ShieldBar");
            SetRect(bar.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(40f, 76f), new Vector2(320f, 20f));

            // 排在最前面，这样结算面板显示时能盖住它
            bar.transform.SetAsFirstSibling();

            CanvasGroup group = bar.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            Image fill = CreateImage("Fill", bar.transform, new Color(0.25f, 0.6f, 1f));
            Stretch(fill.rectTransform);

            TextMeshProUGUI text = CreateText("ShieldText", bar.transform, "30", 16f, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);

            SetRef(hud, "shieldFill", fill.rectTransform);
            SetRef(hud, "shieldText", text);
            SetRef(hud, "shieldGroup", group);
        }

        private static void CreateCoinHudElements(HUDController hud)
        {
            Transform root = hud.transform;

            if (GetRef(hud, "coinCountText") == null)
            {
                TextMeshProUGUI coinCount = CreateText(
                    "CoinCountText", root, "COINS: 100", 32f, TextAlignmentOptions.TopRight);
                Undo.RegisterCreatedObjectUndo(coinCount.gameObject, "Create CoinCountText");
                coinCount.color = new Color(1f, 0.85f, 0.2f);
                SetRect(coinCount.rectTransform, Vector2.one, Vector2.one, Vector2.one,
                    new Vector2(-40f, -80f), new Vector2(500f, 50f));
                coinCount.transform.SetAsFirstSibling();
                SetRef(hud, "coinCountText", coinCount);
            }

            if (GetRef(hud, "coinGainText") == null)
            {
                TextMeshProUGUI coinGain = CreateText(
                    "CoinGainText", root, string.Empty, 28f, TextAlignmentOptions.BottomLeft);
                Undo.RegisterCreatedObjectUndo(coinGain.gameObject, "Create CoinGainText");
                coinGain.color = new Color(1f, 0.88f, 0.25f);
                SetRect(coinGain.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero,
                    new Vector2(40f, 108f), new Vector2(360f, 40f));
                coinGain.transform.SetAsFirstSibling();
                SetRef(hud, "coinGainText", coinGain);
            }
        }

        // =====================================================================
        // 商城与陈列柜
        // =====================================================================

        private static void SetupShop(
            Camera playerCamera,
            PlayerEquipment equipment,
            GameManager gameManager,
            CoinManager coinManager,
            HUDController hud,
            List<string> log)
        {
            // 确保 Blaster 模型网格可读，以便自动判断枪口朝向
            if (AssetImporter.GetAtPath(BlasterModelPath) is ModelImporter blasterImporter && !blasterImporter.isReadable)
            {
                blasterImporter.isReadable = true;
                blasterImporter.SaveAndReimport();
            }

            GameObject blasterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlasterModelPath);
            Material blasterMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlasterMaterialPath);

            // ---------- 玩家可解锁新武器：Blaster ----------
            Weapon blaster = FindBlasterWeapon(playerCamera.transform);
            if (blaster == null)
            {
                blaster = CreateBlasterWeapon(playerCamera.transform, blasterPrefab, blasterMaterial, log);
                log.Add("创建商城武器 Blaster（购买后可选择替换当前武器或放入背包）");
            }
            else if (blaster.transform.Find("Model") == null && blasterPrefab != null)
            {
                FPSModelSwap.ApplyWeaponModel(blaster, blasterPrefab, blasterMaterial, log);
            }

            // 未购买的商城武器不占快捷栏，由 WeaponInventory 在购买/替换时动态管理
            if (RemoveRefFromArray(equipment, "slots", blaster.gameObject))
            {
                log.Add("PlayerEquipment：将 Blaster 移出初始快捷栏（购买后由背包/替换逻辑管理）");
            }

            blaster.gameObject.SetActive(false);

            // ---------- 武器背包与商城管理器 ----------
            WeaponInventory weaponInventory = FindInScene<WeaponInventory>();
            if (weaponInventory == null)
            {
                weaponInventory = Ensure<WeaponInventory>(gameManager.gameObject, log);
            }

            ShopManager shopManager = FindInScene<ShopManager>();
            if (shopManager == null)
            {
                shopManager = Ensure<ShopManager>(gameManager.gameObject, log);
            }

            PlayerWeaponController weaponController = equipment.GetComponent<PlayerWeaponController>();
            SetRef(weaponInventory, "playerEquipment", equipment);
            SetRef(weaponInventory, "weaponController", weaponController);
            SetRef(weaponInventory, "gameManager", gameManager);
            SetRef(weaponInventory, "shopManager", shopManager);

            SetRef(shopManager, "coinManager", coinManager);
            SetRef(shopManager, "playerEquipment", equipment);
            SetRef(shopManager, "weaponInventory", weaponInventory);
            SetRef(shopManager, "gameManager", gameManager);

            ConfigureDefaultBlasterShopItem(shopManager, blaster.gameObject, blasterPrefab, blasterMaterial, log);

            // ---------- 商城与背包 UI 及交互依赖 ----------
            Ensure<GraphicRaycaster>(hud.gameObject, log);
            ShopUI shopUI = Ensure<ShopUI>(hud.gameObject, log);
            SetRef(shopUI, "shopManager", shopManager);
            SetRef(shopUI, "hudController", hud);

            BackpackUI backpackUI = Ensure<BackpackUI>(hud.gameObject, log);
            SetRef(backpackUI, "inventory", weaponInventory);
            SetRef(backpackUI, "hudController", hud);

            if (FindInScene<EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");
                log.Add("创建 EventSystem（支持商城按钮点击和 3D 陈列柜拖拽旋转）");
            }
        }

        private static Weapon FindBlasterWeapon(Transform cameraTransform)
        {
            Transform existing = cameraTransform.Find("Blaster");
            return existing != null ? existing.GetComponent<Weapon>() : null;
        }

        private static Weapon CreateBlasterWeapon(
            Transform cameraTransform, GameObject modelPrefab, Material material, List<string> log)
        {
            var blasterObject = new GameObject("Blaster");
            Undo.RegisterCreatedObjectUndo(blasterObject, "Create Blaster");
            blasterObject.transform.SetParent(cameraTransform, false);
            blasterObject.transform.localPosition = new Vector3(0.3f, -0.25f, 0.6f);
            blasterObject.transform.localRotation = Quaternion.identity;
            blasterObject.transform.localScale = Vector3.one;

            Weapon weapon = blasterObject.AddComponent<Weapon>();
            SetFloat(weapon, "damage", 40f);
            SetFloat(weapon, "fireRate", 5f);
            SetFloat(weapon, "range", 120f);
            SetBool(weapon, "automatic", true);

            WeaponAmmo ammo = blasterObject.AddComponent<WeaponAmmo>();
            SetInt(ammo, "magazineSize", 20);
            SetInt(ammo, "startingReserve", 100);
            SetInt(ammo, "maxReserve", 100);
            SetFloat(ammo, "reloadTime", 1.2f);

            Light muzzle = CreateMuzzleLight(blasterObject.transform);
            muzzle.color = new Color(0.25f, 0.85f, 1f);
            muzzle.intensity = 4f;

            AudioSource source = blasterObject.AddComponent<AudioSource>();
            source.playOnAwake = false;

            WeaponEffects effects = blasterObject.AddComponent<WeaponEffects>();
            SetRef(effects, "muzzleLight", muzzle);
            SetRef(effects, "audioSource", source);

            if (modelPrefab != null)
            {
                FPSModelSwap.ApplyWeaponModel(weapon, modelPrefab, material, log);
            }

            return weapon;
        }

        private static void ConfigureDefaultBlasterShopItem(
            ShopManager shopManager, GameObject blasterObject, GameObject blasterPrefab, Material blasterMaterial,
            List<string> log)
        {
            var serialized = new SerializedObject(shopManager);
            SerializedProperty itemsProp = FindProperty(serialized, "items");
            if (itemsProp == null || !itemsProp.isArray)
            {
                return;
            }

            for (int i = 0; i < itemsProp.arraySize; i++)
            {
                SerializedProperty existing = itemsProp.GetArrayElementAtIndex(i);
                SerializedProperty eqProp = existing.FindPropertyRelative("equipmentObject");
                if (eqProp != null && eqProp.objectReferenceValue == blasterObject)
                {
                    return;
                }
            }

            int index = itemsProp.arraySize;
            itemsProp.arraySize = index + 1;
            SerializedProperty entry = itemsProp.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("id").stringValue = "pulse_blaster";
            entry.FindPropertyRelative("displayName").stringValue = "PULSE BLASTER";
            entry.FindPropertyRelative("description").stringValue =
                "High-impact sci-fi energy rifle. Press 1 / 2 to switch weapons.";
            entry.FindPropertyRelative("price").intValue = 80;
            entry.FindPropertyRelative("equipmentObject").objectReferenceValue = blasterObject;
            entry.FindPropertyRelative("displayModelPrefab").objectReferenceValue = blasterPrefab;
            entry.FindPropertyRelative("displayMaterial").objectReferenceValue = blasterMaterial;
            entry.FindPropertyRelative("displayRotationOffset").vector3Value = new Vector3(0f, 30f, 0f);
            entry.FindPropertyRelative("displayScale").floatValue = 1f;

            serialized.ApplyModifiedProperties();
            log.Add("商城商品列表：上架 PULSE BLASTER（80 金币，使用 blaster-d.fbx 3D 陈列）");
        }

        // =====================================================================
        // 弹药补给箱
        // =====================================================================

        private static int PlaceAmmoPickups(Transform player, int count, List<string> log)
        {
            GameObject group = GameObject.Find("AmmoPickups");
            if (group == null)
            {
                group = new GameObject("AmmoPickups");
                Undo.RegisterCreatedObjectUndo(group, "Create AmmoPickups");
            }

            Material bodyMat = GetOrCreateAmmoCrateMaterial();
            Material accentMat = GetOrCreateAmmoAccentMaterial();

            Physics.SyncTransforms();

            Vector3 forward = player.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            // 在玩家前方及站台沿线找空地放置弹药补给箱（优先避开墙体和障碍）
            var candidateOffsets = new[]
            {
                new Vector2(2.4f, 4.5f),
                new Vector2(-2.4f, 9.5f),
                new Vector2(2.2f, 15.5f),
                new Vector2(-2.2f, 4.5f),
                new Vector2(0f, 7.0f),
                new Vector2(0f, 13.0f),
                new Vector2(2.8f, 2.5f),
                new Vector2(-2.8f, 2.5f),
            };

            var placed = new List<Vector3>();
            foreach (AmmoPickup existing in FindAllInScene<AmmoPickup>())
            {
                placed.Add(existing.transform.position);
            }

            int created = 0;
            foreach (Vector2 offset in candidateOffsets)
            {
                if (created >= count)
                {
                    break;
                }

                Vector3 probe = player.position + right * offset.x + forward * offset.y;
                if (!TryFindAmmoSpot(probe, player, placed, out Vector3 groundPoint))
                {
                    continue;
                }

                created++;
                placed.Add(groundPoint);
                string crateName = UniqueChildName(group.transform, "AmmoCrate");
                CreateAmmoCrate(crateName, groundPoint, group.transform, bodyMat, accentMat);
                log.Add($"放置 {crateName}（位置 {groundPoint.x:F1}, {groundPoint.y:F1}, {groundPoint.z:F1}，+30 备弹，15 秒刷新）");
            }

            if (created == 0)
            {
                Vector3 fallback = player.position + forward * 3.5f;
                if (TryFindGround(fallback, player, out float groundY))
                {
                    fallback.y = groundY;
                }
                else
                {
                    fallback.y = player.position.y - 1f;
                }

                created = 1;
                string crateName = UniqueChildName(group.transform, "AmmoCrate");
                CreateAmmoCrate(crateName, fallback, group.transform, bodyMat, accentMat);
                log.Add($"放置 {crateName} 在玩家前方（+30 备弹，15 秒刷新）");
            }

            return created;
        }

        private static bool TryFindAmmoSpot(
            Vector3 probe, Transform player, List<Vector3> existing, out Vector3 groundPoint)
        {
            groundPoint = Vector3.zero;
            // 从玩家头顶上方 1.5 米向下检测站台地面（避免打到地铁站天花板屋顶）
            Vector3 origin = new Vector3(probe.x, player.position.y + 1.5f, probe.z);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore)
                || hit.normal.y < 0.7f
                || hit.transform.root == player.root
                || hit.collider.GetComponentInParent<EnemyAI>() != null)
            {
                return false;
            }

            Vector3 point = hit.point;
            if (Mathf.Abs(point.y - (player.position.y - 1f)) > 1.2f)
            {
                return false;
            }

            if (Physics.CheckSphere(point + Vector3.up * 0.55f, 0.45f, ~0, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            for (int i = 0; i < existing.Count; i++)
            {
                Vector2 diff = new Vector2(existing[i].x - point.x, existing[i].z - point.z);
                if (diff.magnitude < 3f)
                {
                    return false;
                }
            }

            groundPoint = point;
            return true;
        }

        private static void CreateAmmoCrate(
            string name, Vector3 groundPosition, Transform parent, Material bodyMat, Material accentMat)
        {
            var root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, "Create AmmoCrate");
            root.transform.SetParent(parent, false);
            root.transform.position = groundPosition;

            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.65f, 0f);
            trigger.size = new Vector3(1.2f, 1.3f, 1.2f);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.36f, 0f);

            // 箱体、顶盖与发光绿色补给灯带
            AddVisualCube("Body", visual.transform, Vector3.zero, new Vector3(0.56f, 0.32f, 0.36f), bodyMat);
            AddVisualCube("Lid", visual.transform, new Vector3(0f, 0.18f, 0f), new Vector3(0.60f, 0.08f, 0.40f), bodyMat);
            AddVisualCube("GlowStripeX", visual.transform, new Vector3(0f, 0.225f, 0f), new Vector3(0.32f, 0.025f, 0.10f), accentMat);
            AddVisualCube("GlowStripeZ", visual.transform, new Vector3(0f, 0.225f, 0f), new Vector3(0.10f, 0.025f, 0.26f), accentMat);
            AddVisualCube("SideBandFront", visual.transform, new Vector3(0f, 0f, 0.185f), new Vector3(0.46f, 0.06f, 0.015f), accentMat);
            AddVisualCube("SideBandBack", visual.transform, new Vector3(0f, 0f, -0.185f), new Vector3(0.46f, 0.06f, 0.015f), accentMat);

            var glowLight = new GameObject("GlowLight");
            glowLight.transform.SetParent(root.transform, false);
            glowLight.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            Light light = glowLight.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.25f, 0.95f, 0.45f);
            light.intensity = 1.6f;
            light.range = 2.8f;
            light.shadows = LightShadows.None;

            AmmoPickup pickup = root.AddComponent<AmmoPickup>();
            SetRef(pickup, "visualRoot", visual.transform);
            SetInt(pickup, "ammoAmount", 30);
            SetFloat(pickup, "respawnTime", 15f);
        }

        private static void AddVisualCube(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPos;
            cube.transform.localScale = scale;

            Collider col = cube.GetComponent<Collider>();
            if (col != null)
            {
                Object.DestroyImmediate(col);
            }

            if (mat != null)
            {
                cube.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        private static Material GetOrCreateAmmoCrateMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AmmoCrateMaterialPath);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                return null;
            }

            if (!AssetDatabase.IsValidFolder("Assets/FPS/Materials"))
            {
                AssetDatabase.CreateFolder("Assets/FPS", "Materials");
            }

            material = new Material(shader) { color = new Color(0.20f, 0.28f, 0.22f) };
            SetFloatIfExists(material, "_Metallic", 0.35f);
            SetFloatIfExists(material, "_Smoothness", 0.4f);
            AssetDatabase.CreateAsset(material, AmmoCrateMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static Material GetOrCreateAmmoAccentMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AmmoAccentMaterialPath);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                return null;
            }

            if (!AssetDatabase.IsValidFolder("Assets/FPS/Materials"))
            {
                AssetDatabase.CreateFolder("Assets/FPS", "Materials");
            }

            Color green = new Color(0.22f, 0.95f, 0.45f);
            material = new Material(shader) { color = green };
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", green * 2.2f);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            AssetDatabase.CreateAsset(material, AmmoAccentMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static string UniqueChildName(Transform parent, string baseName)
        {
            for (int i = 1; ; i++)
            {
                string candidate = $"{baseName}_{i}";
                if (parent.Find(candidate) == null)
                {
                    return candidate;
                }
            }
        }

        private static List<T> FindAllInScene<T>() where T : Component
        {
            var results = new List<T>();
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                results.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return results;
        }

        // =====================================================================
        // 敌人
        // =====================================================================

        private static int CreateEnemies(Transform player, int count)
        {
            var parent = new GameObject("Enemies");
            Undo.RegisterCreatedObjectUndo(parent, "Create Enemies");

            Material material = GetOrCreateEnemyMaterial();

            // 相对玩家的候选位置（x, z），依次检查脚下是否有地面
            var offsets = new[]
            {
                new Vector2(6f, 10f), new Vector2(-6f, 10f), new Vector2(0f, 12f),
                new Vector2(9f, 0f), new Vector2(-9f, 0f),
                new Vector2(6f, -10f), new Vector2(-6f, -10f), new Vector2(0f, -12f)
            };

            Physics.SyncTransforms();

            int created = 0;
            foreach (Vector2 offset in offsets)
            {
                if (created >= count)
                {
                    break;
                }

                Vector3 position = player.position + new Vector3(offset.x, 0f, offset.y);
                if (!TryFindGround(position, player, out float groundY))
                {
                    continue;
                }

                // 胶囊高 2 米、轴心在中心，所以中心要比地面高 1 米
                position.y = groundY + 1f;
                created++;
                CreateEnemy($"Enemy_{created}", position, player.position, parent.transform, material);
            }

            if (created == 0)
            {
                Debug.LogWarning(
                    "[FPS 场景配置] 在玩家周围没有检测到地面，敌人被放在玩家前方同一高度，请手动调整位置。");

                for (int i = 0; i < count; i++)
                {
                    Vector3 position = player.position + new Vector3((i - 1) * 4f, 0f, 8f);
                    created++;
                    CreateEnemy($"Enemy_{created}", position, player.position, parent.transform, material);
                }
            }

            return created;
        }

        private static bool TryFindGround(Vector3 position, Transform player, out float groundY)
        {
            Vector3 origin = new Vector3(position.x, position.y + 50f, position.z);

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.7f
                && hit.transform.root != player.root)
            {
                groundY = hit.point.y;
                return true;
            }

            groundY = 0f;
            return false;
        }

        private static void CreateEnemy(
            string name, Vector3 position, Vector3 lookAt, Transform parent, Material material)
        {
            GameObject enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            enemy.name = name;
            enemy.transform.SetParent(parent, true);
            enemy.transform.position = position;

            Vector3 toPlayer = lookAt - position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.001f)
            {
                enemy.transform.rotation = Quaternion.LookRotation(toPlayer);
            }

            if (material != null)
            {
                enemy.GetComponent<Renderer>().sharedMaterial = material;
            }

            Health health = enemy.AddComponent<Health>();
            SetBool(health, "destroyOnDeath", true);

            NavMeshAgent agent = enemy.AddComponent<NavMeshAgent>();
            agent.height = 2f;
            agent.radius = 0.5f;
            agent.baseOffset = 1f;
            agent.angularSpeed = 360f;
            agent.acceleration = 20f;
            // 导航网格在运行时才生成，Agent 先禁用，由 EnemyAI 在开始时启用
            agent.enabled = false;

            enemy.AddComponent<EnemyAI>();
        }

        private static Material GetOrCreateEnemyMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(EnemyMaterialPath);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                return null;
            }

            if (!AssetDatabase.IsValidFolder("Assets/FPS/Materials"))
            {
                AssetDatabase.CreateFolder("Assets/FPS", "Materials");
            }

            material = new Material(shader) { color = new Color(0.85f, 0.2f, 0.2f) };
            AssetDatabase.CreateAsset(material, EnemyMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        // =====================================================================
        // HUD
        // =====================================================================

        private static HUDController CreateHud()
        {
            var canvasObject = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.layer = UILayer;
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create HUD");

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Transform root = canvasObject.transform;
            Vector2 center = new Vector2(0.5f, 0.5f);

            // 准星
            Image crosshair = CreateImage("Crosshair", root, Color.white);
            SetRect(crosshair.rectTransform, center, center, center, Vector2.zero, new Vector2(6f, 6f));

            // 血条（左下角）
            Image healthBar = CreateImage("HealthBar", root, new Color(0f, 0f, 0f, 0.6f));
            SetRect(healthBar.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(40f, 40f), new Vector2(320f, 28f));

            Image healthFill = CreateImage("Fill", healthBar.transform, new Color(0.2f, 0.8f, 0.3f));
            Stretch(healthFill.rectTransform);

            TextMeshProUGUI healthText = CreateText(
                "HealthText", healthBar.transform, "100", 22f, TextAlignmentOptions.Center);
            Stretch(healthText.rectTransform);

            // 弹药（右下角）
            TextMeshProUGUI ammoText = CreateText(
                "AmmoText", root, "30 / 90", 48f, TextAlignmentOptions.BottomRight);
            SetRect(ammoText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-40f, 30f), new Vector2(500f, 70f));

            // 敌人数量（右上角）
            TextMeshProUGUI enemyText = CreateText(
                "EnemyCountText", root, "ENEMIES: 0", 32f, TextAlignmentOptions.TopRight);
            SetRect(enemyText.rectTransform, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-40f, -30f), new Vector2(500f, 50f));

            // 结算面板（全屏，默认隐藏）
            Image messagePanel = CreateImage("MessagePanel", root, new Color(0f, 0f, 0f, 0.7f));
            Stretch(messagePanel.rectTransform);

            TextMeshProUGUI messageText = CreateText(
                "MessageText", messagePanel.transform, "GAME OVER", 96f, TextAlignmentOptions.Center);
            Stretch(messageText.rectTransform);

            messagePanel.gameObject.SetActive(false);

            HUDController hud = canvasObject.AddComponent<HUDController>();
            SetRef(hud, "healthFill", healthFill.rectTransform);
            SetRef(hud, "healthText", healthText);
            SetRef(hud, "ammoText", ammoText);
            SetRef(hud, "enemyCountText", enemyText);
            SetRef(hud, "crosshair", crosshair.gameObject);
            SetRef(hud, "messagePanel", messagePanel.gameObject);
            SetRef(hud, "messageText", messageText);
            return hud;
        }

        private static RectTransform CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UILayer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            Image image = CreateUIObject(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI CreateText(
            string name, Transform parent, string text, float fontSize, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI label = CreateUIObject(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private static void SetRect(
            RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // =====================================================================
        // 通用工具
        // =====================================================================

        private static T FindInScene<T>() where T : Component
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static T Ensure<T>(GameObject target, List<string> log) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component == null)
            {
                component = Undo.AddComponent<T>(target);
                log.Add($"{target.name}：添加 {typeof(T).Name}");
            }

            return component;
        }

        private static SerializedProperty FindProperty(SerializedObject serialized, string propertyName)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError(
                    $"[FPS 场景配置] 在 {serialized.targetObject.GetType().Name} 上找不到字段 {propertyName}。");
            }

            return property;
        }

        private static Object GetRef(Object target, string propertyName)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = FindProperty(serialized, propertyName);
            return property != null ? property.objectReferenceValue : null;
        }

        private static void SetRef(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = FindProperty(serialized, propertyName);
            if (property == null)
            {
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
        }

        private static void SetRefArray(Object target, string propertyName, params Object[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = FindProperty(serialized, propertyName);
            if (property == null)
            {
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedProperties();
        }

        private static int GetArraySize(Object target, string propertyName)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = FindProperty(serialized, propertyName);
            return property != null && property.isArray ? property.arraySize : 0;
        }

        /// <summary>如果数组里还没有 value，就追加到末尾。返回是否发生了追加。</summary>
        private static bool AppendRefToArray(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = FindProperty(serialized, propertyName);
            if (property == null || !property.isArray)
            {
                return false;
            }

            for (int i = 0; i < property.arraySize; i++)
            {
                if (property.GetArrayElementAtIndex(i).objectReferenceValue == value)
                {
                    return false;
                }
            }

            int index = property.arraySize;
            property.arraySize = index + 1;
            property.GetArrayElementAtIndex(index).objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
            return true;
        }

        /// <summary>如果数组里包含 value，则移除所有匹配项。返回是否发生了移除。</summary>
        private static bool RemoveRefFromArray(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = FindProperty(serialized, propertyName);
            if (property == null || !property.isArray)
            {
                return false;
            }

            bool removed = false;
            for (int i = property.arraySize - 1; i >= 0; i--)
            {
                if (property.GetArrayElementAtIndex(i).objectReferenceValue == value)
                {
                    property.DeleteArrayElementAtIndex(i);
                    removed = true;
                }
            }

            if (removed)
            {
                serialized.ApplyModifiedProperties();
            }

            return removed;
        }

        private static void SetBool(Object target, string propertyName, bool value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = FindProperty(serialized, propertyName);
            if (property == null)
            {
                return;
            }

            property.boolValue = value;
            serialized.ApplyModifiedProperties();
        }

        private static void SetFloat(Object target, string propertyName, float value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = FindProperty(serialized, propertyName);
            if (property == null)
            {
                return;
            }

            property.floatValue = value;
            serialized.ApplyModifiedProperties();
        }

        private static void SetInt(Object target, string propertyName, int value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = FindProperty(serialized, propertyName);
            if (property == null)
            {
                return;
            }

            property.intValue = value;
            serialized.ApplyModifiedProperties();
        }
    }
}
