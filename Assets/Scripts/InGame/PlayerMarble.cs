using UnityEngine;

/// <summary>
/// 플레이어 구슬 하나의 물리 동작.
/// - Rigidbody / Collider 자동 보장
/// - 마찰력 최소인 PhysicMaterial 자동 생성/적용
/// - 중력 가속도 3배 (전역 Physics.gravity 는 그대로 두고, AddForce 로 추가)
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class PlayerMarble : MonoBehaviour
{
    [Header("중력 배수 (1 = 기본 중력)")]
    public float gravityMultiplier = 3.0f;

    [Header("구슬 반지름 (비주얼 스케일과는 별도, 참고용 옵션)")]
    public float radius = 0.5f;

    // 모든 구슬이 공유하는 저마찰 PhysicMaterial
    private static PhysicMaterial s_lowFrictionMat;

    private Rigidbody _rb;

    private void Awake()
    {
        Physics.bounceThreshold = 0f;
     
        _rb = GetComponent<Rigidbody>();

        // Rigidbody 기본 설정
        _rb.useGravity = true;                         // 기본 중력 ON
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.drag = 0f;
        _rb.angularDrag = 0.03f;

        // Collider + PhysicMaterial 세팅 (마찰 최소)
        Collider col = GetComponent<Collider>();
        col.material = GetOrCreateLowFrictionMaterial();

        // 구슬답게 SphereCollider 면 반지름을 radius 에 맞춰볼 수도 있음(선택사항)
        if (col is SphereCollider sphere)
        {
            sphere.radius = radius;
        }
    }

    private void FixedUpdate()
    {
        if (gravityMultiplier <= 1f)
            return;

        // Physics.gravity 는 이미 한 번 적용되고 있으므로
        // (gravityMultiplier - 1) 만큼 추가로 가속을 더해줘서 "체감 3배 중력" 구현
        Vector3 extraGravity = (gravityMultiplier - 1f) * Physics.gravity;
        _rb.AddForce(extraGravity, ForceMode.Acceleration);
    }

    private static PhysicMaterial GetOrCreateLowFrictionMaterial()
    {
        if (s_lowFrictionMat != null)
            return s_lowFrictionMat;

        s_lowFrictionMat = new PhysicMaterial("Marble_LowFriction");

        // 쇠구슬 느낌: 마찰은 적당히 낮게
        s_lowFrictionMat.staticFriction = 0.2f;
        s_lowFrictionMat.dynamicFriction = 0.15f;

        // 서로 부딪힐 때 약간은 튀게 (0.2~0.35 사이에서 취향대로)
        s_lowFrictionMat.bounciness = 0.25f;

        // 구슬은 적당히 미끄럽게, 트랙(나무)이 더 거칠게 잡아주게 만들 예정이니 Average
        s_lowFrictionMat.frictionCombine = PhysicMaterialCombine.Average;

        // 튐은 가능한 한 크게 살려서, 구슬끼리 부딪히면 통통 튀도록
        s_lowFrictionMat.bounceCombine = PhysicMaterialCombine.Maximum;

        return s_lowFrictionMat;
    }

}
