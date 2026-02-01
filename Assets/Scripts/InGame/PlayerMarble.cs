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
    [Header("유령 캐릭터 공 위에 지정하기")]
    [Tooltip("리소스 경로 (Resources/Character/Ghost)")]
    [SerializeField] private string ghostPath = "Character/Ghost";
    [Tooltip("공 기준 고스트 위치 오프셋")]
    [SerializeField] private Vector3 ghostOffset = new Vector3(0, 0.5f, 0);
    private Transform ghostTransform;       // 인스턴스 된 고스트    

    [Header("유령 방향 설정")]
    [SerializeField] private bool ghostHorizontalOnly = true;   // 수평(XZ) 방향만 볼지 여부
    [SerializeField] private Vector3 ghostDefaultDir = Vector3.forward; // 초기 기본 방향

    private Vector3 _ghostLastMoveDir;  // 마지막 이동 방향

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


    [Tooltip("깔대기 중심(Transform). PlayerMarbleSpawner에서 StartFunnel.transform을 할당해 줍니다.")]
    public Transform funnelCenter;

    // 한 번 깔대기를 완전히 벗어나면 더 이상 회전 보정 안 함
    private bool _leftFunnel = false;

    // 랜덤 시드용(유니티 랜덤은 시드 설정이 전역이므로 시스템 시드를 사용)
    System.Random myRand = new System.Random();

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

    float funnelSwirlForce;
    void Start()
    {
        System.Random rand = new System.Random();
        funnelSwirlForce = PhysicsManager.Instance != null
            ? PhysicsManager.Instance.funnelSwirlForce
            : 8f;
        funnelSwirlForce += (float)rand.NextDouble() * 2f -1f; // -1 ~ +1 랜덤 보정
        Debug.Log($"[★★★★] {name} funnelSwirlForce={funnelSwirlForce}");

        // 1. 프리팹 로드
        GameObject ghostPrefab = Resources.Load<GameObject>(ghostPath);
        if (ghostPrefab == null)
        {
            Debug.LogError($"Ghost 프리팹을 찾을 수 없습니다. 경로 확인: Resources/{ghostPath}");
            return;
        }

        // 2. 공 주변에 고스트 생성 (일단 부모 없이)
        Vector3 spawnPos = transform.position + ghostOffset;
        GameObject ghostInstance = Instantiate(
            ghostPrefab,
            spawnPos,
            ghostPrefab.transform.rotation   // 프리팹의 기본 회전 사용
        );

        // 3. 나중에 회전 고정할 수 있게 참조 저장
        ghostTransform = ghostInstance.transform;

        // 4. 공의 자식으로 붙이되, 월드 위치/회전은 유지 (true)
        ghostTransform.SetParent(transform, true);

        // 5.고스트가 처음에 바라볼 기본 방향 설정
        // (대부분 트랙 진행 방향 or 현재 transform.forward 사용)
        _ghostLastMoveDir = ghostDefaultDir;
        if (_ghostLastMoveDir.sqrMagnitude < 0.0001f)
            _ghostLastMoveDir = transform.forward;

        if (ghostHorizontalOnly)
        {
            _ghostLastMoveDir.y = 0f;
            if (_ghostLastMoveDir.sqrMagnitude > 0.0001f)
                _ghostLastMoveDir.Normalize();
        }
    }

    void LateUpdate()
    {
        if (ghostTransform == null) return;

        // 1) 위치는 공 + 오프셋 따라가게
        ghostTransform.position = transform.position + ghostOffset;

        // 2) 이동 방향 계산 (Rigidbody 속도)
        Vector3 vel = _rb != null ? _rb.velocity : Vector3.zero;
        Vector3 moveDir = vel;

        // 속도가 너무 느리면 → 마지막 방향 유지
        if (moveDir.sqrMagnitude > 0.0001f)
        {
            if (ghostHorizontalOnly)
            {
                // 수평(XZ) 방향만 사용하고 싶으면 Y를 0으로
                moveDir.y = 0f;
            }

            if (moveDir.sqrMagnitude > 0.0001f)
            {
                moveDir.Normalize();
                _ghostLastMoveDir = moveDir;
            }
        }
        else
        {
            moveDir = _ghostLastMoveDir;
        }

        // 3) 최종적으로 이동 방향을 바라보게 회전
        if (moveDir.sqrMagnitude > 0.0001f)
        {
            ghostTransform.rotation = Quaternion.LookRotation(moveDir, Vector3.up);
        }
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

        // ───── 스타트 깔대기 안에서 시계 방향 회전 보정 ─────
        ApplyFunnelSwirl();

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

    /// <summary>
    /// 스타트 깔대기 안에 있는 동안, 시계 방향으로 계속 회전하도록
    /// 옆 방향(접선) + 약간 중심 쪽으로 가속도를 살짝 준다.
    /// </summary>
    private void ApplyFunnelSwirl()
    {
        if (funnelSwirlForce <= 0f || funnelCenter == null || _leftFunnel)
            return;

        Vector3 pos = transform.position;
        Vector3 center = funnelCenter.position;

        // "깔대기 안에 있다"는 걸 대충 Y값으로 판정
        // → 깔대기 중심보다 어느 정도 위에 있을 때만 회전 보정
        //   (너무 아래로 내려가면 주둥이/트랙으로 빠져야 하니까 보정 중단)
        if (pos.y < center.y - 2f)
        {
            _leftFunnel = true;
            return;
        }

        // 중심 기준 반지름 방향(수평 성분만)
        Vector3 radial = pos - center;
        radial.y = 0f;
        if (radial.sqrMagnitude < 0.001f)
            return;

        radial.Normalize();

        // 시계 방향 접선 벡터 (위에서 내려다 볼 때)
        Vector3 tangentCW = Vector3.Cross(Vector3.up, radial);   // CW

        // 너무 바깥으로 튀어나가지 않도록 약간 중심 쪽으로 끌어당기는 힘도 같이 줌
        Vector3 inward = -radial;

        // 접선 + 중심 방향을 섞어서 부드러운 궤도 유지
        Vector3 swirlDir = (tangentCW * 0.8f + inward * 0.2f).normalized;

        _rb.AddForce(swirlDir * funnelSwirlForce, ForceMode.Acceleration);
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

        // 고스트 마지막 이동 방향도 리스폰 방향으로 맞춰주기
        _ghostLastMoveDir = forward;

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
