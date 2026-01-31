using UnityEngine;

/// <summary>
/// 플레이어 구슬 하나의 물리 동작.
/// - Rigidbody / Collider 자동 보장
/// - MarblePhysicsManager 에서 물리 재질 / 중력 / 기본값을 받아 사용
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class PlayerMarble : MonoBehaviour
{
    [Header("중력 배수 오버라이드 (0 이하이면 매니저 값 사용)")]
    [Tooltip("0 이하이면 MarblePhysicsManager.globalGravityMultiplier 를 사용합니다.")]
    public float gravityMultiplierOverride = 0f;

    [Header("구슬 반지름 (비주얼 스케일과는 별도, Collider용)")]
    public float radius = 0.5f;

    private Rigidbody _rb;
    private PhysicsManager _physicsManager;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        Collider col = GetComponent<Collider>();

        // 물리 매니저 참조
        _physicsManager = PhysicsManager.Instance;
        if (_physicsManager == null)
        {
            Debug.LogWarning("[PlayerMarble] PhysicsManager 가 씬에 없습니다. 기본 Rigidbody/PhysicMaterial 설정으로 동작합니다.");

            // 최소한의 방어 코드
            _rb.useGravity = true;
        }
        else
        {
            // 구슬 Rigidbody 기본 세팅
            _physicsManager.ConfigureMarbleRigidbody(_rb);

            // 구슬용 PhysicMaterial 적용
            PhysicMaterial marbleMat = _physicsManager.GetMarbleMaterial();
            if (marbleMat != null)
                col.material = marbleMat;
        }

        // 구슬 모양에 맞춰 반지름 세팅 (SphereCollider 인 경우에만)
        if (col is SphereCollider sphere)
        {
            sphere.radius = radius;
        }
    }

    private void FixedUpdate()
    {
        if (_physicsManager == null)
            return;

        // 실제로 사용할 중력 배수 결정
        float gravityMul = gravityMultiplierOverride > 0f
            ? gravityMultiplierOverride
            : _physicsManager.globalGravityMultiplier;

        // Physics.gravity 는 그대로 두고, 우리가 배수만큼 곱해서 적용
        Vector3 g = Physics.gravity * gravityMul;
        _rb.AddForce(g, ForceMode.Acceleration);
    }
}
