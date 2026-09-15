using System.Collections.Generic;
using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 소음이 발생하면 바닥에 퍼지는 링을 그린다.
    /// 게임플레이용이라기보다 "소리가 어디까지 닿는지"를 화면에 증명하는 시연용 장치.
    /// GameObject를 만들지 않고 Graphics.DrawMesh로 그린다.
    /// </summary>
    //
    // Graphics.DrawMesh 는 "이번 프레임에 이 메시를 여기 한 번 그려달라"고 직접 렌더러에 부탁한다.
    // GameObject·MeshRenderer 를 만들 필요가 없어서, 소리가 날 때마다 오브젝트를 생성·파괴하는
    // 비용이 통째로 사라진다. 잠깐 나타났다 사라지는 이펙트에 잘 맞는 방식이다.
    public class NoisePingVisualizer : MonoBehaviour
    {
        [Header("Look")]
        public float lifetime = 0.7f;                          // 링이 사라지기까지의 시간
        public Color color = new Color(1f, 0.85f, 0.35f, 0.55f);
        public float groundOffset = 0.04f;                     // 바닥과 겹치지 않게 띄우는 높이
        [Range(8, 96)] public int segments = 56;               // 원을 몇 조각으로 나눌지
        [Range(0.5f, 0.99f)] public float innerRatio = 0.9f;   // 안쪽 구멍의 비율. 0.9면 얇은 테두리

        /// <summary>진행 중인 링 하나의 정보.</summary>
        // 중첩 struct — 이 클래스 안에서만 쓰는 작은 자료 묶음이라 밖에 노출할 이유가 없다.
        struct Ping
        {
            public Vector3 position;
            public float radius;
            public float bornAt;   // 생긴 시각. Time.time 과 비교해 나이를 구한다
        }

        readonly List<Ping> pings = new List<Ping>();   // 지금 화면에 떠 있는 링들
        Mesh ringMesh;                                  // 반지름 1짜리 링 하나. 크기는 행렬로 조절한다
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

        // 소음 이벤트를 구독한다. 경비(GuardAI)와 같은 이벤트를 듣지만 하는 일이 다르다.
        void OnEnable() => NoiseSystem.OnNoise += HandleNoise;
        void OnDisable() => NoiseSystem.OnNoise -= HandleNoise;

        void OnDestroy()
        {
            if (ringMesh != null) Destroy(ringMesh);
            if (material != null) Destroy(material);
        }

        /// <summary>소리가 나면 목록에 링을 하나 추가한다.</summary>
        void HandleNoise(Vector3 position, float radius, float strength, Transform source)
        {
            pings.Add(new Ping { position = position, radius = radius, bornAt = Time.time });
        }

        void Update()
        {
            // 뒤에서부터 도는 이유: 반복 중에 RemoveAt 을 하면 뒤쪽 원소의 번호가 하나씩 당겨진다.
            // 앞에서부터 돌면 지운 다음 원소를 건너뛰게 된다. 뒤에서부터 돌면 그 문제가 없다.
            for (int i = pings.Count - 1; i >= 0; i--)
            {
                float age = Time.time - pings[i].bornAt;
                if (age > lifetime)
                {
                    pings.RemoveAt(i);
                    continue;
                }

                float t = age / lifetime;   // 0(막 생김) ~ 1(사라지기 직전)

                // 작게 시작해서 최종 반경까지 커진다. EaseOut 때문에 처음엔 빠르고 끝에는 느리다.
                float radius = Mathf.Lerp(pings[i].radius * 0.15f, pings[i].radius, EaseOut(t));

                Color c = color;
                c.a *= 1f - t;   // 시간이 갈수록 투명해진다

                mpb.SetColor(BaseColorId, c);

                // Matrix4x4.TRS = 위치(Translate)·회전(Rotate)·크기(Scale)를 하나로 합친 변환 행렬.
                // 메시는 반지름 1로 만들어 두고, 여기서 radius 를 곱해 원하는 크기로 그린다.
                // y 크기를 1로 두는 이유는 평면이라 두께가 없기 때문이다.
                Matrix4x4 matrix = Matrix4x4.TRS(
                    pings[i].position + Vector3.up * groundOffset,
                    Quaternion.identity,
                    new Vector3(radius, 1f, radius));

                Graphics.DrawMesh(ringMesh, matrix, material, 0, null, 0, mpb);
            }
        }

        /// <summary>처음엔 빠르고 끝에서 느려지는 곡선. 퍼지는 파동처럼 보이게 한다.</summary>
        // t=0 → 0, t=0.5 → 0.75, t=1 → 1. 직선(t 그대로)보다 앞쪽이 훨씬 가파르다.
        static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        /// <summary>반지름 1짜리 링 메시를 만든다. 안쪽 원과 바깥쪽 원 사이를 삼각형으로 채운다.</summary>
        void BuildRingMesh()
        {
            ringMesh = new Mesh { name = "NoiseRing" };

            // 조각마다 꼭짓점 2개(안쪽·바깥쪽)
            var vertices = new Vector3[segments * 2];
            // 조각마다 사각형 하나 = 삼각형 2개 = 꼭짓점 번호 6개
            var triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                // 전체 원(2π)을 segments 등분한 각도
                float angle = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                vertices[i * 2] = dir * innerRatio;   // 안쪽 점
                vertices[i * 2 + 1] = dir;            // 바깥쪽 점

                // % 로 마지막 조각이 첫 조각과 이어져 원이 닫힌다.
                int next = (i + 1) % segments;
                int t = i * 6;

                // 사각형 하나를 삼각형 둘로 나눈다.
                //   안i — 밖i — 밖next
                //   안i — 밖next — 안next
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
