using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.XR.MagicLeap;

namespace RSToolkit.MLNavMesh
{
    public class MLNavMeshBaker : MonoBehaviour
    {
        private NavMeshSurface m_surface;
        [SerializeField, Tooltip("The spatial mapper from which to update mesh params.")]
        private MLSpatialMapper m_mlSpatialMapper = null;
        private Coroutine m_delayedBakeNavMesh = null;
        public int BakeTimeout = 2;

        private void Awake()
        {
            m_surface = GetComponent<NavMeshSurface>();
            m_mlSpatialMapper.meshAdded += OnMeshAddedListener;
        }

        private void OnMeshAddedListener(UnityEngine.XR.MeshId obj)
        {
            if (m_delayedBakeNavMesh != null)
            {
                StopCoroutine(m_delayedBakeNavMesh);
            }
            m_delayedBakeNavMesh = StartCoroutine(DelayedBakeNavMesh());
        }

        public void BakeSurface()
        {
            m_surface.BuildNavMesh();
        }

        IEnumerator DelayedBakeNavMesh()
        {
            yield return new WaitForSeconds(BakeTimeout);
            m_surface.BuildNavMesh();
        }

    }
}