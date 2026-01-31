using System.Collections;
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

    [Header("트랙 이탈 판정 및 리스폰")]
    [Tooltip("공중에 떠 있는 시간이 이 값을 넘으면 트랙 이탈로 판단합니다.")]
    public float airborneTimeout = 3f;

    [Tooltip("트랙 이탈 후 리스폰 전에 깜빡이는 시간(초)")]
    public float respawnBlinkDuration = 2f;

    [Tooltip("최대 트랙 이탈 허용 횟수. 이 횟수를 넘으면 실격 처리됩니다.")]
    public int maxOffTrackCount = 5;

    private Rigidbody _rb;
    private PhysicsManager _physicsManager;

    [SerializeField] private float _airborneTimer = 0f;
    [SerializeField] private int _offTrackCount = 0;
    [SerializeField] private int _trackContactCount = 0;
    [SerializeField] private bool _isRespawning = false;
    [SerializeField] private bool _isDisqualified = false;
    [SerializeField] private bool _hasFinished = false;

    private Vector3 _initialPosition;
    private Quaternion _initialRotation;

    [SerializeField] private CheckpointZone _lastCheckpoint;

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

        // 최초 시작 위치/회전 저장 (체크포인트 없을 때 리스폰용)
        _initialPosition = transform.position;
        _initialRotation = transform.rotation;
    }

    private void FixedUpdate()
    {
        if (_physicsManager == null)
            return;

        // 이미 결승선을 통과한 구슬은 더 이상 처리하지 않음
        if (_hasFinished)
            return;

        // 실격된 구슬은 계속 떨어지기만 함 (타이머/리스폰 X)
        if (_isDisqualified)
        {
            float gravityMulDQ = gravityMultiplierOverride > 0f
                ? gravityMultiplierOverride
                : _physicsManager.globalGravityMultiplier;
            Vector3 gDQ = Physics.gravity * gravityMulDQ;
            _rb.AddForce(gDQ, ForceMode.Acceleration);
            return;
        }

        // 리스폰 중에는 중력만 적용, 타이머는 멈춤
        if (_isRespawning)
        {
            float gravityMulRespawn = gravityMultiplierOverride > 0f
                ? gravityMultiplierOverride
                : _physicsManager.globalGravityMultiplier;
            Vector3 gRespawn = Physics.gravity * gravityMulRespawn;
            _rb.AddForce(gRespawn, ForceMode.Acceleration);
            return;
        }

        // 실제로 사용할 중력 배수 결정
        float gravityMul = gravityMultiplierOverride > 0f
            ? gravityMultiplierOverride
            : _physicsManager.globalGravityMultiplier;

        Vector3 g = Physics.gravity * gravityMul;
        _rb.AddForce(g, ForceMode.Acceleration);

        // ───── 트랙 이탈 타이머 갱신 ─────
        if (_trackContactCount <= 0)
        {
            _airborneTimer += Time.fixedDeltaTime;

            if (_airborneTimer >= airborneTimeout)
            {
                _airborneTimer = 0f;
                _offTrackCount++;

                if (_offTrackCount >= maxOffTrackCount)
                {
                    // 실격 처리
                    _isDisqualified = true;

                    if (RaceResultManager.Instance != null)
                    {
                        RaceResultManager.Instance.OnMarbleDisqualified(this);
                    }
                }
                else
                {
                    // 리스폰 절차 시작
                    StartCoroutine(RespawnRoutine());
                }
            }
        }
        else
        {
            _airborneTimer = 0f;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsTrackContact(collision.collider))
        {
            _trackContactCount++;
            _airborneTimer = 0f;
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        if (IsTrackContact(collision.collider))
        {
            _airborneTimer = 0f;
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        if (IsTrackContact(collision.collider))
        {
            _trackContactCount = Mathf.Max(0, _trackContactCount - 1);
        }
    }

    private bool IsTrackContact(Collider col)
    {
        if (col == null)
            return false;

        // 다른 PlayerMarble과의 충돌은 "트랙 접촉"으로 보지 않음
        if (col.GetComponent<PlayerMarble>() != null)
            return false;

        return true;
    }

    private IEnumerator RespawnRoutine()
    {
        if (_isRespawning || _isDisqualified || _hasFinished)
            yield break;

        _isRespawning = true;

        // 현재 물리 정지 + kinematic으로 고정해서 깜빡이는 동안/텔레포트 동안 안 움직이게
        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic = true;

        // 2초간 깜빡임
        float elapsed = 0f;
        float interval = 0.2f;
        Renderer rend = GetComponent<Renderer>();

        while (elapsed < respawnBlinkDuration)
        {
            if (rend != null)
                rend.enabled = !rend.enabled;

            yield return new WaitForSeconds(interval);
            elapsed += interval;
        }

        if (rend != null)
            rend.enabled = true;

        // 리스폰 위치 계산
        Vector3 respawnPos;
        Quaternion respawnRot;

        if (_lastCheckpoint != null)
        {
            _lastCheckpoint.GetRespawnTransform(out respawnPos, out respawnRot);
            Debug.Log($"[PlayerMarble] {name} respawn at checkpoint '{_lastCheckpoint.name}' pos={respawnPos}");
        }
        else
        {
            // 체크포인트가 없으면 최초 출발 위치(깔대기 위)로
            respawnPos = _initialPosition;
            respawnRot = _initialRotation;
            Debug.Log($"[PlayerMarble] {name} respawn at initial start pos={respawnPos}");
        }

        // 실제 위치/회전 텔레포트
        transform.position = respawnPos;
        transform.rotation = respawnRot;

        // 물리 엔진이 새 위치를 인지하도록 한 프레임 기다렸다가
        yield return new WaitForFixedUpdate();

        // 다시 dynamic으로
        _rb.isKinematic = false;
        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        // 살짝 앞으로 밀치는 힘
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        const float respawnPushForce = 5f;
        _rb.AddForce(forward * respawnPushForce, ForceMode.VelocityChange);
        Debug.Log($"[PlayerMarble] {name} respawn push forward={forward * respawnPushForce}");

        // 상태 리셋
        _airborneTimer = 0f;
        _trackContactCount = 0;
        _isRespawning = false;
    }

    public void SetCheckpoint(CheckpointZone zone)
    {
        if (zone == null)
            return;

        _lastCheckpoint = zone;
        _airborneTimer = 0f;
    }

    public void MarkFinished()
    {
        _hasFinished = true;
    }

    public void MarkDisqualified()
    {
        _isDisqualified = true;
    }

}
