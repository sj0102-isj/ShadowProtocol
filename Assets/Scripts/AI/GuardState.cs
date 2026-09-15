namespace Stealth
{
    /// <summary>
    /// 경비 상태머신. 영상에서 이 전이 사이클 자체가 핵심 시연 소재다.
    /// Patrol -> Suspicious -> Chase -> Search -> Return -> Patrol
    /// </summary>
    public enum GuardState
    {
        Patrol,      // 순찰 중
        Suspicious,  // 뭔가 본 것 같음 (멈춰서 응시)
        Search,      // 마지막 목격/소음 지점 수색
        Chase,       // 발각, 추격
        Return       // 순찰 경로로 복귀
    }
}
