using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace FPS.EditorTools
{
    /// <summary>
    /// 编辑器工具：菜单 FPS > 添加带骨骼的敌人 / 更新带骨骼的敌人动画与材质。
    /// 用 Assets/FPS/Animations 下带骨骼的模型和动画（Zombie Walk.fbx、Zombie Attack.fbx、Zombie Death.fbx）
    /// 在场景里添加或同步会播放走路、攻击、死亡动画的敌人：
    /// 自动设置模型的导入方式（Generic 骨骼、走路循环、攻击和死亡单次播放）、生成/更新 Animator Controller、
    /// 应用 texture_diffuse.png 贴图材质，并写入步速、攻击时长/命中延迟和死亡销毁延迟。
    /// 敌人的根物体和普通敌人一样（胶囊碰撞体 + Health + NavMeshAgent + EnemyAI），模型挂在名为 Model 的子物体下。
    /// 每执行一次菜单添加一个；FPS > 一键配置场景 会在没有带骨骼的敌人时添加一个，已有时自动同步动画与材质。
    /// 本脚本只在编辑器中运行，不会进入最终的游戏包。
    /// </summary>
    public static class FPSRiggedEnemy
    {
        private const string MenuPath = "FPS/添加带骨骼的敌人";
        private const string SyncMenuPath = "FPS/更新带骨骼的敌人动画与材质";

        private const string AnimationDir = "Assets/FPS/Animations";
        private const string PreferredModelPath = AnimationDir + "/Zombie Walk.fbx";
        private const string AttackClipPath = AnimationDir + "/Zombie Attack.fbx";
        private const string DeathClipPath = AnimationDir + "/Zombie Death.fbx";
        private const string DiffuseTexturePath = AnimationDir + "/texture_diffuse.png";
        private const string ControllerPath = AnimationDir + "/RiggedEnemy.controller";
        private const string MaterialPath = "Assets/FPS/Materials/RiggedEnemy.mat";

        private const string ModelChildName = "Model";
        private const string SpeedParameter = "Speed";
        private const string WalkCycleParameter = "WalkCycle";
        private const string AttackParameter = "Attack";
        private const string DieParameter = "Die";

        /// <summary>模型的高度（米），略小于 2 米高的胶囊碰撞体。</summary>
        private const float EnemyHeight = 1.9f;

        /// <summary>移动速度是走路动画原速的多少倍。僵尸走路的原速很慢，适当加快才追得上玩家。</summary>
        private const float MoveSpeedFactor = 3f;

        /// <summary>测量不出步速时使用的默认值（米/秒）。</summary>
        private const float FallbackWalkSpeed = 0.5f;

        /// <summary>攻击动画播放到这个比例时造成伤害（对应挥击打中的瞬间）。</summary>
        private const float AttackHitFraction = 0.45f;

        /// <summary>死亡动画播完后尸体在地上多停留的时间（秒）。</summary>
        private const float DeathLingerTime = 0.5f;

        [MenuItem(MenuPath, true)]
        [MenuItem(SyncMenuPath, true)]
        private static bool CanAdd()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem(MenuPath)]
        private static void AddFromMenu()
        {
            Undo.SetCurrentGroupName("FPS 添加带骨骼的敌人");
            var log = new List<string>();
            GameObject enemy = AddRiggedEnemy(log);

            string summary = string.Join("\n- ", log);
            if (enemy == null)
            {
                EditorUtility.DisplayDialog("FPS 带骨骼的敌人", "没有添加敌人：\n\n- " + summary, "确定");
                return;
            }

            Selection.activeGameObject = enemy;
            Debug.Log("[FPS 带骨骼的敌人] " + summary);
            EditorUtility.DisplayDialog("FPS 带骨骼的敌人",
                "已完成：\n\n- " + summary + "\n\n请按 Ctrl+S 保存场景。", "确定");
        }

        [MenuItem(SyncMenuPath)]
        private static void SyncFromMenu()
        {
            Undo.SetCurrentGroupName("FPS 更新带骨骼的敌人动画与材质");
            var log = new List<string>();
            bool updated = SyncExistingRiggedEnemies(log);

            string summary = log.Count > 0 ? string.Join("\n- ", log) : "没有找到需要更新的带骨骼敌人。";
            Debug.Log("[FPS 带骨骼的敌人] " + summary);
            EditorUtility.DisplayDialog("FPS 带骨骼的敌人",
                (updated ? "已完成同步：\n\n- " + summary + "\n\n请按 Ctrl+S 保存场景。" : summary), "确定");
        }

        /// <summary>场景里是否已经有带骨骼的敌人。</summary>
        internal static bool IsRigged(EnemyAI enemy)
        {
            return enemy != null && enemy.GetComponentInChildren<Animator>(true) != null;
        }

        /// <summary>场景里还没有带骨骼的敌人、并且动画文件存在时，添加一个；已有时同步其动画与材质。供一键配置场景调用。</summary>
        internal static void EnsureRiggedEnemy(List<string> log)
        {
            bool hasRigged = false;
            foreach (EnemyAI enemy in FindAllInScene<EnemyAI>())
            {
                if (IsRigged(enemy))
                {
                    hasRigged = true;
                    break;
                }
            }

            if (hasRigged)
            {
                SyncExistingRiggedEnemies(log);
                return;
            }

            if (FindRiggedModelPath() != null)
            {
                AddRiggedEnemy(log);
            }
        }

        /// <summary>同步 Animator Controller、材质，以及场景中所有已有带骨骼敌人的攻击/死亡动画时长参数。</summary>
        internal static bool SyncExistingRiggedEnemies(List<string> log)
        {
            AssetDatabase.Refresh();

            string modelPath = FindRiggedModelPath();
            if (modelPath == null)
            {
                log.Add("在 " + AnimationDir + " 下没有找到带骨骼的模型（需要包含蒙皮网格的 .fbx）");
                return false;
            }

            ConfigureImport(modelPath, true);
            AnimationClip walkClip = FindClip(modelPath);
            if (walkClip == null)
            {
                log.Add(modelPath + " 里没有找到走路动画片段");
                return false;
            }

            AnimationClip attackClip = LoadOptionalClip(AttackClipPath, false);
            AnimationClip deathClip = LoadOptionalClip(DeathClipPath, false);

            AnimatorController controller = GetOrUpdateController(walkClip, attackClip, deathClip);
            Material material = GetOrCreateMaterial();

            int count = 0;
            foreach (EnemyAI ai in FindAllInScene<EnemyAI>())
            {
                if (!IsRigged(ai))
                {
                    continue;
                }

                Animator animator = ai.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    Undo.RecordObject(animator, "Sync Rigged Enemy Animator");
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;
                }

                if (material != null)
                {
                    ApplyMaterialToRenderers(ai.transform, material);
                }

                Health health = ai.GetComponent<Health>();
                ApplyCombatAnimationTiming(ai, health, attackClip, deathClip);
                count++;
            }

            if (count > 0)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            log.Add($"已同步 Animator Controller（Walk"
                    + (attackClip != null ? $" / Attack {attackClip.length:F2}s" : "")
                    + (deathClip != null ? $" / Death {deathClip.length:F2}s" : "") + "）");
            if (material != null)
            {
                log.Add(material.mainTexture != null
                    ? $"已更新材质 {MaterialPath}（贴图 {material.mainTexture.name}）"
                    : $"已确认材质 {MaterialPath}");
            }

            if (count > 0)
            {
                log.Add($"已更新场景中 {count} 个带骨骼敌人的动画引用与时长参数");
            }

            return true;
        }

        /// <summary>在当前场景里添加一个带骨骼、会播放走路/攻击/死亡动画的敌人。失败时返回 null，原因写入 log。</summary>
        internal static GameObject AddRiggedEnemy(List<string> log)
        {
            AssetDatabase.Refresh();

            string modelPath = FindRiggedModelPath();
            if (modelPath == null)
            {
                log.Add("在 " + AnimationDir + " 下没有找到带骨骼的模型（需要包含蒙皮网格的 .fbx）");
                return null;
            }

            ConfigureImport(modelPath, true);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            AnimationClip clip = FindClip(modelPath);
            if (prefab == null || clip == null)
            {
                log.Add(modelPath + " 里没有找到动画片段");
                return null;
            }

            AnimationClip attackClip = LoadOptionalClip(AttackClipPath, false);
            AnimationClip deathClip = LoadOptionalClip(DeathClipPath, false);

            AnimatorController controller = GetOrUpdateController(clip, attackClip, deathClip);
            Measure(prefab, clip, out Bounds bounds, out float rawWalkSpeed);

            float scale = bounds.size.y > 0.0001f ? EnemyHeight / bounds.size.y : 1f;
            float walkSpeed = rawWalkSpeed > 0.01f ? rawWalkSpeed * scale : FallbackWalkSpeed;
            float moveSpeed = Mathf.Clamp(walkSpeed * MoveSpeedFactor, 0.8f, 3.5f);

            // ---------- 根物体：和普通敌人一样的一套组件 ----------
            List<EnemyAI> existing = FindAllInScene<EnemyAI>();
            PlayerMovement player = FindInScene<PlayerMovement>();
            bool placed = TryFindSpot(existing, player, out Vector3 feet);

            Transform parent = existing.Count > 0 ? existing[0].transform.parent : null;
            if (parent == null)
            {
                GameObject group = GameObject.Find("Enemies");
                if (group == null)
                {
                    group = new GameObject("Enemies");
                    Undo.RegisterCreatedObjectUndo(group, "Create Enemies");
                }

                parent = group.transform;
            }

            GameObject enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Undo.RegisterCreatedObjectUndo(enemy, "Create Rigged Enemy");
            enemy.name = UniqueName(parent, "Enemy_Rigged");
            Object.DestroyImmediate(enemy.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(enemy.GetComponent<MeshFilter>());

            enemy.transform.SetParent(parent, true);
            enemy.transform.position = feet + Vector3.up; // 胶囊高 2 米，轴心在中心

            if (player != null)
            {
                Vector3 toPlayer = player.transform.position - enemy.transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.001f)
                {
                    enemy.transform.rotation = Quaternion.LookRotation(toPlayer);
                }
            }

            CapsuleCollider capsule = enemy.GetComponent<CapsuleCollider>();
            capsule.radius = 0.4f;

            Health health = enemy.AddComponent<Health>();
            SetProperty(health, "destroyOnDeath", p => p.boolValue = true);

            NavMeshAgent agent = enemy.AddComponent<NavMeshAgent>();
            agent.height = 2f;
            agent.radius = 0.5f;
            agent.baseOffset = 1f;
            agent.angularSpeed = 360f;
            agent.acceleration = 20f;
            // 导航网格在运行时才生成，Agent 先禁用，由 EnemyAI 在开始时启用
            agent.enabled = false;

            EnemyAI ai = enemy.AddComponent<EnemyAI>();

            // ---------- 模型 ----------
            var holder = new GameObject(ModelChildName);
            holder.transform.SetParent(enemy.transform, false);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            holder.transform.localScale = Vector3.one * scale;
            // 水平方向用模型自己的原点（两脚之间）对齐胶囊中心；脚底落在地面上
            holder.transform.localPosition = new Vector3(0f, -agent.baseOffset - bounds.min.y * scale, 0f);

            Animator animator = instance.GetComponent<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; // 位移由 NavMeshAgent 负责
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            Material material = GetOrCreateMaterial();
            if (material != null)
            {
                ApplyMaterialToRenderers(instance.transform, material);
            }

            SetProperty(ai, "animator", p => p.objectReferenceValue = animator);
            SetProperty(ai, "walkAnimationSpeed", p => p.floatValue = walkSpeed);
            SetProperty(ai, "moveSpeed", p => p.floatValue = moveSpeed);
            ApplyCombatAnimationTiming(ai, health, attackClip, deathClip);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            log.Add($"添加 {enemy.name}：模型 {Path.GetFileName(modelPath)}，走路 {clip.name}（{clip.length:F1} 秒，循环）"
                    + (attackClip != null ? $"，攻击（{attackClip.length:F1} 秒）" : "")
                    + (deathClip != null ? $"，死亡（{deathClip.length:F1} 秒）" : ""));
            log.Add(rawWalkSpeed > 0.01f
                ? $"走路动画步速 {walkSpeed:F2} 米/秒，移动速度设为 {moveSpeed:F2} 米/秒"
                : $"没能测出走路动画的步速，按 {walkSpeed:F2} 米/秒处理，移动速度设为 {moveSpeed:F2} 米/秒");

            if (!placed)
            {
                log.Add($"{enemy.name}：附近没有找到空的地面，位置需要手动调整");
            }

            return enemy;
        }

        // =====================================================================
        // 模型导入
        // =====================================================================

        private static string FindRiggedModelPath()
        {
            if (HasSkinnedMesh(PreferredModelPath))
            {
                return PreferredModelPath;
            }

            if (!AssetDatabase.IsValidFolder(AnimationDir))
            {
                return null;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { AnimationDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (HasSkinnedMesh(path))
                {
                    return path;
                }
            }

            return null;
        }

        private static bool HasSkinnedMesh(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab != null && prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
        }

        /// <summary>骨骼类型设为 Generic（同一套 Mixamo 骨骼无需重定向），按需设置循环播放。</summary>
        private static void ConfigureImport(string path, bool loop)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
            {
                return;
            }

            bool changed = false;

            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                changed = true;
            }

            if (importer.avatarSetup == ModelImporterAvatarSetup.NoAvatar)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }

            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                changed = true;
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            bool clipsChanged = false;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                if (clip.loopTime != loop)
                {
                    clip.loopTime = loop;
                    clipsChanged = true;
                }
            }

            if (clipsChanged)
            {
                importer.clipAnimations = clips;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        private static AnimationClip LoadOptionalClip(string path, bool loop)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            ConfigureImport(path, loop);
            return FindClip(path);
        }

        private static void ApplyCombatAnimationTiming(
            EnemyAI ai, Health health, AnimationClip attackClip, AnimationClip deathClip)
        {
            if (ai != null && attackClip != null && attackClip.length > 0f)
            {
                float duration = attackClip.length;
                float hitDelay = Mathf.Round(duration * AttackHitFraction * 100f) / 100f;
                SetProperty(ai, "attackDuration", p => p.floatValue = duration);
                SetProperty(ai, "attackHitDelay", p => p.floatValue = hitDelay);
            }

            if (health != null && deathClip != null && deathClip.length > 0f)
            {
                float destroyDelay = Mathf.Round((deathClip.length + DeathLingerTime) * 100f) / 100f;
                SetProperty(health, "destroyOnDeath", p => p.boolValue = true);
                SetProperty(health, "destroyDelay", p => p.floatValue = destroyDelay);
            }
        }

        private static void ApplyMaterialToRenderers(Transform root, Material material)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Undo.RecordObject(renderer, "Apply Rigged Enemy Material");
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }

                renderer.sharedMaterials = materials;
            }
        }

        private static AnimationClip FindClip(string path)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    return clip;
                }
            }

            return null;
        }

        // =====================================================================
        // Animator Controller
        // =====================================================================

        /// <summary>
        /// 状态机：Idle / Walk 按 Speed 切换（Walk 播放速度由 WalkCycle 控制）；
        /// 有攻击动画时增加 Attack 触发器与状态（Idle/Walk -> Attack -> Walk/Idle）；
        /// 有死亡动画时增加 Die 触发器与 Death 状态（AnyState -> Death，播完保持倒地直到销毁）。
        /// 在已有 Controller 资产上就地更新，不破坏场景里的资产引用。
        /// </summary>
        private static AnimatorController GetOrUpdateController(
            AnimationClip walkClip, AnimationClip attackClip, AnimationClip deathClip)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            EnsureParameter(controller, SpeedParameter, AnimatorControllerParameterType.Float, 0f);
            EnsureParameter(controller, WalkCycleParameter, AnimatorControllerParameterType.Float, 1f);
            if (attackClip != null)
            {
                EnsureParameter(controller, AttackParameter, AnimatorControllerParameterType.Trigger, 0f);
            }

            if (deathClip != null)
            {
                EnsureParameter(controller, DieParameter, AnimatorControllerParameterType.Trigger, 0f);
            }

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState idle = FindOrCreateState(machine, "Idle", new Vector3(200f, 0f, 0f));
            idle.motion = walkClip;
            idle.speed = 0f;

            AnimatorState walk = FindOrCreateState(machine, "Walk", new Vector3(235f, 65f, 0f));
            walk.motion = walkClip;
            walk.speed = 1f;
            walk.speedParameter = WalkCycleParameter;
            walk.speedParameterActive = true;

            machine.defaultState = idle;

            ClearTransitions(idle);
            ClearTransitions(walk);

            if (attackClip != null)
            {
                AnimatorState attack = FindOrCreateState(machine, "Attack", new Vector3(480f, 30f, 0f));
                attack.motion = attackClip;
                attack.speed = 1f;
                attack.speedParameterActive = false;
                ClearTransitions(attack);

                AnimatorStateTransition idleToAttack = idle.AddTransition(attack);
                idleToAttack.hasExitTime = false;
                idleToAttack.duration = 0.15f;
                idleToAttack.AddCondition(AnimatorConditionMode.If, 0f, AttackParameter);

                AnimatorStateTransition walkToAttack = walk.AddTransition(attack);
                walkToAttack.hasExitTime = false;
                walkToAttack.duration = 0.15f;
                walkToAttack.AddCondition(AnimatorConditionMode.If, 0f, AttackParameter);

                AnimatorStateTransition attackToWalk = attack.AddTransition(walk);
                attackToWalk.hasExitTime = true;
                attackToWalk.exitTime = 0.9f;
                attackToWalk.duration = 0.15f;
                attackToWalk.AddCondition(AnimatorConditionMode.Greater, 0.15f, SpeedParameter);

                AnimatorStateTransition attackToIdle = attack.AddTransition(idle);
                attackToIdle.hasExitTime = true;
                attackToIdle.exitTime = 0.9f;
                attackToIdle.duration = 0.15f;
            }

            AnimatorStateTransition toWalk = idle.AddTransition(walk);
            toWalk.hasExitTime = false;
            toWalk.duration = 0.2f;
            toWalk.AddCondition(AnimatorConditionMode.Greater, 0.15f, SpeedParameter);

            AnimatorStateTransition toIdle = walk.AddTransition(idle);
            toIdle.hasExitTime = false;
            toIdle.duration = 0.25f;
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, SpeedParameter);

            foreach (AnimatorStateTransition existing in machine.anyStateTransitions)
            {
                machine.RemoveAnyStateTransition(existing);
            }

            if (deathClip != null)
            {
                AnimatorState death = FindOrCreateState(machine, "Death", new Vector3(260f, -100f, 0f));
                death.motion = deathClip;
                death.speed = 1f;
                death.speedParameterActive = false;
                ClearTransitions(death);

                AnimatorStateTransition anyToDeath = machine.AddAnyStateTransition(death);
                anyToDeath.hasExitTime = false;
                anyToDeath.duration = 0.1f;
                anyToDeath.canTransitionToSelf = false;
                anyToDeath.AddCondition(AnimatorConditionMode.If, 0f, DieParameter);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void EnsureParameter(
            AnimatorController controller, string name, AnimatorControllerParameterType type, float defaultFloat)
        {
            foreach (AnimatorControllerParameter parameter in controller.parameters)
            {
                if (parameter.name == name)
                {
                    return;
                }
            }

            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = type,
                defaultFloat = defaultFloat
            });
        }

        private static AnimatorState FindOrCreateState(AnimatorStateMachine machine, string name, Vector3 position)
        {
            foreach (ChildAnimatorState child in machine.states)
            {
                if (child.state != null && child.state.name == name)
                {
                    return child.state;
                }
            }

            return machine.AddState(name, position);
        }

        private static void ClearTransitions(AnimatorState state)
        {
            foreach (AnimatorStateTransition transition in state.transitions)
            {
                state.RemoveTransition(transition);
            }
        }

        // =====================================================================
        // 测量模型尺寸和走路动画的步速
        // =====================================================================

        /// <summary>
        /// 在一个临时实例上测量：bounds 是模型未缩放时的包围盒；
        /// walkSpeed 是走路动画原速播放时的步速（未缩放，米/秒）——动画是原地走的，
        /// 着地的那只脚相对身体向后滑动的速度就是步速。测不出来时为 0。
        /// </summary>
        private static void Measure(GameObject prefab, AnimationClip clip, out Bounds bounds, out float walkSpeed)
        {
            GameObject temp = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            temp.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                bounds = MeshBounds(temp);

                var feet = new List<Transform>();
                foreach (Transform bone in temp.GetComponentsInChildren<Transform>(true))
                {
                    if (bone.name.EndsWith("LeftFoot") || bone.name.EndsWith("RightFoot"))
                    {
                        feet.Add(bone);
                    }
                }

                walkSpeed = 0f;
                if (feet.Count == 0 || clip.length <= 0f)
                {
                    return;
                }

                const int samples = 120;
                float step = clip.length / samples;
                var positions = new Vector3[feet.Count, samples + 1];

                for (int s = 0; s <= samples; s++)
                {
                    clip.SampleAnimation(temp, s * step);
                    for (int f = 0; f < feet.Count; f++)
                    {
                        positions[f, s] = feet[f].position;
                    }
                }

                double sum = 0;
                int count = 0;
                float tolerance = bounds.size.y * 0.02f;

                for (int f = 0; f < feet.Count; f++)
                {
                    float lowest = float.MaxValue;
                    for (int s = 0; s <= samples; s++)
                    {
                        lowest = Mathf.Min(lowest, positions[f, s].y);
                    }

                    for (int s = 0; s < samples; s++)
                    {
                        Vector3 a = positions[f, s], b = positions[f, s + 1];
                        if (a.y > lowest + tolerance || b.y > lowest + tolerance)
                        {
                            continue;
                        }

                        sum += new Vector2(b.x - a.x, b.z - a.z).magnitude / step;
                        count++;
                    }
                }

                walkSpeed = count > 0 ? (float)(sum / count) : 0f;
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        /// <summary>
        /// 按实际顶点计算包围盒。蒙皮网格的 Renderer.bounds 留有很大的余量，直接用会让模型偏小并且悬空。
        /// </summary>
        private static Bounds MeshBounds(GameObject target)
        {
            bool any = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            var baked = new Mesh();

            foreach (SkinnedMeshRenderer renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null)
                {
                    continue;
                }

                renderer.BakeMesh(baked, true);
                Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
                foreach (Vector3 vertex in baked.vertices)
                {
                    Vector3 point = toWorld.MultiplyPoint3x4(vertex);
                    if (any)
                    {
                        bounds.Encapsulate(point);
                    }
                    else
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                }
            }

            Object.DestroyImmediate(baked);

            if (!any)
            {
                foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
                {
                    if (any)
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                    else
                    {
                        bounds = renderer.bounds;
                        any = true;
                    }
                }
            }

            return any ? bounds : new Bounds(Vector3.zero, Vector3.one);
        }

        // =====================================================================
        // 位置
        // =====================================================================

        /// <summary>
        /// 在已有敌人旁边（没有敌人时在玩家前方）找一块空地：脚下有地面、站得下、
        /// 和其他敌人至少隔 3 米、离玩家至少 8 米。feet 是脚底位置。
        /// </summary>
        private static bool TryFindSpot(List<EnemyAI> enemies, PlayerMovement player, out Vector3 feet)
        {
            Physics.SyncTransforms();

            Vector3 anchor;
            if (enemies.Count > 0)
            {
                NavMeshAgent agent = enemies[0].GetComponent<NavMeshAgent>();
                anchor = enemies[0].transform.position - Vector3.up * (agent != null ? agent.baseOffset : 1f);
            }
            else if (player != null)
            {
                CharacterController controller = player.GetComponent<CharacterController>();
                float feetOffset = controller != null ? controller.center.y - controller.height * 0.5f : -1f;
                anchor = player.transform.position + Vector3.up * feetOffset + player.transform.forward * 12f;
            }
            else
            {
                anchor = Vector3.zero;
            }

            feet = anchor + new Vector3(3f, 0f, 0f);

            const float cell = 1.5f;
            const int rings = 8;
            float best = float.MaxValue;
            bool found = false;

            for (int ix = -rings; ix <= rings; ix++)
            {
                for (int iz = -rings; iz <= rings; iz++)
                {
                    var candidate = new Vector3(anchor.x + ix * cell, anchor.y, anchor.z + iz * cell);
                    float distance = (candidate - anchor).sqrMagnitude;
                    if (distance >= best)
                    {
                        continue;
                    }

                    if (!Physics.Raycast(candidate + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit,
                            3f, ~0, QueryTriggerInteraction.Ignore)
                        || hit.normal.y < 0.7f
                        || Mathf.Abs(hit.point.y - anchor.y) > 0.5f
                        || hit.collider.GetComponentInParent<EnemyAI>() != null
                        || hit.collider.GetComponentInParent<PlayerMovement>() != null)
                    {
                        continue;
                    }

                    Vector3 point = hit.point;
                    if (Physics.CheckCapsule(point + Vector3.up * 0.75f, point + Vector3.up * 1.5f,
                            0.5f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }

                    if (player != null && HorizontalDistance(player.transform.position, point) < 8f)
                    {
                        continue;
                    }

                    bool crowded = false;
                    foreach (EnemyAI other in enemies)
                    {
                        if (HorizontalDistance(other.transform.position, point) < 3f)
                        {
                            crowded = true;
                            break;
                        }
                    }

                    if (crowded)
                    {
                        continue;
                    }

                    best = distance;
                    feet = point;
                    found = true;
                }
            }

            return found;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        // =====================================================================
        // 通用
        // =====================================================================

        /// <summary>优先使用 Assets/FPS/Animations/texture_diffuse.png 贴图；没有贴图时退回到纯色材质。</summary>
        private static Material GetOrCreateMaterial()
        {
            Texture2D diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(DiffuseTexturePath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool created = false;

            if (material == null)
            {
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

                material = new Material(shader)
                {
                    color = diffuse != null ? Color.white : new Color(0.42f, 0.5f, 0.38f)
                };
                if (material.HasProperty("_Smoothness"))
                {
                    material.SetFloat("_Smoothness", 0.25f);
                }

                AssetDatabase.CreateAsset(material, MaterialPath);
                created = true;
            }

            bool dirty = created;
            if (diffuse != null)
            {
                if (material.mainTexture != diffuse)
                {
                    material.mainTexture = diffuse;
                    dirty = true;
                }

                if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != diffuse)
                {
                    material.SetTexture("_BaseMap", diffuse);
                    dirty = true;
                }

                if (material.color != Color.white)
                {
                    material.color = Color.white;
                    dirty = true;
                }

                if (material.HasProperty("_BaseColor") && material.GetColor("_BaseColor") != Color.white)
                {
                    material.SetColor("_BaseColor", Color.white);
                    dirty = true;
                }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            }

            return material;
        }

        private static string UniqueName(Transform parent, string baseName)
        {
            if (parent.Find(baseName) == null)
            {
                return baseName;
            }

            for (int i = 2; ; i++)
            {
                string name = baseName + "_" + i;
                if (parent.Find(name) == null)
                {
                    return name;
                }
            }
        }

        private static void SetProperty(Object target, string propertyName, System.Action<SerializedProperty> set)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"[FPS 带骨骼的敌人] {target.GetType().Name} 上没有字段 {propertyName}。");
                return;
            }

            set(property);
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
