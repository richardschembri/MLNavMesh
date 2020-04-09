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

        [SerializeField]
        bool m_Bidirectional = true;
        public bool Bidirectional { get { return m_Bidirectional; } set { m_Bidirectional = value; } }

        private float m_agentRadius;
        //private MeshEdge[] m_edges;
        private List<MeshEdge> m_edges;

        public LinkDirection Direction = LinkDirection.Vertical;

        

        [Header("NavMeshLinks")]
        public float minJumpHeight = 0.15f;
        public float maxJumpHeight = 1f;
        public float jumpDistVertical = 0.035f;

        [Header("NavMeshLinks Horizontal")]
        public float maxJumpDistHorizontal = 5f;
        public float linkStartPointOffset = .25f;

        private Vector3 m_obsticleCheckDirection;

        public float m_obsticleCheckYOffset = 0.5f;
        private Vector3 m_obsticleCheckPosition;

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

        private NavMeshSurface m_navMeshSurfaceComponent;
        public NavMeshSurface NavMeshSurfaceComponent
        {
            get
            {
                if (m_navMeshSurfaceComponent == null)
                {
                    m_navMeshSurfaceComponent = GetComponent<NavMeshSurface>();
                }
                return m_navMeshSurfaceComponent;
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
        }



        private Vector3[] GetLinkStartEnd(Vector3 position, Quaternion normal)
        {
            var result = new Vector3[2];
            // Start Position
            result[0] = position + normal * Vector3.forward * m_agentRadius * 2;
            // End Position
            result[1] = (result[0] - Vector3.up * maxJumpHeight * 1.1f);
            result[1] = result[1] + normal * Vector3.forward * jumpDistVertical;
            return result;
        }

        public Vector3 LerpByDistance(Vector3 A, Vector3 B, float x)
        {
            Vector3 P = x * Vector3.Normalize(B - A) + A;
            return P;
        }

        private bool TrySpawnVerticalLink(Vector3 position, Quaternion normal)
        {
            var startEnd = GetLinkStartEnd(position, normal);

            NavMeshHit navMeshHit;
            RaycastHit raycastHit;

            var rayStart = startEnd[0] - new Vector3(0, 0.075f, 0);
            if (Physics.Linecast(rayStart, startEnd[1], out raycastHit, m_navMeshSurfaceComponent.layerMask,
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
            RaycastHit raycastHit;
            m_obsticleCheckDirection = startEnd[1] - startEnd[0];
            m_obsticleCheckPosition = new Vector3(position.x, (position.y + m_obsticleCheckYOffset), position.z);
            // ray cast to check for obsticles
            if (!Physics.Raycast(m_obsticleCheckPosition, m_obsticleCheckDirection, (maxJumpDistHorizontal / 2), m_navMeshSurfaceComponent.layerMask))
            {
                var m_obsticleCheckPositionReverse = (m_obsticleCheckPosition + (m_obsticleCheckDirection));
                //now raycast back the other way to make sure we're not raycasting through the inside of a mesh the first time.
                if (!Physics.Raycast(m_obsticleCheckPositionReverse, -m_obsticleCheckDirection, (maxJumpDistHorizontal + 1), m_navMeshSurfaceComponent.layerMask))
                {
                    //if no walls 1 unit out then check for other colliders using the StartPos offset so as to not detect the edge we are spherecasting from.
                    if (Physics.SphereCast(offsetStartPos, sphereCastRadius, m_obsticleCheckDirection, out raycastHit, maxJumpDistHorizontal, m_navMeshSurfaceComponent.layerMask, QueryTriggerInteraction.Ignore))
                    {
                        var offsetHitPoint = LerpByDistance(raycastHit.point, startEnd[1], .2f);
                        if (NavMesh.SamplePosition(offsetHitPoint, out navMeshHit, 1f, NavMesh.AllAreas))
                        {
                            Vector3 spawnPosition = (position - normal * Vector3.forward * 0.02f);
                            if (Vector3.Distance(position, navMeshHit.position) > 1.1f)
                            {
                                SpawnLink("HorizontalNavMeshLink", spawnPosition, normal, navMeshHit.position, false);
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
            linkComponent.agentTypeID = m_navMeshSurfaceComponent.agentTypeID;

            linkComponent.UpdateLink();
          

            spawnedLink.transform.SetParent(transform);
        }

        private void SpawnLinks()
        {
            // if (m_edges.Length == 0) return;
            if (m_edges.Count == 0) return;
            Clear();
            int linkCount;
            float heightShift;

            // for (int i = 0; i < m_edges.Length; i++)
            for (int i = 0; i < m_edges.Count; i++)
            {
                linkCount = (int)Mathf.Clamp(m_edges[i].Length / linkWidth, 0, 10000);
                heightShift = 0;
                for (int li = 0; li < linkCount; li++) //every edge length segment
                {
                    Vector3 placePos = Vector3.Lerp(
                                           m_edges[i].StartPoint,
                                           m_edges[i].EndPoint,
                                           (float)li / (float)linkCount //position on edge
                                           + 0.5f / (float)linkCount //shift for half link width
                                       ) + m_edges[i].FacingNormal * Vector3.up * heightShift;

                    switch (Direction)
                    {
                        case LinkDirection.Horizontal:
                            TrySpawnHorizontalLink(placePos, m_edges[i].FacingNormal);
                            break;
                        case LinkDirection.Vertical:
                            TrySpawnVerticalLink(placePos, m_edges[i].FacingNormal);
                            break;
                        case LinkDirection.Both:
                            TrySpawnHorizontalLink(placePos, m_edges[i].FacingNormal);
                            TrySpawnVerticalLink(placePos, m_edges[i].FacingNormal);
                            break;
                    }
                    
                }
            }
        }


        public void Bake()
        {
            var settings = NavMesh.GetSettingsByID(NavMeshSurfaceComponent.agentTypeID);
            m_agentRadius = settings.agentRadius;
            m_edges?.Clear();
            m_edges = MeshEdge.GetNavMeshEdges(NavMesh.CalculateTriangulation(), invertFacingNormal, dontAlignYAxis);
            SpawnLinks();
#if UNITY_EDITOR
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

        /*
        private void OnDrawGizmos()
        {
            for (int i = 0; i < m_edges.Count; i++)
            {
                Debug.DrawLine(m_edges[i].StartPoint + new Vector3(0, 0.1f, 0), m_edges[i].EndPoint + new Vector3(0, 0.1f, 0), Color.yellow);
                
            }
        }
        */
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

        // public static MeshEdge[] GetNavMeshEdges(NavMeshTriangulation sourceTriangulation, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        public static List<MeshEdge> GetNavMeshEdges(NavMeshTriangulation sourceTriangulation, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        {
            var m = new Mesh()
            {
                vertices = sourceTriangulation.vertices,
                triangles = sourceTriangulation.indices
            };
            return GetMeshEdges(m, invertFacingNormal, dontAlignYAxis);
        }

        // To optimize
        private static bool TryAddUniqueMeshEdge(ref List<MeshEdge> source, Vector3 startPoint, Vector3 endPoint, bool invertFacingNormal = false, bool dontAlignYAxis = false)
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


        // public static MeshEdge[] GetMeshEdges(Mesh source, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        public static List<MeshEdge> GetMeshEdges(Mesh source, bool invertFacingNormal = false, bool dontAlignYAxis = false)
        {
            // var result = new MeshEdge[source.triangles.Length];
            var result = new List<MeshEdge>();
            for (int i = 0; i < source.triangles.Length - 1; i += 3)
            {
                //CALC FROM MESH OPEN EDGES vertices
                TryAddUniqueMeshEdge(ref result,
                        source.vertices[source.triangles[i]],
                        source.vertices[source.triangles[i + 1]],
                        invertFacingNormal, dontAlignYAxis
                    );
                /*
                result[i] = new MeshEdge(
                        source.vertices[source.triangles[i]],
                        source.vertices[source.triangles[i + 1]],
                        invertFacingNormal, dontAlignYAxis
                    );
                    */
                TryAddUniqueMeshEdge(ref result,
                        source.vertices[source.triangles[i + 1]],
                        source.vertices[source.triangles[i + 2]],
                        invertFacingNormal, dontAlignYAxis
                    );
                /*
                result[i + 1] = new MeshEdge(
                        source.vertices[source.triangles[i + 1]],
                        source.vertices[source.triangles[i + 2]],
                        invertFacingNormal, dontAlignYAxis
                    );
                    */
                TryAddUniqueMeshEdge(ref result,
                        source.vertices[source.triangles[i + 2]],
                        source.vertices[source.triangles[i]],
                        invertFacingNormal, dontAlignYAxis
                    );
                /*
                result[i + 2] = new MeshEdge(
                        source.vertices[source.triangles[i + 2]],
                        source.vertices[source.triangles[i]],
                        invertFacingNormal, dontAlignYAxis
                    );
                    */
            }
            return result;
        }

        
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
