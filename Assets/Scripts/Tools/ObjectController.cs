using UnityEngine;
using Sirenix.OdinInspector;

public class ObjectController : MonoBehaviour
{
    // ─── MOVE ────────────────────────────────────────────────────────────────

    [ToggleGroup("moveEnabled", "Move")]
    public bool moveEnabled = false;

    [ToggleGroup("moveEnabled")]
    public Transform targetTransform;

    [ToggleGroup("moveEnabled")]
    public float moveDuration = 1f;

    [ToggleGroup("moveEnabled")]
    public AnimationCurve moveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [ToggleGroup("moveEnabled")]
    public bool movePingPong = false;

    [ToggleGroup("moveEnabled")]
    [ShowIf("movePingPong")]
    public float movePauseAtEachEnd = 0f;

    private Vector3 _originPos;
    private Quaternion _originRot;
    private float _moveT = 0f;
    private bool _movingToTarget = true;
    private bool _movePausing = false;
    private float _movePauseTimer = 0f;
    private bool _moveRunning = false;

    // ─── ROTATE ──────────────────────────────────────────────────────────────

    [ToggleGroup("rotateEnabled", "Rotate")]
    public bool rotateEnabled = false;

    [ToggleGroup("rotateEnabled")]
    public Vector3 rotateAxis = Vector3.up;

    [ToggleGroup("rotateEnabled")]
    public float rotateSpeed = 90f;

    [ToggleGroup("rotateEnabled")]
    public AnimationCurve rotateCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    [ToggleGroup("rotateEnabled")]
    public bool rotatePingPong = false;

    [ToggleGroup("rotateEnabled")]
    [ShowIf("rotatePingPong")]
    public float rotatePingPongDegrees = 90f;

    [ToggleGroup("rotateEnabled")]
    [ShowIf("rotatePingPong")]
    public float rotatePauseAtEachEnd = 0f;

    private Quaternion _rotateOrigin;
    private float _rotateTravelled = 0f;
    private float _rotateDir = 1f;
    private bool _rotatePausing = false;
    private float _rotatePauseTimer = 0f;

    // ─── SCALE PULSE ─────────────────────────────────────────────────────────

    [ToggleGroup("scalePulse", "Scale Pulse")]
    public bool scalePulse = false;

    [ToggleGroup("scalePulse")]
    public Vector3 pulseTargetScale = Vector3.one * 1.2f;

    [ToggleGroup("scalePulse")]
    public float pulseDuration = 0.5f;

    [ToggleGroup("scalePulse")]
    public AnimationCurve pulseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [ToggleGroup("scalePulse")]
    public float pulsePauseAtEachEnd = 0f;

    [ToggleGroup("scalePulse")]
    public bool pulseOnce = false;

    private Vector3 _originScale;
    private float _pulseT = 0f;
    private bool _pulsingToTarget = true;
    private bool _pulsePausing = false;
    private float _pulsePauseTimer = 0f;
    private bool _pulseOnceReturning = false;

    // ─────────────────────────────────────────────────────────────────────────

    [Header("General")]
    public bool autoStart = true;

    void Start()
    {
        _originPos    = transform.position;
        _originRot    = transform.rotation;
        _rotateOrigin = transform.rotation;
        _originScale  = transform.localScale;

        if (!autoStart) return;

        if (moveEnabled && targetTransform != null) _moveRunning = true;
    }

    void Update()
    {
        HandleMove();
        HandleRotate();
        HandleScale();
    }

    // ─── MOVE ────────────────────────────────────────────────────────────────

    void HandleMove()
    {
        if (!_moveRunning || targetTransform == null) return;

        if (_movePausing)
        {
            _movePauseTimer += Time.deltaTime;
            if (_movePauseTimer >= movePauseAtEachEnd) { _movePausing = false; _movePauseTimer = 0f; }
            return;
        }

        _moveT = Mathf.Clamp01(_moveT + Time.deltaTime / moveDuration);

        Vector3    fromPos = _movingToTarget ? _originPos               : targetTransform.position;
        Vector3    toPos   = _movingToTarget ? targetTransform.position  : _originPos;
        Quaternion fromRot = _movingToTarget ? _originRot               : targetTransform.rotation;
        Quaternion toRot   = _movingToTarget ? targetTransform.rotation  : _originRot;

        float v = moveCurve.Evaluate(_moveT);
        transform.position = Vector3.LerpUnclamped(fromPos, toPos, v);
        transform.rotation = Quaternion.SlerpUnclamped(fromRot, toRot, v);

        if (_moveT >= 1f)
        {
            if (movePingPong) { _movingToTarget = !_movingToTarget; _moveT = 0f; if (movePauseAtEachEnd > 0f) _movePausing = true; }
            else              { _moveRunning = false; }
        }
    }

