using System;          // Action<...> 을 쓰기 위해
using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 소음 이벤트를 전역으로 방송한다. 경비는 여기에 구독해서 반응한다.
    /// 발소리, 동전 던지기, 문 열기 등 모든 소리가 이 경로를 탄다.
    /// </summary>
    //
    // static class = 인스턴스를 만들 수 없는 클래스. 씬에 오브젝트로 존재하지 않고
    // NoiseSystem.Emit(...) 처럼 이름으로 바로 부른다. MonoBehaviour 가 아니다.
    //
    // ── 왜 이벤트로 만들었는가 ──
    // 소리를 내는 쪽(동전, 발소리)이 "경비 10명에게 각각 알려주기"를 하면,
    // 경비가 늘어나거나 드론이 추가될 때마다 소리 내는 쪽 코드를 고쳐야 한다.
    // 이벤트를 쓰면 소리 내는 쪽은 "소리가 났다"고 방송만 하고, 듣고 싶은 쪽이 알아서 구독한다.
    // 서로를 몰라도 되는 이 구조를 느슨한 결합(loose coupling)이라고 한다.
    public static class NoiseSystem
    {
        /// <summary>(발생 위치, 반경, 세기 0~1, 소리를 낸 물체 — 없으면 null)</summary>
        //
        // event = 구독자 목록을 담는 변수. Action<A,B,C,D> 는 "인자 4개를 받고 아무것도 안 돌려주는 함수"를 뜻한다.
        // 경비가 GuardAI.OnEnable 에서 NoiseSystem.OnNoise += HandleNoise; 로 등록한다.
        public static event Action<Vector3, float, float, Transform> OnNoise;

        /// <summary>
        /// source 를 넘기면 경비가 "가서 확인할 대상"으로 삼는다.
        /// 던진 동전처럼 집어들 수 있는 물건이 여기 해당한다.
        /// </summary>
        // = null, = 1f 처럼 기본값을 주면 호출할 때 생략할 수 있다.
        //   Emit(pos, 10f)                 → strength 1, source null
        //   Emit(pos, 10f, 0.7f, transform) → 전부 지정
        public static void Emit(Vector3 position, float radius, float strength = 1f, Transform source = null)
        {
            if (radius <= 0f) return;

            // ?. 는 null 조건 연산자. 구독자가 한 명도 없으면 OnNoise 가 null 이라
            // 그냥 Invoke 하면 예외가 난다. ?. 를 쓰면 null 일 때 아무것도 하지 않는다.
            OnNoise?.Invoke(position, radius, Mathf.Clamp01(strength), source);
        }

        /// <summary>씬 전환 시 구독자가 남지 않도록 정리.</summary>
        // static 변수는 씬을 바꿔도 값이 유지된다. 사라진 경비가 구독자로 남아 있으면
        // 다음 씬에서 소리가 날 때 없는 오브젝트를 부르려다 오류가 난다.
        public static void Clear() => OnNoise = null;
    }
}
