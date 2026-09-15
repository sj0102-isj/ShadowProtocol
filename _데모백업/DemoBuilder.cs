using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Stealth.EditorTools
{
    /// <summary>
    /// 기능 소개 영상용 데모 씬 생성기.
    ///
    /// 스테이지를 새로 만들지 않고 기존 스테이지를 그대로 쓴 뒤 DemoDirector 만 얹어
    /// 별도 씬으로 복사 저장한다. 원본 스테이지 씬은 건드리지 않으므로 게임은 그대로 돌아간다.
    /// 대본은 격자 좌표로 쓴다 — 스테이지 정의와 같은 좌표계라 맵을 보면서 고칠 수 있다.
    /// </summary>
    public static class DemoBuilder
    {
        const string DemoFolder = "Assets/Scenes";

        [MenuItem("Stealth/Demo/1 - 크롤 통로 (Stage 01)")]
        public static void BuildCrawlDemo()
        {
            var s = StageLibrary.Stage01;
            var d = new Script(s);

            // 사무실 정문(14,9)은 경비가 보고 있어서 서쪽 크롤 통로(10,7)가 정답 루트다.
            // 고정하는 것은 사무실 경비 하나뿐이다. 나머지가 다 얼어 있으면 게임이 아니라
            // 인형극처럼 보인다. 다른 경비는 평소대로 순찰한다.
            d.PinGuard(4, 15, 7, Vector3.right);    // 사무실 경비 — 문서에 등을 돌린다
            d.InstantPickup(13, 7);                 // 다가가기만 하면 회수되게
            d.RequireObjectives(1);                 // 하나만 먹고 탈출 가능

            // 원래 시작점(1,14)에서 걸어오면 10초 넘게 걸린다. 1번 경비의 순찰 구간
            // (row 13, x=8~12) 바로 서쪽에 세워 두고 시작한다 — 경비를 끼고 출발하는 그림.
            d.Spawn(7, 12);
            d.Move(8, 10);
            d.Move(9, 7);                 // 크롤 통로 앞. 선 채로 한 박자 쉰다
            d.Wait(0.8f);
            d.Move(11, 7, crawl: true);   // 숙이고 (10,7) 통과
            d.Move(12, 7);                // 통과하면 자동으로 일어선다
            d.KeyDown(Key.E);             // 문서에 닿는 순간 바로 회수되도록 미리 쥔다
            d.Move(13, 7);
            d.Wait(1.2f);
            d.KeyUp(Key.E);

            // 바로 옆 오크통(12,8). 소품 고리는 한쪽만 트여 있고 여기는 북쪽(12,7)이 입구다.
            // 막힌 쪽으로 가면 상자에 걸려 은신 사거리 1.5m 안에 못 들어간다.
            d.Move(12f, 7.4f);
            d.Tap(Key.H);
            d.Wait(0.5f);
            d.FaceGuard(4, Vector3.left);     // 경비가 뒤로 돈다 — 숨어 있어서 안 보인다
            d.Wait(2f);
            d.FaceGuard(4, Vector3.right);    // 원래 보던 쪽으로 돌아간다
            d.Wait(1f);
            d.Tap(Key.H);                     // 나온다
            d.Move(14, 10);                   // 사무실 정문(14,9)으로 빠져나가
            d.Move(16, 13);
            d.Move(23, 15);                   // 탈출구까지
            d.Wait(2f);

            Save(s, d, "Demo_1_Crawl");
        }

        [MenuItem("Stealth/Demo/2 - 은신처 (Stage 01)")]
        public static void BuildHideDemo()
        {
            var s = StageLibrary.Stage01;
            var d = new Script(s);

            // 경비 3번이 동쪽 방을 x=21 세로로 훑는다. 그 동선 위의 오크통(21,10)에 숨어
            // 눈앞으로 지나가게 만드는 것이 이 데모의 그림이다.
            d.Move(3, 13);
            d.Move(20, 13);
            d.Move(22, 11);
            d.Move(21.6f, 10f);   // 이 오크통은 동쪽(22,10)이 입구다
            d.Tap(Key.H);
            d.Wait(7f);
            d.Tap(Key.H);
            d.Wait(1f);
            d.Move(23, 15);

            Save(s, d, "Demo_2_Hide");
        }

        // ------------------------------------------------------------ 내부

        /// <summary>
        /// 격자 좌표로 대본을 쓰기 위한 얇은 껍데기.
        /// 씬을 고치는 지시(PinGuard/InstantPickup)는 단계가 아니라 빌드 직후 한 번 실행된다.
        /// </summary>
        class Script
        {
            public readonly List<DemoStep> Steps = new List<DemoStep>();
            public readonly List<System.Action> SceneEdits = new List<System.Action>();

            readonly int width, height;

            public Script(StageData stage)
            {
                width = stage.grid[0].Length;
                height = stage.grid.Length;
            }

            /// 칸 사이 좌표도 받는다. 오크통 옆처럼 칸 한가운데가 아닌 곳에 서야 할 때가 있다.
            public Vector3 At(float x, float y) => new Vector3(
                (x - (width - 1) * 0.5f) * StageBuilder.Cell,
                0f,
                ((height - 1) * 0.5f - y) * StageBuilder.Cell);

            public void Move(float x, float y, bool crawl = false) => Steps.Add(new DemoStep
            {
                action = DemoAction.MoveTo, point = At(x, y), crawl = crawl
            });

            public void Wait(float seconds) => Steps.Add(new DemoStep
            {
                action = DemoAction.Wait, seconds = seconds
            });

            public void Tap(Key key) => Steps.Add(new DemoStep { action = DemoAction.Tap, key = key });

            public void Hold(Key key, float seconds) => Steps.Add(new DemoStep
            {
                action = DemoAction.Hold, key = key, seconds = seconds
            });

            public void KeyDown(Key key) => Steps.Add(new DemoStep { action = DemoAction.KeyDown, key = key });

            public void KeyUp(Key key) => Steps.Add(new DemoStep { action = DemoAction.KeyUp, key = key });

            public void FaceGuard(int index, Vector3 face) => Steps.Add(new DemoStep
            {
                action = DemoAction.FaceGuard, guardIndex = index, point = face
            });

            public void Throw(float x, float y) => Steps.Add(new DemoStep
            {
                action = DemoAction.Throw, point = At(x, y)
            });

            /// <summary>
            /// 경비를 한 자리에 세우고 한 방향만 보게 한다. 순찰 지점을 하나로 줄이면
            /// GuardAI 가 도착 상태로 머무르고, 그 지점의 회전이 그대로 시선 기준이 된다.
            /// </summary>
            public void PinGuard(int index, float x, float y, Vector3 face) => SceneEdits.Add(() =>
            {
                var go = GameObject.Find($"Guard {index}");
                var routeRoot = GameObject.Find($"Guard {index} Route");
                if (go == null || routeRoot == null) return;

                Vector3 spot = At(x, y);
                Quaternion look = Quaternion.LookRotation(face, Vector3.up);

                for (int i = routeRoot.transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(routeRoot.transform.GetChild(i).gameObject);

                var wp = new GameObject("WP 1");
                wp.transform.SetParent(routeRoot.transform, false);
                wp.transform.SetPositionAndRotation(spot, look);

                var ai = go.GetComponent<GuardAI>();
                ai.waypoints = new[] { wp.transform };
                ai.lookAroundAngle = 0f;   // 두리번거리면 "반대쪽 벽만 본다"가 깨진다
                go.transform.SetPositionAndRotation(spot, look);
            });

            /// <summary>그 칸의 자료를 다가가기만 하면 회수되게 만든다. 영상에서 키 입력을 감춘다.</summary>
            /// <summary>
            /// 플레이어 시작 자리를 옮긴다. 클립은 짧을수록 좋은데 스테이지의 P는
            /// 게임 전체를 기준으로 잡힌 자리라, 보여줄 장면까지 걸어오는 시간이 길다.
            /// </summary>
            public void Spawn(float x, float y) => SceneEdits.Add(() =>
            {
                var pc = Object.FindFirstObjectByType<PlayerController>();
                if (pc != null) pc.transform.position = At(x, y);
            });

            /// <summary>데모에서 요구 자료 수를 줄인다. 하나만 먹고 탈출하는 클립을 만들 때 쓴다.</summary>
            public void RequireObjectives(int count) => SceneEdits.Add(() =>
            {
                var gm = Object.FindFirstObjectByType<StealthGameManager>();
                if (gm != null) gm.requiredObjectives = count;
            });

            public void InstantPickup(float x, float y) => SceneEdits.Add(() =>
            {
                Vector3 spot = At(x, y);
                foreach (var objective in Object.FindObjectsByType<Objective>(FindObjectsSortMode.None))
                {
                    if (Vector3.Distance(objective.transform.position, spot) > 1f) continue;
                    objective.holdDuration = 0.15f;
                    return;
                }
            });
        }

        static void Save(StageData stage, Script script, string demoName)
        {
            if (!StageBuilder.Build(stage)) return;

            for (int i = 0; i < script.SceneEdits.Count; i++) script.SceneEdits[i]();

            var host = new GameObject("--- DEMO ---");
            host.AddComponent<DemoDirector>().steps = script.Steps;

            var scene = EditorSceneManager.GetActiveScene();
            string path = $"{DemoFolder}/{demoName}.unity";
            // "다른 이름으로 저장". 원본 스테이지 씬은 StageBuilder 가 데모 수정 전에 이미
            // 저장해 뒀으므로 디스크의 원본은 깨끗하고, 열려 있는 씬만 데모 씬이 된다.
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[DemoBuilder] '{demoName}' 생성 완료 — 단계 {script.Steps.Count}개. Play 를 누르면 재생된다.");
        }
    }
}
