using UnityEngine;

namespace FPS
{
    /// <summary>
    /// 武器表现模块：枪口闪光、枪身后坐力、命中标记和开火/换弹音效。
    /// 只监听 Weapon 和 WeaponAmmo 的事件，不影响射击逻辑；去掉本组件武器照常工作。
    /// 挂载对象：武器物体（与 Weapon 同一物体）。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Weapon))]
    public class WeaponEffects : MonoBehaviour
    {
        [Header("枪口闪光")]
        [Tooltip("开火时短暂点亮的灯光，一般是枪口位置的点光源。可留空。")]
        [SerializeField] private Light muzzleLight;

        [Tooltip("闪光持续时间（秒）。")]
        [SerializeField, Min(0f)] private float flashDuration = 0.05f;

        [Header("后坐力（只作用于枪模型，不影响准星）")]
        [Tooltip("每发子弹枪身后退的距离（米）。")]
        [SerializeField, Min(0f)] private float kickBack = 0.04f;

        [Tooltip("每发子弹枪口上抬的角度（度）。")]
        [SerializeField, Min(0f)] private float kickUp = 3f;

        [Tooltip("连射时后坐力最多累积到单发的多少倍。")]
        [SerializeField, Min(1f)] private float maxKickMultiplier = 2.5f;

        [Tooltip("恢复速度，越大枪身回位越快。")]
        [SerializeField, Min(0.1f)] private float recoverSpeed = 14f;

        [Header("命中标记")]
        [Tooltip("是否在命中点显示标记。")]
        [SerializeField] private bool showImpacts = true;

        [Tooltip("命中标记的预制体。留空时使用一个小球作为临时标记。")]
        [SerializeField] private GameObject impactPrefab;

        [Tooltip("命中标记的存在时间（秒）。")]
        [SerializeField, Min(0f)] private float impactLifetime = 0.4f;

        [Tooltip("同时存在的命中标记数量上限。")]
        [SerializeField, Range(1, 64)] private int impactPoolSize = 12;

        [Header("音效（可选）")]
        [Tooltip("播放音效的 AudioSource。可留空。")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("开火音效。可留空。")]
        [SerializeField] private AudioClip fireClip;

        [Tooltip("换弹音效。可留空。")]
        [SerializeField] private AudioClip reloadClip;

        private Weapon _weapon;
        private WeaponAmmo _ammo;

        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private float _kick; // 当前后坐力强度，1 表示单发的量
        private float _flashOffTime;

        private GameObject[] _impacts;
        private float[] _impactExpireTimes;
        private int _nextImpact;

        private void Awake()
        {
            _weapon = GetComponent<Weapon>();
            _ammo = GetComponent<WeaponAmmo>();

            _restPosition = transform.localPosition;
            _restRotation = transform.localRotation;

            if (muzzleLight != null)
            {
                muzzleLight.enabled = false;
            }
        }

        private void OnEnable()
        {
            _weapon.Fired += OnFired;
            _weapon.HitSomething += OnHit;

            if (_ammo != null)
            {
                _ammo.ReloadStarted += OnReloadStarted;
            }
        }

        private void OnDisable()
        {
            _weapon.Fired -= OnFired;
            _weapon.HitSomething -= OnHit;

            if (_ammo != null)
            {
                _ammo.ReloadStarted -= OnReloadStarted;
            }

            // 恢复初始姿态，避免停在后坐中途
            _kick = 0f;
            transform.localPosition = _restPosition;
            transform.localRotation = _restRotation;

            if (muzzleLight != null)
            {
                muzzleLight.enabled = false;
            }

            // 武器收起后 Update 不再执行，这里直接清掉还没消失的命中标记
            if (_impacts != null)
            {
                foreach (GameObject impact in _impacts)
                {
                    if (impact != null)
                    {
                        impact.SetActive(false);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (_impacts == null)
            {
                return;
            }

            foreach (GameObject impact in _impacts)
            {
                if (impact != null)
                {
                    Destroy(impact);
                }
            }
        }

        private void Update()
        {
            if (muzzleLight != null && muzzleLight.enabled && Time.time >= _flashOffTime)
            {
                muzzleLight.enabled = false;
            }

            if (_kick > 0f)
            {
                _kick = Mathf.Lerp(_kick, 0f, recoverSpeed * Time.deltaTime);
                if (_kick < 0.001f)
                {
                    _kick = 0f;
                }

                transform.localPosition = _restPosition + Vector3.back * (kickBack * _kick);
                transform.localRotation = _restRotation * Quaternion.Euler(-kickUp * _kick, 0f, 0f);
            }

            UpdateImpacts();
        }

        private void OnFired()
        {
            if (muzzleLight != null && flashDuration > 0f)
            {
                muzzleLight.enabled = true;
                _flashOffTime = Time.time + flashDuration;
            }

            _kick = Mathf.Min(_kick + 1f, maxKickMultiplier);

            PlayClip(fireClip);
        }

        private void OnReloadStarted()
        {
            PlayClip(reloadClip);
        }

        private void PlayClip(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        // ---- 命中标记：用固定数量的对象循环复用，避免频繁创建和销毁 ----

        private void OnHit(RaycastHit hit)
        {
            if (!showImpacts || impactLifetime <= 0f)
            {
                return;
            }

            EnsureImpactPool();

            GameObject impact = _impacts[_nextImpact];
            impact.transform.SetPositionAndRotation(
                hit.point + hit.normal * 0.01f, Quaternion.LookRotation(hit.normal));
            impact.SetActive(true);

            _impactExpireTimes[_nextImpact] = Time.time + impactLifetime;
            _nextImpact = (_nextImpact + 1) % _impacts.Length;
        }

        private void EnsureImpactPool()
        {
            if (_impacts != null)
            {
                return;
            }

            _impacts = new GameObject[impactPoolSize];
            _impactExpireTimes = new float[impactPoolSize];

            for (int i = 0; i < _impacts.Length; i++)
            {
                GameObject impact = impactPrefab != null ? Instantiate(impactPrefab) : CreateDefaultImpact();
                impact.name = $"Impact_{i}";
                impact.SetActive(false);
                _impacts[i] = impact;
            }
        }

        private static GameObject CreateDefaultImpact()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.localScale = Vector3.one * 0.08f;

            // 标记只用于显示，不能挡子弹或挡路
            Collider collider = sphere.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }

            return sphere;
        }

        private void UpdateImpacts()
        {
            if (_impacts == null)
            {
                return;
            }

            for (int i = 0; i < _impacts.Length; i++)
            {
                GameObject impact = _impacts[i];
                if (impact != null && impact.activeSelf && Time.time >= _impactExpireTimes[i])
                {
                    impact.SetActive(false);
                }
            }
        }
    }
}
