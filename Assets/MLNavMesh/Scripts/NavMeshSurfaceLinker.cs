using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

#region Jobs
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using UnityEditor.AI;
#endregion

// Based on source code found in this Unity post: https://forum.unity.com/threads/navmesh-links-generator-for-navmeshcomponents.515143/
namespace RSToolkit.AI
{
    [RequireComponent(typeof(NavMeshSurface))]
    public class NavMeshSurfaceLinker : MonoBehaviour
    {
        public enum LinkDirection
        {
            Horizontal,
            Vertical,
            Both
        }

        private NavMeshSurface _surface;

        private float _agentRadius;
    

        //private List<MeshEdge> _edges = new List<MeshEdge>();
        private LinkedList<MeshEdge> _linkedEdges = new LinkedList<MeshEdge>();
        //private RaycastHit[] _raycastHits = new RaycastHit[1];
        private RaycastHit raycastHit;

        private Vector3[] _startEnd = new Vector3[2];
        private List<MeshEdge> _edges = new List<MeshEdge>(512);
        //private NativeList<MeshEdge> _listEdges = new NativeList<MeshEdge>(Allocator.Persistent);

        [SerializeField]
        private LinkDirection _direction = LinkDirection.Vertical;
        public LinkDirection direction { get { return _direction; } set { _direction = value; } }

        #region NavMeshLinks
        
        [SerializeField]
        private float _minJumpHeight = 0.15f;
        public float minJumpHeight { get { return _minJumpHeight; } set { _minJumpHeight = value; } }

        [SerializeField]
        private float _maxJumpHeight = 1f;
        public float maxJumpHeight { get { return _maxJumpHeight; } set { _maxJumpHeight = value; } }

        [SerializeField]
        private float _jumpDistVertical = 0.035f;
        public float jumpDistVertical { get { return _jumpDistVertical; } set { _jumpDistVertical = value; } }

        #endregion NavMeshLinks

        #region NavMeshLinks Horizontal

        [SerializeField]
        private float _maxJumpDistHorizontal = 5f;
        public float maxJumpDistHorizontal { get { return _maxJumpDistHorizontal; } set { _maxJumpDistHorizontal = value; } }

        [SerializeField]
        private float _linkStartPointOffset = .25f;
        public float linkStartPointOffset { get { return _linkStartPointOffset; } set { _linkStartPointOffset = value; } }

        private Vector3 _obsticleCheckDirection;
        [SerializeField]
        private float _obsticleCheckYOffset = 0.5f;
        public float obsticleCheckYOffset { get { return _obsticleCheckYOffset; } set { _obsticleCheckYOffset = value; } }

        private Vector3 _obsticleCheckOrigin;

        [SerializeField]
        private float _sphereCastRadius = 1f;
        public float sphereCastRadius { get { return _sphereCastRadius; } set { _sphereCastRadius = value; } }

        #endregion NavMeshLinks Horizontal

        #region NavMeshLink Values

        [SerializeField]
        private float _linkWidth = 0.25f;
        public float linkWidth { get { return _linkWidth; } set { _linkWidth = value; } }

        // Area Type
        [SerializeField]
        private int _linkArea;
        public int linkArea { get { return _linkArea; } set { _linkArea = value; } }

        [SerializeField]
        private bool _linkBidirectional;
        public bool linkBidirectional { get { return _linkBidirectional; } set { _linkBidirectional = value; } }

        [SerializeField]
        private int _linkCostModifier = -1;
        public int linkCostModifier { get { return _linkCostModifier; } set { _linkCostModifier = value; } }

        [SerializeField]
        private bool _linkAutoUpdatePosition = true;
        public bool linkAutoUpdatePosition { get { return _linkAutoUpdatePosition; } set { _linkAutoUpdatePosition = value; } }

        #endregion NavMeshLink Values

        #region NavMesh Edge Normal

        [SerializeField]
        private bool _invertFacingNormal = false;
        public bool invertFacingNormal { get { return _invertFacingNormal; } set { _invertFacingNormal = value; } }

