using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

/// <summary>
/// Playerと鉄球を結ぶゲーム向けロープ制約。
/// たるみ中は何もせず、張力はPlayer側を主体に連続的に加える。
/// </summary>
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
    private const float UnwrapClearanceMargin = 0.01f;
    private const int UnwrapClearFixedSteps = 2;
    private const int MaxBallTerrainContactCount = 8;
    private const float MinPathSegmentLength = 0.001f;

    private struct RopeContact
    {
        public Collider2D Collider;
        public Vector2 LocalSurfacePoint;
        public Vector2 LocalNormal;
        public Vector2 LocalPhysicsPoint;
        public Vector2 PhysicsPoint;
        public int ObstacleId;
        public int ClearFixedSteps;
    }
    private bool _prioritizeFlyingTrajectory;
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
    private LayerMask _terrainLayerMask = (1 << 0) | (1 << 6);
    private float _ropeCollisionRadius = 0.14f;
    private float _collisionSkin = 0.01f;
    private ContactFilter2D _terrainFilter;
    private readonly RaycastHit2D[] _ropeCastHits = new RaycastHit2D[16];
    private readonly ContactPoint2D[] _ballTerrainContacts = new ContactPoint2D[MaxBallTerrainContactCount];
    private readonly List<Vector2> _ropePathPoints = new List<Vector2>(MaxWrapPointCount + 2);
    private readonly List<Vector2> _ropeContactPoints = new List<Vector2>(MaxWrapPointCount);
    private readonly List<RopeContact> _ropeContacts = new List<RopeContact>(MaxWrapPointCount);
    private readonly Dictionary<int, int> _routePreferenceByObstacle = new Dictionary<int, int>();
    private readonly Vector2[] _routeCandidate = new Vector2[4];
    private readonly Vector2[] _bestRoute = new Vector2[4];
    private readonly Vector2[] _cachedRoute = new Vector2[4];
    private readonly RopeContact[] _refinedRouteContacts = new RopeContact[4];
    private readonly RopeContact[] _fallbackRouteContacts = new RopeContact[4];

    private Player _player;
    private MorningStarRollingVisual _rollingVisual;

    public float MaxRopeLength => maxRopeLength;
    public float TensionStartRatio => tensionStartRatio;
    public float TensionStrength => tensionStrength;
    public float TensionDamping => tensionDamping;
    public float AirTensionMultiplier => airTensionMultiplier;
    public bool PrioritizesFlyingTrajectory => _prioritizeFlyingTrajectory;
    public int RopeContactPointCount => _ropeContactPoints.Count;

    public Vector2 GetRopeContactPoint(int index)
    {
        return index >= 0 && index < _ropeContactPoints.Count
            ? _ropeContactPoints[index]
            : Vector2.zero;
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

    public void ConfigureTerrainPath(LayerMask layerMask, float ropeRadius, float skin)
    {
        _terrainLayerMask = layerMask;
        _ropeCollisionRadius = Mathf.Max(0.001f, ropeRadius);
        _collisionSkin = Mathf.Max(0f, skin);
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
        _physicsAnchorInitialized = false;
        _anchorLocalVelocity = Vector2.zero;
        _ropePathPoints.Clear();
        _ropeContactPoints.Clear();
        _ropeContacts.Clear();
    }

    private void OnDisable()
    {
        _ropePathPoints.Clear();
        _ropeContactPoints.Clear();
        _ropeContacts.Clear();
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
        UpdateRunJumpMomentumGrace(physicsAnchor, GetRopePathLength());
        ApplyRopeTension(physicsAnchor);
        EnforceSafetyLimit(physicsAnchor);
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
        Vector2 targetLocalPosition = playerTransform.InverseTransformPoint(handAnchor.position);
        if (!_physicsAnchorInitialized)
        {
            _smoothedAnchorLocalPosition = targetLocalPosition;
            _anchorLocalVelocity = Vector2.zero;
            _physicsAnchorInitialized = true;
        }
        else
        {
            _smoothedAnchorLocalPosition = Vector2.SmoothDamp(
                _smoothedAnchorLocalPosition,
                targetLocalPosition,
                ref _anchorLocalVelocity,
                anchorFollowTime,
                Mathf.Infinity,
                Time.fixedDeltaTime);
        }

        // Player本体の移動は即時反映し、Animation/Facing由来の局所Anchor差分だけを補間する。
        return playerTransform.TransformPoint(_smoothedAnchorLocalPosition);
    }

    private void RebuildRopePath(Vector2 start, Vector2 end)
    {
        RefreshStoredContacts();
        TryRemoveRedundantContact(start, end);
        RebuildPointLists(start, end);

        int attempts = 0;
        while (_ropeContacts.Count < MaxWrapPointCount && attempts++ < MaxWrapPointCount * 2)
        {
            bool insertedContact = false;
            for (int segmentIndex = 0; segmentIndex < _ropePathPoints.Count - 1; segmentIndex++)
            {
                Vector2 segmentStart = _ropePathPoints[segmentIndex];
                Vector2 segmentEnd = _ropePathPoints[segmentIndex + 1];
                Collider2D startCollider = segmentIndex > 0
                    ? _ropeContacts[segmentIndex - 1].Collider
                    : null;
                Collider2D endCollider = segmentIndex < _ropeContacts.Count
                    ? _ropeContacts[segmentIndex].Collider
                    : null;
                if (!TryFindTerrainHit(
                        segmentStart,
                        segmentEnd,
                        _ropeCollisionRadius,
                        startCollider,
                        endCollider,
                        out RaycastHit2D hit))
                {
                    continue;
                }

                Bounds routingBounds = GetRoutingBounds(hit);
                int obstacleId = GetObstacleId(hit.collider, routingBounds);
                int availablePointCount = MaxWrapPointCount - _ropeContacts.Count;
                int routeCount = BuildRouteAroundCollider(
                    segmentStart,
                    segmentEnd,
                    routingBounds,
                    obstacleId,
                    availablePointCount);
                int insertedCount = InsertRouteContacts(
                    segmentIndex,
                    segmentStart,
                    segmentEnd,
                    hit,
                    obstacleId,
                    routeCount);
                if (insertedCount <= 0)
                    continue;

                RebuildPointLists(start, end);
                insertedContact = true;
                break;
            }

            if (!insertedContact)
                break;
        }

        RebuildPointLists(start, end);
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

            Transform colliderTransform = contact.Collider.transform;
            Vector2 surfaceNormal = colliderTransform.TransformVector(contact.LocalNormal);
            if (surfaceNormal.sqrMagnitude <= MinPathSegmentLength * MinPathSegmentLength)
            {
                _ropeContacts.RemoveAt(i);
                continue;
            }

            surfaceNormal.Normalize();
            contact.PhysicsPoint = colliderTransform.TransformPoint(contact.LocalPhysicsPoint);
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
            Collider2D previousCollider = i > 0 ? _ropeContacts[i - 1].Collider : null;
            Collider2D nextCollider = i < _ropeContacts.Count - 1
                ? _ropeContacts[i + 1].Collider
                : null;
            bool directPathClear = !TryFindTerrainHit(
                previous,
                next,
                _ropeCollisionRadius + _collisionSkin + UnwrapClearanceMargin,
                previousCollider,
                nextCollider,
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

    private int InsertRouteContacts(
        int insertionIndex,
        Vector2 segmentStart,
        Vector2 segmentEnd,
        RaycastHit2D hit,
        int obstacleId,
        int routeCount)
    {
        Vector2 fallbackNormal = hit.normal.sqrMagnitude > MinPathSegmentLength * MinPathSegmentLength
            ? hit.normal.normalized
            : Vector2.up;
        if (routeCount <= 0)
        {
            _bestRoute[0] = hit.point + fallbackNormal * GetContactClearance();
            routeCount = 1;
        }

        int candidateCount = BuildContactCandidates(
            hit.collider,
            fallbackNormal,
            obstacleId,
            routeCount,
            preserveSafeCandidate: false,
            _refinedRouteContacts);
        bool useRefinedContacts = candidateCount > 0
            && IsRouteClearForCollider(
                segmentStart,
                segmentEnd,
                _refinedRouteContacts,
                candidateCount,
                hit.collider);
        RopeContact[] contacts = _refinedRouteContacts;
        if (!useRefinedContacts)
        {
            candidateCount = BuildContactCandidates(
                hit.collider,
                fallbackNormal,
                obstacleId,
                routeCount,
                preserveSafeCandidate: true,
                _fallbackRouteContacts);
            if (candidateCount <= 0)
                return 0;
            contacts = _fallbackRouteContacts;
        }

        int insertedCount = 0;
        for (int routeIndex = 0; routeIndex < candidateCount; routeIndex++)
        {
            RopeContact contact = contacts[routeIndex];
            if (IsDuplicateContact(contact))
                continue;

            _ropeContacts.Insert(insertionIndex + insertedCount, contact);
            insertedCount++;
        }

        return insertedCount;
    }

    private int BuildContactCandidates(
        Collider2D collider,
        Vector2 fallbackNormal,
        int obstacleId,
        int routeCount,
        bool preserveSafeCandidate,
        RopeContact[] output)
    {
        int count = 0;
        for (int routeIndex = 0; routeIndex < routeCount; routeIndex++)
        {
            if (!TryCreateContact(
                    collider,
                    _bestRoute[routeIndex],
                    fallbackNormal,
                    obstacleId,
                    preserveSafeCandidate,
                    out RopeContact contact))
            {
                continue;
            }

            output[count++] = contact;
        }

        return count;
    }

    private bool TryCreateContact(
        Collider2D collider,
        Vector2 outsideCandidate,
        Vector2 fallbackNormal,
        int obstacleId,
        bool preserveSafeCandidate,
        out RopeContact contact)
    {
        contact = default;
        if (collider == null)
            return false;

        Vector2 surfacePoint = collider.ClosestPoint(outsideCandidate);
        Vector2 surfaceNormal = outsideCandidate - surfacePoint;
        if (surfaceNormal.sqrMagnitude <= MinPathSegmentLength * MinPathSegmentLength)
            surfaceNormal = fallbackNormal;
        if (surfaceNormal.sqrMagnitude <= MinPathSegmentLength * MinPathSegmentLength)
            return false;

        surfaceNormal.Normalize();
        Vector2 physicsPoint = preserveSafeCandidate
            ? outsideCandidate
            : surfacePoint + surfaceNormal * GetContactClearance();
        Transform colliderTransform = collider.transform;
        contact = new RopeContact
        {
            Collider = collider,
            LocalSurfacePoint = colliderTransform.InverseTransformPoint(surfacePoint),
            LocalNormal = colliderTransform.InverseTransformVector(surfaceNormal).normalized,
            LocalPhysicsPoint = colliderTransform.InverseTransformPoint(physicsPoint),
            PhysicsPoint = physicsPoint,
            ObstacleId = obstacleId,
            ClearFixedSteps = 0
        };
        return true;
    }

    private bool IsRouteClearForCollider(
        Vector2 start,
        Vector2 end,
        RopeContact[] contacts,
        int contactCount,
        Collider2D targetCollider)
    {
        for (int segmentIndex = 0; segmentIndex <= contactCount; segmentIndex++)
        {
            Vector2 segmentStart = segmentIndex == 0
                ? start
                : contacts[segmentIndex - 1].PhysicsPoint;
            Vector2 segmentEnd = segmentIndex == contactCount
                ? end
                : contacts[segmentIndex].PhysicsPoint;
            Collider2D startCollider = segmentIndex > 0 ? targetCollider : null;
            Collider2D endCollider = segmentIndex < contactCount ? targetCollider : null;
            if (SegmentCrossesCollider(
                    segmentStart,
                    segmentEnd,
                    targetCollider,
                    startCollider,
                    endCollider))
            {
                return false;
            }
        }

        return true;
    }

    private bool SegmentCrossesCollider(
        Vector2 start,
        Vector2 end,
        Collider2D targetCollider,
        Collider2D startCollider,
        Collider2D endCollider)
    {
        Vector2 movement = end - start;
        float distance = movement.magnitude;
        if (targetCollider == null || distance <= MinPathSegmentLength)
            return false;

        int hitCount = Physics2D.CircleCast(
            start,
            _ropeCollisionRadius,
            movement / distance,
            _terrainFilter,
            _ropeCastHits,
            distance);
        float endpointTolerance = Mathf.Max(0.005f, _collisionSkin + 0.002f);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = _ropeCastHits[i];
            if (hit.collider != targetCollider)
                continue;
            if (hit.collider == startCollider && hit.distance <= endpointTolerance)
                continue;
            if (hit.collider == endCollider && distance - hit.distance <= endpointTolerance)
                continue;
            return true;
        }

        return false;
    }

    private bool IsDuplicateContact(RopeContact candidate)
    {
        float mergeDistance = Mathf.Max(0.01f, _ropeCollisionRadius * 0.25f);
        float mergeDistanceSqr = mergeDistance * mergeDistance;
        for (int i = 0; i < _ropeContacts.Count; i++)
        {
            RopeContact existing = _ropeContacts[i];
            if (existing.Collider != candidate.Collider
                || existing.ObstacleId != candidate.ObstacleId)
            {
                continue;
            }

            if ((existing.PhysicsPoint - candidate.PhysicsPoint).sqrMagnitude <= mergeDistanceSqr)
                return true;
        }

        return false;
    }

    private float GetContactClearance()
    {
        return _ropeCollisionRadius + Mathf.Max(_collisionSkin, 0.001f);
    }

    private bool TryFindTerrainHit(
        Vector2 start,
        Vector2 end,
        float castRadius,
        Collider2D startCollider,
        Collider2D endCollider,
        out RaycastHit2D nearest)
    {
        nearest = default;
        Vector2 movement = end - start;
        float distance = movement.magnitude;
        if (distance <= MinPathSegmentLength || _terrainLayerMask.value == 0)
            return false;

        int hitCount = Physics2D.CircleCast(
            start,
            Mathf.Max(0.001f, castRadius),
            movement / distance,
            _terrainFilter,
            _ropeCastHits,
            distance);
        float nearestDistance = float.PositiveInfinity;
        float endpointTolerance = Mathf.Max(0.005f, _collisionSkin + 0.002f);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = _ropeCastHits[i];
            if (hit.collider == null || hit.collider.isTrigger || hit.distance >= nearestDistance)
            {
                continue;
            }

            if (hit.collider == startCollider && hit.distance <= endpointTolerance)
                continue;
            if (hit.collider == endCollider && distance - hit.distance <= endpointTolerance)
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

        Vector3 center = tilemap.GetCellCenterWorld(cell);
        Vector3 scale = tilemap.transform.lossyScale;
        Vector3 size = Vector3.Scale(
            tilemap.cellSize,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        return new Bounds(center, size);
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
        Vector2 firstCorner = GetBoundsCorner(bounds, startCorner);
        Vector2 lastCorner = GetBoundsCorner(bounds, endCorner);
        if (SegmentCrossesBoundsInterior(start, firstCorner, bounds)
            || SegmentCrossesBoundsInterior(lastCorner, end, bounds))
        {
            length = 0f;
            return 0;
        }

        int count = 0;
        int corner = startCorner;
        output[count++] = firstCorner;
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

    private static bool SegmentCrossesBoundsInterior(Vector2 start, Vector2 end, Rect bounds)
    {
        const float inset = 0.002f;
        Rect interior = Rect.MinMaxRect(
            bounds.xMin + inset,
            bounds.yMin + inset,
            bounds.xMax - inset,
            bounds.yMax - inset);
        if (interior.width <= 0f || interior.height <= 0f)
            return false;

        float tMin = 0f;
        float tMax = 1f;
        Vector2 delta = end - start;
        if (!ClipSegment(-delta.x, start.x - interior.xMin, ref tMin, ref tMax)
            || !ClipSegment(delta.x, interior.xMax - start.x, ref tMin, ref tMax)
            || !ClipSegment(-delta.y, start.y - interior.yMin, ref tMin, ref tMax)
            || !ClipSegment(delta.y, interior.yMax - start.y, ref tMin, ref tMax))
        {
            return false;
        }

        return tMax >= tMin && tMax > 0.0001f && tMin < 0.9999f;
    }

    private static bool ClipSegment(float denominator, float numerator, ref float tMin, ref float tMax)
    {
        if (Mathf.Abs(denominator) <= 0.000001f)
            return numerator >= 0f;

        float t = numerator / denominator;
        if (denominator < 0f)
        {
            if (t > tMax)
                return false;
            if (t > tMin)
                tMin = t;
        }
        else
        {
            if (t < tMin)
                return false;
            if (t < tMax)
                tMax = t;
        }

        return true;
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
        float distance = GetRopePathLength();
        if (distance <= 0.001f)
        {
            ResetGroundPullEase();
            return;
        }

        float startDistance = maxRopeLength * tensionStartRatio;
        if (distance <= startDistance)
        {
            ResetGroundPullEase();
            return;
        }

        Vector2 playerPathDirection = GetPlayerPathDirection(handPosition);
        Vector2 ballPathDirection = GetBallPathDirection(handPosition);
        float tensionRange = Mathf.Max(0.01f, maxRopeLength - startDistance);
        float tautness = Mathf.Clamp01((distance - startDistance) / tensionRange);
        tautness = tautness * tautness * (3f - 2f * tautness);

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
            // 境界ではPlayerだけをBallへ引き、Ball側の反作用は0へ収束させる。
            // SafetyLimitが外向き速度を止めた直後に内向きForceが残る跳ね返りを防ぐ。
            float ballReactionRatio = RestBallReactionMultiplier * (1f - tautness);
            float ballForce = forceMagnitude
                / playerMultiplier
                * ballReactionRatio;
            AddBallTerrainAwareForce(-ballPathDirection * ballForce);
        }
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
        Vector2 allowedForce = RemoveTerrainInwardComponent(force);
        if (allowedForce.sqrMagnitude > 0.000001f)
            morningStarRb.AddForce(allowedForce, ForceMode2D.Force);
    }

    private Vector2 RemoveTerrainInwardComponent(Vector2 value)
    {
        if (morningStarRb == null || value.sqrMagnitude <= 0.000001f)
            return value;

        int contactCount = morningStarRb.GetContacts(_terrainFilter, _ballTerrainContacts);
        Vector2 ballCenter = morningStarRb.worldCenterOfMass;
        for (int i = 0; i < contactCount; i++)
        {
            ContactPoint2D contact = _ballTerrainContacts[i];
            Collider2D terrainCollider = contact.collider;
            if (terrainCollider == null || terrainCollider.attachedRigidbody == morningStarRb)
                terrainCollider = contact.otherCollider;
            if (terrainCollider == null
                || (_terrainLayerMask.value & (1 << terrainCollider.gameObject.layer)) == 0)
            {
                continue;
            }

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

    private void EnforceSafetyLimit(Vector2 handPosition)
    {
        float distance = GetRopePathLength();
        if (distance <= maxRopeLength || distance <= 0.001f)
            return;

        Vector2 playerPathDirection = GetPlayerPathDirection(handPosition);
        Vector2 ballPathDirection = GetBallPathDirection(handPosition);
        float separatingSpeed = Vector2.Dot(morningStarRb.linearVelocity, ballPathDirection)
            - Vector2.Dot(playerRb.linearVelocity, playerPathDirection);
        if (separatingSpeed > 0f)
        {
            // BallとPlayerの共通移動は残し、鎖外方向へ離れる成分だけを除去する。
            Vector2 correction = RemoveTerrainInwardComponent(
                -ballPathDirection * separatingSpeed);
            morningStarRb.linearVelocity += correction;
        }
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
