using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

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

        [SerializeField]
        bool _Bidirectional = true;
        public bool Bidirectional { get { return _Bidirectional; } set { _Bidirectional = value; } }

        private float _agentRadius;
        //private MeshEdge[] _edges;        

        private List<MeshEdge> _edges = new List<MeshEdge>();
        //private RaycastHit[] _raycastHits = new RaycastHit[1];
        private RaycastHit raycastHit;

        private Vector3[] _startEnd = new Vector3[2];
        //private List<MeshEdge> _edges;

        public LinkDirection Direction = LinkDirection.Vertical;

        [Header("NavMeshLinks")]
        public float minJumpHeight = 0.15f;
        public float maxJumpHeight = 1f;
        public float jumpDistVertical = 0.035f;

        [Header("NavMeshLinks Horizontal")]
        public float maxJumpDistHorizontal = 5f;
        public float linkStartPointOffset = .25f;

        private Vector3 _obsticleCheckDirection;

        public float _obsticleCheckYOffset = 0.5f;
        private Vector3 _obsticleCheckOrigin;

        public float sphereCastRadius = 1f;

        [Header("NavMeshLink Values")]
        public float linkWidth = 0.25f;
        public int linkArea;
        public bool linkBidirectional;
        public int linkCostModifier = -1;
        public bool linkAutoUpdatePosition = true;

        [Header("NavMesh Edge Normal")]
        public bool invertFacingNormal = false;
        public bool dontAlignYAxis = false;

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
                    DestroyImmediate(navMeshLinks[i]);
                }
            }
            _edges.Clear();
        }

        #region MeshEdge
        // public static MeshEdge[] GetNavMeshEdges(NavMeshTriangulation sourceTriangulation, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        public void SetNavMeshEdges(NavMeshTriangulation sourceTriangulation, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        {
            var m = new Mesh()
            {
                vertices = sourceTriangulation.vertices,
                triangles = sourceTriangulation.indices
            };

            SetMeshEdges(m, invertFacingNormal, dontAlignYAxis);
        }

        // To optimize
        private bool TryAddUniqueMeshEdge(ref List<MeshEdge> source, Vector3 startPoint, Vector3 endPoint, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        {
            var edge = source.FirstOrDefault(s => (s.StartPoint == startPoint && s.EndPoint == endPoint)
                                || (s.StartPoint == endPoint && s.EndPoint == startPoint));
            if (edge == null)
            {
                source.Add(new MeshEdge(startPoint, endPoint, invertFacingNormal, dontAlignYAxis));
                return true;
            }
            else
            {
                // Not an edge, remove
                source.Remove(edge);
                return false;
            }

        }

        private bool TryAddUniqueMeshEdge(int edgeIndex, Vector3 positionA, Vector3 positionB, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        {
            if (IsSameEdge(_edges[edgeIndex], positionA, positionB))
            {
                var edge = _edges[edgeIndex];
                // Not an edge, remove
                _edges.Remove(edge);
                edge = null;
                return true;
            }
            else
            {

                return false;
            }

        }

        private bool IsSameEdge(MeshEdge edge, Vector3 positionA, Vector3 positionB)
        {
            return (edge.StartPoint == positionA && edge.EndPoint == positionB)
                || (edge.StartPoint == positionB && edge.EndPoint == positionA);
        }

        // public static MeshEdge[] GetMeshEdges(Mesh source, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        public void SetMeshEdges(Mesh source, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        {
            if (_edges.Count == 0 && source.triangles.Length > 2)
            {
                _edges.Add(new MeshEdge(source.vertices[source.triangles[0]], source.vertices[source.triangles[1]], invertFacingNormal, dontAlignYAxis));
                _edges.Add(new MeshEdge(source.vertices[source.triangles[1]], source.vertices[source.triangles[2]], invertFacingNormal, dontAlignYAxis));
                _edges.Add(new MeshEdge(source.vertices[source.triangles[2]], source.vertices[source.triangles[0]], invertFacingNormal, dontAlignYAxis));
            }
            MeshEdge edge;
            bool addA = true;
            bool addB = true;
            bool addC = true;

            //CALC FROM MESH OPEN EDGES vertices
            for (int ti = 0; ti < source.triangles.Length; ti += 3)
            {
                addA = !(IsPositionAtBoundryEdge(source.vertices[source.triangles[ti]]) || IsPositionAtBoundryEdge(source.vertices[source.triangles[ti + 1]]));
                addB = !(IsPositionAtBoundryEdge(source.vertices[source.triangles[ti + 1]]) || IsPositionAtBoundryEdge(source.vertices[source.triangles[ti + 2]]));
                addC = !(IsPositionAtBoundryEdge(source.vertices[source.triangles[ti + 2]]) || IsPositionAtBoundryEdge(source.vertices[source.triangles[ti]]));

                if(!addA && !addB && !addC)
                {
                    continue;
                }

                for (int ei = _edges.Count - 1; ei > 0; ei--)
                {

                    edge = _edges[ei];
                    
                    if (addA && IsSameEdge(edge, source.vertices[source.triangles[ti]], source.vertices[source.triangles[ti + 1]]))
                    {
                        _edges.Remove(edge);
                        edge = null;
                        addA = false;
                    }
                    else if (addB && IsSameEdge(edge, source.vertices[source.triangles[ti + 1]], source.vertices[source.triangles[ti + 2]]))
                    {
                        _edges.Remove(edge);
                        edge = null;
                        addB = false;
                    }
                    else if (addC && IsSameEdge(edge, source.vertices[source.triangles[ti + 2]], source.vertices[source.triangles[ti]]))
                    {
                        _edges.Remove(edge);
                        edge = null;
                        addC = false;
                    }
                }

                if (addA)
                {
                    _edges.Add(new MeshEdge(source.vertices[source.triangles[ti]], source.vertices[source.triangles[ti + 1]], invertFacingNormal, dontAlignYAxis));
                }
                if (addB)
                {
                    _edges.Add(new MeshEdge(source.vertices[source.triangles[ti + 1]], source.vertices[source.triangles[ti + 2]], invertFacingNormal, dontAlignYAxis));
                }
                if (addC)
                {
                    _edges.Add(new MeshEdge(source.vertices[source.triangles[ti + 2]], source.vertices[source.triangles[ti]], invertFacingNormal, dontAlignYAxis));
                }
            }
            
        }
        #endregion MeshEdge

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


                    switch (Direction)
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

    public class MeshEdge
    {

        public Vector3 StartPoint { get; private set; }
        public Vector3 EndPoint { get; private set; }

        public Vector3 StartPointUp { get; private set; }
    
        public Vector3 EndPointUp { get; private set; }

        public float Length { get; private set; }
        public Quaternion FacingNormal { get; private set; }
        private const float TRIGGER_ANGLE = 0.999f;

        public MeshEdge(Vector3 startPoint, Vector3 endPoint, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        {
            StartPoint = startPoint;
            EndPoint = endPoint;
            Length = Vector3.Distance(StartPoint, EndPoint);

            CalculateFacingNormal(dontAlignYAxis);

            if (invertFacingNormal)
            {
                InvertFacingNormal();
            }
        }

        public void InvertFacingNormal()
        {
            FacingNormal = Quaternion.Euler(Vector3.up * 180) * FacingNormal;
        }

        private Quaternion SubCalculateFacingNormal()
        {
            return Quaternion.LookRotation(
                      Vector3.Cross(EndPoint - StartPoint,
                                    Vector3.Lerp(EndPointUp, StartPointUp, 0.5f) -
                                        Vector3.Lerp(EndPoint, StartPoint, 0.5f)
                                    )
                      );
        }

        private void CalculateFacingNormal(bool dontAlignYAxis = false)
        {
            FacingNormal = Quaternion.LookRotation(Vector3.Cross(EndPoint - StartPoint, Vector3.up));
            if (StartPointUp.sqrMagnitude > 0)
            {
                FacingNormal = SubCalculateFacingNormal();


                //FIX FOR NORMALs POINTING DIRECT TO UP/DOWN
                if (Mathf.Abs(Vector3.Dot(Vector3.up, (FacingNormal * Vector3.forward).normalized)) >
                    TRIGGER_ANGLE)
                {
                    StartPointUp += new Vector3(0, 0.1f, 0);
                    FacingNormal = SubCalculateFacingNormal();
                }
            }

            if (dontAlignYAxis)
            {
                FacingNormal = Quaternion.LookRotation(
                    FacingNormal * Vector3.forward,
                    Quaternion.LookRotation(EndPoint - StartPoint) * Vector3.up
                );
            }

        }

        #region Static Functions

        

        
        #endregion

    }



#if UNITY_EDITOR


    [CustomEditor(typeof(NavMeshSurfaceLinker))]
    [CanEditMultipleObjects]
    public class NavMeshSurfaceLinkerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

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
