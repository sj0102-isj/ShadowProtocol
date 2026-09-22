using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 클리어 기록과 무한모드 최고 점수. PlayerPrefs에 저장한다.
    /// 세이브 파일을 따로 둘 만큼 데이터가 많지 않다.
    /// </summary>
    //
    // PlayerPrefs = 유니티가 제공하는 아주 단순한 저장소. "이름 → 값" 쌍만 저장한다.
    // 윈도우에서는 레지스트리에, 다른 OS에서는 파일에 들어간다.
    // int / float / string 세 종류만 저장할 수 있어서 복잡한 데이터에는 안 맞지만,
    // 이 게임처럼 "깼는가 / 랭크 / 최고점수" 정도면 이걸로 충분하다.
    //
    // static class 라 씬에 오브젝트가 없어도 StageProgress.IsCleared(...) 처럼 바로 부른다.
    public static class StageProgress
    {
        // 키 이름을 상수로 모아두는 이유: 저장할 때와 읽을 때 오타가 나면
        // 아무 오류 없이 조용히 "저장이 안 되는" 버그가 된다. 상수로 쓰면 오타가 불가능하다.
        const string ClearedPrefix = "stealth.cleared.";
        const string RankPrefix = "stealth.rank.";
        const string HighScoreKey = "stealth.endless.highscore";
        const string UnlockAllKey = "stealth.unlockall";

        /// <summary>그 스테이지를 깼는가.</summary>
        // PlayerPrefs 는 bool 을 저장할 수 없어서 0/1 정수로 대신한다.
        // GetInt 의 두 번째 인자는 "키가 없을 때 돌려줄 기본값"이다.
        public static bool IsCleared(string sceneName) =>
            !string.IsNullOrEmpty(sceneName) && PlayerPrefs.GetInt(ClearedPrefix + sceneName, 0) == 1;

        /// <summary>클리어 기록. 랭크는 더 좋은 것만 남긴다.</summary>
        public static void MarkCleared(string sceneName, string rank)
        {
            if (string.IsNullOrEmpty(sceneName)) return;

            PlayerPrefs.SetInt(ClearedPrefix + sceneName, 1);

            // 이미 S를 받은 스테이지를 C로 다시 깼다고 기록을 깎으면 안 된다.
            if (RankScore(rank) > RankScore(GetRank(sceneName)))
                PlayerPrefs.SetString(RankPrefix + sceneName, rank);

            // Save() 를 불러야 디스크에 실제로 쓰인다. 안 부르면 게임이 정상 종료될 때만 저장돼서
            // 강제 종료하면 기록이 날아간다.
            PlayerPrefs.Save();
        }

        public static string GetRank(string sceneName) =>
            string.IsNullOrEmpty(sceneName) ? "" : PlayerPrefs.GetString(RankPrefix + sceneName, "");

        /// <summary>랭크 문자를 숫자로 바꿔 비교할 수 있게 한다. "S" > "A" 는 문자열 비교로는 틀린 답이 나온다.</summary>
        static int RankScore(string rank) => rank switch
        {
            "S" => 4,
            "A" => 3,
            "B" => 2,
            "C" => 1,   // 지금은 나오지 않는다. 예전에 저장된 기록을 비교하려고 남겨 둔다
            _ => 0      // 기록 없음
        };

        /// <summary>무한모드 최고 점수. 넣을 때 자동으로 "더 높을 때만" 저장된다.</summary>
        //
        // 프로퍼티에 get 과 set 을 직접 구현한 예다. 필드처럼 쓰지만 안에서 검사를 할 수 있다.
        //   StageProgress.EndlessHighScore = 12;   ← 기존 기록이 20이면 무시된다
        // 부르는 쪽이 "더 높은지 비교하기"를 매번 안 해도 되고, 빠뜨릴 수도 없다.
        public static int EndlessHighScore
        {
            get => PlayerPrefs.GetInt(HighScoreKey, 0);
            set
            {
                if (value <= EndlessHighScore) return;   // 낮은 점수는 조용히 버린다
                PlayerPrefs.SetInt(HighScoreKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>시연·녹화용 전체 해금. 스테이지를 다 깨지 않고도 무한모드를 보여줄 수 있게 한다.</summary>
        public static bool UnlockAll
        {
            get => PlayerPrefs.GetInt(UnlockAllKey, 0) == 1;
            set { PlayerPrefs.SetInt(UnlockAllKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>무한모드가 열렸는가. 일반 스테이지를 전부 클리어해야 한다.</summary>
        public static bool EndlessUnlocked
        {
            get
            {
                if (UnlockAll) return true;

                foreach (StageEntry stage in StageCatalog.Stages)
                {
                    if (stage.isEndless) continue;               // 무한모드 자신은 검사 대상이 아니다
                    if (!IsCleared(stage.sceneName)) return false; // 하나라도 안 깼으면 잠김
                }
                return true;
            }
        }

        /// <summary>기록 전체 삭제. 에디터 메뉴 Stealth > Reset Progress 에서 부른다.</summary>
        public static void ResetAll()
        {
            foreach (StageEntry stage in StageCatalog.Stages)
            {
                PlayerPrefs.DeleteKey(ClearedPrefix + stage.sceneName);
                PlayerPrefs.DeleteKey(RankPrefix + stage.sceneName);
            }

            PlayerPrefs.DeleteKey(HighScoreKey);
            PlayerPrefs.DeleteKey(UnlockAllKey);
            PlayerPrefs.Save();
        }
    }
}
