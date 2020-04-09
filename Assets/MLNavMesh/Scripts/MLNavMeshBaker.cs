using RSToolkit.AI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.XR.MagicLeap;

namespace RSToolkit.AI.MLNavMesh
{
    [RequireComponent(typeof(NavMeshSurfaceLinker))]
    [RequireComponent(typeof(NavMeshSurface))]
    public class MLNavMeshBaker : MonoBehaviour
    {
        private NavMeshSurface m_surface;
        private NavMeshSurfaceLinker m_linker;

        [SerializeField, Tooltip("The spatial mapper from which to update mesh params.")]
        private MLSpatialMapper m_mlSpatialMapper = null;
        private Coroutine m_delayedBakeNavMesh = null;
        public int BakeTimer = 2;
        
        public class OnMLNavMeshBakedEvent : UnityEvent<MLNavMeshBaker>{}
        public OnMLNavMeshBakedEvent OnNavMeshBaked { get; private set; } = new OnMLNavMeshBakedEvent();
        public bool IsGoingToBake
        {
            get
            {
                return m_delayedBakeNavMesh == null;
            }
        }

        private void Awake()
        {
            m_surface = GetComponent<NavMeshSurface>();
            m_linker = GetComponent<NavMeshSurfaceLinker>();

            m_mlSpatialMapper.meshAdded += OnMeshAddedListener;
            m_mlSpatialMapper.meshUpdated += OnMeshUpdatedListener;
        }

        private void OnMeshUpdatedListener(UnityEngine.XR.MeshId obj)
        {
            OnMeshAddedOrUpdated();
        }

        private void OnMeshAddedListener(UnityEngine.XR.MeshId obj)
        {
            OnMeshAddedOrUpdated();
        }

        private void OnMeshAddedOrUpdated()
        {
            if (m_delayedBakeNavMesh != null)
            {
                StopCoroutine(m_delayedBakeNavMesh);
                m_delayedBakeNavMesh = null;
            }
            m_delayedBakeNavMesh = StartCoroutine(DelayedBakeNavMesh());
        }

        public void BakeSurface()
        {
            m_surface.BuildNavMesh();
            m_linker.Bake();
        }

        IEnumerator DelayedBakeNavMesh()
        {
            yield return new WaitForSeconds(BakeTimer);
            BakeSurface();
            OnNavMeshBaked.Invoke(this);           
        }

    }
}