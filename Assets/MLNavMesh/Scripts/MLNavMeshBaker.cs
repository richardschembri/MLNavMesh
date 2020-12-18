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

        #region Components
        private NavMeshSurface _surface;
        private NavMeshSurfaceLinker _linker;
        [SerializeField]       
        private NavMeshObstacle _roofObstaclePrefab;
        public NavMeshObstacle RoofObstacle { get; private set; } = null;
        [SerializeField]       
        private float _roofObsticleHeight = 0.15f;
        #endregion Components

        private bool AdjustRoofObstacle()
        {
            var sourceBounds = _surface.navMeshData.sourceBounds;
            if (RoofObstacle == null || sourceBounds.max.y < .5f)
            {
                return false;
            }
            
            RoofObstacle.transform.position = new Vector3(sourceBounds.center.x, sourceBounds.max.y, sourceBounds.center.z);
            RoofObstacle.size = new Vector3(sourceBounds.size.x, _roofObsticleHeight, sourceBounds.size.z);
            return true;
        }

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

            SpawnRoofObstacle();
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
                AdjustRoofObstacle();
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

        private void SpawnRoofObstacle()
        {
            if(_roofObstaclePrefab != null)
            {
                RoofObstacle = Instantiate(_roofObstaclePrefab, new Vector3(0f, 100f, 0f), Quaternion.Euler(0f, 0f, 0f), _surface.transform.parent);
                RoofObstacle.transform.localScale = new Vector3(1f, 1f, 1f);
            }
        }

    }
}