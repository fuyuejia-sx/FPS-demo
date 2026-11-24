using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FPS
{
    /// <summary>
    /// 运行时导航网格构建：游戏开始时根据场景中的碰撞体生成 NavMesh，供敌人的 NavMeshAgent 寻路。
    /// 使用 Unity 内置 API，不需要安装 AI Navigation 包，也不需要手动烘焙。
    /// 挂载对象：场景中一个独立的空物体（例如 NavMeshBaker），整个场景只需要一个。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)] // 尽早执行，保证敌人启动时 NavMesh 已经存在
    public class RuntimeNavMeshBaker : MonoBehaviour
    {
        [Tooltip("构建范围的尺寸（米），以本物体位置为中心。需要覆盖所有敌人会走到的地面。")]
        [SerializeField] private Vector3 boundsSize = new Vector3(200f, 100f, 200f);

        [Tooltip("哪些层的碰撞体参与构建。默认所有层。")]
        [SerializeField] private LayerMask includedLayers = ~0;

        private NavMeshDataInstance _instance;

        private void Awake()
        {
            Build();
        }

        private void OnDestroy()
        {
            _instance.Remove();
        }

        /// <summary>重新构建导航网格。场景地形发生较大变化后可以调用。</summary>
        public void Build()
        {
            _instance.Remove();

            var bounds = new Bounds(transform.position, boundsSize);
            var sources = new List<NavMeshBuildSource>();

            NavMeshBuilder.CollectSources(
                bounds, includedLayers, NavMeshCollectGeometry.PhysicsColliders,
                0, new List<NavMeshBuildMarkup>(), sources);

            // 角色自身的碰撞体和触发器不算地形
            sources.RemoveAll(IsNotTerrain);

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(
                settings, sources, bounds, Vector3.zero, Quaternion.identity);

            if (data == null)
            {
                Debug.LogError($"{nameof(RuntimeNavMeshBaker)}: 导航网格构建失败。", this);
                return;
            }

            _instance = NavMesh.AddNavMeshData(data);
        }

        private static bool IsNotTerrain(NavMeshBuildSource source)
        {
            Component component = source.component;
            if (component == null)
            {
                return false;
            }

            if (component is Collider collider && collider.isTrigger)
            {
                return true;
            }

            return component.GetComponentInParent<NavMeshAgent>() != null
                || component.GetComponentInParent<CharacterController>() != null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position, boundsSize);
        }
    }
}
