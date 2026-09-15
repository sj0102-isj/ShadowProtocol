using System.Collections.Generic;
using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 무한모드. 탈출 조건이 없고, 목숨 3개가 다할 때까지 자료를 계속 회수해 점수를 쌓는다.
    /// 자료는 항상 정해진 개수만큼 맵에 존재하며, 회수되면 다른 자리에서 되살아난다.
    /// </summary>
    public class EndlessMode : MonoBehaviour
    {
        public static EndlessMode Instance { get; private set; }

        [Header("Respawn")]
        [Tooltip("새 자료가 플레이어에게서 최소한 이만큼 떨어진 곳에 생긴다.")]
        public float minDistanceFromPlayer = 22f;
        [Tooltip(
            "자료끼리 떨어뜨리는 최소 거리(m). 맵의 방 하나가 대략 20m라, 이 값이 그보다 작으면 " +
            "자료 다섯 개가 한 구역에 몰려도 조건을 만족해서 맵의 나머지가 통째로 놀게 된다.")]
        public float minDistanceBetweenObjectives = 20f;
        [Tooltip("조건을 만족하는 자리를 못 찾으면 조건을 완화하며 재시도하는 횟수.")]
        public int spawnAttempts = 60;

        /// <summary>걸어 다닐 수 있는 칸들의 월드 좌표. 스테이지 빌더가 채운다.</summary>
        public List<Vector3> spawnPoints = new List<Vector3>();

        public int Score => StealthGameManager.Instance != null ? StealthGameManager.Instance.Collected : 0;
        public int HighScore { get; private set; }
        public bool NewRecord { get; private set; }

        Transform player;

        void Awake()
        {
            Instance = this;
            HighScore = StageProgress.EndlessHighScore;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            var pc = Object.FindFirstObjectByType<PlayerController>();
            if (pc != null) player = pc.transform;

            var gm = StealthGameManager.Instance;
            if (gm != null) gm.OnMissionEnded += HandleMissionEnded;
        }

        void HandleMissionEnded(MissionState state)
        {
            NewRecord = Score > StageProgress.EndlessHighScore;
            StageProgress.EndlessHighScore = Score;      // 더 높을 때만 저장된다
            HighScore = StageProgress.EndlessHighScore;
        }

        /// <summary>자료를 회수했을 때 StealthGameManager 가 호출한다.</summary>
        public void OnObjectiveCollected(Objective objective)
        {
            if (objective == null) return;

            // 중앙의 고액 자료처럼 자리 자체가 의미인 것은 원래 자리에서 되살아난다.
            // 무작위로 옮기면 "위험한 한가운데를 뚫고 가서 먹는다"는 설계가 사라진다.
            if (objective.fixedSpawn)
            {
                // 쿨다운이 걸려 있으면 Objective 가 스스로 시간을 재고 되살아난다.
                if (objective.respawnDelay <= 0f) objective.RespawnAt(objective.HomePosition);
                return;
            }

            if (TryFindSpawn(objective, out Vector3 position)) objective.RespawnAt(position);
        }

        /// <summary>
        /// 거리 조건을 만족하는 자리를 찾는다. 못 찾으면 조건을 점점 풀어서라도 자리를 낸다.
        /// 자료가 안 생겨 게임이 멈추는 것보다는 가까이 생기는 편이 낫다.
        /// </summary>
        bool TryFindSpawn(Objective self, out Vector3 position)
        {
            position = self.transform.position;
            if (spawnPoints.Count == 0) return false;

            for (int attempt = 0; attempt < spawnAttempts; attempt++)
            {
                // 시도할수록 조건을 완화한다.
                float relax = 1f - attempt / (float)spawnAttempts;
                float needPlayer = minDistanceFromPlayer * relax;
                float needOther = minDistanceBetweenObjectives * relax;

                Vector3 candidate = spawnPoints[Random.Range(0, spawnPoints.Count)];

                if (player != null && Flat(candidate, player.position) < needPlayer) continue;
                if (TooCloseToAnother(self, candidate, needOther)) continue;

                position = candidate;
                return true;
            }

            position = spawnPoints[Random.Range(0, spawnPoints.Count)];
            return true;
        }

        static bool TooCloseToAnother(Objective self, Vector3 candidate, float minDistance)
        {
            for (int i = 0; i < Objective.All.Count; i++)
            {
                Objective other = Objective.All[i];
                if (other == null || other == self || other.Collected) continue;
                if (Flat(candidate, other.transform.position) < minDistance) return true;
            }
            return false;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
