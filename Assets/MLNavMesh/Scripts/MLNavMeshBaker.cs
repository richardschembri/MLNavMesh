using RSToolkit.AI;
using System;
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

        public enum BakeConditions
        {
            MESH_ADDED,
            MESH_UPDATED,
            BOTH
        }

        public enum BakeTypes
        {
            NONE,
            SURFACE,
            LINK,
            BOTH
        }

        public bool HasBaked { get; private set; } = false;
        private NavMeshSurface _surface;
        private NavMeshSurfaceLinker _linker;

        public BakeConditions BakeCondition = BakeConditions.BOTH;

        [SerializeField, Tooltip("The spatial mapper from which to update mesh params.")]
        private MLSpatialMapper _mlSpatialMapper = null;
        private Coroutine _delayedBakeNavMesh = null;

        [SerializeField]       
        private BakeTypes _autoBake = BakeTypes.BOTH;
        public BakeTypes AutoBake { get { return _autoBake; } set{ _autoBake = value; } }

        public float BakeTimer = 2;

        // public float FirstBakeTimer = 5;
        
        public class OnMLNavMeshBakedEvent : UnityEvent<MLNavMeshBaker, BakeTypes>{}
        public OnMLNavMeshBakedEvent OnNavMeshBaked { get; private set; } = new OnMLNavMeshBakedEvent();
        public bool IsGoingToBake
        {
            get
            {
                return _delayedBakeNavMesh == null;
            }
        }

        private void Awake()
        {
            _surface = GetComponent<NavMeshSurface>();
            _linker = GetComponent<NavMeshSurfaceLinker>();

            switch (BakeCondition)
            {
                case BakeConditions.MESH_ADDED:
                    _mlSpatialMapper.meshAdded += OnMeshAddedListener;
                    break;
                case BakeConditions.MESH_UPDATED:
                    _mlSpatialMapper.meshUpdated += OnMeshUpdatedListener;
                    break;
                case BakeConditions.BOTH:
                    _mlSpatialMapper.meshAdded += OnMeshAddedListener;
                    _mlSpatialMapper.meshUpdated += OnMeshUpdatedListener;
                    break;
            }
            
            
            // StartCoroutine(DelayedFirstBakeNavMesh());
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
            if (_delayedBakeNavMesh != null)
            {
                StopCoroutine(_delayedBakeNavMesh);
                _delayedBakeNavMesh = null;
            }
            _delayedBakeNavMesh = StartCoroutine(DelayedBakeNavMesh());
        }

        public bool Bake(BakeTypes bakeType)
        {
            HasBaked = false;
            switch (bakeType)
            {
                case BakeTypes.BOTH:
                    _surface.BuildNavMesh();
                    _linker.Bake();
                    HasBaked = true;
                    break;
                case BakeTypes.SURFACE:
                    _surface.BuildNavMesh();
                    HasBaked = true;
                    break;
                case BakeTypes.LINK:
                    _linker.Bake();
                    HasBaked = true;
                    break;
            }

            if (HasBaked)
            {
                OnNavMeshBaked.Invoke(this, bakeType);
            }

            return HasBaked;
        }

        IEnumerator DelayedBakeNavMesh()
        {
            yield return new WaitForSeconds(BakeTimer);
            Bake(AutoBake);                    
        }

        /*
        IEnumerator DelayedFirstBakeNavMesh()
        {
            yield return new WaitForSeconds(FirstBakeTimer);
            if (!HasBaked)
            {
                BakeSurface();
            }
        }
        */

    }
}