namespace Stealth
{
    /// <summary>씬을 넘나들며 유지되는 아주 작은 세션 상태.</summary>
    //
    // static 변수는 씬을 바꿔도 값이 유지된다. MonoBehaviour 의 필드였다면 씬을 새로 로드하는
    // 순간 오브젝트와 함께 사라진다. "씬을 넘어 살아남아야 하는 값"에 static 을 쓰는 이유다.
    public static class GameSession
    {
        /// <summary>재시작·스테이지 선택으로 씬을 다시 열 때 타이틀 화면을 건너뛴다.</summary>
        public static bool SkipTitle;
    }

    /// <summary>스테이지 하나의 정보를 담는 묶음.</summary>
    //
    // struct(구조체) = class 와 비슷하지만 "값"으로 다뤄진다. 변수에 넣으면 복사본이 만들어진다.
    // 여기서는 데이터 몇 개를 묶어두는 용도라 struct 가 알맞다.
    // 동작(메서드)이 붙고 여러 곳에서 같은 것을 공유해야 하면 class 를 쓴다.
    public struct StageEntry
    {
        public string sceneName;   // 실제 .unity 파일 이름. SceneManager.LoadScene 에 넘긴다
        public string code;        // 화면에 표시할 번호 (STAGE 01)
        public string title;       // 화면에 표시할 이름 (창고)
        /// <summary>이 스테이지에서 처음 등장하는 기능. 선택 화면에 표시된다.</summary>
        public string newFeature;
        public string summary;
        /// <summary>씬이 실제로 만들어져 있는가. 아직 안 만든 스테이지는 false.</summary>
        public bool built;
        public bool isEndless;     // 무한모드인가. 탈출 조건이 없고 목숨제로 돌아간다
    }

    /// <summary>
    /// 스테이지 목록. 스테이지마다 기능이 하나씩 늘어나고,
    /// 전부 클리어하면 모든 기능을 쓰는 무한모드가 열린다.
    ///
    /// 새 스테이지를 만들면 built 를 true 로 바꾸면 선택 화면에 뜬다.
    /// </summary>
    //
    // 스테이지 정보를 씬마다 흩어 두지 않고 여기 한 곳에 모아 둔 이유는,
    // 선택 화면·해금 판정·씬 로드가 전부 이 목록 하나만 보면 되게 하기 위해서다.
    // 스테이지를 추가할 때 고칠 곳이 이 파일 하나뿐이다.
    public static class StageCatalog
    {
        // readonly 배열 + 객체 초기화자. { new StageEntry { ... }, ... } 형태로
        // 필드 이름을 적어가며 채우면 순서를 안 외워도 되고 읽기도 쉽다.
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

            // 일반 스테이지는 항상 고를 수 있고, 무한모드만 해금 조건을 본다.
            // "무한모드가 아니거나, 무한모드가 해금됐으면 true" 라는 뜻이다.
            return !stage.isEndless || StageProgress.EndlessUnlocked;
        }
    }
}
