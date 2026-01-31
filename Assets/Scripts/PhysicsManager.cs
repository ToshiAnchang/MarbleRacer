using UnityEngine;

/// <summary>
/// 마블 레이스 전체의 물리 규칙을 관리하는 매니저.
/// - 전역 Physics 설정 (bounceThreshold 등)
/// - 구슬 / 트랙용 PhysicMaterial 값 관리 (마찰, 탄성, Combine 모드)
/// - 구슬 Rigidbody 기본 세팅 함수 제공
/// 
/// 씬에 1개만 존재하도록 Singleton 패턴으로 구성.
/// 구슬/트랙 스크립트는 이 매니저에서 제공하는
/// PhysicMaterial & 설정만 가져다 씀.
/// </summary>
[DefaultExecutionOrder(-200)]   // 다른 애들(Awake)보다 먼저 실행되게
public class PhysicsManager : MonoBehaviour
{
    public static PhysicsManager Instance { get; private set; }

    [Header("************  Physics Manager  **************")]
    [Header("트랙, 구슬의 물리적인 특성을 조절할수 있습니다.")]
    [Header("*********************************************")]

    // ───────────────────────────────── 전역 물리 설정 ─────────────────────────────────
    [Header("전역 물리 설정")]
    [Tooltip("Unity Physics에서 느린 충돌도 튐을 계산할지 여부를 결정하는 임계치.\n값이 낮을수록 약한 충돌에서도 bounciness가 적용됩니다.")]
    public float bounceThreshold = 0.0f;

    [Tooltip("구슬에 적용할 중력 배수. (1 = 기본 중력)\n구슬 쪽에서 Physics.gravity * globalGravityMultiplier 로 사용합니다.")]
    public float globalGravityMultiplier = 4.0f;

    // ───────────────────────────────── StartFunnel(= 깔대기) PhysicMaterial ─────────────────────────────────
    [Header("깔대기의 PhysicMaterial 특성 셋업")]
    [Tooltip("Assets/Material/StartFunnel_HighFriction 를 여기로 드래그해서 할당하세요.")]
    public PhysicMaterial startMaterial;

    [Tooltip("깔대기 정지 마찰 계수 (staticFriction)")]
    public float startStaticFriction = 0.0f;

    [Tooltip("깔대기 이동 마찰 계수 (dynamicFriction)")]
    public float startDynamicFriction = 0.0f;

    [Range(0f, 1f)]
    [Tooltip("깔대기 탄성 계수 (bounciness). 보통 0으로 둡니다.")]
    public float startBounciness = 0.5f;

    [Tooltip("깔대기 마찰 Combine 모드 - 다른 오브젝트와 만났을때 어느 쪽인가")]
    public PhysicMaterialCombine startFrictionCombine = PhysicMaterialCombine.Maximum;

    [Tooltip("깔대기 탄성 Combine 모드 - 다른 오브젝트와 만났을때 어느 쪽인가")]
    public PhysicMaterialCombine startBounceCombine = PhysicMaterialCombine.Maximum;


    // ───────────────────────────────── 트랙 PhysicMaterial ─────────────────────────────────
    [Header("트랙의 PhysicMaterial 특성 셋업")]
    [Tooltip("Assets/Material/WoodTrack_HighFriction 를 여기로 드래그해서 할당하세요.")]
    public PhysicMaterial trackMaterial;

    [Tooltip("트랙 정지 마찰 계수 (staticFriction)")]
    public float trackStaticFriction = 0.3f;

    [Tooltip("트랙 이동 마찰 계수 (dynamicFriction)")]
    public float trackDynamicFriction = 0.2f;

    [Range(0f, 1f)]
    [Tooltip("트랙 탄성 계수 (bounciness). 보통 0으로 둡니다.")]
    public float trackBounciness = 0.1f;

    [Tooltip("트랙 마찰 Combine 모드 - 다른 오브젝트와 만났을때 어느 쪽인가")]
    public PhysicMaterialCombine trackFrictionCombine = PhysicMaterialCombine.Maximum;

    [Tooltip("트랙 탄성 Combine 모드 - 다른 오브젝트와 만났을때 어느 쪽인가")]
    public PhysicMaterialCombine trackBounceCombine = PhysicMaterialCombine.Minimum;

    // ───────────────────────────────── 구슬 PhysicMaterial ─────────────────────────────────
    [Header("구슬(쇠구슬) PhysicMaterial")]
    [Tooltip("구슬에 쓸 PhysicMaterial.\n비워두면 런타임에 자동 생성 후 아래 값으로 세팅합니다.")]
    public PhysicMaterial marbleMaterial;

    [Tooltip("구슬 정지 마찰 계수 (staticFriction)")]
    public float marbleStaticFriction = 0.1f;

    [Tooltip("구슬 동마찰 계수 (dynamicFriction)")]
    public float marbleDynamicFriction = 0.2f;

    [Range(0f, 1f)]
    [Tooltip("구슬 탄성 계수 (bounciness). 구슬끼리 살짝 튀게 하려면 0.2~0.3 정도.")]
    public float marbleBounciness = 0.0f;

    [Tooltip("구슬 마찰 Combine 모드 - 다른 오브젝트와 만났을때 어느 쪽인가")]
    public PhysicMaterialCombine marbleFrictionCombine = PhysicMaterialCombine.Average;

    [Tooltip("구슬 탄성 Combine 모드 - 다른 오브젝트와 만났을때 어느 쪽인가")]
    public PhysicMaterialCombine marbleBounceCombine = PhysicMaterialCombine.Maximum;

