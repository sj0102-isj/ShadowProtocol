namespace Stealth
{
    /// <summary>
    /// 경비 상태머신. 영상에서 이 전이 사이클 자체가 핵심 시연 소재다.
    /// Patrol -> Suspicious -> Chase -> Search -> Return -> Patrol
    /// </summary>
    // enum(열거형) = "값이 이 목록 중 하나뿐"인 자료형을 직접 만드는 문법.
    //
    // 왜 숫자(0,1,2)나 문자열("Patrol")을 쓰지 않고 enum 을 쓰는가:
    //   - 오타가 컴파일 단계에서 잡힌다. "Patrl" 이라고 쓰면 빌드가 안 된다
    //   - 목록에 없는 값이 들어올 수 없다. 숫자였다면 99가 들어와도 막을 방법이 없다
    //   - 코드를 읽을 때 state == 2 보다 state == GuardState.Search 가 훨씬 명확하다
    //
    // 상태머신(state machine) = "지금 어떤 상태인지"를 하나 저장해 두고, 상태마다 다르게
    // 행동하게 만드는 설계. 경비 AI 처럼 행동이 국면별로 딱 갈리는 대상에 잘 맞는다.
    // 실제로 상태에 따라 갈라지는 코드는 GuardAI.cs 에 있다.
    public enum GuardState
    {
        Patrol,      // 순찰 중
        Suspicious,  // 뭔가 본 것 같음 (멈춰서 응시)
        Search,      // 마지막 목격/소음 지점 수색
        Chase,       // 발각, 추격
        Return       // 순찰 경로로 복귀
    }
}
