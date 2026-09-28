using System.Collections.Generic;
using UnityEngine;

public class OutlineObject : MonoBehaviour
{
    public Material outlineMaterial;
    private Renderer[] objectRenderers;
    private bool isHighlighted = false;
    private bool isInitialized = false;

    private void Initialize()
    {
        if (isInitialized) return;
        isInitialized = true;

        objectRenderers = GetComponentsInChildren<Renderer>();

        // Automatically bake smoothed normals to prevent edge gap artifacts
        foreach (Renderer rend in objectRenderers)
        {
            MeshFilter filter = rend.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                BakeSmoothNormals(filter);
            }
        }
    }

    private void BakeSmoothNormals(MeshFilter filter)
    {
        Mesh mesh = Instantiate(filter.sharedMesh);
        Vector3[] normals = mesh.normals;
        Vector3[] vertices = mesh.vertices;

        Dictionary<Vector3, Vector3> normalDictionary = new Dictionary<Vector3, Vector3>();

        for (int i = 0; i < vertices.Length; i++)
        {
            if (!normalDictionary.ContainsKey(vertices[i]))
            {
                normalDictionary[vertices[i]] = Vector3.zero;
            }
            normalDictionary[vertices[i]] += normals[i];
        }

        Vector3[] smoothNormals = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            smoothNormals[i] = normalDictionary[vertices[i]].normalized;
        }

        mesh.SetUVs(1, smoothNormals); // Store smoothed normals in UV2
        filter.mesh = mesh;
    }

    public void EnableHighlight(bool enable)
    {
        Initialize();

        if (isHighlighted == enable || outlineMaterial == null) return;
        isHighlighted = enable;

        foreach (Renderer rend in objectRenderers)
        {
            Material[] mats = rend.sharedMaterials;

            if (enable)
            {
                Material[] newMats = new Material[mats.Length + 1];
                mats.CopyTo(newMats, 0);
                newMats[newMats.Length - 1] = outlineMaterial;
                rend.materials = newMats;
            }
            else
            {
                if (mats.Length > 1)
                {
                    Material[] newMats = new Material[mats.Length - 1];
                    for (int i = 0; i < newMats.Length; i++)
                    {
                        newMats[i] = mats[i];
                    }
                    rend.materials = newMats;
                }
            }
        }
    }
}