    // ───────────────────────────────── 구슬 Rigidbody 기본값 ─────────────────────────────────
    [Header("구슬 Rigidbody 기본값")]
    [Tooltip("공기저항(선형 Drag)")]
    public float marbleDrag = 0.0f;

    [Tooltip("회전 공기저항(Angular Drag)")]
    public float marbleAngularDrag = 0.03f;

    [Tooltip("구슬 충돌 감지 모드")]
    public CollisionDetectionMode marbleCollisionMode = CollisionDetectionMode.ContinuousDynamic;

    [Tooltip("구슬 보간 옵션 (카메라가 부드럽게 보이도록 Interpolate 추천)")]
    public RigidbodyInterpolation marbleInterpolation = RigidbodyInterpolation.Interpolate;

    // ───────────────────────────────── Unity 라이프사이클 ─────────────────────────────────
    private void Awake()
    {
        // 싱글톤 보장
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ApplyGlobalPhysicsSettings();
        ApplyAllMaterialSettings();
    }

    // 인스펙터에서 값 바꿀 때마다 에디터 상에서도 바로 반영되게
    private void OnValidate()
    {
        // 씬에 여러 개 있으면 꼬이니까, 자기 자신이 Singleton 이거나
        // 아직 Singleton 이 없을 때만 적용
        if (Instance == null || Instance == this)
        {
            ApplyGlobalPhysicsSettings();
            ApplyAllMaterialSettings();
        }
    }

    // ───────────────────────────────── 전역 Physics 적용 ─────────────────────────────────
    private void ApplyGlobalPhysicsSettings()
    {
        Physics.bounceThreshold = bounceThreshold;
        // Physics.gravity 자체는 건들지 않고,
        // 개별 구슬에서 globalGravityMultiplier 를 곱해서 사용.
    }

    // ───────────────────────────────── Material 설정 적용 ─────────────────────────────────
    private void ApplyAllMaterialSettings()
    {
        ApplyStartFunnelMaterialSettings();
        ApplyTrackMaterialSettings();
        ApplyMarbleMaterialSettings();
    }

    private void ApplyStartFunnelMaterialSettings()
    {
        startMaterial = Resources.Load<PhysicMaterial>("Material/StartFunnel");
        if (startMaterial == null) return;

        startMaterial.staticFriction = startStaticFriction;
        startMaterial.dynamicFriction = startDynamicFriction;
        startMaterial.bounciness = startBounciness;
        startMaterial.frictionCombine = startFrictionCombine;
        startMaterial.bounceCombine = startBounceCombine;
    }

    private void ApplyTrackMaterialSettings()
    {
        trackMaterial = Resources.Load<PhysicMaterial>("Material/WoodTrack");
        if (trackMaterial == null) return;

        trackMaterial.staticFriction = trackStaticFriction;
        trackMaterial.dynamicFriction = trackDynamicFriction;
        trackMaterial.bounciness = trackBounciness;
        trackMaterial.frictionCombine = trackFrictionCombine;
        trackMaterial.bounceCombine = trackBounceCombine;
    }

    private void ApplyMarbleMaterialSettings()
    {
        // 없으면 새로 만들고, 있으면 그대로 값만 덮어씀
        if (marbleMaterial == null)
        {
            marbleMaterial = new PhysicMaterial("Marble_LowFriction_FromManager");
        }

        marbleMaterial.staticFriction = marbleStaticFriction;
        marbleMaterial.dynamicFriction = marbleDynamicFriction;
        marbleMaterial.bounciness = marbleBounciness;
        marbleMaterial.frictionCombine = marbleFrictionCombine;
        marbleMaterial.bounceCombine = marbleBounceCombine;
    }

    // ───────────────────────────────── 외부에서 호출하는 API ─────────────────────────────────

    /// <summary>
    /// 구슬(쇠구슬)에 사용할 PhysicMaterial 참조를 반환.
    /// 값(마찰/탄성 등)은 PhysicsManager 인스펙터에서 관리합니다.
    /// </summary>
    public PhysicMaterial GetMarbleMaterial()
    {
        // 혹시라도 아직 생성 안 됐다면 한 번 만들어 주고 설정 적용
        if (marbleMaterial == null)
        {
            marbleMaterial = new PhysicMaterial("Marble_LowFriction_FromManager");
        }

        ApplyMarbleMaterialSettings();
        return marbleMaterial;
    }

    /// <summary>
    /// 트랙(나무 레일)에 사용할 PhysicMaterial 참조를 반환.
    /// 실제 자산은 Assets/Material/WoodTrack_HighFriction 를 할당해서 사용.
    /// </summary>
    public PhysicMaterial GetTrackMaterial()
    {
        if (trackMaterial == null)
        {
            Debug.LogWarning("[PhysicsManager] trackMaterial 이 설정되어 있지 않습니다. 트랙 콜라이더에 물리 재질이 적용되지 않습니다.");
            return null;
        }

        ApplyTrackMaterialSettings();
        return trackMaterial;
    }

    /// <summary>
    /// 구슬용 Rigidbody 기본값을 세팅해 주는 헬퍼 함수.
    /// PlayerMarble에서 호출해서 공통 세팅을 유지합니다.
    /// </summary>
    public void ConfigureMarbleRigidbody(Rigidbody rb)
    {
        if (rb == null) return;

        // 기본 중력은 끄고, 우리가 직접 중력을 적용할 예정
        rb.useGravity = false;

        rb.drag = marbleDrag;
        rb.angularDrag = marbleAngularDrag;
        rb.collisionDetectionMode = marbleCollisionMode;
        rb.interpolation = marbleInterpolation;
    }
}
