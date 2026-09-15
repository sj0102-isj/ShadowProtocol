using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Stealth
{
    public enum DemoAction
    {
        /// <summary>point 까지 걸어간다. crawl 이면 웅크린 채로.</summary>
        MoveTo,
        /// <summary>seconds 동안 가만히 선다. 화면에 무슨 일이 벌어지는지 보여줄 시간.</summary>
        Wait,
        /// <summary>key 를 한 번 누른다. H 은신 / F 두꺼비집 / V 환풍구.</summary>
        Tap,
        /// <summary>key 를 seconds 동안 누르고 있는다. E 자료 회수.</summary>
        Hold,
        /// <summary>point 를 겨냥해 동전을 던진다.</summary>
        Throw,
        /// <summary>key 를 누른 채로 다음 단계로 넘어간다. 걸어가면서 E를 쥐고 있을 때 쓴다.</summary>
        KeyDown,
        /// <summary>KeyDown 으로 눌러둔 키를 뗀다.</summary>
        KeyUp,
        /// <summary>guardIndex 번 경비의 시선을 point 방향으로 즉시 돌린다. 연출용.</summary>
        FaceGuard,
    }

    [System.Serializable]
    public struct DemoStep
    {
        public DemoAction action;
        public Vector3 point;
        public float seconds;
        public Key key;
        public bool crawl;
        public int guardIndex;
    }

    /// <summary>
    /// 기능 소개 영상용 자동 재생기. Play 를 누르면 플레이어가 정해진 대본대로 움직인다.
    ///
    /// 게임 로직을 흉내 내는 대신 가짜 키보드·마우스를 만들어 실제 입력으로 흘려보낸다.
    /// 그래서 은신·동전·환풍구·두꺼비집이 전부 사람이 누른 것과 똑같은 경로를 탄다.
    /// 데모에서 되는 것은 실제로도 된다 — 영상용으로 따로 구현한 가짜가 아니다.
    /// </summary>
    public class DemoDirector : MonoBehaviour
    {
        public List<DemoStep> steps = new List<DemoStep>();

        [Tooltip("Play 직후 카메라가 자리를 잡을 때까지 기다리는 시간.")]
        public float startDelay = 1.2f;
        [Tooltip("이 거리 안에 들어오면 도착으로 친다.")]
        public float arriveRadius = 0.4f;
        [Tooltip("한 구간에서 이만큼 헤매면 포기하고 다음으로 넘어간다. 벽에 끼어 영상이 멈추는 것을 막는다.")]
        public float stuckTimeout = 12f;
        public bool loop;

        PlayerController player;
        Camera cam;
        Keyboard keys;
        Mouse mouse;
        NavMeshPath path;
        readonly List<Key> held = new List<Key>();

        IEnumerator Start()
        {
            player = Object.FindFirstObjectByType<PlayerController>();
            cam = Camera.main;
            path = new NavMeshPath();
            if (player == null) yield break;

            keys = InputSystem.AddDevice<Keyboard>("DemoKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("DemoMouse");
            keys.MakeCurrent();
            mouse.MakeCurrent();

            if (StealthGameManager.Instance != null) StealthGameManager.Instance.StartMission();
            player.ScriptedInput = true;

            yield return new WaitForSeconds(startDelay);

            do
            {
                for (int i = 0; i < steps.Count; i++) yield return Run(steps[i]);
            }
            while (loop);

            player.ScriptedInput = false;
        }

        void OnDestroy()
        {
            if (keys != null) InputSystem.RemoveDevice(keys);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
        }

        IEnumerator Run(DemoStep step)
        {
            switch (step.action)
            {
                case DemoAction.MoveTo:
                    yield return Walk(step.point, step.crawl);
                    break;
                case DemoAction.Wait:
                    player.ScriptedWorldDir = Vector3.zero;
                    yield return new WaitForSeconds(step.seconds);
                    break;
                case DemoAction.Tap:
                    yield return Tap(step.key);
                    break;
                case DemoAction.Hold:
                    yield return Hold(step.key, step.seconds);
                    break;
                case DemoAction.Throw:
                    yield return Throw(step.point);
                    break;
                case DemoAction.KeyDown:
                    Down(step.key);
                    yield return null;
                    break;
                case DemoAction.KeyUp:
                    Up(step.key);
                    yield return null;
                    break;
                case DemoAction.FaceGuard:
                    var guard = GameObject.Find("Guard " + step.guardIndex);
                    if (guard != null) guard.GetComponent<GuardAI>().ForceLook(step.point);
                    yield return null;
                    break;
            }
        }

        /// <summary>
        /// NavMesh 경로의 꼭짓점을 차례로 밟는다. 직선으로 밀면 모퉁이에 걸려 데모가 멈춘다.
        /// 크롤 통로는 애초에 NavMesh에서 걷어낸 구간이라 경로가 안 나오므로 직선으로 간다.
        /// </summary>
        IEnumerator Walk(Vector3 target, bool crawl)
        {
            player.ScriptedCrawl = crawl;

            // 목표가 선반 위처럼 갈 수 없는 자리면 가장 가까운 걸을 수 있는 자리로 당긴다.
            // 없으면 경로 계산이 실패해 직선 이동으로 넘어가고, 그대로 벽에 처박힌다.
            if (!crawl && NavMesh.SamplePosition(target, out NavMeshHit landing, 3f, NavMesh.AllAreas))
                target = landing.position;

            if (crawl ||
                !NavMesh.CalculatePath(player.transform.position, target, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete)
            {
                yield return Straight(target);
                yield break;
            }

            Vector3[] corners = path.corners;
            for (int i = 1; i < corners.Length; i++) yield return Straight(corners[i]);
        }

        IEnumerator Straight(Vector3 target)
        {
            float elapsed = 0f;
            while (Flat(player.transform.position, target) > arriveRadius && elapsed < stuckTimeout)
            {
                Vector3 dir = target - player.transform.position;
                dir.y = 0f;
                player.ScriptedWorldDir = dir.normalized;
                elapsed += Time.deltaTime;
                yield return null;
            }

            player.ScriptedWorldDir = Vector3.zero;
        }

        IEnumerator Tap(Key key)
        {
            Down(key);
            yield return null;
            yield return null;   // wasPressedThisFrame 가 한 번은 true 로 읽히도록
            Up(key);
            yield return null;
        }

        IEnumerator Hold(Key key, float seconds)
        {
            Down(key);
            yield return new WaitForSeconds(seconds);
            Up(key);
            yield return null;
        }

        // 눌린 키 전체를 매번 다시 보낸다. 키 하나만 담아 보내면 앞서 쥐고 있던 키가 풀린다.
        void Down(Key key)
        {
            if (held.Contains(key)) return;
            held.Add(key);
            Sync();
        }

        void Up(Key key)
        {
            if (!held.Remove(key)) return;
            Sync();
        }

        void Sync() => InputSystem.QueueStateEvent(keys, new KeyboardState(held.ToArray()));

        /// <summary>
        /// 동전은 마우스로 겨냥한다. 목표 지점을 화면 좌표로 바꿔 가짜 마우스를 그리로 옮기고 Q를 누른다.
        /// </summary>
        IEnumerator Throw(Vector3 target)
        {
            if (cam == null) yield break;

            Vector3 screen = cam.WorldToScreenPoint(target);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(screen.x, screen.y) });
            yield return null;
            yield return null;   // CoinThrow 가 조준점을 갱신할 한 프레임
            yield return Tap(Key.Q);
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
