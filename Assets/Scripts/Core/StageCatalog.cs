namespace Stealth
{
    /// <summary>씬을 넘나들며 유지되는 아주 작은 세션 상태.</summary>
    public static class GameSession
    {
        /// <summary>재시작·스테이지 선택으로 씬을 다시 열 때 타이틀 화면을 건너뛴다.</summary>
        public static bool SkipTitle;
    }

    public struct StageEntry
    {
        public string sceneName;
        public string code;
        public string title;
        /// <summary>이 스테이지에서 처음 등장하는 기능. 선택 화면에 표시된다.</summary>
        public string newFeature;
        public string summary;
        /// <summary>씬이 실제로 만들어져 있는가. 아직 안 만든 스테이지는 false.</summary>
        public bool built;
        public bool isEndless;
    }

    /// <summary>
    /// 스테이지 목록. 스테이지마다 기능이 하나씩 늘어나고,
    /// 전부 클리어하면 모든 기능을 쓰는 무한모드가 열린다.
    ///
    /// 새 스테이지를 만들면 built 를 true 로 바꾸면 선택 화면에 뜬다.
    /// </summary>
    public static class StageCatalog
    {
        public static readonly StageEntry[] Stages =
        {
            new StageEntry
            {
                sceneName = "SampleScene",
                code = "STAGE 01",
                title = "창고",
                newFeature = "은신처 · 크롤 통로",
                summary = "경비 4명 · 기밀 자료 3개",
                built = true,
            },
            new StageEntry
            {
                sceneName = "Stage02",
                code = "STAGE 02",
                title = "저택",
                newFeature = "동전 던지기 — 소음으로 유인",
                summary = "경비를 소리로 끌어내 빈틈을 만든다",
                built = true,
            },
            new StageEntry
            {
                sceneName = "Stage03",
                code = "STAGE 03",
                title = "사무동",
                newFeature = "정전 — 10초간 완전히 안 보인다",
                summary = "두꺼비집을 내려 감시 구역을 돌파한다",
                built = true,
            },
            new StageEntry
            {
                sceneName = "Stage04",
                code = "STAGE 04",
                title = "연구소",
                newFeature = "감시 드론 — 벽이 통하지 않는다",
                summary = "복도를 훑는 드론. 머리 위를 막아야 피한다",
                built = true,
            },
            new StageEntry
            {
                sceneName = "Stage05",
                code = "STAGE 05",
                title = "금고층",
                newFeature = "환풍구 이동 (V)",
                summary = "감시망을 통째로 건너뛰는 지름길",
                built = true,
            },
            new StageEntry
            {
                sceneName = "Endless",
                code = "ENDLESS",
                title = "무한 모드",
                newFeature = "모든 기능 · 목숨 3개",
                summary = "중앙 열람홀 + 구역 4개 · 자료가 계속 리스폰된다. 최고 점수에 도전",
                built = true,
                isEndless = true,
            },
        };

        /// <summary>선택 화면에서 고를 수 있는가.</summary>
        public static bool IsSelectable(StageEntry stage)
        {
            if (!stage.built) return false;
            return !stage.isEndless || StageProgress.EndlessUnlocked;
        }
    }
}