    // ─── ROTATE ──────────────────────────────────────────────────────────────

    void HandleRotate()
    {
        if (!rotateEnabled) return;

        if (_rotatePausing)
        {
            _rotatePauseTimer += Time.deltaTime;
            if (_rotatePauseTimer >= rotatePauseAtEachEnd) { _rotatePausing = false; _rotatePauseTimer = 0f; }
            return;
        }

        float speedThisFrame = rotateSpeed * rotateCurve.Evaluate(
            rotatePingPong ? _rotateTravelled / rotatePingPongDegrees : 0.5f
        ) * Time.deltaTime;

        if (rotatePingPong)
        {
            _rotateTravelled += speedThisFrame;
            transform.Rotate(rotateAxis, speedThisFrame * _rotateDir, Space.Self);

            if (_rotateTravelled >= rotatePingPongDegrees)
            {
                float overshoot = _rotateTravelled - rotatePingPongDegrees;
                transform.Rotate(rotateAxis, -overshoot * _rotateDir, Space.Self);
                _rotateTravelled = 0f;
                _rotateDir = -_rotateDir;
                if (rotatePauseAtEachEnd > 0f) _rotatePausing = true;
            }
        }
        else
        {
            transform.Rotate(rotateAxis, speedThisFrame, Space.Self);
        }
    }

    // ─── SCALE PULSE ─────────────────────────────────────────────────────────

    void HandleScale()
    {
        if (!scalePulse) return;

        if (_pulsePausing)
        {
            _pulsePauseTimer += Time.deltaTime;
            if (_pulsePauseTimer >= pulsePauseAtEachEnd) { _pulsePausing = false; _pulsePauseTimer = 0f; }
            return;
        }

        _pulseT = Mathf.Clamp01(_pulseT + Time.deltaTime / pulseDuration);

        Vector3 fromScale = _pulsingToTarget ? _originScale     : pulseTargetScale;
        Vector3 toScale   = _pulsingToTarget ? pulseTargetScale : _originScale;

        transform.localScale = Vector3.LerpUnclamped(fromScale, toScale, pulseCurve.Evaluate(_pulseT));

        if (_pulseT >= 1f)
        {
            if (pulseOnce && _pulsingToTarget)
            {
                _pulseOnceReturning = true;
            }

            _pulsingToTarget = !_pulsingToTarget;
            _pulseT = 0f;

            if (pulsePauseAtEachEnd > 0f)
                _pulsePausing = true;

            if (pulseOnce && _pulseOnceReturning && !_pulsingToTarget)
            {
                transform.localScale = _originScale;
                scalePulse = false;
                _pulseOnceReturning = false;
            }
        }
    }

    // ─── PUBLIC API ──────────────────────────────────────────────────────────

    public void StartMove()
    {
        _originPos = transform.position;
        _originRot = transform.rotation;
        _moveT = 0f; _movingToTarget = true; _movePausing = false;
        moveEnabled = true;
        _moveRunning = true;
    }

    public void StopMove()
    {
        _moveRunning = false;
        moveEnabled  = false;
    }

    public void ResetMove()
    {
        StopMove();
        transform.position = _originPos;
        transform.rotation = _originRot;
    }

    public void StartRotate()
    {
        _rotateOrigin    = transform.rotation;
        _rotateTravelled = 0f;
        _rotateDir       = 1f;
        _rotatePausing   = false;
        rotateEnabled    = true;
    }

    public void StopRotate()
    {
        rotateEnabled = false;
    }

    public void ResetRotate()
    {
        StopRotate();
        transform.rotation = _rotateOrigin;
    }

    public void StartScalePulse()
    {
        _originScale        = transform.localScale;
        _pulseT             = 0f;
        _pulsingToTarget    = true;
        _pulsePausing       = false;
        _pulseOnceReturning = false;
        scalePulse          = true;
    }

    public void StopScalePulse()
    {
        scalePulse = false;
    }

    public void ResetScalePulse()
    {
        StopScalePulse();
        transform.localScale = _originScale;
    }
}
