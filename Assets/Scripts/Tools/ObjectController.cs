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
    public bool rotateContinuous = true;

    [ToggleGroup("rotateEnabled")]
    [ShowIf("@!rotateContinuous")]
    public float rotateOnceDegrees = 90f;

    [ToggleGroup("rotateEnabled")]
    [ShowIf("@!rotateContinuous")]
    public bool rotatePingPong = false;

    [ToggleGroup("rotateEnabled")]
    [ShowIf("@!rotateContinuous && rotatePingPong")]
    public float rotatePauseAtEachEnd = 0f;

    private bool _rotateOnceDone = false;

    private Quaternion _rotateOrigin;
    private float _rotateTravelled = 0f;
    private float _rotateDir = 1f;
    private bool _rotatePausing = false;
    private float _rotatePauseTimer = 0f;

    // ─── OSCILLATE ───────────────────────────────────────────────────────────

    [ToggleGroup("oscillateEnabled", "Oscillate")]
    public bool oscillateEnabled = false;

    [ToggleGroup("oscillateEnabled")]
    [Tooltip("Direction to move along, e.g. Vector3.up for bobbing, Vector3.right for horizontal.")]
    public Vector3 oscillateAxis = Vector3.up;

    [ToggleGroup("oscillateEnabled")]
    [Tooltip("Max distance to travel from the starting position, each direction.")]
    public float oscillateAmplitude = 0.25f;

    [ToggleGroup("oscillateEnabled")]
    public float oscillateFrequency = 1f;

    [ToggleGroup("oscillateEnabled")]
    public AnimationCurve oscillateCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [ToggleGroup("oscillateEnabled")]
    public float oscillatePauseAtEachEnd = 0f;

    private Vector3 _oscillateOrigin;
    private float _oscillateTime = 0f;
    private bool _oscillatePausing = false;
    private float _oscillatePauseTimer = 0f;
    private float _oscillateLastSign = 0f;

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
        _rotateOrigin    = transform.rotation;
        _oscillateOrigin = transform.position;
        _originScale     = transform.localScale;

        if (!autoStart) return;

        if (moveEnabled && targetTransform != null) _moveRunning = true;
    }

    void Update()
    {
        HandleMove();
        HandleRotate();
        HandleOscillate();
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

        // Continuous mode: just spin forever at rotateSpeed (curve sampled at its midpoint)
        if (rotateContinuous)
        {
            float contSpeed = rotateSpeed * rotateCurve.Evaluate(0.5f) * Time.deltaTime;
            transform.Rotate(rotateAxis, contSpeed, Space.Self);
            return;
        }

        // Non-continuous: either ping-pong between +/- rotateOnceDegrees, or rotate once and stop
        if (_rotatePausing)
        {
            _rotatePauseTimer += Time.deltaTime;
            if (_rotatePauseTimer >= rotatePauseAtEachEnd) { _rotatePausing = false; _rotatePauseTimer = 0f; }
            return;
        }

        if (rotatePingPong)
        {
            float speedThisFrame = rotateSpeed * rotateCurve.Evaluate(_rotateTravelled / rotateOnceDegrees) * Time.deltaTime;

            _rotateTravelled += speedThisFrame;
            transform.Rotate(rotateAxis, speedThisFrame * _rotateDir, Space.Self);

            if (_rotateTravelled >= rotateOnceDegrees)
            {
                float overshoot = _rotateTravelled - rotateOnceDegrees;
                transform.Rotate(rotateAxis, -overshoot * _rotateDir, Space.Self);
                _rotateTravelled = 0f;
                _rotateDir = -_rotateDir;
                if (rotatePauseAtEachEnd > 0f) _rotatePausing = true;
            }
        }
        else
        {
            if (_rotateOnceDone) return;

            float speedThisFrame = rotateSpeed * rotateCurve.Evaluate(_rotateTravelled / rotateOnceDegrees) * Time.deltaTime;
            _rotateTravelled += speedThisFrame;
            transform.Rotate(rotateAxis, speedThisFrame, Space.Self);

            if (_rotateTravelled >= rotateOnceDegrees)
            {
                float overshoot = _rotateTravelled - rotateOnceDegrees;
                transform.Rotate(rotateAxis, -overshoot, Space.Self);
                _rotateOnceDone = true;
                rotateEnabled   = false;
            }
        }
    }

    // ─── OSCILLATE ───────────────────────────────────────────────────────────

    void HandleOscillate()
    {
        if (!oscillateEnabled) return;

        if (_oscillatePausing)
        {
            _oscillatePauseTimer += Time.deltaTime;
            if (_oscillatePauseTimer >= oscillatePauseAtEachEnd) { _oscillatePausing = false; _oscillatePauseTimer = 0f; }
            return;
        }

        _oscillateTime += Time.deltaTime * oscillateFrequency;

        // sin goes -1..1; remap to 0..1 for the curve, then back to -1..1
        float sin        = Mathf.Sin(_oscillateTime * Mathf.PI * 2f);
        float t          = (sin + 1f) * 0.5f;
        float curveValue = oscillateCurve.Evaluate(t);
        float signedVal  = curveValue * 2f - 1f;

        Vector3 offset = oscillateAxis.normalized * (signedVal * oscillateAmplitude);
        transform.position = _oscillateOrigin + offset;

        // detect when sin crosses a peak (+1 or -1) to trigger the pause
        if (oscillatePauseAtEachEnd > 0f)
        {
            float sign   = Mathf.Sign(sin);
            float absSin = Mathf.Abs(sin);
            if (absSin > 0.99f && sign != _oscillateLastSign)
            {
                _oscillateLastSign = sign;
                _oscillatePausing  = true;
            }
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
        _rotateOnceDone  = false;
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

    public void StartOscillate()
    {
        _oscillateOrigin   = transform.position;
        _oscillateTime     = 0f;
        _oscillatePausing  = false;
        _oscillateLastSign = 0f;
        oscillateEnabled   = true;
    }

    public void StopOscillate()
    {
        oscillateEnabled = false;
    }

    public void ResetOscillate()
    {
        StopOscillate();
        transform.position = _oscillateOrigin;
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
