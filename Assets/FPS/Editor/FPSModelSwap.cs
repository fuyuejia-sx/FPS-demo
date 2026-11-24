using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace FPS.EditorTools
{
    /// <summary>
    /// 编辑器工具：菜单 FPS > 替换为下载的模型。
    /// 把场景里的枪、敌人、盾牌和场景地形换成导入到 Assets/FPS/Models 下的模型：
    /// gun（枪）、target（敌人）、shield（盾）、evn（地铁站场景）。
    /// 做法是保留根物体上的脚本和碰撞体，只移除旧的网格，再把模型作为名为 Model 的子物体挂上去，
    /// 并根据模型的实际几何形状自动计算朝向、缩放和位置；材质用各文件夹里的 PBR 贴图生成。
    /// 场景模型会被放大到真实尺寸、加上碰撞体和灯光，旧的地面和方块会被隐藏（不删除），
    /// 不在新场景地面上的玩家、敌人和靶子会被挪到站台上。
    /// 可以重复执行：每次会先删除旧的 Model 子物体和 Environment 再重新生成，已经站在站台上的角色不会被移动。
    /// FPS > 一键配置场景 在结束时也会自动调用这里的逻辑。
    /// 本脚本只在编辑器中运行，不会进入最终的游戏包。
    /// </summary>
    public static class FPSModelSwap
    {
        private const string MenuPath = "FPS/替换为下载的模型";

        private const string ModelsRoot = "Assets/FPS/Models";
        private const string GunDir = ModelsRoot + "/gun";
        private const string ShieldDir = ModelsRoot + "/shield";
        private const string EnemyDir = ModelsRoot + "/target";
        private const string PropModelFile = "base.fbx";

        private const string EnvironmentModelPath = ModelsRoot + "/evn/pack/combined_world/fbx/scene_pbr.fbx";
        private const string EnvironmentTextureDir = ModelsRoot + "/evn/pack/combined_world/fbx/texture_pbr";

        private const string MaterialDir = "Assets/FPS/Materials";
        private const string GeneratedDir = MaterialDir + "/Generated";
        private const string GunMaterialPath = MaterialDir + "/ImportedGun.mat";
        private const string ShieldMaterialPath = MaterialDir + "/ImportedShield.mat";
        private const string EnemyMaterialPath = MaterialDir + "/ImportedEnemy.mat";
        private const string StationLightMaterialPath = MaterialDir + "/StationLight.mat";
        private const string StationGlassMaterialPath = MaterialDir + "/StationGlass.mat";

        private const string ModelChildName = "Model";
        private const string EnvironmentName = "Environment";

        /// <summary>枪在画面中的长度（米）。</summary>
        private const float GunLength = 0.55f;

        /// <summary>盾牌的宽度（米）。</summary>
        private const float ShieldWidth = 0.75f;

        /// <summary>盾牌上沿相对相机中心的高度（米）。负值表示在准星下方，不挡住准星。</summary>
        private const float ShieldTopY = -0.1f;

        /// <summary>盾牌离相机的距离（米）。</summary>
        private const float ShieldDistance = 0.7f;

        /// <summary>敌人模型的高度（米），略小于 2 米高的胶囊碰撞体。</summary>
        private const float EnemyHeight = 1.9f;

        /// <summary>场景模型是缩小的微缩模型，导入后按最长边放大到这个长度（米），站台净高约 4 米。</summary>
        private const float EnvironmentLength = 60f;

        /// <summary>比这更薄的水平贴片（盲道凸点、地面标线、灯罩等）不加碰撞体，避免走上去时镜头抖动。</summary>
        private const float MinColliderThickness = 0.08f;

        /// <summary>扫描地面时的网格间距（米）。</summary>
        private const float FloorCellSize = 0.75f;

        /// <summary>角色站立所需的净空半径和高度（米）。</summary>
        private const float StandRadius = 0.7f;
        private const float StandHeight = 2.2f;

        private enum Facing
        {
            /// <summary>枪：最长的水平轴是前后方向，握把所在的一端是后方。</summary>
            GunGrip,

            /// <summary>盾：最薄的水平轴是前后方向，中间鼓出去的一面是正面。</summary>
            ShieldBulge,

            /// <summary>角色：较短的水平轴是前后方向，正面按模型文件的原始朝向（+Z）。</summary>
            Character
        }

        private sealed class EnvironmentInfo
        {
            public Transform Root;
            public Bounds Bounds;
            public bool AlongZ;
            public bool HasFloor;
            public float FloorY;
            public readonly List<Vector3> Cells = new List<Vector3>();
            public float AlongMin, AlongMax, LateralMin, LateralMax;

            public float Along(Vector3 v) => AlongZ ? v.z : v.x;
            public float Lateral(Vector3 v) => AlongZ ? v.x : v.z;

            public Vector3 PointAt(float alongFraction, float lateralFraction)
            {
                float along = Mathf.Lerp(AlongMin, AlongMax, alongFraction);
                float lateral = Mathf.Lerp(LateralMin, LateralMax, lateralFraction);
                return AlongZ ? new Vector3(lateral, FloorY, along) : new Vector3(along, FloorY, lateral);
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool CanSwap()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem(MenuPath)]
        private static void Swap()
        {
            Undo.SetCurrentGroupName("FPS 替换模型");
            var log = new List<string>();

            if (!ApplyImportedModels(log))
            {
                EditorUtility.DisplayDialog("FPS 模型替换",
                    log.Count > 0
                        ? "没有完成任何替换：\n\n- " + string.Join("\n- ", log)
                        : "场景中没有找到武器、盾牌和敌人。\n请先执行 FPS > 一键配置场景。",
                    "确定");
                return;
            }

            string summary = "已完成以下替换：\n\n- " + string.Join("\n- ", log) + "\n\n请按 Ctrl+S 保存场景。";
            Debug.Log("[FPS 模型替换] " + summary);
            EditorUtility.DisplayDialog("FPS 模型替换", summary, "确定");
        }

        /// <summary>
        /// 把当前场景里的枪、盾、敌人和场景换成导入的模型，过程写入 log。
        /// 某个模型文件不存在时跳过对应的部分。返回是否至少完成了一项替换。
        /// </summary>
        internal static bool ApplyImportedModels(List<string> log)
        {
            // 模型可能是刚拷进来的，先让 Unity 导入
            AssetDatabase.Refresh();

            GameObject gunPrefab = LoadModel(GunDir + "/" + PropModelFile, log);
            GameObject shieldPrefab = LoadModel(ShieldDir + "/" + PropModelFile, log);
            GameObject enemyPrefab = LoadModel(EnemyDir + "/" + PropModelFile, log);
            GameObject environmentPrefab = LoadEnvironmentModel(log);

            Weapon weapon = FindInScene<Weapon>();
            PlayerShield playerShield = FindInScene<PlayerShield>();
            GameObject shieldObject = FindShieldObject(playerShield);
            List<EnemyAI> enemies = FindAllInScene<EnemyAI>();

            bool changed = false;

            if (environmentPrefab != null)
            {
                EnvironmentInfo environment = BuildEnvironment(environmentPrefab, log);
                HideOldEnvironment(environment, log);
                PlaceCharacters(environment, enemies, log);
                changed = true;
            }

            if (weapon != null && gunPrefab != null)
            {
                SwapWeapon(weapon, gunPrefab, GetOrCreatePropMaterial(GunMaterialPath, GunDir), log);
                changed = true;
            }

            if (shieldObject != null && shieldPrefab != null)
            {
                SwapShield(shieldObject, shieldPrefab, GetOrCreatePropMaterial(ShieldMaterialPath, ShieldDir), log);
                changed = true;
            }

            if (enemyPrefab != null && enemies.Count > 0)
            {
                Material material = GetOrCreatePropMaterial(EnemyMaterialPath, EnemyDir);
                foreach (EnemyAI enemy in enemies)
                {
                    // 带骨骼的敌人（FPS > 添加带骨骼的敌人）有自己的模型和动画，不替换
                    if (FPSRiggedEnemy.IsRigged(enemy))
                    {
                        continue;
                    }

                    SwapEnemy(enemy, enemyPrefab, material, log);
                }

                changed = true;
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            return changed;
        }

        private static GameObject LoadModel(string path, List<string> log)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                log.Add("没有找到模型 " + path + "，已跳过");
            }

            return prefab;
        }

        private static GameObject LoadEnvironmentModel(List<string> log)
        {
            // 运行时导航网格是从碰撞体网格生成的，打包后需要网格可读
            if (AssetImporter.GetAtPath(EnvironmentModelPath) is ModelImporter importer && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            return LoadModel(EnvironmentModelPath, log);
        }

        private static GameObject FindShieldObject(PlayerShield playerShield)
        {
            if (playerShield == null)
            {
                return null;
            }

            SerializedProperty property = new SerializedObject(playerShield).FindProperty("shieldObject");
            if (property != null && property.objectReferenceValue is GameObject assigned)
            {
                return assigned;
            }

            Camera camera = playerShield.GetComponentInChildren<Camera>(true);
            Transform found = camera != null ? camera.transform.Find("Shield") : null;
            return found != null ? found.gameObject : null;
        }

        // =====================================================================
        // 武器
        // =====================================================================

        private static void SwapWeapon(Weapon weapon, GameObject prefab, Material material, List<string> log)
        {
            Transform root = weapon.transform;
            RemovePlaceholderMesh(root.gameObject);

            // 占位方块是靠缩放拉成长条的，换成模型后根物体要恢复成 1
            Undo.RecordObject(root, "Reset Weapon Scale");
            root.localScale = Vector3.one;

            // 离相机太近时枪托会被相机的近裁剪面切掉，必要时往前挪一点
            Camera viewCamera = root.GetComponentInParent<Camera>();
            if (viewCamera != null && root.parent == viewCamera.transform)
            {
                float minZ = viewCamera.nearClipPlane + 0.05f + GunLength * 0.5f;
                if (root.localPosition.z < minZ)
                {
                    Vector3 position = root.localPosition;
                    position.z = minZ;
                    root.localPosition = position;
                }
            }

            Transform model = AttachModel(root, prefab, Facing.GunGrip, out Bounds bounds, out bool detected);

            float scale = bounds.size.z > 0.0001f ? GunLength / bounds.size.z : 1f;
            model.localScale = Vector3.one * scale;
            model.localPosition = -bounds.center * scale;
            ApplyMaterial(model, material, false);

            // 枪口闪光灯挪到新的枪口位置（枪管在枪身的上半部分）
            Transform muzzle = root.Find("MuzzleFlash");
            if (muzzle != null)
            {
                Undo.RecordObject(muzzle, "Move MuzzleFlash");
                muzzle.localPosition = new Vector3(0f, bounds.size.y * scale * 0.2f, GunLength * 0.5f + 0.05f);
            }

            log.Add($"{root.name}：替换为导入的枪模型" + (detected ? "" : "（朝向未能自动判断，按默认处理）"));
        }

        // =====================================================================
        // 盾牌
        // =====================================================================

        private static void SwapShield(GameObject shieldObject, GameObject prefab, Material material, List<string> log)
        {
            Transform root = shieldObject.transform;
            RemovePlaceholderMesh(shieldObject);

            // 占位方块是靠缩放压成薄板的，换成模型后根物体要恢复成 1
            Undo.RecordObject(root, "Reset Shield Transform");
            root.localScale = Vector3.one;

            Transform model = AttachModel(root, prefab, Facing.ShieldBulge, out Bounds bounds, out bool detected);

            float scale = bounds.size.x > 0.0001f ? ShieldWidth / bounds.size.x : 1f;
            float height = bounds.size.y * scale;
            model.localScale = Vector3.one * scale;
            model.localPosition = -bounds.center * scale;

            // 盾牌是不透明的实体：只让上沿露在视野下方，不挡住准星
            Camera viewCamera = root.parent != null ? root.parent.GetComponent<Camera>() : null;
            if (viewCamera != null)
            {
                root.localPosition = new Vector3(
                    0f, ShieldTopY - height * 0.5f, Mathf.Max(ShieldDistance, viewCamera.nearClipPlane + 0.3f));
            }

            ApplyMaterial(model, material, true);

            log.Add($"{root.name}：替换为导入的盾牌模型" + (detected ? "" : "（朝向未能自动判断，按默认处理）"));
        }

        // =====================================================================
        // 敌人
        // =====================================================================

        private static void SwapEnemy(EnemyAI enemy, GameObject prefab, Material material, List<string> log)
        {
            Transform root = enemy.transform;
            RemovePlaceholderMesh(root.gameObject);

            Transform model = AttachModel(root, prefab, Facing.Character, out Bounds bounds, out _);

            float scale = bounds.size.y > 0.0001f ? EnemyHeight / bounds.size.y : 1f;

            // 根物体的原点在胶囊中心（离地 Base Offset），模型的脚底要落在地面上
            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            float feetY = agent != null ? -agent.baseOffset : -1f;

            model.localScale = Vector3.one * scale;
            model.localPosition = new Vector3(
                -bounds.center.x * scale,
                feetY - bounds.min.y * scale,
                -bounds.center.z * scale);
            ApplyMaterial(model, material, false);

            // 机器人比原来的胶囊宽，把命中用的胶囊放大到与模型相称。
            // 寻路半径不改：导航网格是按默认半径生成的，两者需要保持一致。
            float radius = (bounds.size.x + bounds.size.z) * 0.25f * scale;
            CapsuleCollider capsule = enemy.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                Undo.RecordObject(capsule, "Fit Enemy Collider");
                capsule.radius = Mathf.Clamp(radius, 0.3f, capsule.height * 0.5f);
            }

            log.Add($"{root.name}：替换为导入的敌人模型");
        }

        // =====================================================================
        // 场景
        // =====================================================================

        private static EnvironmentInfo BuildEnvironment(GameObject prefab, List<string> log)
        {
            foreach (GameObject sceneRoot in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (sceneRoot.name == EnvironmentName)
                {
                    Undo.DestroyObjectImmediate(sceneRoot);
                }
            }

            var rootObject = new GameObject(EnvironmentName);
            Undo.RegisterCreatedObjectUndo(rootObject, "Create Environment");
            var info = new EnvironmentInfo { Root = rootObject.transform };

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, rootObject.transform);
            Transform model = instance.transform;

            // 按最长的水平边放大到真实尺寸
            Bounds bounds = RendererBounds(instance);
            float longest = Mathf.Max(bounds.size.x, bounds.size.z);
            float scale = longest > 0.0001f ? EnvironmentLength / longest : 1f;
            model.localScale *= scale;

            int colliders = 0;
            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(filter.gameObject, StaticEditorFlags.BatchingStatic);

                Renderer renderer = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null
                    || (renderer != null && renderer.bounds.size.y < MinColliderThickness))
                {
                    continue;
                }

                if (filter.GetComponent<Collider>() == null)
                {
                    filter.gameObject.AddComponent<MeshCollider>();
                }

                colliders++;
            }

            ApplyEnvironmentMaterials(instance);

            Physics.SyncTransforms();
            info.Bounds = RendererBounds(instance);
            info.AlongZ = info.Bounds.size.z >= info.Bounds.size.x;
            ScanFloor(info);

            // 把场景挪到世界原点，站台地面落在 y = 0
            var shift = new Vector3(
                -info.Bounds.center.x, info.HasFloor ? -info.FloorY : -info.Bounds.min.y, -info.Bounds.center.z);
            model.position += shift;
            info.Bounds.center += shift;
            info.FloorY += shift.y;
            for (int i = 0; i < info.Cells.Count; i++)
            {
                info.Cells[i] += shift;
            }

            Physics.SyncTransforms();
            UpdateCellRange(info);

            CreateBoundary(info);
            int lights = CreateLights(info);

            log.Add($"创建场景 {EnvironmentName}：放大 {scale:F2} 倍（长 {EnvironmentLength:F0} 米），" +
                    $"{colliders} 个碰撞体，{lights} 盏灯");

            if (!info.HasFloor)
            {
                log.Add("场景中没有检测到可以站立的地面，玩家和敌人的位置没有调整，请手动摆放");
            }

            return info;
        }

        private static Bounds RendererBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(target.transform.position, Vector3.zero);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        /// <summary>
        /// 用网格状的射线找出场景里的主地面：朝上、头顶有天花板（在室内）、站得下一个角色的位置里，
        /// 面积最大的那一层高度就是站台地面。
        /// </summary>
        private static void ScanFloor(EnvironmentInfo info)
        {
            Bounds bounds = info.Bounds;
            float top = bounds.max.y + 1f;
            float depth = bounds.size.y + 2f;
            const float bucketSize = 0.25f;

            var candidates = new List<Vector3>();
            var buckets = new Dictionary<int, int>();

            for (float x = bounds.min.x + FloorCellSize * 0.5f; x < bounds.max.x; x += FloorCellSize)
            {
                for (float z = bounds.min.z + FloorCellSize * 0.5f; z < bounds.max.z; z += FloorCellSize)
                {
                    RaycastHit[] hits = Physics.RaycastAll(
                        new Vector3(x, top, z), Vector3.down, depth, ~0, QueryTriggerInteraction.Ignore);

                    foreach (RaycastHit hit in hits)
                    {
                        if (hit.normal.y < 0.7f || !hit.collider.transform.IsChildOf(info.Root))
                        {
                            continue;
                        }

                        Vector3 point = hit.point;

                        // 头顶必须有东西（排除屋顶的上表面），并且有足够的净高
                        if (!Physics.Raycast(point + Vector3.up * 0.1f, Vector3.up, out RaycastHit ceiling,
                                200f, ~0, QueryTriggerInteraction.Ignore)
                            || ceiling.distance < StandHeight)
                        {
                            continue;
                        }

                        if (Physics.CheckCapsule(
                                point + Vector3.up * (StandRadius + 0.2f), point + Vector3.up * (StandHeight - StandRadius),
                                StandRadius, ~0, QueryTriggerInteraction.Ignore))
                        {
                            continue;
                        }

                        candidates.Add(point);
                        int bucket = Mathf.RoundToInt(point.y / bucketSize);
                        buckets.TryGetValue(bucket, out int count);
                        buckets[bucket] = count + 1;
                    }
                }
            }

            int bestBucket = 0, bestCount = 0;
            foreach (KeyValuePair<int, int> pair in buckets)
            {
                if (pair.Value > bestCount)
                {
                    bestBucket = pair.Key;
                    bestCount = pair.Value;
                }
            }

            if (bestCount == 0)
            {
                return;
            }

            float sum = 0f;
            foreach (Vector3 point in candidates)
            {
                if (Mathf.RoundToInt(point.y / bucketSize) == bestBucket)
                {
                    info.Cells.Add(point);
                    sum += point.y;
                }
            }

            info.HasFloor = true;
            info.FloorY = sum / info.Cells.Count;
        }

        private static void UpdateCellRange(EnvironmentInfo info)
        {
            if (info.Cells.Count == 0)
            {
                return;
            }

            info.AlongMin = info.LateralMin = float.MaxValue;
            info.AlongMax = info.LateralMax = float.MinValue;
            foreach (Vector3 cell in info.Cells)
            {
                info.AlongMin = Mathf.Min(info.AlongMin, info.Along(cell));
                info.AlongMax = Mathf.Max(info.AlongMax, info.Along(cell));
                info.LateralMin = Mathf.Min(info.LateralMin, info.Lateral(cell));
                info.LateralMax = Mathf.Max(info.LateralMax, info.Lateral(cell));
            }
        }

        /// <summary>场景两端是敞开的，在四周加一圈看不见的墙，防止走出场景后掉下去。</summary>
        private static void CreateBoundary(EnvironmentInfo info)
        {
            var boundary = new GameObject("Boundary");
            boundary.transform.SetParent(info.Root, false);

            Bounds b = info.Bounds;
            const float thickness = 1f;
            float height = b.size.y + 6f;
            float centerY = b.center.y + 3f;

            AddWall(boundary, new Vector3(b.max.x + thickness * 0.5f, centerY, b.center.z),
                new Vector3(thickness, height, b.size.z + thickness * 2f));
            AddWall(boundary, new Vector3(b.min.x - thickness * 0.5f, centerY, b.center.z),
                new Vector3(thickness, height, b.size.z + thickness * 2f));
            AddWall(boundary, new Vector3(b.center.x, centerY, b.max.z + thickness * 0.5f),
                new Vector3(b.size.x + thickness * 2f, height, thickness));
            AddWall(boundary, new Vector3(b.center.x, centerY, b.min.z - thickness * 0.5f),
                new Vector3(b.size.x + thickness * 2f, height, thickness));
        }

        private static void AddWall(GameObject boundary, Vector3 center, Vector3 size)
        {
            BoxCollider wall = boundary.AddComponent<BoxCollider>();
            wall.center = center;
            wall.size = size;
        }

        /// <summary>站台在室内，平行光照不进来，沿站台方向每隔一段放一盏点光源。</summary>
        private static int CreateLights(EnvironmentInfo info)
        {
            if (!info.HasFloor)
            {
                return 0;
            }

            var parent = new GameObject("Lights");
            parent.transform.SetParent(info.Root, false);

            int count = Mathf.Max(1, Mathf.RoundToInt((info.AlongMax - info.AlongMin) / 9f));
            for (int i = 0; i < count; i++)
            {
                Vector3 position = info.PointAt((i + 0.5f) / count, 0.5f);

                float height = 3f;
                if (Physics.Raycast(position + Vector3.up * 0.2f, Vector3.up, out RaycastHit ceiling,
                        50f, ~0, QueryTriggerInteraction.Ignore))
                {
                    height = Mathf.Max(1.5f, (ceiling.distance + 0.2f) * 0.8f);
                }

                var lightObject = new GameObject($"StationLight_{i + 1}");
                lightObject.transform.SetParent(parent.transform, false);
                lightObject.transform.position = position + Vector3.up * height;

                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.95f, 0.97f, 1f);
                light.intensity = 12f;
                light.range = 16f;
                light.shadows = LightShadows.None;
            }

            return count;
        }

        /// <summary>
        /// 场景模型里带贴图的摆件用 texture_pbr 文件夹里的贴图重新生成材质；
        /// 灯罩换成自发光材质，玻璃换成半透明材质；其余纯色材质沿用模型自带的。
        /// </summary>
        private static void ApplyEnvironmentMaterials(GameObject instance)
        {
            var propMaterials = new Dictionary<string, Material>();
            Material lightMaterial = null;
            Material glassMaterial = null;

            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                string prefix = renderer.name.EndsWith("_Mesh")
                    ? renderer.name.Substring(0, renderer.name.Length - "_Mesh".Length)
                    : renderer.name;

                if (!propMaterials.TryGetValue(prefix, out Material propMaterial))
                {
                    string texturePrefix = EnvironmentTextureDir + "/" + prefix + "_texture_";
                    propMaterial = File.Exists(texturePrefix + "diffuse.png")
                        ? GetOrCreatePbrMaterial(
                            MaterialDir + "/Station_" + prefix + ".mat",
                            texturePrefix + "diffuse.png", texturePrefix + "normal.png",
                            texturePrefix + "metallic.png", texturePrefix + "roughness.png", null)
                        : null;
                    propMaterials[prefix] = propMaterial;
                }

                Material[] materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    string materialName = materials[i] != null ? materials[i].name : string.Empty;
                    Material replacement = null;

                    if (propMaterial != null)
                    {
                        replacement = propMaterial;
                    }
                    else if (materialName.Contains("Fluorescent"))
                    {
                        replacement = lightMaterial != null
                            ? lightMaterial
                            : lightMaterial = GetOrCreateStationLightMaterial();
                    }
                    else if (materialName.Contains("Glass"))
                    {
                        replacement = glassMaterial != null
                            ? glassMaterial
                            : glassMaterial = GetOrCreateStationGlassMaterial();
                    }

                    if (replacement != null)
                    {
                        materials[i] = replacement;
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
        }

        /// <summary>把旧的地面、方块等场景物体隐藏起来（不删除，可以在 Hierarchy 里重新勾选）。</summary>
        private static void HideOldEnvironment(EnvironmentInfo info, List<string> log)
        {
            var hidden = new List<string>();

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!root.activeSelf || root.transform == info.Root || IsGameplayObject(root))
                {
                    continue;
                }

                if (root.GetComponentInChildren<Renderer>(true) == null
                    && root.GetComponentInChildren<Collider>(true) == null)
                {
                    continue;
                }

                Undo.RecordObject(root, "Hide Old Environment");
                root.SetActive(false);
                hidden.Add(root.name);
            }

            if (hidden.Count > 0)
            {
                log.Add("隐藏旧场景物体：" + string.Join("、", hidden));
            }
        }

        private static bool IsGameplayObject(GameObject root)
        {
            return root.GetComponentInChildren<Camera>(true) != null
                || root.GetComponentInChildren<Light>(true) != null
                || root.GetComponentInChildren<Canvas>(true) != null
                || root.GetComponentInChildren<Health>(true) != null
                || root.GetComponentInChildren<RuntimeNavMeshBaker>(true) != null
                || root.GetComponentInChildren<GameManager>(true) != null;
        }

        // =====================================================================
        // 把角色摆到新场景里
        // =====================================================================

        private static void PlaceCharacters(EnvironmentInfo info, List<EnemyAI> enemies, List<string> log)
        {
            if (!info.HasFloor)
            {
                return;
            }

            var taken = new List<Vector3>();
            var pending = new List<System.Action>();

            // ---------- 玩家：站在站台一端，面向站台中间 ----------
            PlayerMovement player = FindInScene<PlayerMovement>();
            Vector3 playerPoint = info.PointAt(0.9f, 0.5f);

            if (player != null)
            {
                CharacterController controller = player.GetComponent<CharacterController>();
                float feetOffset = controller != null
                    ? (controller.center.y - controller.height * 0.5f) * player.transform.lossyScale.y
                    : -1f;

                Vector3 feet = player.transform.position + Vector3.up * feetOffset;
                if (IsOnFloor(info, feet))
                {
                    playerPoint = feet;
                    taken.Add(feet);
                }
                else if (TryPickCell(info, 0.9f, 0.5f, taken, 0f, out playerPoint))
                {
                    taken.Add(playerPoint);

                    Vector3 forward = info.AlongZ ? Vector3.back : Vector3.left;
                    Undo.RecordObject(player.transform, "Place Player");
                    player.transform.SetPositionAndRotation(
                        playerPoint + Vector3.up * (0.05f - feetOffset), Quaternion.LookRotation(forward));
                    log.Add($"{player.name}：移到站台上");
                }
            }

            // ---------- 敌人：分散在站台中段到另一端，面向玩家 ----------
            float[] lateralFractions = { 0.3f, 0.7f, 0.5f };
            int moved = 0;

            // 已经站在站台上的敌人先占位，其余的再找空位
            var toPlace = new List<EnemyAI>();
            foreach (EnemyAI enemy in enemies)
            {
                float baseOffset = EnemyBaseOffset(enemy);
                Vector3 feet = enemy.transform.position - Vector3.up * baseOffset;
                if (IsOnFloor(info, feet))
                {
                    taken.Add(feet);
                }
                else
                {
                    toPlace.Add(enemy);
                }
            }

            for (int i = 0; i < toPlace.Count; i++)
            {
                EnemyAI enemy = toPlace[i];
                float alongFraction = Mathf.Max(0.06f, 0.5f - 0.17f * (i % 3) - 0.04f * (i / 3));

                if (!TryPickCell(info, alongFraction, lateralFractions[i % 3], taken, 3f, out Vector3 point))
                {
                    continue;
                }

                taken.Add(point);
                moved++;

                Vector3 position = point + Vector3.up * EnemyBaseOffset(enemy);
                Vector3 toPlayer = playerPoint - point;
                toPlayer.y = 0f;

                Undo.RecordObject(enemy.transform, "Place Enemy");
                enemy.transform.SetPositionAndRotation(
                    position,
                    toPlayer.sqrMagnitude > 0.001f ? Quaternion.LookRotation(toPlayer) : enemy.transform.rotation);
            }

            if (moved > 0)
            {
                log.Add($"{moved} 个敌人：移到站台上");
            }

            // ---------- 其他可以被打的物体（靶子）：放在玩家前方 ----------
            int dummyIndex = 0;
            foreach (Health health in FindAllInScene<Health>())
            {
                if (health.GetComponent<EnemyAI>() != null
                    || health.GetComponentInParent<PlayerMovement>() != null
                    || health.transform.IsChildOf(info.Root))
                {
                    continue;
                }

                Bounds bounds = RendererBounds(health.gameObject);
                Vector3 bottom = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                if (IsOnFloor(info, bottom))
                {
                    continue;
                }

                float lateral = dummyIndex % 2 == 0 ? 0.25f : 0.75f;
                if (!TryPickCell(info, 0.72f - 0.06f * (dummyIndex / 2), lateral, taken, 2f, out Vector3 point))
                {
                    continue;
                }

                dummyIndex++;
                taken.Add(point);

                Undo.RecordObject(health.transform, "Place Target");
                health.transform.position += point - bottom;
                log.Add($"{health.name}：移到站台上");
            }
        }

        private static float EnemyBaseOffset(EnemyAI enemy)
        {
            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            return agent != null ? agent.baseOffset : 1f;
        }

        /// <summary>脚底位置 feet 是否已经站在新场景的主地面上。</summary>
        private static bool IsOnFloor(EnvironmentInfo info, Vector3 feet)
        {
            if (Mathf.Abs(feet.y - info.FloorY) > 1f)
            {
                return false;
            }

            return Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit,
                       2f, ~0, QueryTriggerInteraction.Ignore)
                   && hit.collider.transform.IsChildOf(info.Root)
                   && Mathf.Abs(hit.point.y - info.FloorY) < 0.5f;
        }

        /// <summary>在可站立的格子里找离目标位置最近、且与已占用位置保持 separation 距离的一个。</summary>
        private static bool TryPickCell(
            EnvironmentInfo info, float alongFraction, float lateralFraction,
            List<Vector3> taken, float separation, out Vector3 point)
        {
            Vector3 target = info.PointAt(alongFraction, lateralFraction);
            float best = float.MaxValue;
            point = target;
            bool found = false;

            foreach (Vector3 cell in info.Cells)
            {
                float distance = (cell - target).sqrMagnitude;
                if (distance >= best)
                {
                    continue;
                }

                bool free = true;
                foreach (Vector3 other in taken)
                {
                    if ((other - cell).sqrMagnitude < separation * separation)
                    {
                        free = false;
                        break;
                    }
                }

                if (free)
                {
                    best = distance;
                    point = cell;
                    found = true;
                }
            }

            return found;
        }

        // =====================================================================
        // 材质
        // =====================================================================

        /// <summary>用模型文件夹里的 texture_*.png 生成材质（gun、shield、target 三个文件夹的命名一致）。</summary>
        private static Material GetOrCreatePropMaterial(string materialPath, string modelDir)
        {
            return GetOrCreatePbrMaterial(
                materialPath,
                modelDir + "/texture_diffuse.png", modelDir + "/texture_normal.png",
                modelDir + "/texture_metallic.png", modelDir + "/texture_roughness.png",
                modelDir + "/texture_emissive.png");
        }

        private static Material GetOrCreatePbrMaterial(
            string materialPath, string diffusePath, string normalPath,
            string metallicPath, string roughnessPath, string emissivePath)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material != null)
            {
                return material;
            }

            Texture2D diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(diffusePath);
            Shader shader = FindLitShader();
            if (diffuse == null || shader == null)
            {
                Debug.LogWarning($"[FPS 模型替换] 没有找到贴图 {diffusePath} 或 Lit 着色器，将使用模型自带的材质。");
                return null;
            }

            EnsureFolder(MaterialDir);
            material = new Material(shader) { mainTexture = diffuse };
            SetTextureIfExists(material, "_BaseMap", diffuse);

            // 法线贴图：需要把导入类型改成 Normal map
            if (File.Exists(normalPath))
            {
                if (AssetImporter.GetAtPath(normalPath) is TextureImporter importer
                    && importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.SaveAndReimport();
                }

                Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                if (normal != null && material.HasProperty("_BumpMap"))
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }
            }

            // 金属度 + 粗糙度：Unity 的 Lit 着色器要求合成一张图（R = 金属度，A = 光滑度）
            string maskPath = GeneratedDir + "/" + Path.GetFileNameWithoutExtension(materialPath) + "_MetallicSmoothness.png";
            Texture2D mask = GetOrCreateMetallicSmoothness(metallicPath, roughnessPath, maskPath);
            if (mask != null && material.HasProperty("_MetallicGlossMap"))
            {
                material.SetTexture("_MetallicGlossMap", mask);
                material.EnableKeyword("_METALLICSPECGLOSSMAP"); // URP Lit
                material.EnableKeyword("_METALLICGLOSSMAP");     // 内置 Standard
                SetFloatIfExists(material, "_Metallic", 1f);
                SetFloatIfExists(material, "_Smoothness", 1f);
                SetFloatIfExists(material, "_GlossMapScale", 1f);
            }
            else
            {
                SetFloatIfExists(material, "_Smoothness", 0.3f);
            }

            // 自发光（只有部分模型带）
            if (!string.IsNullOrEmpty(emissivePath) && File.Exists(emissivePath))
            {
                Texture2D emissive = AssetDatabase.LoadAssetAtPath<Texture2D>(emissivePath);
                if (emissive != null && material.HasProperty("_EmissionMap"))
                {
                    material.SetTexture("_EmissionMap", emissive);
                    material.SetColor("_EmissionColor", Color.white);
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }

            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static Texture2D GetOrCreateMetallicSmoothness(
            string metallicPath, string roughnessPath, string outputPath)
        {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
            if (existing != null)
            {
                return existing;
            }

            // 直接读 PNG 文件，不依赖贴图的导入设置（是否可读、是否压缩）
            Texture2D metallic = LoadPng(metallicPath);
            Texture2D roughness = LoadPng(roughnessPath);
            if (metallic == null && roughness == null)
            {
                return null;
            }

            if (metallic != null && roughness != null
                && (metallic.width != roughness.width || metallic.height != roughness.height))
            {
                Debug.LogWarning($"[FPS 模型替换] {metallicPath} 与 {roughnessPath} 尺寸不一致，已忽略粗糙度贴图。");
                Object.DestroyImmediate(roughness);
                roughness = null;
            }

            Texture2D size = metallic != null ? metallic : roughness;
            Color32[] metallicPixels = metallic != null ? metallic.GetPixels32() : null;
            Color32[] roughnessPixels = roughness != null ? roughness.GetPixels32() : null;
            var pixels = new Color32[size.width * size.height];

            for (int i = 0; i < pixels.Length; i++)
            {
                byte m = metallicPixels != null ? metallicPixels[i].r : (byte)0;
                byte smoothness = roughnessPixels != null ? (byte)(255 - roughnessPixels[i].r) : (byte)128;
                pixels[i] = new Color32(m, m, m, smoothness);
            }

            var packed = new Texture2D(size.width, size.height, TextureFormat.RGBA32, false, true);
            packed.SetPixels32(pixels);
            byte[] png = packed.EncodeToPNG();

            Object.DestroyImmediate(packed);
            if (metallic != null)
            {
                Object.DestroyImmediate(metallic);
            }

            if (roughness != null)
            {
                Object.DestroyImmediate(roughness);
            }

            EnsureFolder(GeneratedDir);
            File.WriteAllBytes(outputPath, png);
            AssetDatabase.ImportAsset(outputPath);

            if (AssetImporter.GetAtPath(outputPath) is TextureImporter importer)
            {
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        }

        private static Texture2D LoadPng(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                Object.DestroyImmediate(texture);
                return null;
            }

            return texture;
        }

        private static Material GetOrCreateStationLightMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(StationLightMaterialPath);
            Shader shader = FindLitShader();
            if (material != null || shader == null)
            {
                return material;
            }

            EnsureFolder(MaterialDir);
            material = new Material(shader) { color = Color.white };
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", new Color(0.9f, 0.95f, 1f) * 2f);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            AssetDatabase.CreateAsset(material, StationLightMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static Material GetOrCreateStationGlassMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(StationGlassMaterialPath);
            Shader shader = FindLitShader();
            if (material != null || shader == null)
            {
                return material;
            }

            EnsureFolder(MaterialDir);
            material = new Material(shader) { color = new Color(0.75f, 0.88f, 0.92f, 0.25f) };
            SetFloatIfExists(material, "_Smoothness", 0.9f);
            FPSSceneSetup.MakeTransparent(material);

            AssetDatabase.CreateAsset(material, StationGlassMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static Shader FindLitShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            return shader != null ? shader : Shader.Find("Standard");
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private static void SetFloatIfExists(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void SetTextureIfExists(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, texture);
            }
        }

        private static void ApplyMaterial(Transform model, Material material, bool disableShadows)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (disableShadows)
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                if (material == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }

                renderer.sharedMaterials = materials;
            }
        }

        // =====================================================================
        // 通用：挂载模型并计算朝向
        // =====================================================================

        private static void RemovePlaceholderMesh(GameObject target)
        {
            MeshRenderer renderer = target.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Undo.DestroyObjectImmediate(renderer);
            }

            MeshFilter filter = target.GetComponent<MeshFilter>();
            if (filter != null)
            {
                Undo.DestroyObjectImmediate(filter);
            }
        }

        /// <summary>
        /// 在 root 下创建名为 Model 的子物体并放入模型实例，旋转它使模型的“前方”对准 +Z。
        /// 返回 Model 子物体；bounds 是旋转之后、缩放之前模型在 Model 父空间中的包围盒。
        /// </summary>
        private static Transform AttachModel(
            Transform root, GameObject prefab, Facing facing, out Bounds bounds, out bool orientationDetected)
        {
            Transform old = root.Find(ModelChildName);
            if (old != null)
            {
                Undo.DestroyObjectImmediate(old.gameObject);
            }

            var holderObject = new GameObject(ModelChildName);
            Undo.RegisterCreatedObjectUndo(holderObject, "Create Model");
            Transform holder = holderObject.transform;
            holder.SetParent(root, false);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder);

            // 模型只负责显示，命中判定和碰撞由根物体负责
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }

            List<Vector3> points = CollectPoints(holder, instance, out bool hasVertices);

            Bounds raw = BoundsOf(points, Quaternion.identity);
            bool forwardIsX = facing == Facing.GunGrip ? raw.size.x > raw.size.z : raw.size.x < raw.size.z;
            float yaw = forwardIsX ? -90f : 0f;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

            float sign;
            switch (facing)
            {
                case Facing.GunGrip:
                    sign = DetectGunForward(points, rotation);
                    orientationDetected = hasVertices && sign != 0f;
                    break;
                case Facing.ShieldBulge:
                    sign = DetectShieldFront(points, rotation);
                    orientationDetected = hasVertices && sign != 0f;
                    break;
                default:
                    sign = 1f;
                    orientationDetected = true;
                    break;
            }

            // 判断不出来时按模型文件的原始朝向处理（导入的模型正面都朝 +Z）
            if (!orientationDetected)
            {
                sign = 1f;
            }

            if (sign < 0f)
            {
                yaw += 180f;
            }

            rotation = Quaternion.Euler(0f, yaw, 0f);
            holder.localRotation = rotation;
            bounds = BoundsOf(points, rotation);
            return holder;
        }

        /// <summary>收集模型所有顶点在 holder 空间中的位置。读不到顶点时退回到用网格包围盒的角点。</summary>
        private static List<Vector3> CollectPoints(Transform holder, GameObject instance, out bool hasVertices)
        {
            var points = new List<Vector3>();
            hasVertices = false;

            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                Vector3[] vertices = null;
                try
                {
                    vertices = mesh.vertices;
                }
                catch (System.Exception)
                {
                    // 网格不可读时走下面的包围盒分支
                }

                if (vertices != null && vertices.Length > 0)
                {
                    hasVertices = true;
                }
                else
                {
                    Bounds b = mesh.bounds;
                    vertices = new Vector3[8];
                    for (int i = 0; i < 8; i++)
                    {
                        vertices[i] = b.center + Vector3.Scale(b.extents, new Vector3(
                            (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    }
                }

                Matrix4x4 toHolder = holder.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 vertex in vertices)
                {
                    points.Add(toHolder.MultiplyPoint3x4(vertex));
                }
            }

            if (points.Count == 0)
            {
                points.Add(Vector3.zero);
            }

            return points;
        }

        private static Bounds BoundsOf(List<Vector3> points, Quaternion rotation)
        {
            var bounds = new Bounds(rotation * points[0], Vector3.zero);
            for (int i = 1; i < points.Count; i++)
            {
                bounds.Encapsulate(rotation * points[i]);
            }

            return bounds;
        }

        /// <summary>
        /// 判断枪口朝 +Z 还是 -Z：握把是整把枪最靠下的部分，而且总是在枪的后半段，
        /// 所以最低的那些顶点偏向哪一端，哪一端就是后方。
        /// 返回 1 表示枪口朝 +Z，-1 表示朝 -Z，0 表示无法判断。
        /// </summary>
        private static float DetectGunForward(List<Vector3> points, Quaternion rotation)
        {
            Bounds bounds = BoundsOf(points, rotation);
            float cut = bounds.min.y + bounds.size.y * 0.1f;

            double sum = 0;
            int count = 0;
            foreach (Vector3 point in points)
            {
                Vector3 p = rotation * point;
                if (p.y <= cut)
                {
                    sum += p.z;
                    count++;
                }
            }

            if (count == 0)
            {
                return 0f;
            }

            float offset = (float)(sum / count) - bounds.center.z;
            if (Mathf.Abs(offset) < bounds.size.z * 0.05f)
            {
                return 0f;
            }

            return offset < 0f ? 1f : -1f;
        }

        /// <summary>
        /// 判断盾牌正面朝 +Z 还是 -Z：盾面是向外鼓的弧形，中间比左右两边更靠前。
        /// 返回 1 表示正面朝 +Z，-1 表示朝 -Z，0 表示无法判断（例如平板盾）。
        /// </summary>
        private static float DetectShieldFront(List<Vector3> points, Quaternion rotation)
        {
            Bounds bounds = BoundsOf(points, rotation);
            float halfWidth = bounds.extents.x;
            if (halfWidth <= 0f)
            {
                return 0f;
            }

            double centerSum = 0, edgeSum = 0;
            int centerCount = 0, edgeCount = 0;

            foreach (Vector3 point in points)
            {
                Vector3 p = rotation * point;
                float side = Mathf.Abs(p.x - bounds.center.x) / halfWidth;
                if (side < 0.3f)
                {
                    centerSum += p.z;
                    centerCount++;
                }
                else if (side > 0.8f)
                {
                    edgeSum += p.z;
                    edgeCount++;
                }
            }

            if (centerCount == 0 || edgeCount == 0)
            {
                return 0f;
            }

            float difference = (float)(centerSum / centerCount - edgeSum / edgeCount);
            if (Mathf.Abs(difference) < bounds.size.z * 0.05f)
            {
                return 0f;
            }

            return difference > 0f ? 1f : -1f;
        }

        private static T FindInScene<T>() where T : Component
        {
            List<T> all = FindAllInScene<T>();
            return all.Count > 0 ? all[0] : null;
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
    }
}
