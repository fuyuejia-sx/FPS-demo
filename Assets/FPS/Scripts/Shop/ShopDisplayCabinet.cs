using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS
{
    /// <summary>
    /// 实时 3D 陈列柜：在远离主场景的独立展示空间里搭建带灯光、底座、隔断与玻璃框的军械陈列柜，
    /// 旋转陈列商品的真实 3D 模型，并通过专用相机渲染到 RenderTexture 供商城界面显示。
    /// 仅在商城打开时启用相机与旋转更新，正常战斗时零额外渲染开销；所有几何体不带碰撞体。
    /// </summary>
    public class ShopDisplayCabinet : MonoBehaviour
    {
        private static readonly Vector3 StudioOrigin = new Vector3(0f, -200f, 0f);

        private const int MinCabinetBays = 3;
        private const float BaySpacing = 1.65f;
        private const float CabinetHeight = 2.05f;
        private const float CabinetDepth = 1.15f;
        private const float PedestalTopY = 0.24f;
        private const float TargetModelExtent = 0.92f;
        private const float AutoRotateSpeed = 28f;

        private sealed class BaySlot
        {
            public Transform Turntable;
            public Vector3 BaseLocalPosition;
            public Renderer RingRenderer;
            public Light BayLight;
            public bool HasItem;
            public float BaseYaw;
        }

        private readonly List<BaySlot> _bays = new List<BaySlot>();
        private readonly List<Material> _runtimeMaterials = new List<Material>();

        private GameObject _studioRoot;
        private Camera _showcaseCamera;
        private RenderTexture _renderTexture;

        private Material _ringSelectedMaterial;
        private Material _ringOwnedMaterial;
        private Material _ringIdleMaterial;

        private int _selectedIndex;
        private bool _isActive;
        private bool _isDragging;
        private float _cameraTargetX;

        /// <summary>实时渲染的陈列柜画面纹理。</summary>
        public RenderTexture OutputTexture => _renderTexture;

        /// <summary>陈列柜当前的展示隔间数量（至少 3 格）。</summary>
        public int BayCount => _bays.Count;

        /// <summary>根据商城商品列表搭建 3D 陈列柜。</summary>
        public void Initialize(IReadOnlyList<ShopItemData> items)
        {
            Cleanup();

            int itemCount = items != null ? items.Count : 0;
            int bayCount = Mathf.Max(MinCabinetBays, itemCount);

            _renderTexture = new RenderTexture(1280, 720, 24)
            {
                name = "ShopCabinetRT",
                antiAliasing = 4
            };
            _renderTexture.Create();

            _studioRoot = new GameObject("ShopDisplayCabinet_Studio");
            _studioRoot.transform.position = StudioOrigin;

            BuildCabinetShell(bayCount);
            BuildStudioLighting(bayCount);
            BuildBays(items, bayCount);
            BuildCamera(bayCount);

            SelectItem(0);
            SetActive(false);
        }

        /// <summary>开启或关闭陈列柜渲染（随商城开关同步）。</summary>
        public void SetActive(bool active)
        {
            _isActive = active;
            if (_studioRoot != null)
            {
                _studioRoot.SetActive(active);
            }

            if (_showcaseCamera != null)
            {
                _showcaseCamera.enabled = active;
            }
        }

        /// <summary>选中并聚焦到指定商品隔间。</summary>
        public void SelectItem(int index)
        {
            if (_bays.Count == 0)
            {
                return;
            }

            _selectedIndex = Mathf.Clamp(index, 0, _bays.Count - 1);

            // 单排 3 格以内时镜头居中展示整个陈列柜，同时略微向选中格倾斜聚焦
            float bayX = BayCenterX(_selectedIndex, _bays.Count);
            _cameraTargetX = _bays.Count <= 3 ? bayX * 0.28f : bayX;

            RefreshBayHighlights(null);
        }

        /// <summary>刷新各个隔间的底座光环状态（选中 / 已拥有 / 待机）。</summary>
        public void RefreshBayHighlights(ShopManager shop)
        {
            for (int i = 0; i < _bays.Count; i++)
            {
                BaySlot bay = _bays[i];
                bool selected = i == _selectedIndex;
                bool owned = shop != null && bay.HasItem && shop.IsOwned(i);

                if (bay.RingRenderer != null)
                {
                    if (owned)
                    {
                        bay.RingRenderer.sharedMaterial = _ringOwnedMaterial;
                    }
                    else if (selected && bay.HasItem)
                    {
                        bay.RingRenderer.sharedMaterial = _ringSelectedMaterial;
                    }
                    else
                    {
                        bay.RingRenderer.sharedMaterial = _ringIdleMaterial;
                    }
                }

                if (bay.BayLight != null)
                {
                    if (!bay.HasItem)
                    {
                        bay.BayLight.intensity = 1.4f;
                    }
                    else if (selected)
                    {
                        bay.BayLight.intensity = 7.5f;
                    }
                    else
                    {
                        bay.BayLight.intensity = 3.5f;
                    }
                }
            }
        }

        /// <summary>用户在陈列柜画面上拖动鼠标时，手动旋转当前选中的展示模型。</summary>
        public void SetDragging(bool dragging)
        {
            _isDragging = dragging;
        }

        public void RotateSelected(float deltaYaw)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _bays.Count)
            {
                return;
            }

            BaySlot bay = _bays[_selectedIndex];
            if (bay.Turntable != null)
            {
                bay.Turntable.Rotate(0f, -deltaYaw, 0f, Space.Self);
            }
        }

        private void Update()
        {
            if (!_isActive || _studioRoot == null)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            float t = Time.unscaledTime;

            for (int i = 0; i < _bays.Count; i++)
            {
                BaySlot bay = _bays[i];
                if (bay.Turntable == null)
                {
                    continue;
                }

                if (!(_isDragging && i == _selectedIndex))
                {
                    float speed = i == _selectedIndex ? AutoRotateSpeed : AutoRotateSpeed * 0.55f;
                    bay.Turntable.Rotate(0f, speed * dt, 0f, Space.Self);
                }

                float hover = Mathf.Sin(t * 2.1f + i * 1.4f) * 0.028f;
                bay.Turntable.localPosition = bay.BaseLocalPosition + Vector3.up * hover;
            }

            if (_showcaseCamera != null)
            {
                Vector3 pos = _showcaseCamera.transform.localPosition;
                pos.x = Mathf.Lerp(pos.x, _cameraTargetX, Mathf.Clamp01(dt * 7f));
                _showcaseCamera.transform.localPosition = pos;
                _showcaseCamera.transform.LookAt(
                    StudioOrigin + new Vector3(_cameraTargetX * 0.65f, 0.92f, 0f), Vector3.up);
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            _bays.Clear();

            if (_studioRoot != null)
            {
                Destroy(_studioRoot);
                _studioRoot = null;
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }

            for (int i = 0; i < _runtimeMaterials.Count; i++)
            {
                if (_runtimeMaterials[i] != null)
                {
                    Destroy(_runtimeMaterials[i]);
                }
            }

            _runtimeMaterials.Clear();
        }

        // =====================================================================
        // 陈列柜 3D 结构搭建
        // =====================================================================

        private void BuildCabinetShell(int bayCount)
        {
            float totalWidth = bayCount * BaySpacing + 0.36f;

            Material bodyMat = CreateLitMaterial(new Color(0.10f, 0.13f, 0.18f), 0.55f, 0.45f);
            Material trimMat = CreateLitMaterial(new Color(0.18f, 0.22f, 0.29f), 0.7f, 0.6f);
            Material backMat = CreateLitMaterial(new Color(0.07f, 0.09f, 0.14f), 0.2f, 0.3f);
            Material glassMat = CreateGlassMaterial(new Color(0.45f, 0.82f, 1f, 0.11f));
            Material accentCyan = CreateEmissiveMaterial(new Color(0.15f, 0.85f, 1f), 2.2f);
            Material accentWarm = CreateEmissiveMaterial(new Color(1f, 0.78f, 0.25f), 1.8f);

            _ringSelectedMaterial = CreateEmissiveMaterial(new Color(0.2f, 0.9f, 1f), 2.6f);
            _ringOwnedMaterial = CreateEmissiveMaterial(new Color(1f, 0.82f, 0.22f), 2.4f);
            _ringIdleMaterial = CreateEmissiveMaterial(new Color(0.22f, 0.30f, 0.42f), 0.5f);

            // 背板、底台与顶棚
            CreateBox("BackWall", new Vector3(0f, CabinetHeight * 0.5f, CabinetDepth * 0.5f),
                new Vector3(totalWidth, CabinetHeight, 0.12f), backMat);
            CreateBox("FloorDeck", new Vector3(0f, 0.04f, 0f),
                new Vector3(totalWidth, 0.12f, CabinetDepth), bodyMat);
            CreateBox("TopCanopy", new Vector3(0f, CabinetHeight, 0f),
                new Vector3(totalWidth + 0.08f, 0.14f, CabinetDepth + 0.08f), bodyMat);

            // 顶棚和底台前沿的发光灯带
            CreateBox("TopLightStrip", new Vector3(0f, CabinetHeight - 0.08f, -CabinetDepth * 0.44f),
                new Vector3(totalWidth - 0.18f, 0.03f, 0.04f), accentCyan);
            CreateBox("BottomTrimStrip", new Vector3(0f, 0.105f, -CabinetDepth * 0.47f),
                new Vector3(totalWidth - 0.18f, 0.02f, 0.03f), accentWarm);

            // 左右边框立柱与各展示格之间的玻璃隔板
            for (int i = 0; i <= bayCount; i++)
            {
                float x = (i - bayCount * 0.5f) * BaySpacing;
                bool outer = i == 0 || i == bayCount;
                float pillarW = outer ? 0.12f : 0.055f;

                CreateBox($"PillarFront_{i}", new Vector3(x, CabinetHeight * 0.5f, -CabinetDepth * 0.46f),
                    new Vector3(pillarW, CabinetHeight, 0.08f), trimMat);
                CreateBox($"PillarBack_{i}", new Vector3(x, CabinetHeight * 0.5f, CabinetDepth * 0.44f),
                    new Vector3(pillarW, CabinetHeight, 0.08f), trimMat);

                if (!outer)
                {
                    CreateBox($"GlassDivider_{i}", new Vector3(x, CabinetHeight * 0.5f, 0f),
                        new Vector3(0.015f, CabinetHeight - 0.18f, CabinetDepth - 0.16f), glassMat);
                }
                else
                {
                    CreateBox($"SideWall_{i}", new Vector3(x, CabinetHeight * 0.5f, 0f),
                        new Vector3(0.08f, CabinetHeight, CabinetDepth), bodyMat);
                }
            }
        }

        private void BuildStudioLighting(int bayCount)
        {
            // 主正面补光 + 冷色侧轮廓光，让枪械细节和金属材质清晰立体
            CreatePointLight("KeyLight", new Vector3(0f, 2.2f, -2.6f),
                new Color(1f, 0.97f, 0.92f), 14f, 12f);
            CreatePointLight("RimLightLeft", new Vector3(-bayCount * 0.9f, 1.8f, -1.2f),
                new Color(0.55f, 0.82f, 1f), 8f, 10f);
            CreatePointLight("RimLightRight", new Vector3(bayCount * 0.9f, 1.8f, -1.2f),
                new Color(1f, 0.85f, 0.65f), 8f, 10f);
        }

        private void BuildBays(IReadOnlyList<ShopItemData> items, int bayCount)
        {
            Material pedestalMat = CreateLitMaterial(new Color(0.16f, 0.20f, 0.27f), 0.75f, 0.65f);
            Material backBadgeMat = CreateLitMaterial(new Color(0.11f, 0.15f, 0.22f), 0.4f, 0.45f);

            int itemCount = items != null ? items.Count : 0;

            for (int i = 0; i < bayCount; i++)
            {
                float x = BayCenterX(i, bayCount);

                // 圆形展台底座 + 顶部发光光环
                CreateCylinder($"Pedestal_{i}", new Vector3(x, 0.16f, 0f),
                    new Vector3(0.95f, 0.08f, 0.95f), pedestalMat);
                Renderer ring = CreateCylinder($"PedestalRing_{i}", new Vector3(x, 0.245f, 0f),
                    new Vector3(0.84f, 0.012f, 0.84f), _ringIdleMaterial);

                // 背板装饰槽
                CreateBox($"BackPanel_{i}", new Vector3(x, 1.05f, CabinetDepth * 0.42f),
                    new Vector3(BaySpacing - 0.28f, 1.35f, 0.03f), backBadgeMat);

                // 每个隔间顶部独立射灯
                Light bayLight = CreatePointLight($"BayLight_{i}",
                    new Vector3(x, CabinetHeight - 0.22f, -0.15f),
                    new Color(0.88f, 0.95f, 1f), 4f, 3.6f);

                var slot = new BaySlot
                {
                    RingRenderer = ring,
                    BayLight = bayLight,
                    HasItem = i < itemCount && items[i] != null
                };

                if (slot.HasItem)
                {
                    GameObject turntable = new GameObject($"Turntable_{i}");
                    turntable.transform.SetParent(_studioRoot.transform, false);
                    slot.BaseLocalPosition = new Vector3(x, 0.96f, -0.02f);
                    turntable.transform.localPosition = slot.BaseLocalPosition;

                    ShopItemData item = items[i];
                    GameObject model = InstantiateDisplayModel(item, turntable.transform);
                    if (model != null)
                    {
                        NormalizeDisplayModel(model, item);
                    }

                    turntable.transform.localRotation = Quaternion.Euler(item.displayRotationOffset);
                    slot.Turntable = turntable.transform;
                    slot.BaseYaw = item.displayRotationOffset.y;
                }

                _bays.Add(slot);
            }
        }

        private void BuildCamera(int bayCount)
        {
            var camObj = new GameObject("ShowcaseCamera");
            camObj.transform.SetParent(_studioRoot.transform, false);

            // 根据陈列柜宽度自动计算合适距离，确保整排 3 格陈列柜饱满入画
            float distance = bayCount <= 3 ? 4.1f : 4.4f;
            camObj.transform.localPosition = new Vector3(0f, 1.08f, -distance);
            camObj.transform.LookAt(StudioOrigin + new Vector3(0f, 0.95f, 0f), Vector3.up);

            _showcaseCamera = camObj.AddComponent<Camera>();
            _showcaseCamera.clearFlags = CameraClearFlags.SolidColor;
            _showcaseCamera.backgroundColor = new Color(0.04f, 0.06f, 0.10f, 1f);
            _showcaseCamera.fieldOfView = 38f;
            _showcaseCamera.nearClipPlane = 0.1f;
            _showcaseCamera.farClipPlane = 15f;
            _showcaseCamera.targetTexture = _renderTexture;
            _showcaseCamera.allowHDR = true;
            _showcaseCamera.depth = -10;
        }

        private GameObject InstantiateDisplayModel(ShopItemData item, Transform parent)
        {
            GameObject source = item.displayModelPrefab;
            if (source == null && item.equipmentObject != null)
            {
                Transform modelChild = item.equipmentObject.transform.Find("Model");
                source = modelChild != null ? modelChild.gameObject : item.equipmentObject;
            }

            if (source == null)
            {
                return null;
            }

            GameObject instance = Instantiate(source, parent, false);
            instance.name = "DisplayModel";
            instance.SetActive(true);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            // 移除一切逻辑脚本、碰撞体、灯光、音频，只保留纯 3D 外观网格（逆序移除避免 RequireComponent 警告）
            Component[] components = instance.GetComponentsInChildren<Component>(true);
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component comp = components[i];
                if (comp == null || comp is Transform || comp is MeshFilter || comp is Renderer)
                {
                    continue;
                }

                Destroy(comp);
            }

            if (item.displayMaterial != null)
            {
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        mats[i] = item.displayMaterial;
                    }

                    renderer.sharedMaterials = mats;
                }
            }

            return instance;
        }

        private static void NormalizeDisplayModel(GameObject model, ShopItemData item)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            float maxAxis = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float scale = maxAxis > 0.0001f ? (TargetModelExtent / maxAxis) * Mathf.Max(0.1f, item.displayScale) : 1f;

            model.transform.localScale = Vector3.one * scale;
            Vector3 localCenter = model.transform.parent.InverseTransformPoint(bounds.center);
            model.transform.localPosition = -localCenter * scale;

            // 如果模型最长边沿 Z 轴（常见枪械），侧转 90 度面向观众，陈列效果更直观
            if (bounds.size.z > bounds.size.x * 1.2f)
            {
                model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                model.transform.localPosition = Quaternion.Euler(0f, 90f, 0f) * (-localCenter * scale);
            }
        }

        private static float BayCenterX(int index, int bayCount)
        {
            return (index - (bayCount - 1) * 0.5f) * BaySpacing;
        }

        private Renderer CreateBox(string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            return CreatePrimitive(PrimitiveType.Cube, name, localPos, scale, mat);
        }

        private Renderer CreateCylinder(string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            return CreatePrimitive(PrimitiveType.Cylinder, name, localPos, scale, mat);
        }

        private Renderer CreatePrimitive(
            PrimitiveType type, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(_studioRoot.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;

            Collider col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && mat != null)
            {
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            return renderer;
        }

        private Light CreatePointLight(string name, Vector3 localPos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_studioRoot.transform, false);
            go.transform.localPosition = localPos;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        private Material CreateLitMaterial(Color color, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            _runtimeMaterials.Add(mat);
            return mat;
        }

        private Material CreateEmissiveMaterial(Color color, float intensity)
        {
            Material mat = CreateLitMaterial(color, 0f, 0.5f);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", color * intensity);
                mat.EnableKeyword("_EMISSION");
            }

            return mat;
        }

        private Material CreateGlassMaterial(Color color)
        {
            Material mat = CreateLitMaterial(color, 0.1f, 0.92f);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }
    }
}