        [SerializeField]
        private bool _dontAlignYAxis = false;
        public bool dontAlignYAxis { get { return _dontAlignYAxis; } set { _dontAlignYAxis = value; } }

        #endregion NavMesh Edge Normal

        private NavMeshSurface _navMeshSurfaceComponent;
        public NavMeshSurface NavMeshSurfaceComponent
        {
            get
            {
                if (_navMeshSurfaceComponent == null)
                {
                    _navMeshSurfaceComponent = GetComponent<NavMeshSurface>();
                }
                return _navMeshSurfaceComponent;
            }
        }


        public void Clear()
        {
            var navMeshLinks = GetComponentsInChildren<NavMeshLink>();
            for (int i = 0; i < navMeshLinks.Length; i++)
            {
                if (navMeshLinks[i] != null)
                {
                    DestroyImmediate(navMeshLinks[i].gameObject);
                }
            }
            _edges.Clear();
            //_linkedEdges.Clear();
        }

        #region MeshEdge

        public void SetNavMeshEdges(NavMeshTriangulation sourceTriangulation, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        {
            // Run as SetMeshEdge as a job.
            var sourceBounds = _surface.navMeshData.sourceBounds;

            var job = new SetMeshEdgeTask(invertFacingNormal, dontAlignYAxis,
                                            sourceBounds, minJumpHeight, maxJumpHeight, jumpDistVertical,
                                            sourceTriangulation.vertices, sourceTriangulation.indices);

            var jobHandle = job.Schedule(); // Run the job

            jobHandle.Complete();   // Wait until it completes

            //_edges.AddRange(job.edges); // Get the output result

            foreach (var item in job.edges)
            {
                _edges.Add(item);
            }

            job.edges.Dispose();    // dispose the job edges.
            job.indices.Dispose();
            job.vertices.Dispose();
        }       

        #endregion MeshEdge

        #region Convert to Jobs
        //private JobHandle SetMeshEdgeJob(NavMeshTriangulation source, bool invertFacingNormal, bool dontAlignYAxis, Bounds bounds)
        //{
        //    var job = new SetMeshEdgeTask(source, invertFacingNormal, dontAlignYAxis, _listEdges, bounds, minJumpHeight, maxJumpHeight, jumpDistVertical);
        //    return job.Schedule();
        //}

        [BurstCompile]
        public struct SetMeshEdgeTask : IJob
        {
            //public NavMeshTriangulation source;
            public bool invertFacingNormal;
            public bool dontAlignYAxis;
            public NativeList<MeshEdge> edges;
            public Bounds bounds;
            public float minJumpHeight;
            public float maxJumpHeight;
            public float jumpDistVertical;
            public NativeArray<Vector3> vertices;
            public NativeArray<int> indices;

            //public SetMeshEdgeTask(NavMeshTriangulation source, bool invertFacingNormal, bool dontAlignYAxis,
            public SetMeshEdgeTask(bool invertFacingNormal, bool dontAlignYAxis, Bounds bounds,
                float minJumpHeight, float maxJumpHeight, float jumpDistVertical,
                Vector3[] vertices, int[] indices)
            {
                //this.source = source;
                this.invertFacingNormal = invertFacingNormal;
                this.dontAlignYAxis = dontAlignYAxis;
                this.edges = new NativeList<MeshEdge>(Allocator.Persistent);
                this.bounds = bounds;
                this.minJumpHeight = minJumpHeight;
                this.maxJumpHeight = maxJumpHeight;
                this.jumpDistVertical = jumpDistVertical;
                this.vertices = new NativeArray<Vector3>(vertices, Allocator.Persistent);
                this.indices = new NativeArray<int>(indices, Allocator.Persistent);
            }

            public void Execute()
            {
                SetMeshEdges(invertFacingNormal, dontAlignYAxis);
            }

            //vertices = sourceTriangulation.vertices,
            //triangles = sourceTriangulation.indices

            private void SetMeshEdges(bool invertFacingNormal, bool dontAlignYAxis)
            {
                if (edges.Length == 0 && indices.Length > 2)
                {
                    edges.Add(new MeshEdge(vertices[indices[0]], vertices[indices[1]], invertFacingNormal, dontAlignYAxis));
                    edges.Add(new MeshEdge(vertices[indices[1]], vertices[indices[2]], invertFacingNormal, dontAlignYAxis));
                    edges.Add(new MeshEdge(vertices[indices[2]], vertices[indices[0]], invertFacingNormal, dontAlignYAxis));
                }

                MeshEdge edge;
                bool addA = true;
                bool addB = true;
                bool addC = true;

                //CALC FROM MESH OPEN EDGES vertices
                for (int ti = 0; ti < indices.Length; ti += 3)
                {
                    addA = !(IsPositionAtBoundryEdge(vertices[indices[ti]]) || IsPositionAtBoundryEdge(vertices[indices[ti + 1]]));
                    addB = !(IsPositionAtBoundryEdge(vertices[indices[ti + 1]]) || IsPositionAtBoundryEdge(vertices[indices[ti + 2]]));
                    addC = !(IsPositionAtBoundryEdge(vertices[indices[ti + 2]]) || IsPositionAtBoundryEdge(vertices[indices[ti]]));

                    if (!addA && !addB && !addC)
                    {
                        continue;
                    }

                    for (int ei = edges.Length - 1; ei > 0; ei--)
                    {

                        edge = edges[ei];

                        if (addA && IsSameEdge(edge, vertices[indices[ti]], vertices[indices[ti + 1]]))
                        {
                            edges.RemoveAtSwapBack(ei);
                            //_edges.Remove(edge);
                            addA = false;
                        }
                        else if (addB && IsSameEdge(edge, vertices[indices[ti + 1]], vertices[indices[ti + 2]]))
                        {
                            edges.RemoveAtSwapBack(ei);
                            //_edges.Remove(edge);
                            addB = false;
                        }
                        else if (addC && IsSameEdge(edge, vertices[indices[ti + 2]], vertices[indices[ti]]))
                        {
                            edges.RemoveAtSwapBack(ei);
                            //_edges.Remove(edge);
                            addC = false;
                        }
                    }

                    if (addA)
                    {
                        edges.Add(new MeshEdge(vertices[indices[ti]], vertices[indices[ti + 1]], invertFacingNormal, dontAlignYAxis));
                    }
                    if (addB)
                    {
                        edges.Add(new MeshEdge(vertices[indices[ti + 1]], vertices[indices[ti + 2]], invertFacingNormal, dontAlignYAxis));
                    }
                    if (addC)
                    {
                        edges.Add(new MeshEdge(vertices[indices[ti + 2]], vertices[indices[ti]], invertFacingNormal, dontAlignYAxis));
                    }
                }
            }

            public bool IsSameEdge(MeshEdge edge, Vector3 positionA, Vector3 positionB)
            {
                return (edge.StartPoint == positionA && edge.EndPoint == positionB)
                    || (edge.StartPoint == positionB && edge.EndPoint == positionA);
            }

            private bool IsPositionAtBoundryEdge(Vector3 position)
            {
                return (Vector3.Distance(position, new Vector3(position.x, bounds.min.y, position.z)) < minJumpHeight)
                        || (Vector3.Distance(position, new Vector3(position.x, bounds.max.y, position.z)) < minJumpHeight)
                        || (Vector3.Distance(position, new Vector3(bounds.min.x, position.y, position.z)) < jumpDistVertical)
                        || (Vector3.Distance(position, new Vector3(bounds.max.x, position.y, position.z)) < jumpDistVertical)
                        || (Vector3.Distance(position, new Vector3(position.x, position.y, bounds.min.z)) < jumpDistVertical)
                        || (Vector3.Distance(position, new Vector3(position.x, position.y, bounds.max.z)) < jumpDistVertical);
            }
        }

        [ContextMenu("CountListEdges")]
        private void CountListEdges() => Debug.Log($"Edges count: {_edges.Count}");
        #endregion

        private Vector3[] GetLinkStartEnd(Vector3 position, Quaternion normal)
        {
            // Start Position
            _startEnd[0] = position + normal * Vector3.forward * _agentRadius * 2;
            // End Position
            _startEnd[1] = (_startEnd[0] - Vector3.up * maxJumpHeight * 1.1f);
            _startEnd[1] = _startEnd[1] + normal * Vector3.forward * jumpDistVertical;
            return _startEnd;
        }

        public Vector3 LerpByDistance(Vector3 A, Vector3 B, float x)
        {
            Vector3 P = x * Vector3.Normalize(B - A) + A;
            return P;
        }

        private bool IsPositionAtBoundryEdge(Vector3 position)
        {
            return (Vector3.Distance(position, new Vector3(position.x, _surface.navMeshData.sourceBounds.min.y, position.z)) < minJumpHeight)
                    || (Vector3.Distance(position, new Vector3(position.x, _surface.navMeshData.sourceBounds.max.y, position.z)) < minJumpHeight)
                    || (Vector3.Distance(position, new Vector3(_surface.navMeshData.sourceBounds.min.x, position.y, position.z)) < jumpDistVertical)
                    || (Vector3.Distance(position, new Vector3(_surface.navMeshData.sourceBounds.max.x, position.y, position.z)) < jumpDistVertical)
                    || (Vector3.Distance(position, new Vector3(position.x, position.y, _surface.navMeshData.sourceBounds.min.z)) < jumpDistVertical)
                    || (Vector3.Distance(position, new Vector3(position.x, position.y, _surface.navMeshData.sourceBounds.max.z)) < jumpDistVertical);
        }

        private bool TrySpawnVerticalLink(Vector3 position, Quaternion normal)
        {
            // Check if position is not at the lowest / highest position

            var startEnd = GetLinkStartEnd(position, normal);

            NavMeshHit navMeshHit;
            RaycastHit raycastHit;

            var rayStart = startEnd[0] - new Vector3(0, 0.075f, 0);
            if (Physics.Linecast(rayStart, startEnd[1], out raycastHit, _navMeshSurfaceComponent.layerMask,
                    QueryTriggerInteraction.Ignore))
            {
                if (NavMesh.SamplePosition(raycastHit.point, out navMeshHit, 1f, NavMesh.AllAreas))
                {
                    
                    if (Vector3.Distance(position, navMeshHit.position) > minJumpHeight)
                    {

                        Vector3 spawnPosition = (position - normal * Vector3.forward * 0.02f);
                        if ((spawnPosition.y - navMeshHit.position.y) > minJumpHeight)
                        {
                            SpawnLink("VerticalNavMeshLink", spawnPosition, normal, navMeshHit.position, linkBidirectional);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool TrySpawnHorizontalLink(Vector3 position, Quaternion normal)
        {
            var startEnd = GetLinkStartEnd(position, normal);
            var offsetStartPos = LerpByDistance(startEnd[0], startEnd[1], linkStartPointOffset);

            NavMeshHit navMeshHit;
            // RaycastHit raycastHit;
            _obsticleCheckDirection = startEnd[1] - startEnd[0];
            _obsticleCheckOrigin = new Vector3(position.x, (position.y + _obsticleCheckYOffset), position.z);
            // ray cast to check for obsticles
            if (!Physics.Raycast(_obsticleCheckOrigin, _obsticleCheckDirection, (maxJumpDistHorizontal / 2), _navMeshSurfaceComponent.layerMask, QueryTriggerInteraction.Ignore))
            // if (Physics.RaycastNonAlloc(_obsticleCheckOrigin, _obsticleCheckDirection, _raycastHits, (maxJumpDistHorizontal / 2), _navMeshSurfaceComponent.layerMask, QueryTriggerInteraction.Ignore) > 0)
            {
                var _obsticleCheckPositionReverse = (_obsticleCheckOrigin + (_obsticleCheckDirection));
                //now raycast back the other way to make sure we're not raycasting through the inside of a mesh the first time.
                if (!Physics.Raycast(_obsticleCheckPositionReverse, -_obsticleCheckDirection, (maxJumpDistHorizontal + 1), _navMeshSurfaceComponent.layerMask, QueryTriggerInteraction.Ignore))
                // if (Physics.RaycastNonAlloc(_obsticleCheckPositionReverse, -_obsticleCheckDirection, _raycastHits,  (maxJumpDistHorizontal + 1), _navMeshSurfaceComponent.layerMask, QueryTriggerInteraction.Ignore) > 0)
                {
                    //if no walls 1 unit out then check for other colliders using the StartPos offset so as to not detect the edge we are spherecasting from.
                    if (Physics.SphereCast(offsetStartPos, sphereCastRadius, _obsticleCheckDirection, out raycastHit, maxJumpDistHorizontal, _navMeshSurfaceComponent.layerMask, QueryTriggerInteraction.Ignore))
                    // if (Physics.SphereCastNonAlloc(offsetStartPos, sphereCastRadius, _obsticleCheckDirection, _raycastHits, maxJumpDistHorizontal, _navMeshSurfaceComponent.layerMask, QueryTriggerInteraction.Ignore) > 0)
                    {
                        var offsetHitPoint = LerpByDistance(raycastHit.point, startEnd[1], .2f);
                        // var offsetHitPoint = LerpByDistance(_raycastHits[0].point, startEnd[1], .2f);
                        if (NavMesh.SamplePosition(offsetHitPoint, out navMeshHit, 1f, NavMesh.AllAreas))
                        {
                            Vector3 spawnPosition = (position - normal * Vector3.forward * 0.02f);
                            if (Vector3.Distance(position, navMeshHit.position) > 1.1f)
                            {
                                SpawnLink("HorizontalNavMeshLink", spawnPosition, normal, navMeshHit.position, false);
                                return true;
                            }
                        }
                    }
                        
                }
            }
            return false;
        }

        private void SpawnLink(string name, Vector3 position, Quaternion normal, Vector3 endPosition, bool bidirectional)
        {
            var spawnedLink = new GameObject(name, typeof(NavMeshLink));
            spawnedLink.transform.position = position;
            spawnedLink.transform.rotation = normal;

            var linkComponent = spawnedLink.GetComponent<NavMeshLink>();
            linkComponent.startPoint = Vector3.zero;
            linkComponent.endPoint = linkComponent.transform.InverseTransformPoint(endPosition);
            linkComponent.width = linkWidth;
            linkComponent.area = linkArea;
            linkComponent.bidirectional = bidirectional;
            linkComponent.costModifier = linkCostModifier;
            linkComponent.autoUpdate = linkAutoUpdatePosition;
            linkComponent.agentTypeID = _navMeshSurfaceComponent.agentTypeID;

            linkComponent.UpdateLink();
          
            spawnedLink.transform.SetParent(transform);
        }


        private void SpawnLinks()
        {
            if (_edges.Count == 0) return;
            
            
            int linkCount;
            float heightShift;

            // for (int i = 0; i < m_edges.Length; i++)
            for (int i = 0; i < _edges.Count; i++)
            {
                linkCount = (int)Mathf.Clamp(_edges[i].Length / linkWidth, 0, 10000);
                heightShift = 0;
                Vector3 placePos;
                for (int li = 0; li < linkCount; li++) //every edge length segment
                {
                    placePos = Vector3.Lerp(
                                           _edges[i].StartPoint,
                                           _edges[i].EndPoint,
                                           (float)li / (float)linkCount //position on edge
                                           + 0.5f / (float)linkCount //shift for half link width
                                       ) + _edges[i].FacingNormal * Vector3.up * heightShift;


                    switch (direction)
                    {
                        case LinkDirection.Horizontal:
                            TrySpawnHorizontalLink(placePos, _edges[i].FacingNormal);
                            break;
                        case LinkDirection.Vertical:
                            TrySpawnVerticalLink(placePos, _edges[i].FacingNormal);
                            break;
                        case LinkDirection.Both:
                            TrySpawnHorizontalLink(placePos, _edges[i].FacingNormal);
                            TrySpawnVerticalLink(placePos, _edges[i].FacingNormal);
                            break;
                    }
                
                }
            }
        }

        public void Bake()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) _surface = GetComponent<NavMeshSurface>();
#endif

            var settings = NavMesh.GetSettingsByID(NavMeshSurfaceComponent.agentTypeID);
            _agentRadius = settings.agentRadius;

            Clear();
            
            SetNavMeshEdges(NavMesh.CalculateTriangulation(), invertFacingNormal, dontAlignYAxis);
            SpawnLinks();

#if UNITY_EDITOR
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

      
        private void Awake()
        {
            _surface = GetComponent<NavMeshSurface>();
        }
        

    }
    
    public static class MeshEdgeManager
    {
        private const float TRIGGER_ANGLE = 0.999f;

        public static Quaternion GetInverceFacingNormal(Quaternion facingNormal)
        {
            return Quaternion.Euler(Vector3.up * 180) * facingNormal;
        }

        private static Quaternion SubCalculateFacingNormal(Vector3 startPoint, Vector3 endPoint, Vector3 startPointUp, Vector3 endPointUp)
        {
            return Quaternion.LookRotation(
                      Vector3.Cross(endPoint - startPoint,
                                    Vector3.Lerp(endPointUp, startPointUp, 0.5f) -
                                        Vector3.Lerp(endPoint, startPoint, 0.5f)
                                    )
                      );
        }

        public static Quaternion CalculateFacingNormal(Vector3 startPoint, Vector3 endPoint, out Vector3 startPointUp, out Vector3 endPointUp, bool dontAlignYAxis = false)
        {
            startPointUp = Vector3.zero;
            endPointUp = Vector3.zero;

            var result = Quaternion.LookRotation(Vector3.Cross(endPoint - startPoint, Vector3.up));
            if (startPointUp.sqrMagnitude > 0)
            {
                result = SubCalculateFacingNormal(startPoint, endPoint, startPointUp, endPointUp);

                //FIX FOR NORMALs POINTING DIRECT TO UP/DOWN
                if (Mathf.Abs(Vector3.Dot(Vector3.up, (result * Vector3.forward).normalized)) >
                    TRIGGER_ANGLE)
                {
                    startPointUp += new Vector3(0, 0.1f, 0);
                    result = SubCalculateFacingNormal(startPoint, endPoint, startPointUp, endPointUp);
                }
            }

            if (dontAlignYAxis)
            {
                result = Quaternion.LookRotation(
                    result * Vector3.forward,
                    Quaternion.LookRotation(endPoint - startPoint) * Vector3.up
                );
            }

            return result;
        }

        public static bool IsSameEdge(MeshEdge edge, Vector3 positionA, Vector3 positionB)
        {
            return (edge.StartPoint == positionA && edge.EndPoint == positionB)
                || (edge.StartPoint == positionB && edge.EndPoint == positionA);
        }

    }

    public struct MeshEdge
    {
        public Vector3 StartPoint { get; internal set; }
        public Vector3 EndPoint { get; internal set; }

        public Vector3 StartPointUp { get; internal set; }

        public Vector3 EndPointUp { get; internal set; }

        public float Length { get; internal set; }
        public Quaternion FacingNormal { get; internal set; }

        public MeshEdge(Vector3 startPoint, Vector3 endPoint, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        {
            StartPoint = startPoint;
            EndPoint = endPoint;
            Length = Vector3.Distance(StartPoint, EndPoint);

            Vector3 startPointUp;
            Vector3 endPointUp;
            FacingNormal = MeshEdgeManager.CalculateFacingNormal(StartPoint, EndPoint, out startPointUp, out endPointUp, dontAlignYAxis);

            StartPointUp = startPointUp;
            EndPointUp = endPointUp;

            if (invertFacingNormal)
            {
                FacingNormal = MeshEdgeManager.GetInverceFacingNormal(FacingNormal);
            }
        }
    }
 
#if UNITY_EDITOR


    [CustomEditor(typeof(NavMeshSurfaceLinker))]
    [CanEditMultipleObjects]
    public class NavMeshSurfaceLinkerEditor : Editor
    {

    #region SerializedProperties
        SerializedProperty _direction;
        
    #region NavMeshLinks
        SerializedProperty _minJumpHeight;
        SerializedProperty _maxJumpHeight;
        SerializedProperty _jumpDistVertical;
    #endregion NavMeshLinks

    #region NavMeshLinks Horizontal
        SerializedProperty _maxJumpDistHorizontal;
        SerializedProperty _linkStartPointOffset;
        SerializedProperty _obsticleCheckYOffset;
        SerializedProperty _sphereCastRadius;
    #endregion NavMeshLinks Horizontal

    #region NavMeshLink Values
        SerializedProperty _linkWidth;
        SerializedProperty _linkArea;
        SerializedProperty _linkBidirectional;
        SerializedProperty _linkCostModifier;
        SerializedProperty _linkAutoUpdatePosition;
    #endregion NavMeshLink Values

    #region NavMesh Edge Normal
        SerializedProperty _invertFacingNormal;
        SerializedProperty _dontAlignYAxis;
    #endregion NavMesh Edge Normal

    #endregion SerializedProperties

        void OnEnable()
        {
            _direction = serializedObject.FindProperty("_direction");

            // NavMeshLinks
            _minJumpHeight = serializedObject.FindProperty("_minJumpHeight");
            _maxJumpHeight = serializedObject.FindProperty("_maxJumpHeight");
            _jumpDistVertical = serializedObject.FindProperty("_jumpDistVertical");

            // NavMeshLinks Horizontal
            _maxJumpDistHorizontal = serializedObject.FindProperty("_maxJumpDistHorizontal");
            _linkStartPointOffset = serializedObject.FindProperty("_linkStartPointOffset");
            _obsticleCheckYOffset = serializedObject.FindProperty("_obsticleCheckYOffset");
            _sphereCastRadius = serializedObject.FindProperty("_sphereCastRadius");

            // NavMeshLink Values
            _linkWidth = serializedObject.FindProperty("_linkWidth");
            _linkArea = serializedObject.FindProperty("_linkArea");
            _linkBidirectional = serializedObject.FindProperty("_linkBidirectional");
            _linkCostModifier = serializedObject.FindProperty("_linkCostModifier");
            _linkAutoUpdatePosition = serializedObject.FindProperty("_linkAutoUpdatePosition");

            // NavMesh Edge Normal
            _invertFacingNormal = serializedObject.FindProperty("_invertFacingNormal");
            _dontAlignYAxis = serializedObject.FindProperty("_dontAlignYAxis");
        }

        public override void OnInspectorGUI()
        {
            //DrawDefaultInspector();
            serializedObject.Update();

            EditorGUILayout.PropertyField(_direction);

            EditorGUILayout.LabelField("NavMeshLinks", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_minJumpHeight);
            EditorGUILayout.PropertyField(_maxJumpHeight);
            EditorGUILayout.PropertyField(_jumpDistVertical);
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField("NavMeshLinks Horizontal", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_maxJumpDistHorizontal);
            EditorGUILayout.PropertyField(_linkStartPointOffset);
            EditorGUILayout.PropertyField(_obsticleCheckYOffset);
            EditorGUILayout.PropertyField(_sphereCastRadius);
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField("NavMeshLink Values", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;                        
            EditorGUILayout.PropertyField(_linkWidth);
            NavMeshComponentsGUIUtility.AreaPopup("Area Type", _linkArea);
            EditorGUILayout.PropertyField(_linkBidirectional);
            EditorGUILayout.PropertyField(_linkCostModifier);
            EditorGUILayout.PropertyField(_linkAutoUpdatePosition);            
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField("NavMesh Edge Normal", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_invertFacingNormal);
            EditorGUILayout.PropertyField(_dontAlignYAxis);
            EditorGUI.indentLevel--;

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUIUtility.labelWidth);
            if (GUILayout.Button("Clear"))
            {
                foreach (var targ in targets)
                {
                    ((NavMeshSurfaceLinker)targ).Clear();
                }
            }

            if (GUILayout.Button("Bake"))
            {
                foreach (var targ in targets)
                {
                    ((NavMeshSurfaceLinker)targ).Bake();
                }
            }
            GUILayout.EndHorizontal();

        }
    }

#endif
}
