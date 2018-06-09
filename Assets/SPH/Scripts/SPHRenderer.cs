using UnityEngine;

[RequireComponent(typeof(SPHSimulationBehaviour))]
public class SPHRenderer : MonoBehaviour
{
    [SerializeField]
    Material _material;

    SPHSimulationBehaviour _simulation;

    int _idPositionBuffer;
    int _idZIndexBuffer;

    private void Awake()
    {
        _simulation = GetComponent<SPHSimulationBehaviour>();

        _idPositionBuffer = Shader.PropertyToID("_positionBuffer");
        _idZIndexBuffer = Shader.PropertyToID("_zIndexBuffer");
    }

    private void OnRenderObject()
    {
        _material.SetBuffer(_idPositionBuffer, _simulation.positionBuffer);
        _material.SetBuffer(_idZIndexBuffer, _simulation.zIndexBuffer);
        _material.SetPass(0);
        Graphics.DrawProcedural(MeshTopology.Points, 1, _simulation.numParticles);
    }
}
