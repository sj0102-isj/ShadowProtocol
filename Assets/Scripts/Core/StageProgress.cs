using UnityEngine;

namespace Stealth
{
    /// <summary>
    /// 클리어 기록과 무한모드 최고 점수. PlayerPrefs에 저장한다.
    /// 세이브 파일을 따로 둘 만큼 데이터가 많지 않다.
    /// </summary>
    public static class StageProgress
    {
        const string ClearedPrefix = "stealth.cleared.";
        const string RankPrefix = "stealth.rank.";
        const string HighScoreKey = "stealth.endless.highscore";
        const string UnlockAllKey = "stealth.unlockall";

        public static bool IsCleared(string sceneName) =>
            !string.IsNullOrEmpty(sceneName) && PlayerPrefs.GetInt(ClearedPrefix + sceneName, 0) == 1;

        /// <summary>클리어 기록. 랭크는 더 좋은 것만 남긴다.</summary>
        public static void MarkCleared(string sceneName, string rank)
        {
            if (string.IsNullOrEmpty(sceneName)) return;

            PlayerPrefs.SetInt(ClearedPrefix + sceneName, 1);

            if (RankScore(rank) > RankScore(GetRank(sceneName)))
                PlayerPrefs.SetString(RankPrefix + sceneName, rank);

            PlayerPrefs.Save();
        }

        public static string GetRank(string sceneName) =>
            string.IsNullOrEmpty(sceneName) ? "" : PlayerPrefs.GetString(RankPrefix + sceneName, "");

        static int RankScore(string rank) => rank switch
        {
            "S" => 4,
            "A" => 3,
            "B" => 2,
            "C" => 1,
            _ => 0
        };

        public static int EndlessHighScore
        {
            get => PlayerPrefs.GetInt(HighScoreKey, 0);
            set
            {
                if (value <= EndlessHighScore) return;
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
                    if (stage.isEndless) continue;
                    if (!IsCleared(stage.sceneName)) return false;
                }
                return true;
            }
        }

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
