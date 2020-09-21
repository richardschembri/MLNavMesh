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
        private NativeList<MeshEdge> _listEdges = new NativeList<MeshEdge>(Allocator.Persistent);

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
                    DestroyImmediate(navMeshLinks[i].gameObject);
                }
            }
            // _edges.Clear();
            //_linkedEdges.Clear();
        }

        #region MeshEdge
        // public static MeshEdge[] GetNavMeshEdges(NavMeshTriangulation sourceTriangulation, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        public void SetNavMeshEdges(NavMeshTriangulation sourceTriangulation, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        {
            //var m = new Mesh()
            //{
            //    vertices = sourceTriangulation.vertices,
            //    triangles = sourceTriangulation.indices
            //};

            //SetMeshEdges(m, invertFacingNormal, dontAlignYAxis);          // List && Struct
            //SetMeshLinkedEdges(m, invertFacingNormal, dontAlignYAxis);    // LinkedList && Struct | Class.

            // Run as SetMeshEdge as a job.
            var sourceBounds = _surface.navMeshData.sourceBounds;
            //NativeArray<Vector3> vertices = new NativeArray<Vector3>(sourceTriangulation.vertices, Allocator.Persistent);
            //NativeArray<int> indices = new NativeArray<int>(sourceTriangulation.indices, Allocator.Persistent);

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

        /*
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
        */

        

        //public void SetMeshLinkedEdges(Mesh source, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        //{
        //    LinkedListNode<MeshEdge> edgeNode;
        //    if (!_linkedEdges.Any() && source.triangles.Length > 2)
        //    {
        //        edgeNode = _linkedEdges.AddFirst(new MeshEdge(source.vertices[source.triangles[0]], source.vertices[source.triangles[1]], invertFacingNormal, dontAlignYAxis));
        //        edgeNode = _linkedEdges.AddAfter(edgeNode, new MeshEdge(source.vertices[source.triangles[1]], source.vertices[source.triangles[2]], invertFacingNormal, dontAlignYAxis));
        //        _linkedEdges.AddAfter(edgeNode, new MeshEdge(source.vertices[source.triangles[2]], source.vertices[source.triangles[0]], invertFacingNormal, dontAlignYAxis));
        //    }

        //    bool addA = true;
        //    bool addB = true;
        //    bool addC = true;

        //    //CALC FROM MESH OPEN EDGES vertices
        //    for (int ti = 0; ti < source.triangles.Length; ti += 3)
        //    {
        //        addA = !(IsPositionAtBoundryEdge(source.vertices[source.triangles[ti]]) || IsPositionAtBoundryEdge(source.vertices[source.triangles[ti + 1]]));
        //        addB = !(IsPositionAtBoundryEdge(source.vertices[source.triangles[ti + 1]]) || IsPositionAtBoundryEdge(source.vertices[source.triangles[ti + 2]]));
        //        addC = !(IsPositionAtBoundryEdge(source.vertices[source.triangles[ti + 2]]) || IsPositionAtBoundryEdge(source.vertices[source.triangles[ti]]));

        //        if (!addA && !addB && !addC)
        //        {
        //            continue;
        //        }
        //        edgeNode = _linkedEdges.First;
                
        //        while (edgeNode != null)
        //        {                    
        //            if (addA && MeshEdgeManager.IsSameEdge(edgeNode.Value, source.vertices[source.triangles[ti]], source.vertices[source.triangles[ti + 1]]))  //edgeNode.Value.HasPoints(source.vertices[source.triangles[ti]], source.vertices[source.triangles[ti + 1]]))
        //            {
        //                _linkedEdges.Remove(edgeNode);
        //                addA = false;
        //            }
        //            else if (addB && MeshEdgeManager.IsSameEdge(edgeNode.Value, source.vertices[source.triangles[ti + 1]], source.vertices[source.triangles[ti + 2]]))  //edgeNode.Value.HasPoints(source.vertices[source.triangles[ti + 1]], source.vertices[source.triangles[ti + 2]]))
        //            {
        //                _linkedEdges.Remove(edgeNode);
        //                addB = false;
        //            }
        //            else if (addC && MeshEdgeManager.IsSameEdge(edgeNode.Value, source.vertices[source.triangles[ti + 2]], source.vertices[source.triangles[ti]])) //edgeNode.Value.HasPoints(source.vertices[source.triangles[ti + 2]], source.vertices[source.triangles[ti]]))
        //            {
        //                _linkedEdges.Remove(edgeNode);
        //                addC = false;
        //            }
        //            edgeNode = edgeNode.Next;
        //        }

        //        edgeNode = _linkedEdges.Last;


        //        if (addA)
        //        {

        //            _linkedEdges.AddAfter(edgeNode, new MeshEdge(source.vertices[source.triangles[ti]], source.vertices[source.triangles[ti + 1]], invertFacingNormal, dontAlignYAxis));                                      
        //        }
        //        if (addB)
        //        {

        //            _linkedEdges.AddAfter(edgeNode, new MeshEdge(source.vertices[source.triangles[ti + 1]], source.vertices[source.triangles[ti + 2]], invertFacingNormal, dontAlignYAxis));                   
        //        }
        //        if (addC)
        //        {
        //            _linkedEdges.AddAfter(edgeNode, new MeshEdge(source.vertices[source.triangles[ti + 2]], source.vertices[source.triangles[ti]], invertFacingNormal, dontAlignYAxis));
        //        }

        //    }
        //}
       
        //public void SetMeshEdges(Mesh source, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        //{
        //    if (_edges.Count == 0 && source.triangles.Length > 2)
        //    {
        //        _edges.Add(new MeshEdge(source.vertices[source.triangles[0]], source.vertices[source.triangles[1]], invertFacingNormal, dontAlignYAxis));
        //        _edges.Add(new MeshEdge(source.vertices[source.triangles[1]], source.vertices[source.triangles[2]], invertFacingNormal, dontAlignYAxis));
        //        _edges.Add(new MeshEdge(source.vertices[source.triangles[2]], source.vertices[source.triangles[0]], invertFacingNormal, dontAlignYAxis));
        //    }
        //    MeshEdge edge;
        //    bool addA = true;
        //    bool addB = true;
        //    bool addC = true;
            
        //    //CALC FROM MESH OPEN EDGES vertices
        //    for (int ti = 0; ti < source.triangles.Length; ti += 3)
        //    {
        //        addA = !(IsPositionAtBoundryEdge(source.vertices[source.triangles[ti]]) || IsPositionAtBoundryEdge(source.vertices[source.triangles[ti + 1]]));
        //        addB = !(IsPositionAtBoundryEdge(source.vertices[source.triangles[ti + 1]]) || IsPositionAtBoundryEdge(source.vertices[source.triangles[ti + 2]]));
        //        addC = !(IsPositionAtBoundryEdge(source.vertices[source.triangles[ti + 2]]) || IsPositionAtBoundryEdge(source.vertices[source.triangles[ti]]));

        //        if(!addA && !addB && !addC)
        //        {
        //            continue;
        //        }

        //        for (int ei = _edges.Count - 1; ei > 0; ei--)
        //        {

        //            edge = _edges[ei];
                    
        //            if (addA && MeshEdgeManager.IsSameEdge(edge, source.vertices[source.triangles[ti]], source.vertices[source.triangles[ti + 1]]))
        //            {
        //                _edges.Remove(edge);
        //                addA = false;
        //            }
        //            else if (addB && MeshEdgeManager.IsSameEdge(edge, source.vertices[source.triangles[ti + 1]], source.vertices[source.triangles[ti + 2]]))
        //            {
        //                _edges.Remove(edge);
        //                addB = false;
        //            }
        //            else if (addC && MeshEdgeManager.IsSameEdge(edge, source.vertices[source.triangles[ti + 2]], source.vertices[source.triangles[ti]]))
        //            {
        //                _edges.Remove(edge);
        //                addC = false;
        //            }
        //        }

        //        if (addA)
        //        {
        //            _edges.Add(new MeshEdge(source.vertices[source.triangles[ti]], source.vertices[source.triangles[ti + 1]], invertFacingNormal, dontAlignYAxis));
        //        }
        //        if (addB)
        //        {
        //            _edges.Add(new MeshEdge(source.vertices[source.triangles[ti + 1]], source.vertices[source.triangles[ti + 2]], invertFacingNormal, dontAlignYAxis));
        //        }
        //        if (addC)
        //        {
        //            _edges.Add(new MeshEdge(source.vertices[source.triangles[ti + 2]], source.vertices[source.triangles[ti]], invertFacingNormal, dontAlignYAxis));
        //        }
        //    }
        //}
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

        /*
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
        */

        private void SpawnLinks()
        {
            if (!_linkedEdges.Any()) return;


            int linkCount;
            float heightShift;
            LinkedListNode<MeshEdge> edgeNode = _linkedEdges.First;
            Vector3 placePos;
            
            while(edgeNode != null)
            {
                linkCount = (int)Mathf.Clamp(edgeNode.Value.Length / linkWidth, 0, 10000);
                heightShift = 0;
                for (int li = 0; li < linkCount; li++) //every edge length segment
                {
                    placePos = Vector3.Lerp(
                                           edgeNode.Value.StartPoint,
                                           edgeNode.Value.EndPoint,
                                           (float)li / (float)linkCount //position on edge
                                           + 0.5f / (float)linkCount //shift for half link width
                                       ) + edgeNode.Value.FacingNormal * Vector3.up * heightShift;


                    switch (Direction)
                    {
                        case LinkDirection.Horizontal:
                            TrySpawnHorizontalLink(placePos, edgeNode.Value.FacingNormal);
                            break;
                        case LinkDirection.Vertical:
                            TrySpawnVerticalLink(placePos, edgeNode.Value.FacingNormal);
                            break;
                        case LinkDirection.Both:
                            TrySpawnHorizontalLink(placePos, edgeNode.Value.FacingNormal);
                            TrySpawnVerticalLink(placePos, edgeNode.Value.FacingNormal);
                            break;
                    }

                }
                edgeNode = edgeNode.Next;
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

    // normal struct MeshEdge
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
 
        /*
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
                PopulateValues(startPoint, endPoint, invertFacingNormal, dontAlignYAxis);
            }

            public void PopulateValues(Vector3 startPoint, Vector3 endPoint, bool invertFacingNormal = false, bool dontAlignYAxis = false)
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


        public bool HasPoints(Vector3 pointA, Vector3 pointB)
        {
            return (StartPoint == pointA && EndPoint == pointB)
                || (StartPoint == pointB && EndPoint == pointA);
        }



        #endregion

    }
        
       */

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
