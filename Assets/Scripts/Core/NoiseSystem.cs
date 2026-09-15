using System;
using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 소음 이벤트를 전역으로 방송한다. 경비는 여기에 구독해서 반응한다.
    /// 발소리, 동전 던지기, 문 열기 등 모든 소리가 이 경로를 탄다.
    /// </summary>
    public static class NoiseSystem
    {
        /// <summary>(발생 위치, 반경, 세기 0~1, 소리를 낸 물체 — 없으면 null)</summary>
        public static event Action<Vector3, float, float, Transform> OnNoise;

        /// <summary>
        /// source 를 넘기면 경비가 "가서 확인할 대상"으로 삼는다.
        /// 던진 동전처럼 집어들 수 있는 물건이 여기 해당한다.
        /// </summary>
        public static void Emit(Vector3 position, float radius, float strength = 1f, Transform source = null)
        {
            if (radius <= 0f) return;
            OnNoise?.Invoke(position, radius, Mathf.Clamp01(strength), source);
        }

        /// <summary>씬 전환 시 구독자가 남지 않도록 정리.</summary>
        public static void Clear() => OnNoise = null;
    }
}
