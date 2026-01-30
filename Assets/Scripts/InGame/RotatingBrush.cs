using UnityEngine;

/// <summary>
/// 회전 브러시 장애물:
/// - 지정한 축을 기준으로 왕복 회전(Oscillate)
/// - 또는 한 방향으로 계속 회전(Continuous)
/// 
/// 이 스크립트가 붙은 오브젝트에는:
/// - Rigidbody (isKinematic = true, 이 스크립트에서 자동 보장)
/// 자식 오브젝트(Body)에:
/// - Collider(Box 등, isTrigger = false)
/// 가 달려 있으면 물리적으로 구슬을 밀어낼 수 있습니다.
/// </summary>
[DisallowMultipleComponent]
public class RotatingBrush : MonoBehaviour
{
    public enum MotionType
    {
        Oscillate,      // 왔다갔다 휘두르는 모션
        Continuous      // 한 방향으로 계속 도는 모션
    }

    [Header("회전 모션 설정")]
    public MotionType motionType = MotionType.Continuous;

    [Tooltip("회전 축 (기본값: Y축). 로컬 기준입니다.")]
    public Vector3 localAxis = Vector3.up;

    [Tooltip("왕복 회전일 때 최대 회전 각도(양방향 합계가 아니라 한쪽 최대 각도입니다). 예: 45면 -45~+45도 범위로 움직임.")]
    public float swingAngle = 180f;

    [Tooltip("왕복 회전 속도 (라디안이 아니라 단순 배속 느낌. 1~3 정도 권장).")]
    public float oscillateSpeed = 1f;

    [Tooltip("여러 브러시가 있을 때 시작 위상을 다르게 줘서 타이밍을 어긋나게 만들 수 있습니다. (도 단위)")]
    public float phaseOffsetDegrees = 0f;

    [Tooltip("Continuous 모드일 때, 초당 회전 속도(도/초). 양수면 시계 방향, 음수면 반시계 방향.")]
    public float continuousSpeedDegPerSec = 360f;

    [Header("디버그 / 안전 장치")]
    [Tooltip("게임 중에 축 방향을 변경할 수 있도록 normalize 여부를 제어합니다.")]
    public bool normalizeAxisOnStart = true;

    private Quaternion _initialLocalRotation;
    private float _phaseOffsetRad;

    private static GameObject s_spinnerPrefab;

    // ★ 여기에서는 축 normalize + Rigidbody 세팅만 하고,
    //    아직 기준 회전은 저장하지 않습니다.
    private void Awake()
    {
        if (normalizeAxisOnStart && localAxis.sqrMagnitude > 0.0001f)
        {
            localAxis = localAxis.normalized;
        }

        EnsureKinematicRigidbody();
    }

    // ★ Start 시점에, 이미 외부에서 세팅해 둔 rotation(경사 맞춰 둔 상태)을
    //    기준 회전으로 저장합니다.
    private void Start()
    {
        _initialLocalRotation = transform.localRotation;
        _phaseOffsetRad = phaseOffsetDegrees * Mathf.Deg2Rad;
    }

    private void Update()
    {
        switch (motionType)
        {
            case MotionType.Oscillate:
                UpdateOscillate();
                break;

            case MotionType.Continuous:
                UpdateContinuous();
                break;
        }
    }

    private void UpdateOscillate()
    {
        float t = Mathf.Sin(Time.time * oscillateSpeed + _phaseOffsetRad);
        float angle = t * swingAngle;

        // ★ Start에서 저장한 "기준 회전"을 중심으로 회전
        transform.localRotation = _initialLocalRotation * Quaternion.AngleAxis(angle, localAxis);
    }

    private void UpdateContinuous()
    {
        float angleDelta = continuousSpeedDegPerSec * Time.deltaTime;
        transform.localRotation *= Quaternion.AngleAxis(angleDelta, localAxis);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // 필요하면 여기서 구슬에 추가 힘을 줄 수 있음
    }

    private void EnsureKinematicRigidbody()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        rb.isKinematic = true;
        rb.useGravity = false;
    }

    #region Static Helper

    public static GameObject CreateBrush(
        string name,
        Transform parent,
        Vector3 worldPosition,
        Vector3 brushScale,
        Vector3 localAxis,
        float swingAngle,
        float oscillateSpeed,
        float phaseOffsetDegrees = 0f)
    {
        // 1) Spinner 프리팹 로드 (매번 로드 – 간단하게, 수정은 이 메소드 안에서만)
        GameObject prefab = Resources.Load<GameObject>("Objects/Spinner");
        if (prefab == null)
        {
            Debug.LogError("[RotatingBrush] Resources/Objects/Spinner.prefab 을 찾을 수 없습니다.");
            return null;
        }

        // 2) 프리팹 “원래姿勢” 그대로 월드에 생성
        GameObject go = Object.Instantiate(prefab);
        go.name = name;

        // 프리팹에 저장된 회전 그대로 두고, 위치만 트랙 계산 위치로
        go.transform.position = worldPosition;
        go.transform.rotation = prefab.transform.rotation;

        // 3) 부모 타일에 붙이되, 월드 트랜스폼 그대로 유지
        if (parent != null)
            go.transform.SetParent(parent, true); // worldPositionStays = true

        // 4) 회전 모션 설정 (기존 로직 그대로)
        RotatingBrush brush = go.GetComponent<RotatingBrush>();
        if (brush == null)
            brush = go.AddComponent<RotatingBrush>();

//        brush.motionType = MotionType.Oscillate;
        brush.localAxis = localAxis;            // 나중에 Vector3.up 넘겨줄 예정
        brush.swingAngle = swingAngle;
        brush.oscillateSpeed = oscillateSpeed;
        brush.phaseOffsetDegrees = phaseOffsetDegrees;

        // 루트에 Rigidbody(isKinematic) 보장 → 자식 콜라이더랑 같이 회전
        brush.EnsureKinematicRigidbody();

        return go;
    }
    #endregion

}
