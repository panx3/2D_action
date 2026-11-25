using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

/// <summary>
/// Playerと鉄球を結ぶゲーム向けロープ制約。
/// たるみ中は何もせず、張力はPlayer側を主体に連続的に加える。
/// </summary>
// Evaluate live contacts after Player jumps and launcher state/velocity writes.
[DefaultExecutionOrder(100)]
public class ChainConstraint2D : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform handAnchor;
    [SerializeField] private Rigidbody2D playerRb;
    [SerializeField] private Rigidbody2D morningStarRb;

    [Header("Runtime Configuration (MorningStarLauncher owns these values)")]
    [FormerlySerializedAs("maxChainLength")]
    [SerializeField, HideInInspector] private float maxRopeLength = 4.5f;
    [SerializeField, HideInInspector] private float tensionStartRatio = 0.84f;
    [SerializeField, HideInInspector] private float tensionStrength = 42f;
    [SerializeField, HideInInspector] private float tensionDamping = 5f;
    [SerializeField, HideInInspector] private float maxTensionForce = 65f;
    [SerializeField, HideInInspector] private float airTensionMultiplier = 1.8f;
    [SerializeField, HideInInspector] private float groundPullEaseTime = 0.2f;
    [SerializeField, HideInInspector] private float movingPullResistance = 0.12f;
    [SerializeField, HideInInspector] private float runJumpMomentumThreshold = 4f;
    [SerializeField, HideInInspector] private float jumpMomentumGraceTime = 0.3f;
    [SerializeField, HideInInspector] private float runJumpTensionMultiplier = 0.25f;
    [SerializeField, HideInInspector] private float anchorFollowTime = 0.08f;
    [SerializeField, HideInInspector] private float maxBallSpeed = 20f;

    private const float RestBallReactionMultiplier = 0.35f;
    private const float GroundInitialResistanceMultiplier = 1f;
    private const float GroundPullInputThreshold = 0.1f;
    private const int MaxWrapPointCount = 6;
    private const float WrapRouteHysteresis = 0.08f;
    private const int UnwrapClearFixedSteps = 2;
    private const int MaxBallContactCount = 8;
    private const float MinPathSegmentLength = 0.001f;

    private struct RopeContact
    {
        public Collider2D Collider;
        public Vector2 LocalPhysicsPoint;
        public Vector2 PhysicsPoint;
        public int ClearFixedSteps;
    }
    private bool _prioritizeFlyingTrajectory;
    private bool _suppressPlayerPull;
    private float _groundPullElapsed;
    private float _groundPullDirection;
    private bool _groundStateInitialized;
    private bool _wasPlayerGrounded;
    private float _runJumpMomentumGraceRemaining;
    private float _runJumpTensionScale = 1f;
    private float _runJumpMomentumDirectionX;
    private Vector2 _smoothedAnchorLocalPosition;
    private Vector2 _anchorLocalVelocity;
    private bool _physicsAnchorInitialized;
    private Vector2? _suspendedPlayerAnchorLocal;
    private bool _restoringHandAnchor;
    private LayerMask _terrainLayerMask = (1 << 0) | (1 << 6);
    private float _ropeCollisionRadius = 0.14f;
    private float _collisionSkin = 0.01f;
    private ContactFilter2D _terrainFilter;
    private readonly RaycastHit2D[] _ropeCastHits = new RaycastHit2D[16];
    private readonly ContactPoint2D[] _ballContacts = new ContactPoint2D[MaxBallContactCount];
    private readonly List<RaycastHit2D> _bodyCastHits = new List<RaycastHit2D>(16);
    private readonly List<Vector2> _ropePathPoints = new List<Vector2>(MaxWrapPointCount + 2);
    private readonly List<Vector2> _ropeContactPoints = new List<Vector2>(MaxWrapPointCount);
    private readonly List<RopeContact> _ropeContacts = new List<RopeContact>(MaxWrapPointCount);
    private readonly Dictionary<int, int> _routePreferenceByObstacle = new Dictionary<int, int>();
    private readonly Vector2[] _routeCandidate = new Vector2[4];
    private readonly Vector2[] _bestRoute = new Vector2[4];
    private readonly Vector2[] _cachedRoute = new Vector2[4];
    private readonly Vector2[] _refinedRoute = new Vector2[4];
    private readonly List<Vector2> _tilePhysicsShape = new List<Vector2>(32);
    private readonly OneWayRopeSupport2D _oneWaySupport = new OneWayRopeSupport2D();
    private float _ropeSagStrength = 0.6f;
    private float _ropeMaxSag = 1.5f;
    private bool _hasResolvedRopePath;

    private Player _player;
    private MorningStarRollingVisual _rollingVisual;
    private bool _playerClimbActive;
    private Vector2 _playerClimbAnchorLocal;
    private float _playerClimbSpeed;
    private float _playerClimbMinimumLength;

    public Vector2 LastAppliedPlayerTension { get; private set; }
    public float MaxRopeLength => maxRopeLength;
    public float TensionStartRatio => tensionStartRatio;
    public float TensionStrength => tensionStrength;
    public float TensionDamping => tensionDamping;
    public float AirTensionMultiplier => airTensionMultiplier;
    public bool PrioritizesFlyingTrajectory => _prioritizeFlyingTrajectory;
    public bool IsPlayerPullSuppressed => _suppressPlayerPull
        || (_player != null && (_player.IsElectricStunned || _player.IsItemDrinking || _player.IsLadderClimbing));
    public float CurrentRopeLength => GetRopePathLength();
    public int RopeContactPointCount => _ropeContactPoints.Count;
    public bool HasOneWaySupport => _oneWaySupport.IsSupporting;
    internal bool PullsUpward => _hasResolvedRopePath
        && _ropePathPoints[1].y > _ropePathPoints[0].y;

    internal void SetSuspendedPlayerAnchor(Vector2? anchorLocal)
    {
        if (anchorLocal.HasValue && !_suspendedPlayerAnchorLocal.HasValue)
        {
            _smoothedAnchorLocalPosition = anchorLocal.Value;
            _anchorLocalVelocity = Vector2.zero;
            _physicsAnchorInitialized = true;
            _restoringHandAnchor = false;
        }
        else if (!anchorLocal.HasValue && _suspendedPlayerAnchorLocal.HasValue)
            _restoringHandAnchor = true;
        _suspendedPlayerAnchorLocal = anchorLocal;
    }

    public void ConfigurePlayerClimb(Vector2 anchorLocal, float speed, float minimumLength)
    {
        _playerClimbActive = true;
        _playerClimbAnchorLocal = anchorLocal;
        _playerClimbSpeed = Mathf.Max(0f, speed);
        _playerClimbMinimumLength = Mathf.Max(0.1f, minimumLength);
        ResetGroundPullEase();
        _runJumpMomentumGraceRemaining = 0f;
    }

    public void StopPlayerClimb()
    {
        if (!_playerClimbActive)
            return;
        _playerClimbActive = false;
        _physicsAnchorInitialized = false;
    }

    public Vector2 GetRopeContactPoint(int index)
    {
        if (index < 0 || index >= _ropeContactPoints.Count)
            return Vector2.zero;
        if (_oneWaySupport.IsSupporting)
            return _ropeContactPoints[index];

        RopeContact contact = _ropeContacts[index];
        return contact.Collider != null
            ? (Vector2)contact.Collider.transform.TransformPoint(contact.LocalPhysicsPoint)
            : contact.PhysicsPoint;
    }

    public float MaxBallSpeed
    {
        get => maxBallSpeed;
        set => maxBallSpeed = Mathf.Max(0f, value);
    }

    public void SetMaxRopeLength(float length)
    {
        maxRopeLength = Mathf.Max(0.1f, length);
    }

    public void ConfigureTension(
        float startRatio,
        float strength,
        float damping,
        float maxForce,
        float airMultiplier,
        float pullEaseTime,
        float movingResistance,
        float momentumThreshold,
        float momentumGraceTime,
        float momentumTensionMultiplier,
        float anchorSmoothingTime)
    {
        tensionStartRatio = Mathf.Clamp(startRatio, 0.5f, 0.98f);
        tensionStrength = Mathf.Max(0f, strength);
        tensionDamping = Mathf.Max(0f, damping);
        maxTensionForce = Mathf.Max(0f, maxForce);
        airTensionMultiplier = Mathf.Max(1f, airMultiplier);
        groundPullEaseTime = Mathf.Max(0.01f, pullEaseTime);
        movingPullResistance = Mathf.Clamp(movingResistance, 0.1f, GroundInitialResistanceMultiplier);
        runJumpMomentumThreshold = Mathf.Max(0f, momentumThreshold);
        jumpMomentumGraceTime = Mathf.Max(0f, momentumGraceTime);
        runJumpTensionMultiplier = Mathf.Clamp01(momentumTensionMultiplier);
        anchorFollowTime = Mathf.Max(0.01f, anchorSmoothingTime);
    }

    public void ConfigureTerrainPath(LayerMask layerMask, float ropeRadius, float skin,
        float sagStrength = 0.6f, float maxSag = 1.5f)
    {
        _terrainLayerMask = layerMask;
        _ropeCollisionRadius = Mathf.Max(0.001f, ropeRadius);
        _collisionSkin = Mathf.Max(0f, skin);
        _ropeSagStrength = Mathf.Max(0f, sagStrength);
        _ropeMaxSag = Mathf.Max(0f, maxSag);
        RefreshTerrainFilter();
    }

    public void SetFlyingTrajectoryPriority(bool prioritize)
    {
        _prioritizeFlyingTrajectory = prioritize;
        if (prioritize)
        {
            _runJumpMomentumGraceRemaining = 0f;
            _runJumpTensionScale = 1f;
            _runJumpMomentumDirectionX = 0f;
        }
    }

    public void SetPlayerPullSuppressed(bool suppressed)
    {
        if (_suppressPlayerPull == suppressed)
            return;

        _suppressPlayerPull = suppressed;
        ResetGroundPullEase();
        _runJumpMomentumGraceRemaining = 0f;
        _physicsAnchorInitialized = false;
        if (suppressed)
            StopPlayerClimb();
    }

    private void Reset()
    {
        morningStarRb = GetComponent<Rigidbody2D>();
    }

    private void Awake()
    {
        ResolveReferences();
        RefreshTerrainFilter();
    }

    private void OnEnable()
    {
        _oneWaySupport.Clear();
        _physicsAnchorInitialized = false;
        _anchorLocalVelocity = Vector2.zero;
        _ropePathPoints.Clear();
        _ropeContactPoints.Clear();
        _ropeContacts.Clear();
        _hasResolvedRopePath = false;
    }

    private void OnDisable()
    {
        _suspendedPlayerAnchorLocal = null;
        _restoringHandAnchor = false;
        _oneWaySupport.Clear();
        StopPlayerClimb();
        _ropePathPoints.Clear();
        _ropeContactPoints.Clear();
        _ropeContacts.Clear();
        _hasResolvedRopePath = false;
    }

    private void OnValidate()
    {
        maxRopeLength = Mathf.Max(0.1f, maxRopeLength);
        tensionStartRatio = Mathf.Clamp(tensionStartRatio, 0.5f, 0.98f);
        tensionStrength = Mathf.Max(0f, tensionStrength);
        tensionDamping = Mathf.Max(0f, tensionDamping);
        maxTensionForce = Mathf.Max(0f, maxTensionForce);
        airTensionMultiplier = Mathf.Max(1f, airTensionMultiplier);
        groundPullEaseTime = Mathf.Max(0.01f, groundPullEaseTime);
        movingPullResistance = Mathf.Clamp(movingPullResistance, 0.1f, GroundInitialResistanceMultiplier);
        runJumpMomentumThreshold = Mathf.Max(0f, runJumpMomentumThreshold);
        jumpMomentumGraceTime = Mathf.Max(0f, jumpMomentumGraceTime);
        runJumpTensionMultiplier = Mathf.Clamp01(runJumpTensionMultiplier);
        anchorFollowTime = Mathf.Max(0.01f, anchorFollowTime);
        maxBallSpeed = Mathf.Max(0f, maxBallSpeed);
        _ropeCollisionRadius = Mathf.Max(0.001f, _ropeCollisionRadius);
        _collisionSkin = Mathf.Max(0f, _collisionSkin);
        RefreshTerrainFilter();
    }

    private void FixedUpdate()
    {
        LastAppliedPlayerTension = Vector2.zero;
        if (!enabled)
            return;

        ResolveReferences();
        if (handAnchor == null || playerRb == null || morningStarRb == null)
        {
            ResetGroundPullEase();
            return;
        }

        Vector2 physicsAnchor = UpdatePhysicsAnchorWorld();
        RebuildRopePath(physicsAnchor, morningStarRb.position);
        if (!_hasResolvedRopePath)
        {
            ResetGroundPullEase();
            UpdateRunJumpMomentumGrace(physicsAnchor, 0f);
            ClampBallSpeed();
            return;
        }

        if (_playerClimbActive)
        {
            ApplyPlayerClimbMotion(physicsAnchor);
            if (_oneWaySupport.IsSupporting)
                RebuildRopePath(UpdatePhysicsAnchorWorld(), morningStarRb.position);
            ClampBallSpeed();
            return;
        }
        physicsAnchor = EnforceSafetyLimit(physicsAnchor);
        if (_hasResolvedRopePath && _player != null && _player.IsLadderClimbing)
            LimitLadderMovementAtBlockedBall(physicsAnchor);
        UpdateRunJumpMomentumGrace(physicsAnchor, _hasResolvedRopePath ? GetRopePathLength() : 0f);
        if (_hasResolvedRopePath)
            ApplyRopeTension(physicsAnchor);
        ClampBallSpeed();
    }

    private void ResolveReferences()
    {
        if (morningStarRb == null)
            morningStarRb = GetComponent<Rigidbody2D>();

        if (playerRb == null && handAnchor != null)
            playerRb = handAnchor.GetComponentInParent<Rigidbody2D>();

        if (_player == null && playerRb != null)
            _player = playerRb.GetComponent<Player>();

        if (_rollingVisual == null && morningStarRb != null)
            _rollingVisual = morningStarRb.GetComponent<MorningStarRollingVisual>();
    }

    private void RefreshTerrainFilter()
    {
        _terrainFilter = new ContactFilter2D();
        _terrainFilter.SetLayerMask(_terrainLayerMask);
        _terrainFilter.useTriggers = false;
    }

    private Vector2 UpdatePhysicsAnchorWorld()
    {
        Transform playerTransform = playerRb.transform;
        if (_player != null && _player.IsLadderClimbing)
            return ResolvePhysicsAnchor(Player.LadderChainAnchor);
        if (_playerClimbActive)
            return ResolvePhysicsAnchor(_playerClimbAnchorLocal);
        Vector2 targetLocalPosition = _suspendedPlayerAnchorLocal
            ?? (Vector2)playerTransform.InverseTransformPoint(handAnchor.position);
        if (!_physicsAnchorInitialized)
        {
            _smoothedAnchorLocalPosition = targetLocalPosition;
            _anchorLocalVelocity = Vector2.zero;
            _physicsAnchorInitialized = true;
        }
        else
        {
            Vector2 nextLocalPosition = Vector2.SmoothDamp(
                _smoothedAnchorLocalPosition,
                targetLocalPosition,
                ref _anchorLocalVelocity,
                anchorFollowTime,
                Mathf.Infinity,
                Time.fixedDeltaTime);
            if (_restoringHandAnchor && _hasResolvedRopePath && !_prioritizeFlyingTrajectory)
                nextLocalPosition = LimitHandAnchorChange(nextLocalPosition);

            _smoothedAnchorLocalPosition = nextLocalPosition;
            if ((targetLocalPosition - nextLocalPosition).sqrMagnitude <= MinPathSegmentLength * MinPathSegmentLength)
                _restoringHandAnchor = false;
        }

        // Player本体の移動は即時反映し、Animation/Facing由来の局所Anchor差分だけを補間する。
        return ResolvePhysicsAnchor(_smoothedAnchorLocalPosition);
    }

    private Vector2 LimitHandAnchorChange(Vector2 nextLocalPosition)
    {
        // When the ball rolls off, use the available slack to lower the hand.
        // The pose change itself shouldn't create a length error at the ball.
        Vector2 firstPoint = _ropePathPoints.Count == 2 ? morningStarRb.position : _ropePathPoints[1];
        Vector2 offset = ResolvePhysicsAnchor(_smoothedAnchorLocalPosition) - firstPoint;
        Vector2 movement = ResolvePhysicsAnchor(nextLocalPosition) - firstPoint - offset;
        float movementSquared = movement.sqrMagnitude;
        if (movementSquared <= MinPathSegmentLength * MinPathSegmentLength)
            return nextLocalPosition;

        float remainingLength = 0f;
        for (int i = 1; i < _ropePathPoints.Count - 1; i++)
        {
            Vector2 next = i == _ropePathPoints.Count - 2 ? morningStarRb.position : _ropePathPoints[i + 1];
            remainingLength += Vector2.Distance(_ropePathPoints[i], next);
        }
        float firstSpanLimit = Mathf.Max(offset.magnitude, maxRopeLength - remainingLength);
        float limitSquared = firstSpanLimit * firstSpanLimit;
        if ((offset + movement).sqrMagnitude <= limitSquared)
            return nextLocalPosition;

        // Intersect the moving hand with the available first span's radius.
        float along = Vector2.Dot(offset, movement);
        float room = Mathf.Max(0f, limitSquared - offset.sqrMagnitude);
        float fraction = (Mathf.Sqrt(along * along + movementSquared * room) - along) / movementSquared;
        _anchorLocalVelocity = Vector2.zero;
        return Vector2.Lerp(_smoothedAnchorLocalPosition, nextLocalPosition, fraction);
    }

    private Vector2 ResolvePhysicsAnchor(Vector2 localAnchor)
    {
        Vector2 target = playerRb.position + (Vector2)playerRb.transform.TransformVector(localAnchor);
        return KeepAnchorOnPlayerSideOfTerrain(playerRb.worldCenterOfMass, target);
    }

    internal Vector2 KeepVisualAnchorOnPlayerSideOfTerrain(Vector2 target)
    {
        if (playerRb == null)
            return target;

        Vector2 centerOffset = playerRb.worldCenterOfMass - playerRb.position;
        Vector2 renderedCenter = (Vector2)playerRb.transform.position + centerOffset;
        target = KeepAnchorOnPlayerSideOfTerrain(renderedCenter, target);
        if (!_hasResolvedRopePath || _oneWaySupport.IsSupporting)
            return target;

        Vector2 next = _ropeContacts.Count > 0 ? GetRopeContactPoint(0) : _ropePathPoints[1];
        if (!TryFindTerrainHit(next, target, _ropeCollisionRadius, out _))
            return target;

        // The animation can go around the other side of the ledge while the
        // physical hand is still following its smoothed path. Keep the drawn
        // hand as close to its sprite as the validated first span allows.
        Vector2 safe = _ropePathPoints[0];
        if (TryFindTerrainHit(next, safe, _ropeCollisionRadius, out _))
            return KeepAnchorOnPlayerSideOfTerrain(next, target);

        for (int i = 0; i < 5; i++)
        {
            Vector2 candidate = (safe + target) * 0.5f;
            if (TryFindTerrainHit(next, candidate, _ropeCollisionRadius, out _))
                target = candidate;
            else
                safe = candidate;
        }
        return safe;
    }

    private Vector2 KeepAnchorOnPlayerSideOfTerrain(Vector2 approach, Vector2 target)
    {
        if (!TryFindTerrainHit(approach, target, _ropeCollisionRadius, out RaycastHit2D hit))
            return target;

        if (hit.collider.TryGetComponent(out ElectricDoor door))
        {
            target = door.KeepOnApproachSide(hit.collider, approach, target, GetContactClearance());
            if (!TryFindTerrainHit(approach, target, _ropeCollisionRadius, out hit))
                return target;
        }

        // Hand sprites can reach through a wall even when the body cannot.
        float safeDistance = Mathf.Max(0f, hit.distance - Mathf.Max(_collisionSkin, 0.001f));
        return approach + (target - approach).normalized * safeDistance;
    }

    internal Vector2 KeepOnPlayerSideOfClosedDoor(Vector2 target, float radius)
    {
        if (playerRb == null)
            return target;

        // An animated hand or recall target can cross a closed gate while the body is blocked.
        Vector2 approach = playerRb.position;
        Vector2 movement = target - approach;
        float distance = movement.magnitude;
        float clearance = radius + _collisionSkin;
        int hitCount = Physics2D.CircleCast(approach, clearance,
            distance > MinPathSegmentLength ? movement / distance : Vector2.right,
            _terrainFilter, _ropeCastHits, distance);
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = _ropeCastHits[i].collider;
            if (hit != null && hit.TryGetComponent(out ElectricDoor door))
                target = door.KeepOnApproachSide(hit, approach, target, clearance);
        }

        return target;
    }

    private void ApplyPlayerClimbMotion(Vector2 anchor)
    {
        float distance = GetRopePathLength();
        float dt = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        float shortening = Mathf.Min(_playerClimbSpeed * dt,
            Mathf.Max(0f, distance - _playerClimbMinimumLength));
        maxRopeLength = Mathf.Max(_playerClimbMinimumLength, distance - shortening);

        // 地形に支えられたBallを引き戻すSafetyLimit/反作用は使わない。
        // Playerだけを最初の巻き付き点へ進め、衝突解決はPhysicsに任せる。
        // 毎stepの実距離から速度を決めるので、障害物で止まっても短縮を蓄積しない。
        Vector2 direction = GetPlayerPathDirection(anchor);
        if (direction.sqrMagnitude <= 0.0001f)
            return;
        float currentSpeed = Vector2.Dot(playerRb.linearVelocity, direction);
        playerRb.linearVelocity += direction * (shortening / dt - currentSpeed);
    }

    private void RebuildRopePath(Vector2 start, Vector2 end)
    {
        RefreshStoredContacts();
        TryRemoveRedundantContact(start, end);
        bool hadContacts = _ropeContacts.Count > 0;
        _hasResolvedRopePath = TryBuildSolidPath(start, end);
        if (!_hasResolvedRopePath && hadContacts)
        {
            // A moving obstacle can invalidate an old route. Retry from the
            // endpoints before using any of that route for tension.
            _ropeContacts.Clear();
            _hasResolvedRopePath = TryBuildSolidPath(start, end);
        }

        if (!_hasResolvedRopePath)
        {
            _ropeContacts.Clear();
            _oneWaySupport.Clear();
            _ropePathPoints.Clear();
            _ropeContactPoints.Clear();
            return;
        }

        _oneWaySupport.Rebuild(_ropePathPoints, maxRopeLength, _ropeSagStrength,
            _ropeMaxSag, _ropeCollisionRadius, _collisionSkin, _terrainFilter, playerRb, morningStarRb);
        if (_oneWaySupport.IsSupporting)
        {
            _ropeContactPoints.Clear();
            for (int i = 1; i < _ropePathPoints.Count - 1; i++)
                _ropeContactPoints.Add(_ropePathPoints[i]);
        }
    }

    private bool TryBuildSolidPath(Vector2 start, Vector2 end)
    {
        while (true)
        {
            RebuildPointLists(start, end);
            bool clear = true;
            bool inserted = false;
            for (int segmentIndex = 0; segmentIndex < _ropePathPoints.Count - 1; segmentIndex++)
            {
                Vector2 segmentStart = _ropePathPoints[segmentIndex];
                Vector2 segmentEnd = _ropePathPoints[segmentIndex + 1];
                if (!TryFindTerrainHit(segmentStart, segmentEnd, _ropeCollisionRadius, out RaycastHit2D hit))
                    continue;

                // A stale contact can leave a blocked span even after both ends come clear.
                // Use the clear direct span before adding corners that could loop around the obstacle.
                if (_ropeContacts.Count > 0 && !TryFindTerrainHit(start, end, _ropeCollisionRadius, out _))
                {
                    _ropeContacts.Clear();
                    RebuildPointLists(start, end);
                    return true;
                }

                clear = false;
                int availablePointCount = MaxWrapPointCount - _ropeContacts.Count;
                if (availablePointCount == 0)
                    return false;

                int routeCount = FindRouteAroundHit(segmentStart, segmentEnd, hit, availablePointCount);
                if (routeCount == 0)
                    continue;

                InsertRouteContacts(segmentIndex, segmentStart, segmentEnd, hit.collider, routeCount);
                inserted = true;
                break;
            }

            // Every insertion consumes at least one of the available points.
            if (!inserted)
                return clear;
        }
    }

    private int FindRouteAroundHit(Vector2 start, Vector2 end, RaycastHit2D hit, int availablePointCount)
    {
        Collider2D collider = hit.collider;
        Bounds bounds = GetRoutingBounds(hit);
        int obstacleId = GetObstacleId(collider, bounds);
        int count = BuildRouteAroundCollider(start, end, bounds, collider, obstacleId, availablePointCount);
        if (count != 0 || !collider.TryGetComponent(out Tilemap tilemap))
            return count;

        // A cell edge can be buried in a neighboring tile of the same collider.
        // Search around both ends of the obstruction before giving up on it.
        if (TryFindTerrainHit(end, start, _ropeCollisionRadius, out RaycastHit2D reverse, collider))
        {
            bounds.Encapsulate(GetRoutingBounds(reverse));
            count = BuildRouteAroundCollider(start, end, bounds, collider, obstacleId, availablePointCount);
        }
        if (count != 0)
            return count;

        // Allow clearance for a staggered seam between the two hit cells.
        bounds.Expand(GetWorldCellSize(tilemap) * 2f);
        return BuildRouteAroundCollider(start, end, bounds, collider, obstacleId, availablePointCount);
    }

    private void RefreshStoredContacts()
    {
        for (int i = _ropeContacts.Count - 1; i >= 0; i--)
        {
            RopeContact contact = _ropeContacts[i];
            if (contact.Collider == null
                || !contact.Collider.enabled
                || !contact.Collider.gameObject.activeInHierarchy
                || (_terrainLayerMask.value & (1 << contact.Collider.gameObject.layer)) == 0)
            {
                _ropeContacts.RemoveAt(i);
                continue;
            }

            Vector2 renderedPoint = contact.Collider.transform.TransformPoint(contact.LocalPhysicsPoint);
            Rigidbody2D body = contact.Collider.attachedRigidbody;
            if (body != null)
            {
                Vector2 renderedOffset = renderedPoint - (Vector2)body.transform.position;
                Vector2 bodyLocalPoint = Quaternion.Inverse(body.transform.rotation) * renderedOffset;
                contact.PhysicsPoint = body.GetRelativePoint(bodyLocalPoint);
            }
            else
            {
                contact.PhysicsPoint = renderedPoint;
            }
            _ropeContacts[i] = contact;
        }
    }

    private void TryRemoveRedundantContact(Vector2 start, Vector2 end)
    {
        for (int i = 0; i < _ropeContacts.Count; i++)
        {
            Vector2 previous = i == 0 ? start : _ropeContacts[i - 1].PhysicsPoint;
            Vector2 next = i == _ropeContacts.Count - 1
                ? end
                : _ropeContacts[i + 1].PhysicsPoint;
            bool directPathClear = !TryFindTerrainHit(
                previous,
                next,
                _ropeCollisionRadius + _collisionSkin * 0.5f,
                out _);

            RopeContact contact = _ropeContacts[i];
            contact.ClearFixedSteps = directPathClear
                ? contact.ClearFixedSteps + 1
                : 0;
            _ropeContacts[i] = contact;
            if (contact.ClearFixedSteps < UnwrapClearFixedSteps)
                continue;

            _ropeContacts.RemoveAt(i);
            return;
        }
    }

    private void RebuildPointLists(Vector2 start, Vector2 end)
    {
        _ropePathPoints.Clear();
        _ropeContactPoints.Clear();
        _ropePathPoints.Add(start);
        for (int i = 0; i < _ropeContacts.Count; i++)
        {
            Vector2 point = _ropeContacts[i].PhysicsPoint;
            _ropePathPoints.Add(point);
            _ropeContactPoints.Add(point);
        }
        _ropePathPoints.Add(end);
    }

    private void InsertRouteContacts(
        int index, Vector2 start, Vector2 end, Collider2D collider, int count)
    {
        if (count > 1)
        {
            ShortenRouteAlongEdge(start, end, collider, count, 0, 1, start);
            ShortenRouteAlongEdge(start, end, collider, count, count - 1, count - 2, end);
        }

        // Refine the bounds corners onto the actual surface, but keep the
        // checked outer route if refinement cuts across the collider.
        for (int i = 0; i < count; i++)
        {
            Vector2 surface = collider.ClosestPoint(_bestRoute[i]);
            Vector2 offset = _bestRoute[i] - surface;
            _refinedRoute[i] = offset.sqrMagnitude > MinPathSegmentLength * MinPathSegmentLength
                ? surface + offset.normalized * GetContactClearance()
                : _bestRoute[i];
        }

        Vector2[] route = IsCandidateRouteClear(start, end, _refinedRoute, count, collider)
            ? _refinedRoute : _bestRoute;
        for (int i = 0; i < count; i++)
        {
            Vector2 renderedPoint = route[i];
            Rigidbody2D body = collider.attachedRigidbody;
            if (body != null)
            {
                Vector2 bodyLocalPoint = body.GetPoint(route[i]);
                renderedPoint = body.transform.position + body.transform.rotation * bodyLocalPoint;
            }
            _ropeContacts.Insert(index + i, new RopeContact
            {
                Collider = collider,
                LocalPhysicsPoint = collider.transform.InverseTransformPoint(renderedPoint),
                PhysicsPoint = route[i]
            });
        }
    }

    private void ShortenRouteAlongEdge(
        Vector2 start, Vector2 end, Collider2D collider, int count, int index, int neighbor, Vector2 endpoint)
    {
        // Next to a stair-stepped cliff, the rope can join a face part way up.
        // Going all the way to the lower bounds corner would add false length.
        Vector2 corner = _bestRoute[index];
        Vector2 edge = _bestRoute[neighbor] - corner;
        float fraction = Mathf.Clamp01(Vector2.Dot(endpoint - corner, edge) / edge.sqrMagnitude);
        if (fraction <= 0f || fraction >= 1f)
            return;

        _bestRoute[index] = corner + edge * fraction;
        if (!IsCandidateRouteClear(start, end, _bestRoute, count, collider))
            _bestRoute[index] = corner;
    }

    private float GetContactClearance()
    {
        return _ropeCollisionRadius + Mathf.Max(_collisionSkin, 0.001f);
    }

    private bool TryFindTerrainHit(
        Vector2 start, Vector2 end, float castRadius, out RaycastHit2D nearest,
        Collider2D targetCollider = null)
    {
        nearest = default;
        Vector2 movement = end - start;
        float distance = movement.magnitude;
        if (distance <= MinPathSegmentLength || _terrainLayerMask.value == 0)
            return false;

        int hitCount = Physics2D.CircleCast(start, castRadius, movement / distance,
            _terrainFilter, _ropeCastHits, distance);
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = _ropeCastHits[i];
            if (hit.collider == null || hit.distance >= nearestDistance
                || (targetCollider != null && hit.collider != targetCollider))
                continue;

            // The top-support history owns directional one-way collision.
            if (hit.collider.GetComponentInParent<OneWayPlatform>() != null)
                continue;

            nearest = hit;
            nearestDistance = hit.distance;
        }

        return nearest.collider != null;
    }

    private int BuildRouteAroundCollider(
        Vector2 start,
        Vector2 end,
        Bounds routingBounds,
        Collider2D collider,
        int obstacleId,
        int availablePointCount)
    {
        if (availablePointCount <= 0)
            return 0;

        float clearance = GetContactClearance();
        Rect expandedBounds = Rect.MinMaxRect(
            routingBounds.min.x - clearance,
            routingBounds.min.y - clearance,
            routingBounds.max.x + clearance,
            routingBounds.max.y + clearance);

        int bestCount = 0;
        int bestRouteKey = -1;
        float bestLength = float.PositiveInfinity;
        for (int startCorner = 0; startCorner < 4; startCorner++)
        {
            for (int endCorner = 0; endCorner < 4; endCorner++)
            {
                for (int step = -1; step <= 1; step += 2)
                {
                    int routeKey = EncodeRouteKey(startCorner, endCorner, step);
                    int count = BuildBoundsRouteCandidate(
                        start,
                        end,
                        expandedBounds,
                        routeKey,
                        _routeCandidate,
                        out float length);
                    if (count <= 0 || count > availablePointCount || length >= bestLength)
                        continue;
                    if (!IsCandidateRouteClear(start, end, _routeCandidate, count, collider))
                        continue;

                    bestCount = count;
                    bestLength = length;
                    bestRouteKey = routeKey;
                    CopyRoute(_routeCandidate, _bestRoute, count);
                }
            }
        }

        if (_routePreferenceByObstacle.TryGetValue(obstacleId, out int cachedRouteKey))
        {
            int cachedCount = BuildBoundsRouteCandidate(
                start,
                end,
                expandedBounds,
                cachedRouteKey,
                _cachedRoute,
                out float cachedLength);
            if (cachedCount > 0
                && cachedCount <= availablePointCount
                && IsCandidateRouteClear(start, end, _cachedRoute, cachedCount, collider)
                && cachedLength <= bestLength + WrapRouteHysteresis)
            {
                CopyRoute(_cachedRoute, _bestRoute, cachedCount);
                return cachedCount;
            }
        }

        if (bestCount > 0)
        {
            if (_routePreferenceByObstacle.Count >= 32
                && !_routePreferenceByObstacle.ContainsKey(obstacleId))
            {
                _routePreferenceByObstacle.Clear();
            }

            _routePreferenceByObstacle[obstacleId] = bestRouteKey;
        }

        return bestCount;
    }

    private bool IsCandidateRouteClear(Vector2 start, Vector2 end, Vector2[] points, int count, Collider2D collider)
    {
        float mergeDistance = Mathf.Max(0.01f, _ropeCollisionRadius * 0.25f);
        float mergeDistanceSquared = mergeDistance * mergeDistance;
        Vector2 previous = start;
        for (int i = 0; i < count; i++)
        {
            Vector2 point = points[i];
            if (collider.OverlapPoint(point) || (point - previous).sqrMagnitude <= mergeDistanceSquared)
                return false;
            for (int j = 0; j < _ropeContacts.Count; j++)
                if ((point - _ropeContacts[j].PhysicsPoint).sqrMagnitude <= mergeDistanceSquared)
                    return false;
            if (TryFindTerrainHit(previous, point, _ropeCollisionRadius, out _, collider))
                return false;
            previous = point;
        }

        return (end - previous).sqrMagnitude > mergeDistanceSquared
            && !TryFindTerrainHit(previous, end, _ropeCollisionRadius, out _, collider);
    }

    private Bounds GetRoutingBounds(RaycastHit2D hit)
    {
        Collider2D collider = hit.collider;
        if (collider == null)
            return default;

        Tilemap tilemap = collider.GetComponent<Tilemap>();
        if (tilemap == null)
            return collider.bounds;

        Vector2 normal = hit.normal.sqrMagnitude > MinPathSegmentLength * MinPathSegmentLength
            ? hit.normal.normalized
            : Vector2.up;
        Vector3 probe = hit.point - normal * Mathf.Max(0.02f, _collisionSkin + 0.01f);
        Vector3Int cell = tilemap.WorldToCell(probe);
        if (!tilemap.HasTile(cell))
        {
            float nearestDistance = float.PositiveInfinity;
            Vector3Int nearestCell = cell;
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    Vector3Int candidate = cell + new Vector3Int(x, y, 0);
                    if (!tilemap.HasTile(candidate))
                        continue;

                    float distance = ((Vector2)tilemap.GetCellCenterWorld(candidate) - hit.point).sqrMagnitude;
                    if (distance >= nearestDistance)
                        continue;

                    nearestDistance = distance;
                    nearestCell = candidate;
                }
            }

            cell = nearestCell;
        }

        // セル全体の矩形には草や透明余白も含まれる。Spriteの物理形状で範囲を取る。
        // 特に坂の終端では透明な上端を回避しようとして、鎖が床下へ迂回してしまう。
        Sprite sprite = tilemap.GetSprite(cell);
        if (sprite != null && tilemap.GetColliderType(cell) == Tile.ColliderType.Sprite)
        {
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            Vector3 origin = tilemap.CellToLocalInterpolated((Vector3)cell + tilemap.tileAnchor);
            Matrix4x4 tileTransform = tilemap.orientationMatrix * tilemap.GetTransformMatrix(cell);
            int shapeCount = sprite.GetPhysicsShapeCount();
            for (int shape = 0; shape < shapeCount; shape++)
            {
                sprite.GetPhysicsShape(shape, _tilePhysicsShape);
                foreach (Vector2 point in _tilePhysicsShape)
                {
                    Vector3 world = tilemap.transform.TransformPoint(origin + tileTransform.MultiplyPoint3x4(point));
                    min = Vector3.Min(min, world); max = Vector3.Max(max, world);
                }
            }
            if (shapeCount > 0 && min.x <= max.x && min.y <= max.y)
                return new Bounds((min + max) * 0.5f, max - min);
        }

        Vector3 center = tilemap.GetCellCenterWorld(cell);
        return new Bounds(center, GetWorldCellSize(tilemap));
    }

    private static Vector3 GetWorldCellSize(Tilemap tilemap)
    {
        Vector3 scale = tilemap.transform.lossyScale;
        return Vector3.Scale(
            tilemap.cellSize,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
    }

    private static int GetObstacleId(Collider2D collider, Bounds bounds)
    {
        unchecked
        {
            int hash = collider != null ? collider.GetInstanceID() : 0;
            hash = hash * 397 ^ Mathf.RoundToInt(bounds.center.x * 100f);
            hash = hash * 397 ^ Mathf.RoundToInt(bounds.center.y * 100f);
            return hash;
        }
    }

    private int BuildBoundsRouteCandidate(
        Vector2 start,
        Vector2 end,
        Rect bounds,
        int routeKey,
        Vector2[] output,
        out float length)
    {
        DecodeRouteKey(routeKey, out int startCorner, out int endCorner, out int step);
        int count = 0;
        int corner = startCorner;
        output[count++] = GetBoundsCorner(bounds, corner);
        while (corner != endCorner && count < output.Length)
        {
            corner = (corner + step + 4) % 4;
            output[count++] = GetBoundsCorner(bounds, corner);
        }

        if (corner != endCorner)
        {
            length = 0f;
            return 0;
        }

        length = Vector2.Distance(start, output[0]);
        for (int i = 1; i < count; i++)
            length += Vector2.Distance(output[i - 1], output[i]);
        length += Vector2.Distance(output[count - 1], end);
        return count;
    }

    private static int EncodeRouteKey(int startCorner, int endCorner, int step)
    {
        return ((startCorner * 4 + endCorner) * 2) + (step > 0 ? 1 : 0);
    }

    private static void DecodeRouteKey(int routeKey, out int startCorner, out int endCorner, out int step)
    {
        step = (routeKey & 1) != 0 ? 1 : -1;
        int corners = routeKey >> 1;
        startCorner = corners / 4;
        endCorner = corners % 4;
    }

    private static Vector2 GetBoundsCorner(Rect bounds, int index)
    {
        switch (index)
        {
            case 0: return new Vector2(bounds.xMin, bounds.yMin);
            case 1: return new Vector2(bounds.xMin, bounds.yMax);
            case 2: return new Vector2(bounds.xMax, bounds.yMax);
            default: return new Vector2(bounds.xMax, bounds.yMin);
        }
    }

    private static void CopyRoute(Vector2[] source, Vector2[] destination, int count)
    {
        for (int i = 0; i < count; i++)
            destination[i] = source[i];
    }

    private float GetRopePathLength()
    {
        float length = 0f;
        for (int i = 1; i < _ropePathPoints.Count; i++)
            length += Vector2.Distance(_ropePathPoints[i - 1], _ropePathPoints[i]);
        return length;
    }

    private Vector2 GetPlayerPathDirection(Vector2 fallbackStart)
    {
        Vector2 target = _ropePathPoints.Count > 1
            ? _ropePathPoints[1]
            : morningStarRb.position;
        Vector2 direction = target - fallbackStart;
        return direction.sqrMagnitude > MinPathSegmentLength * MinPathSegmentLength
            ? direction.normalized
            : Vector2.zero;
    }

    private Vector2 GetBallPathDirection(Vector2 fallbackStart)
    {
        Vector2 previous = _ropePathPoints.Count > 1
            ? _ropePathPoints[_ropePathPoints.Count - 2]
            : fallbackStart;
        Vector2 direction = morningStarRb.position - previous;
        return direction.sqrMagnitude > MinPathSegmentLength * MinPathSegmentLength
            ? direction.normalized
            : Vector2.zero;
    }

    private void ApplyRopeTension(Vector2 handPosition)
    {
        // トランポリン跳躍中はBall側の長さ補正だけで追従させ、Playerへ力を返さない。
        // 吊り下がりの荷重は最大長の制約で支え、弾性の張力で上下に押し戻さない。
        if (IsPlayerPullSuppressed || _suspendedPlayerAnchorLocal.HasValue)
            return;

        float distance = GetRopePathLength();
        if (distance <= 0.001f)
        {
            ResetGroundPullEase();
            return;
        }

        // Flying中は最大長まで既存の射出慣性を優先し、境界で初めて張力を伝える。
        // Rest / Draggingは従来どおり手前から連続的に張力を立ち上げる。
        float startDistance = maxRopeLength * tensionStartRatio;
        if (_prioritizeFlyingTrajectory && distance < maxRopeLength - 0.001f)
        {
            ResetGroundPullEase();
            return;
        }

        if (!_prioritizeFlyingTrajectory && distance <= startDistance)
        {
            ResetGroundPullEase();
            return;
        }

        Vector2 playerPathDirection = GetPlayerPathDirection(handPosition);
        Vector2 ballPathDirection = GetBallPathDirection(handPosition);
        float tautness = 1f;
        if (!_prioritizeFlyingTrajectory)
        {
            float tensionRange = Mathf.Max(0.01f, maxRopeLength - startDistance);
            tautness = Mathf.Clamp01((distance - startDistance) / tensionRange);
            tautness = tautness * tautness * (3f - 2f * tautness);
        }

        float separatingSpeed = Mathf.Max(
            0f,
            Vector2.Dot(morningStarRb.linearVelocity, ballPathDirection)
            - Vector2.Dot(playerRb.linearVelocity, playerPathDirection));
        bool grounded = _player != null && _player.IsGrounded;
        float playerMultiplier = grounded ? 1f : airTensionMultiplier;
        float groundResistanceMultiplier = GetGroundPullResistanceMultiplier(grounded, playerPathDirection);
        float forceMagnitude = (tautness * tensionStrength + separatingSpeed * tensionDamping)
            * playerMultiplier
            * groundResistanceMultiplier;
        if (maxTensionForce > 0f)
            forceMagnitude = Mathf.Min(
                forceMagnitude,
                maxTensionForce * playerMultiplier * groundResistanceMultiplier);
        if (forceMagnitude <= 0f)
            return;

        Vector2 playerDirection = playerPathDirection;
        if (grounded)
        {
            playerDirection.y = 0f;
            if (playerDirection.sqrMagnitude > 0.0001f)
                playerDirection.Normalize();
        }

        if (playerDirection.sqrMagnitude > 0.0001f)
        {
            Vector2 playerForce = playerDirection * forceMagnitude;
            float runJumpMomentumProtection = 0f;
            if (!grounded)
                runJumpMomentumProtection = ProtectRunJumpMomentum(ref playerForce);

            LastAppliedPlayerTension = playerForce;
            playerRb.AddForce(playerForce, ForceMode2D.Force);
            ApplyRunJumpBallFollow(
                ballPathDirection,
                forceMagnitude,
                playerMultiplier,
                runJumpMomentumProtection);
        }

        // Flying中は照準軌道を優先し、通常張力でBallをPlayer側へ戻さない。
        // Restでは弱い反作用だけを返し、方向転換時の遅れと慣性を残す。
        if (!_prioritizeFlyingTrajectory)
        {
            // 空中のPlayerを床上のBallが支えている時も張力を伝え続ける。
            // 最大長で反作用を消すと崖上のBallが固定支点のように残ってしまう。
            bool supportingPlayer = !grounded
                && playerRb.worldCenterOfMass.y < morningStarRb.worldCenterOfMass.y
                && HasBallFloorSupport();
            float ballReactionRatio = RestBallReactionMultiplier * (supportingPlayer ? 1f : (1f - tautness));
            float ballForce = forceMagnitude
                / playerMultiplier
                * ballReactionRatio;
            AddBallTerrainAwareForce(-ballPathDirection * ballForce);
        }
    }

    private bool HasBallFloorSupport()
    {
        int count = morningStarRb.GetContacts(_ballContacts);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = _ballContacts[i];
            if (!contact.enabled)
                continue;
            Collider2D other = contact.collider != null && contact.collider.attachedRigidbody == morningStarRb
                ? contact.otherCollider : contact.collider;
            if (other != null && !other.isTrigger
                && (_terrainLayerMask.value & (1 << other.gameObject.layer)) != 0
                && contact.normal.y > 0.5f)
                return true;
        }
        return false;
    }

    private float GetGroundPullResistanceMultiplier(bool grounded, Vector2 directionToBall)
    {
        if (!grounded || _player == null)
        {
            ResetGroundPullEase();
            return 1f;
        }

        float moveInput = _player.MoveInputX;
        bool pullingBall = Mathf.Abs(moveInput) > GroundPullInputThreshold
                           && Mathf.Abs(directionToBall.x) > GroundPullInputThreshold
                           && moveInput * directionToBall.x < 0f;
        if (!pullingBall)
        {
            ResetGroundPullEase();
            return GroundInitialResistanceMultiplier;
        }

        float pullDirection = Mathf.Sign(moveInput);
        if (_groundPullDirection != 0f && pullDirection != _groundPullDirection)
            _groundPullElapsed = 0f;

        _groundPullDirection = pullDirection;
        _groundPullElapsed += Time.fixedDeltaTime;

        float progress = Mathf.Clamp01(_groundPullElapsed / groundPullEaseTime);
        progress = progress * progress * (3f - 2f * progress);
        return Mathf.Lerp(GroundInitialResistanceMultiplier, movingPullResistance, progress);
    }

    private void ResetGroundPullEase()
    {
        _groundPullElapsed = 0f;
        _groundPullDirection = 0f;
    }

    private void UpdateRunJumpMomentumGrace(Vector2 handPosition, float pathLength)
    {
        bool grounded = _player != null && _player.IsGrounded;
        if (!_groundStateInitialized)
        {
            _groundStateInitialized = true;
            _wasPlayerGrounded = grounded;
            return;
        }

        bool leftGroundThisStep = _wasPlayerGrounded && !grounded;
        if (grounded || _prioritizeFlyingTrajectory)
        {
            _runJumpMomentumGraceRemaining = 0f;
            _runJumpTensionScale = 1f;
            _runJumpMomentumDirectionX = 0f;
        }
        else if (leftGroundThisStep && ShouldProtectRunJumpMomentum(handPosition, pathLength))
        {
            _runJumpMomentumGraceRemaining = jumpMomentumGraceTime;
            _runJumpMomentumDirectionX = Mathf.Sign(playerRb.linearVelocity.x);

            float horizontalSpeed = Mathf.Abs(playerRb.linearVelocity.x);
            float fullProtectionSpeed = Mathf.Max(
                runJumpMomentumThreshold + 0.01f,
                runJumpMomentumThreshold * 2f);
            float speedFactor = Mathf.InverseLerp(
                runJumpMomentumThreshold,
                fullProtectionSpeed,
                horizontalSpeed);
            speedFactor = speedFactor * speedFactor * (3f - 2f * speedFactor);
            _runJumpTensionScale = Mathf.Lerp(1f, runJumpTensionMultiplier, speedFactor);
        }
        else
        {
            _runJumpMomentumGraceRemaining = Mathf.Max(
                0f,
                _runJumpMomentumGraceRemaining - Time.fixedDeltaTime);
        }

        _wasPlayerGrounded = grounded;
    }

    private bool ShouldProtectRunJumpMomentum(Vector2 handPosition, float pathLength)
    {
        if (jumpMomentumGraceTime <= 0f
            || Mathf.Abs(playerRb.linearVelocity.x) < runJumpMomentumThreshold
            || _rollingVisual == null
            || !_rollingVisual.IsGrounded)
        {
            return false;
        }

        Vector2 rope = morningStarRb.position - handPosition;
        bool ropeIsTaut = pathLength > maxRopeLength * tensionStartRatio;
        bool ballTrailsMomentum = playerRb.linearVelocity.x * rope.x < 0f;
        return ropeIsTaut && ballTrailsMomentum;
    }

    private float ProtectRunJumpMomentum(ref Vector2 playerForce)
    {
        if (_runJumpMomentumGraceRemaining <= 0f || jumpMomentumGraceTime <= 0f)
            return 0f;

        float progress = 1f - _runJumpMomentumGraceRemaining / jumpMomentumGraceTime;
        progress = Mathf.Clamp01(progress);
        progress = progress * progress * (3f - 2f * progress);
        float tensionScale = Mathf.Lerp(_runJumpTensionScale, 1f, progress);

        // 追加加速はせず、離地時に得た慣性へ逆らうBall張力だけを弱める。
        if (playerForce.x * _runJumpMomentumDirectionX < 0f)
            playerForce.x *= tensionScale;

        if (playerRb.linearVelocity.y > 0f && playerForce.y < 0f)
            playerForce.y *= tensionScale;

        return 1f - tensionScale;
    }

    private void ApplyRunJumpBallFollow(
        Vector2 directionToBall,
        float forceMagnitude,
        float playerMultiplier,
        float momentumProtection)
    {
        if (momentumProtection <= 0f
            || _prioritizeFlyingTrajectory
            || directionToBall.x * _runJumpMomentumDirectionX >= 0f)
        {
            return;
        }

        // Player側で抑えた抵抗に応じ、遅れたBallへ同等の加速度を滑らかに渡す。
        // ForceModeなので瞬間移動や追加Impulseにはならず、Grace終了時に自然に0へ戻る。
        float playerMass = Mathf.Max(0.01f, playerRb.mass);
        float ballToPlayerMassRatio = morningStarRb.mass / playerMass;
        float baseForceMagnitude = forceMagnitude / Mathf.Max(1f, playerMultiplier);
        float ballFollowForce = baseForceMagnitude
                                * momentumProtection
                                * ballToPlayerMassRatio;
        AddBallTerrainAwareForce(-directionToBall * ballFollowForce);
    }

    private void AddBallTerrainAwareForce(Vector2 force)
    {
        Vector2 allowedForce = RemoveCollisionInwardComponent(force);
        if (allowedForce.sqrMagnitude > 0.000001f)
            morningStarRb.AddForce(allowedForce, ForceMode2D.Force);
    }

    private Vector2 RemoveCollisionInwardComponent(Vector2 value)
    {
        return RemoveBodyCollisionInwardComponent(morningStarRb, value);
    }

    private Vector2 RemoveBodyCollisionInwardComponent(Rigidbody2D body, Vector2 value)
    {
        if (body == null || value.sqrMagnitude <= 0.000001f)
            return value;

        // ロープ経路用の地形Maskでは絞らない。敵・仕掛けなども含め、
        // 実際の接触面へ押し込む補正だけを除き、接線方向の移動は残す。
        int contactCount = body.GetContacts(_ballContacts);
        Vector2 ballCenter = body.worldCenterOfMass;
        for (int i = 0; i < contactCount; i++)
        {
            ContactPoint2D contact = _ballContacts[i];
            if (!contact.enabled)
                continue;
            Vector2 normal = contact.normal;
            if (normal.sqrMagnitude <= MinPathSegmentLength * MinPathSegmentLength)
                continue;

            normal.Normalize();
            if (Vector2.Dot(normal, ballCenter - contact.point) < 0f)
                normal = -normal;

            float inwardMagnitude = Vector2.Dot(value, normal);
            if (inwardMagnitude < 0f)
                value -= normal * inwardMagnitude;
        }

        return value;
    }

    private Vector2 GetSafeEndpointCorrection(
        Rigidbody2D body, Vector2 endpoint, Vector2 target, float excessLength)
    {
        Vector2 toTarget = target - endpoint;
        float segmentLength = toTarget.magnitude;
        if (segmentLength <= MinPathSegmentLength || excessLength <= 0f)
            return Vector2.zero;

        Vector2 direction = RemoveBodyCollisionInwardComponent(body, toTarget / segmentLength);
        if (direction.sqrMagnitude <= 0.000001f)
            return Vector2.zero;
        direction.Normalize();

        // 接触面に沿って動ける場合、その方向で実際に短くなる距離を解く。
        // 法線成分を消しただけの残量をPlayerへ転嫁して跳ね上げない。
        float along = Mathf.Max(0f, Vector2.Dot(toTarget, direction));
        float perpendicularSquared = Mathf.Max(0f, toTarget.sqrMagnitude - along * along);
        float targetLength = Mathf.Max(0f, segmentLength - excessLength);
        float distance = along - Mathf.Sqrt(Mathf.Max(0f,
            targetLength * targetLength - perpendicularSquared));
        if (distance <= MinPathSegmentLength)
            return Vector2.zero;

        // 接触履歴だけでは、まだ接触していない壁を跨ぐ位置補正を防げない。
        // 実際のCollider形状を移動先までCastし、貫通する手前で止める。
        int hitCount = body.Cast(direction, _bodyCastHits, distance);
        float skin = Mathf.Max(_collisionSkin, 0.001f);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = _bodyCastHits[i];
            if (hit.collider == null || hit.collider.isTrigger)
                continue;
            OneWayPlatform oneWay = hit.collider.GetComponentInParent<OneWayPlatform>();
            if (oneWay != null && !oneWay.BlocksBodyCorrection(body, direction, hit.normal))
                continue;
            // 接線方向・接触面から離れる方向の移動は妨げない。
            if (Vector2.Dot(direction, hit.normal) >= -0.0001f)
                continue;
            distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - skin));
        }

        return direction * distance;
    }

    private void LimitLadderMovementAtBlockedBall(Vector2 anchor)
    {
        if (_ropePathPoints.Count < 2) return;
        Vector2 step = playerRb.linearVelocity * Time.fixedDeltaTime;
        if (step.sqrMagnitude < 0.000001f) return;
        Vector2 nextPoint = _ropePathPoints[1];
        float currentLength = GetRopePathLength();
        float increase = Vector2.Distance(anchor + step, nextPoint) - Vector2.Distance(anchor, nextPoint);
        if (increase <= 0f) return;
        float excess = currentLength + increase - Mathf.Max(currentLength, maxRopeLength);
        if (excess <= 0.0001f) return;
        Vector2 ball = morningStarRb.position;
        Vector2 lastPoint = _ropePathPoints[_ropePathPoints.Count - 2];
        if (_ropePathPoints.Count == 2) lastPoint += step;
        Vector2 correction = GetSafeEndpointCorrection(morningStarRb, ball, lastPoint, excess);
        float available = Vector2.Distance(ball, lastPoint) - Vector2.Distance(ball + correction, lastPoint);
        if (available + 0.001f < excess)
            playerRb.linearVelocity *= Mathf.Clamp01((Mathf.Max(0f, maxRopeLength - currentLength) + available) / increase);
    }

    private Vector2 EnforceSafetyLimit(Vector2 handPosition)
    {
        float distance = GetRopePathLength();
        if (distance <= 0.001f)
            return handPosition;

        bool suspended = _suspendedPlayerAnchorLocal.HasValue;
        Vector2 playerPathDirection = GetPlayerPathDirection(handPosition);
        Vector2 ballPathDirection = GetBallPathDirection(handPosition);

        if (distance > maxRopeLength)
        {
            // A supported ball rolls under the rope's velocity correction.
            // Spending the length error sideways at its floor contact can move it off the ledge in one step.
            if (!suspended)
            {
                float overshoot = distance - maxRopeLength;
                Vector2 ballTarget = _ropePathPoints[_ropePathPoints.Count - 2];
                Vector2 ballCorrection = GetSafeEndpointCorrection(
                    morningStarRb, morningStarRb.position, ballTarget, overshoot);
                if (ballCorrection != Vector2.zero)
                {
                    morningStarRb.position += ballCorrection;
                    RebuildRopePath(handPosition, morningStarRb.position);
                    if (!_hasResolvedRopePath)
                        return handPosition;
                }
            }

            float blockedRemainder = GetRopePathLength() - maxRopeLength;
            if (!IsPlayerPullSuppressed && blockedRemainder > 0.0001f && playerPathDirection.sqrMagnitude > 0.0001f)
            {
                Vector2 playerCorrection = GetSafeEndpointCorrection(
                    playerRb, handPosition, _ropePathPoints[1], blockedRemainder);
                if (playerCorrection != Vector2.zero)
                {
                    playerRb.position += playerCorrection;
                    handPosition = KeepAnchorOnPlayerSideOfTerrain(
                        playerRb.worldCenterOfMass, handPosition + playerCorrection);
                    RebuildRopePath(handPosition, morningStarRb.position);
                    if (!_hasResolvedRopePath)
                        return handPosition;
                }
            }

            distance = GetRopePathLength();
            playerPathDirection = GetPlayerPathDirection(handPosition);
            ballPathDirection = GetBallPathDirection(handPosition);
        }

        float step = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        Vector2 playerVelocity = playerRb.linearVelocity;
        Vector2 projectedPlayerVelocity = playerVelocity;
        if (suspended)
        {
            // Account for the player's queued movement and gravity before enforcing the
            // limit, so each step doesn't first fall through it and then snap back.
            projectedPlayerVelocity += (playerRb.totalForce / playerRb.mass
                + Physics2D.gravity * playerRb.gravityScale) * step;
        }

        float separatingSpeed = Vector2.Dot(morningStarRb.linearVelocity, ballPathDirection)
            - Vector2.Dot(projectedPlayerVelocity, playerPathDirection);
        float remainingLength = Mathf.Max(0f, maxRopeLength - distance);
        float allowedSeparatingSpeed = remainingLength / step;
        float excessSeparatingSpeed = separatingSpeed - allowedSeparatingSpeed;
        if (excessSeparatingSpeed > 0f)
        {
            // 次のPhysics stepで最大長を越える外向き相対速度だけを除去する。
            // Ballが自由ならPlayerの速度は変えず、Ballが地形に阻まれた残量だけ
            // Player側の「さらに離れる成分」を止める。
            Vector2 ballVelocityCorrection = RemoveCollisionInwardComponent(
                -ballPathDirection * excessSeparatingSpeed);
            morningStarRb.linearVelocity += ballVelocityCorrection;

            float correctedByBall = Mathf.Clamp(
                -Vector2.Dot(ballVelocityCorrection, ballPathDirection),
                0f,
                excessSeparatingSpeed);
            float blockedRemainder = excessSeparatingSpeed - correctedByBall;
            if (!IsPlayerPullSuppressed && blockedRemainder > 0.0001f && playerPathDirection.sqrMagnitude > 0.0001f)
            {
                // Brake the player's separating motion here. Ordinary tension
                // supplies acceleration toward the rope, including in the air.
                float outwardSpeed = -Vector2.Dot(projectedPlayerVelocity, playerPathDirection);
                if (outwardSpeed > 0f)
                {
                    Vector2 correctedVelocity = playerVelocity
                        + playerPathDirection * Mathf.Min(blockedRemainder, outwardSpeed);
                    playerRb.linearVelocity = RemoveBodyCollisionInwardComponent(playerRb, correctedVelocity);
                }
            }
        }

        return handPosition;
    }

    private void ClampBallSpeed()
    {
        if (maxBallSpeed <= 0f)
            return;

        Vector2 velocity = morningStarRb.linearVelocity;
        if (velocity.sqrMagnitude > maxBallSpeed * maxBallSpeed)
            morningStarRb.linearVelocity = velocity.normalized * maxBallSpeed;
    }
}
