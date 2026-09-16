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
        // 무작위 좌표를 뽑아 "여기 걸을 수 있나?" 검사하는 대신, 걸을 수 있는 칸 목록에서
        // 하나를 고르는 방식이다. 실패하는 시도가 없어서 훨씬 빠르고 확실하다.
        public List<Vector3> spawnPoints = new List<Vector3>();

        /// <summary>지금 점수. 게임 매니저가 세는 값을 그대로 보여준다.</summary>
        public int Score => StealthGameManager.Instance != null ? StealthGameManager.Instance.Collected : 0;

        public int HighScore { get; private set; }
        public bool NewRecord { get; private set; }

        Transform player;

        /// <summary>이번 판을 시작할 때의 기록. "신기록!" 을 띄울지 판단하는 기준이다.</summary>
        //
        // HighScore 는 점수를 딸 때마다 갱신되므로 기준으로 쓸 수 없다.
        // (1점만 따도 HighScore 가 따라 올라가서 "지금 점수 > HighScore" 가 영원히 거짓이 된다)
        // 그래서 판이 시작될 때의 값을 따로 얼려 둔다.
        int recordAtStart;

        void Awake()
        {
            Instance = this;
            HighScore = StageProgress.EndlessHighScore;   // 저장된 기록을 읽어온다
            recordAtStart = HighScore;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            var pc = Object.FindFirstObjectByType<PlayerController>();
            if (pc != null) player = pc.transform;

            // 게임 매니저의 "미션 종료" 이벤트를 구독한다.
            // 매니저는 누가 듣는지 몰라도 되고, 우리는 끝나는 순간을 정확히 알 수 있다.
            var gm = StealthGameManager.Instance;
            if (gm != null) gm.OnMissionEnded += HandleMissionEnded;
        }

        /// <summary>미션이 끝나면(목숨 소진) 최고 점수를 갱신한다.</summary>
        void HandleMissionEnded(MissionState state) => SaveScore();

        /// <summary>지금 점수를 곧바로 기록에 반영한다.</summary>
        //
        // 예전에는 미션이 끝나는 순간(목숨 소진)에만 저장했다.
        // 그런데 무한모드에는 "끝"이 아닌 퇴장 경로가 여러 개 있다.
        //
        //   ESC -> 스테이지 선택으로 나가기   <- OnMissionEnded 가 안 불린다
        //   창 닫기 / 강제 종료               <- 마찬가지
        //
        // 이 경로로 나가면 20점을 따 놓고도 기록이 0으로 남았다.
        // 그래서 "끝날 때 저장"을 버리고 점수를 딸 때마다 저장하는 방식으로 바꿨다.
        // 나가는 방법이 몇 가지든 상관없어진다 - 이미 저장돼 있기 때문이다.
        //
        // 매번 저장하는 게 낭비처럼 보이지만 자료 회수는 몇 초에 한 번이고
        // 저장하는 값은 정수 하나다. 체감되는 비용이 없다.
        void SaveScore()
        {
            NewRecord = Score > recordAtStart;

            // StageProgress.EndlessHighScore 의 set 안에 "더 높을 때만 저장" 검사가 들어 있다.
            // 그래서 여기서는 비교 없이 그냥 넣어도 된다.
            StageProgress.EndlessHighScore = Score;
            HighScore = StageProgress.EndlessHighScore;
        }

        /// <summary>자료를 회수했을 때 StealthGameManager 가 호출한다.</summary>
        public void OnObjectiveCollected(Objective objective)
        {
            // 점수가 오른 직후다. 여기서 바로 저장해 두면 어떻게 나가도 기록이 남는다.
            SaveScore();

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
        //
        // 이런 식으로 "완벽한 답을 못 찾으면 기준을 낮춰서라도 답을 낸다"는 방식을
        // 우아한 성능 저하(graceful degradation)라고 부른다. 게임 로직에서 자주 쓰인다.
        bool TryFindSpawn(Objective self, out Vector3 position)
        {
            position = self.transform.position;
            if (spawnPoints.Count == 0) return false;

            for (int attempt = 0; attempt < spawnAttempts; attempt++)
            {
                // 시도할수록 조건을 완화한다.
                // attempt 가 0이면 relax = 1(원래 조건), 마지막에는 거의 0(조건 없음)이 된다.
                float relax = 1f - attempt / (float)spawnAttempts;
                float needPlayer = minDistanceFromPlayer * relax;
                float needOther = minDistanceBetweenObjectives * relax;

                Vector3 candidate = spawnPoints[Random.Range(0, spawnPoints.Count)];

                if (player != null && Flat(candidate, player.position) < needPlayer) continue;
                if (TooCloseToAnother(self, candidate, needOther)) continue;

                position = candidate;
                return true;
            }

            // 끝까지 못 찾았으면 아무 데나 고른다. 자료가 안 나오는 것보다는 낫다.
            position = spawnPoints[Random.Range(0, spawnPoints.Count)];
            return true;
        }

        /// <summary>다른 자료와 너무 가까운가.</summary>
        static bool TooCloseToAnother(Objective self, Vector3 candidate, float minDistance)
        {
            for (int i = 0; i < Objective.All.Count; i++)
            {
                Objective other = Objective.All[i];

                // 자기 자신과 이미 회수된 자료는 비교 대상이 아니다.
                if (other == null || other == self || other.Collected) continue;

                if (Flat(candidate, other.transform.position) < minDistance) return true;
            }
            return false;
        }

        /// <summary>높이를 무시한 평면 거리.</summary>
        // 매개변수 a, b 는 값 복사본이라 여기서 y 를 0으로 바꿔도 부르는 쪽 값은 안 바뀐다.
        // Vector3 가 struct(값 타입)이기 때문이다. class 였다면 원본이 함께 바뀐다.
        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
