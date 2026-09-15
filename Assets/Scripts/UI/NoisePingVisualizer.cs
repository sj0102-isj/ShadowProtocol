using System.Collections.Generic;
using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 소음이 발생하면 바닥에 퍼지는 링을 그린다.
    /// 게임플레이용이라기보다 "소리가 어디까지 닿는지"를 화면에 증명하는 시연용 장치.
    /// GameObject를 만들지 않고 Graphics.DrawMesh로 그린다.
    /// </summary>
    public class NoisePingVisualizer : MonoBehaviour
    {
        [Header("Look")]
        public float lifetime = 0.7f;
        public Color color = new Color(1f, 0.85f, 0.35f, 0.55f);
        public float groundOffset = 0.04f;
        [Range(8, 96)] public int segments = 56;
        [Range(0.5f, 0.99f)] public float innerRatio = 0.9f;

        struct Ping
        {
            public Vector3 position;
            public float radius;
            public float bornAt;
        }

        readonly List<Ping> pings = new List<Ping>();
        Mesh ringMesh;
        Material material;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            BuildRingMesh();

            Shader shader = Shader.Find("Stealth/FOVCone");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            material = new Material(shader);
            mpb = new MaterialPropertyBlock();
        }

        void OnEnable() => NoiseSystem.OnNoise += HandleNoise;
        void OnDisable() => NoiseSystem.OnNoise -= HandleNoise;

        void OnDestroy()
        {
            if (ringMesh != null) Destroy(ringMesh);
            if (material != null) Destroy(material);
        }

        void HandleNoise(Vector3 position, float radius, float strength, Transform source)
        {
            pings.Add(new Ping { position = position, radius = radius, bornAt = Time.time });
        }

        void Update()
        {
            for (int i = pings.Count - 1; i >= 0; i--)
            {
                float age = Time.time - pings[i].bornAt;
                if (age > lifetime)
                {
                    pings.RemoveAt(i);
                    continue;
                }

                float t = age / lifetime;
                float radius = Mathf.Lerp(pings[i].radius * 0.15f, pings[i].radius, EaseOut(t));

                Color c = color;
                c.a *= 1f - t;

                mpb.SetColor(BaseColorId, c);

                Matrix4x4 matrix = Matrix4x4.TRS(
                    pings[i].position + Vector3.up * groundOffset,
                    Quaternion.identity,
                    new Vector3(radius, 1f, radius));

                Graphics.DrawMesh(ringMesh, matrix, material, 0, null, 0, mpb);
            }
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        void BuildRingMesh()
        {
            ringMesh = new Mesh { name = "NoiseRing" };

            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = dir * innerRatio;
                vertices[i * 2 + 1] = dir;

                int next = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = i * 2 + 1;
                triangles[t + 2] = next * 2 + 1;
                triangles[t + 3] = i * 2;
                triangles[t + 4] = next * 2 + 1;
                triangles[t + 5] = next * 2;
            }

            ringMesh.vertices = vertices;
            ringMesh.triangles = triangles;
            ringMesh.RecalculateBounds();
        }
    }
}
