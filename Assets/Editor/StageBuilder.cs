using System.Collections.Generic;
using Unity.AI.Navigation;          // NavMeshSurface — 런타임/에디터에서 NavMesh 굽기
using UnityEditor;                  // MenuItem, EditorSceneManager 등 에디터 전용 기능
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;            // Volume, VolumeProfile, AmbientMode
using UnityEngine.Rendering.Universal;  // Bloom, Vignette, Tonemapping, ColorAdjustments

// ★ 이 파일은 Assets/Editor/ 폴더 안에 있다. 유니티는 이 폴더의 스크립트를
//   "에디터 전용"으로 취급해서 게임 빌드에 포함하지 않는다.
//   using UnityEditor; 를 쓰는 코드는 반드시 이 폴더에 있어야 빌드가 깨지지 않는다.
namespace Stealth.EditorTools
{
    /// <summary>
    /// 문자 그리드에서 그레이박스 스테이지를 통째로 생성한다.
    /// 씬을 손으로 만들지 않기 때문에 레이아웃 수정 -> 재생성이 몇 초면 끝난다.
    ///
    /// 그리드 기호
    ///   #  벽        (시야 차단 + 이동 차단)
    ///   X  선반      (시야 차단 + 이동 차단, 창고 느낌)
    ///   c  크롤 통로 (웅크려야만 통과. 경비는 지나갈 수 없는 지름길)
    ///   h  은신 지점 (웅크리고 있으면 시야에서 완전히 사라짐)
    ///   .  바닥
    ///   P  플레이어 시작 지점
    ///   E  탈출 구역
    ///   O  기밀 자료
    ///   L  두꺼비집  (F로 내리면 10초 정전)
    ///   G  고액 자료 (5점. 제자리에서 되살아난다)
    ///   1~9 환풍구  (같은 숫자 두 칸이 짝. V로 반대쪽으로 이동)
    ///
    /// 공중 순찰 드론은 격자에 찍지 않고 StageData.droneRoutes 로 따로 준다. 벽을 무시하기 때문이다.
    /// </summary>
    //
    // ── 이 도구가 하는 일 ──
    // 문자열 배열(맵 그림)을 읽어서 씬에 벽·바닥·경비·자료를 전부 코드로 만들어 놓는다.
    // 손으로 오브젝트를 배치하지 않기 때문에
    //   - 맵을 고치려면 문자 한 글자만 바꾸면 되고
    //   - 공용 규칙(벽 높이, 경비 설정)을 바꾸면 전 스테이지에 한 번에 반영되고
    //   - 씬 파일이 깨져도 메뉴 한 번이면 복구된다.
    //
    // 대신 씬을 손으로 꾸민 내용은 다시 만들 때 전부 지워진다. 그래서 데모 씬처럼
    // 손을 댄 것은 별도 파일로 저장해야 한다.
    public static class StageBuilder
    {
        const string RootName = "--- STAGE ---";   // 생성물을 전부 담는 최상위 오브젝트 이름
        const float Cell = 2f;                     // 격자 한 칸의 실제 크기(m)
        const float WallHeight = 2.6f;
        const float ShelfHeight = 2.0f;

        /// <summary>
        /// 크롤 통로의 천장 높이. 웅크린 플레이어(CharacterController 높이 1.1)는 지나가고,
        /// 서 있는 플레이어(1.8)와 경비(1.8)는 막힌다.
        /// NavMesh는 이 높이를 통과 못 하는 것으로 보고 아래를 걷어내므로 경비는 길을 못 찾는다.
        /// </summary>
        const float CrawlClearance = 1.25f;

        /// <summary>
        /// 은신처를 이루는 오크통·상자의 배치 반경과 높이.
        /// 높이는 GuardVision.rayHeight(1.5) 보다 낮게 둔다. 그래야 시야 콘이 이 소품에 잘리지 않고,
        /// "콘이 통 위를 지나가는데 그 뒤에 웅크려 있어서 안 보인다"는 그림이 거짓말이 되지 않는다.
        /// </summary>
        const float PropRingRadius = 0.78f;
        const float PropHeight = 0.90f;

        /// <summary>그리드 기준 상/우/하/좌. 그리드 y는 남쪽으로 증가하므로 월드 z는 부호가 반대다.</summary>
        static readonly Vector2Int[] Neighbors =
        {
            new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0),
        };

        // ------------------------------------------------------------ 메뉴

        /// <summary>
        /// 모든 스테이지를 한 번에 생성한다. 공용 코드를 고치면 전부 다시 만들어야 하므로
        /// 메뉴를 하나씩 누르는 것보다 이게 기본 동작이다.
        /// 스테이지 1을 마지막에 만들어 그 씬이 열린 채로 끝난다.
        /// </summary>
        // [MenuItem] 을 붙이면 유니티 상단 메뉴에 항목이 생긴다.
        //   "Stealth/Build ALL Stages"  →  Stealth 메뉴 안의 항목 이름
        //   %#a                         →  단축키. % = Ctrl, # = Shift, & = Alt
        //                                  즉 Ctrl+Shift+A
        //   priority = 0                →  메뉴에서 위로 올린다
        [MenuItem("Stealth/Build ALL Stages %#a", priority = 0)]
        public static void BuildAll()
        {
            StageData[] order =
            {
                StageLibrary.Stage02,
                StageLibrary.Stage03,
                StageLibrary.Stage04,
                StageLibrary.Stage05,
                StageLibrary.Endless,
                StageLibrary.Stage01,
            };

            foreach (StageData stage in order)
            {
                if (!Build(stage))
                {
                    Debug.LogWarning($"[StageBuilder] '{stage.displayName}' 에서 중단했습니다.");
                    return;
                }
            }

            Debug.Log($"[StageBuilder] 전체 생성 완료 — 스테이지 {order.Length}개.");
        }

        [MenuItem("Stealth/Build Stage 01 - Warehouse %#1")]
        public static void BuildStage01() => Build(StageLibrary.Stage01);

        [MenuItem("Stealth/Build Stage 02 - Mansion %#2")]
        public static void BuildStage02() => Build(StageLibrary.Stage02);

        [MenuItem("Stealth/Build Stage 03 - Office %#3")]
        public static void BuildStage03() => Build(StageLibrary.Stage03);

        [MenuItem("Stealth/Build Stage 04 - Laboratory %#4")]
        public static void BuildStage04() => Build(StageLibrary.Stage04);

        [MenuItem("Stealth/Build Stage 05 - Vault %#5")]
        public static void BuildStage05() => Build(StageLibrary.Stage05);

        [MenuItem("Stealth/Build Endless Mode %#0")]
        public static void BuildEndless() => Build(StageLibrary.Endless);

        [MenuItem("Stealth/Reset Progress")]
        public static void ResetProgress()
        {
            StageProgress.ResetAll();
            Debug.Log("[StageBuilder] 클리어 기록과 최고 점수를 모두 지웠습니다.");
        }

        [MenuItem("Stealth/Rebake NavMesh")]
        public static void RebakeNavMesh()
        {
            var surface = Object.FindFirstObjectByType<NavMeshSurface>();
            if (surface == null)
            {
                Debug.LogWarning("[StageBuilder] NavMeshSurface를 찾을 수 없습니다. 먼저 스테이지를 생성하세요.");
                return;
            }

            BakeNavMesh(surface);
            Debug.Log("[StageBuilder] NavMesh 재베이크 완료.");
        }

        // ------------------------------------------------------------ 빌드

        /// <summary>
        /// 스테이지 하나를 통째로 만든다. 이 파일에서 가장 중요한 함수.
        ///
        /// 순서에 의미가 있다:
        ///   1. 지오메트리(벽·바닥) 생성  →  NavMesh 굽기
        ///   2. 그 다음에 경비·자료 생성 — 먼저 만들면 사람이 벽으로 구워져 길이 막힌다
        ///   3. 마지막에 시스템·카메라·조명
        /// </summary>
        public static bool Build(StageData stage)
        {
            if (!OpenOrCreateScene(stage.sceneName)) return false;

            var scene = EditorSceneManager.GetActiveScene();

            // 이전에 만든 것을 먼저 지운다. 안 지우면 생성할 때마다 벽이 겹쳐 쌓인다.
            // DestroyImmediate 는 에디터에서 즉시 지우는 함수다(게임 중에는 Destroy 를 쓴다).
            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject(RootName);

            ValidateGrid(stage);   // 줄 길이가 다르거나 P/E 가 없으면 여기서 경고를 띄운다

            // grid 는 문자열 배열이다. 한 줄의 글자 수가 가로, 줄 수가 세로가 된다.
            int width = stage.grid[0].Length;
            int height = stage.grid.Length;

            // --- 1. 지오메트리 (NavMesh 베이크 대상) ---
            var geometry = new GameObject("Geometry");
            geometry.transform.SetParent(root.transform, false);

            BuildFloor(geometry.transform, width, height);
            BuildBlocks(geometry.transform, stage, width, height);
            BuildCrawlLintels(geometry.transform, stage, width, height);
            BuildHideProps(geometry.transform, stage, width, height);

            // NavMesh = 경비가 걸어다닐 수 있는 영역을 미리 계산해 둔 그물망.
            // 이걸 구워두면 GuardAI 가 SetDestination 한 줄로 벽을 피해 길을 찾는다.
            var surface = geometry.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;   // 이 오브젝트의 자식만 대상으로
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            BakeNavMesh(surface);

            // --- 2. 액터 / 목표물 (NavMesh에 포함되면 안 되므로 베이크 후 생성) ---
            var actors = new GameObject("Actors");
            actors.transform.SetParent(root.transform, false);

            Vector3 playerSpawn = FindCellWorld(stage, 'P', width, height);
            BuildPlayer(actors.transform, playerSpawn, stage.allowCoinThrow);

            for (int i = 0; i < stage.guardRoutes.Length; i++)
                BuildGuard(actors.transform, stage.guardRoutes[i], i + 1, width, height);

            // 배회형 경비는 고정 순찰 경비 다음 번호를 이어받는다. 하이어라키에서 구분되도록
            // 이름에 (배회) 를 붙인다.
            for (int i = 0; i < stage.roamRoutes.Length; i++)
                BuildGuard(actors.transform, stage.roamRoutes[i],
                    stage.guardRoutes.Length + i + 1, width, height, roam: true);

            for (int i = 0; i < stage.droneRoutes.Length; i++)
                BuildDrone(actors.transform, stage.droneRoutes[i], i + 1, width, height);

            var pickups = new GameObject("Objectives");
            pickups.transform.SetParent(root.transform, false);

            List<Vector3> walkable = CollectWalkableCells(stage, width, height);
            int objectiveIndex = 1;

            // 고액 자료(G)는 자리 자체가 설계라서 무한모드에서도 격자에 찍은 그대로 둔다.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (stage.grid[y][x] != 'G') continue;
                    BuildObjective(pickups.transform, GridToWorld(x, y, width, height), objectiveIndex++, stage.prizePoints);
                }
            }

            if (stage.endless)
            {
                // 무한모드는 일반 자료(O)의 표시를 쓰지 않는다. 시작 위치도 매 판 달라야 하므로
                // 걸을 수 있는 칸 중에서 서로 떨어진 자리를 골라 배치한다.
                foreach (Vector3 spot in PickSpreadSpots(walkable, playerSpawn, stage.endlessObjectiveCount))
                    BuildObjective(pickups.transform, spot, objectiveIndex++);
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (stage.grid[y][x] != 'O') continue;
                        BuildObjective(pickups.transform, GridToWorld(x, y, width, height), objectiveIndex++);
                    }
                }
            }

            BuildFloorDetail(root.transform, width, height);
            BuildMarkers(root.transform, stage, width, height);
            BuildVents(root.transform, stage, width, height);

            // 무한모드에는 탈출 조건이 없다.
            if (!stage.endless) BuildExit(root.transform, FindCellWorld(stage, 'E', width, height));

            // --- 3. 시스템 / 카메라 / 조명 ---
            BuildSystems(root.transform, stage, walkable);
            SetupCamera();
            SetupLight();
            SetupPostProcessing(root.transform);
            EnsureSceneRegistered();

            EditorSceneManager.MarkSceneDirty(scene);

            // 재시작(R/F5)과 스테이지 선택은 SceneManager.LoadScene 으로 디스크에서 씬을 다시 읽는다.
            // 저장하지 않으면 생성한 스테이지가 아니라 이전에 저장된 씬이 로드되므로 반드시 저장한다.
            if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = root;

            Debug.Log($"[StageBuilder] '{stage.displayName}' 생성 완료 — " +
                      $"{width}x{height} 셀, 경비 {stage.guardRoutes.Length}명, " +
                      $"자료 {objectiveIndex - 1}개.");

            return true;
        }

        // ------------------------------------------------------- 지오메트리

        static void BuildFloor(Transform parent, int width, int height)
        {
            // 텍스처(체커)를 쓰므로 색은 흰색으로 둔다.
            // URP 는 _BaseColor 와 텍스처를 곱하기 때문에, 예전처럼 어두운 색을 그대로 두면
            // 어두운 텍스처 × 어두운 색 = 거의 검정이 되어 무늬가 보이지 않는다.
            // 텍스처 자체가 이미 어두운 회색(약 0.18)이라 흰색을 곱하면 예전 밝기와 비슷해진다.
            var floor = CreateBox(parent, "Floor",
                new Vector3(0f, -0.1f, 0f),
                new Vector3(width * Cell, 0.2f, height * Cell),
                MaterialFor("Greybox_Floor", Color.white,
                            "Universal Render Pipeline/Lit", "dark_texture_08.png"));

            // 체커 한 칸이 격자 한 칸(2 m)과 정확히 맞도록 반복 횟수를 정한다.
            // 텍스처 한 장에 체커가 4×4 이므로 8 m 마다 한 장이면 체커 하나가 2 m 가 된다.
            SetTiling(floor, width * Cell / 8f, height * Cell / 8f);

            floor.isStatic = true;
        }

        /// <summary>
        /// 같은 문자가 가로로 이어지면 하나의 박스로 합친다.
        /// 오브젝트 수가 줄고 씬 하이어라키가 읽기 쉬워진다.
        /// </summary>
        /// <summary>
        /// 벽(#)과 선반(X)을 만든다.
        ///
        /// 칸마다 큐브를 하나씩 놓지 않고, 가로로 이어진 같은 문자를 한 덩어리로 묶어
        /// 긴 큐브 하나로 만든다(런 렝스 압축). 47칸짜리 벽이 큐브 47개가 아니라 1개가 되므로
        /// 오브젝트 수가 크게 줄고 렌더링도 빨라진다.
        /// </summary>
        static void BuildBlocks(Transform parent, StageData stage, int width, int height)
        {
            var wallParent = new GameObject("Walls").transform;
            wallParent.SetParent(parent, false);
            var shelfParent = new GameObject("Shelves").transform;
            shelfParent.SetParent(parent, false);

            Material wallMat = MaterialFor("Greybox_Wall", new Color(0.34f, 0.35f, 0.40f));
            Material shelfMat = MaterialFor("Greybox_Shelf", new Color(0.40f, 0.31f, 0.23f));

            int wallCount = 0;
            int shelfCount = 0;

            for (int y = 0; y < height; y++)
            {
                int x = 0;
                while (x < width)
                {
                    char c = stage.grid[y][x];
                    if (c != '#' && c != 'X') { x++; continue; }

                    // 같은 문자가 몇 칸이나 이어지는지 센다.
                    int runStart = x;
                    while (x < width && stage.grid[y][x] == c) x++;
                    int runEnd = x - 1;   // while 이 한 칸 더 갔으므로 하나 뺀다

                    bool isWall = c == '#';
                    float h = isWall ? WallHeight : ShelfHeight;
                    float length = (runEnd - runStart + 1) * Cell;   // 이어진 칸 수 × 칸 크기

                    // 덩어리의 한가운데 좌표. 시작과 끝의 평균을 GridToWorld 와 같은 식으로 변환한다.
                    float centerX = ((runStart + runEnd) * 0.5f - (width - 1) * 0.5f) * Cell;
                    float centerZ = ((height - 1) * 0.5f - y) * Cell;

                    var box = CreateBox(
                        isWall ? wallParent : shelfParent,
                        isWall ? $"Wall_{++wallCount}" : $"Shelf_{++shelfCount}",
                        new Vector3(centerX, h * 0.5f, centerZ),
                        new Vector3(length, h, Cell * (isWall ? 1f : 0.9f)),
                        isWall ? wallMat : shelfMat);
                    box.isStatic = true;

                    // 장식을 얹는다. 콜라이더가 없어서 이동·시야 판정은 그대로다.
                    if (isWall) DecorateWall(box.transform);
                    else DecorateShelf(box.transform);
                }
            }
        }

        /// <summary>
        /// 벽에 상단 몰딩과 허리 높이 띠를 붙인다.
        ///
        /// 좌표는 부모(벽 박스) 기준이라 -0.5 ~ +0.5 범위다. 부모가 아무리 길어도
        /// 같은 값이 그대로 통하기 때문에, 길이를 신경 쓰지 않고 붙일 수 있다.
        ///
        /// 살짝 튀어나오게(1.02배) 만들어야 보인다. 안쪽에 넣으면 부모에 파묻혀 안 보인다.
        /// 튀어나오는 양이 2cm 라 통로 폭에는 영향이 없다.
        /// </summary>
        static void DecorateWall(Transform wall)
        {
            Material cap = MaterialFor("Greybox_WallCap", new Color(0.22f, 0.23f, 0.28f));
            Material line = MaterialFor("Greybox_WallLine", new Color(0.45f, 0.47f, 0.54f));

            // 천장 쪽 몰딩
            var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.name = "Cap";
            top.transform.SetParent(wall, false);
            top.transform.localPosition = new Vector3(0f, 0.43f, 0f);
            top.transform.localScale = new Vector3(1.01f, 0.12f, 1.03f);
            StripCollider(top);
            top.GetComponent<Renderer>().sharedMaterial = cap;

            // 허리 높이 띠. 벽이 통짜로 보이지 않게 가로선을 하나 넣는 것이 핵심이다.
            var belt = GameObject.CreatePrimitive(PrimitiveType.Cube);
            belt.name = "Line";
            belt.transform.SetParent(wall, false);
            belt.transform.localPosition = new Vector3(0f, -0.10f, 0f);
            belt.transform.localScale = new Vector3(1.01f, 0.035f, 1.02f);
            StripCollider(belt);
            belt.GetComponent<Renderer>().sharedMaterial = line;

            // 바닥 걸레받이
            var skirt = GameObject.CreatePrimitive(PrimitiveType.Cube);
            skirt.name = "Skirt";
            skirt.transform.SetParent(wall, false);
            skirt.transform.localPosition = new Vector3(0f, -0.44f, 0f);
            skirt.transform.localScale = new Vector3(1.01f, 0.10f, 1.02f);
            StripCollider(skirt);
            skirt.GetComponent<Renderer>().sharedMaterial = cap;
        }

        /// <summary>
        /// 선반에 가로 판자와 양끝 기둥을 붙인다.
        /// 통짜 나무 상자였던 것이 '층이 있는 선반'으로 읽히게 만드는 것이 목적이다.
        /// </summary>
        static void DecorateShelf(Transform shelf)
        {
            Material board = MaterialFor("Greybox_ShelfBoard", new Color(0.55f, 0.43f, 0.29f));
            Material post = MaterialFor("Greybox_ShelfPost", new Color(0.24f, 0.18f, 0.13f));

            // 층 판자 세 장. 위에서 내려다보는 시점이라 맨 위 판이 제일 잘 보인다.
            foreach (float y in new[] { 0.46f, 0.08f, -0.30f })
            {
                var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plank.name = "Board";
                plank.transform.SetParent(shelf, false);
                plank.transform.localPosition = new Vector3(0f, y, 0f);
                plank.transform.localScale = new Vector3(1.005f, 0.055f, 1.06f);
                StripCollider(plank);
                plank.GetComponent<Renderer>().sharedMaterial = board;
            }

            // 양끝 기둥. localScale.x 가 1이면 부모 길이와 같으므로,
            // 얇게 만들어 좌우 끝에 붙이면 길이에 상관없이 항상 끝에 온다.
            for (int side = -1; side <= 1; side += 2)
            {
                var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leg.name = side < 0 ? "Post_L" : "Post_R";
                leg.transform.SetParent(shelf, false);
                leg.transform.localPosition = new Vector3(side * 0.485f, 0f, 0f);
                leg.transform.localScale = new Vector3(0.035f, 1.01f, 1.07f);
                StripCollider(leg);
                leg.GetComponent<Renderer>().sharedMaterial = post;
            }
        }

        /// <summary>
        /// 크롤 통로 위에 떠 있는 천장 블록. 벽에 난 구멍을 만드는 셈이다.
        /// NavMesh 베이크 대상이어야 경비가 이 길을 쓰지 못한다.
        /// </summary>
        static void BuildCrawlLintels(Transform parent, StageData stage, int width, int height)
        {
            Transform lintelParent = null;
            Material mat = MaterialFor("Greybox_Lintel", new Color(0.28f, 0.30f, 0.36f));

            int count = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (stage.grid[y][x] != 'c') continue;

                    if (lintelParent == null)
                    {
                        lintelParent = new GameObject("CrawlLintels").transform;
                        lintelParent.SetParent(parent, false);
                    }

                    Vector3 cell = GridToWorld(x, y, width, height);
                    float h = WallHeight - CrawlClearance;
                    var box = CreateBox(lintelParent, $"Lintel_{++count}",
                        new Vector3(cell.x, CrawlClearance + h * 0.5f, cell.z),
                        new Vector3(Cell, h, Cell),
                        mat);
                    box.isStatic = true;
                }
            }
        }

        /// <summary>
        /// 은신처를 이루는 창고 소품. 오크통과 나무 상자를 'ㄷ'자로 세워 한쪽만 트인 공간을 만든다.
        /// 열린 방향은 바닥으로 이어지는 이웃 칸 쪽으로 잡아 반드시 들어갈 수 있게 한다.
        ///
        /// NavMesh 베이크 대상에 포함시켜야 경비가 소품을 뚫고 지나가려다 끼지 않는다.
        /// </summary>
        static void BuildHideProps(Transform parent, StageData stage, int width, int height)
        {
            Transform propParent = null;
            Material barrelMat = MaterialFor("Prop_Barrel", new Color(0.33f, 0.20f, 0.14f));
            Material crateMat = MaterialFor("Prop_Crate", new Color(0.46f, 0.34f, 0.20f));

            int spotIndex = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (stage.grid[y][x] != 'h') continue;

                    if (propParent == null)
                    {
                        propParent = new GameObject("HideProps").transform;
                        propParent.SetParent(parent, false);
                    }

                    spotIndex++;
                    var cluster = new GameObject($"HideCluster {spotIndex}").transform;
                    cluster.SetParent(propParent, false);
                    cluster.position = GridToWorld(x, y, width, height);

                    int opening = FindOpenNeighbor(stage, x, y, width, height);

                    int placed = 0;
                    for (int i = 0; i < Neighbors.Length; i++)
                    {
                        if (i == opening) continue;
                        placed++;

                        var offset = new Vector3(Neighbors[i].x, 0f, -Neighbors[i].y) * PropRingRadius;
                        bool barrel = (spotIndex + i) % 2 == 0;
                        float h = PropHeight - placed * 0.03f;

                        GameObject prop;
                        if (barrel)
                        {
                            prop = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                            prop.name = $"Barrel {placed}";
                            prop.transform.localScale = new Vector3(0.54f, h * 0.5f, 0.54f);
                        }
                        else
                        {
                            prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            prop.name = $"Crate {placed}";
                            prop.transform.localScale = new Vector3(0.62f, h, 0.62f);
                        }

                        prop.transform.SetParent(cluster, false);
                        prop.transform.localPosition = offset + Vector3.up * (h * 0.5f);
                        prop.transform.localRotation = Quaternion.Euler(0f, placed * 17f, 0f);
                        prop.GetComponent<Renderer>().sharedMaterial = barrel ? barrelMat : crateMat;

                        // 장식을 얹는다. 오크통에는 쇠테, 상자에는 판자 보강대.
                        // 콜라이더는 떼어내므로 NavMesh 나 시야 판정에는 영향이 없다.
                        if (barrel) DecorateBarrel(prop.transform);
                        else DecorateCrate(prop.transform);
                        prop.isStatic = true;
                    }
                }
            }
        }

        /// <summary>
        /// 오크통에 쇠테 두 줄을 두른다. 민무늬 원기둥과 '통'을 가르는 것이 이 두 줄이다.
        /// 부모의 크기를 물려받으므로 좌표는 원기둥 원본 기준(높이 -1 ~ +1)이다.
        /// </summary>
        static void DecorateBarrel(Transform barrel)
        {
            Material band = MaterialFor("Prop_BarrelBand", new Color(0.22f, 0.22f, 0.25f));

            foreach (float y in new[] { 0.45f, -0.45f })
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "Band";
                ring.transform.SetParent(barrel, false);
                ring.transform.localPosition = new Vector3(0f, y, 0f);
                ring.transform.localScale = new Vector3(1.06f, 0.09f, 1.06f);
                StripCollider(ring);
                ring.GetComponent<Renderer>().sharedMaterial = band;
            }

            // 뚜껑. 윗면 색이 달라야 위에서 내려다보는 쿼터뷰에서 형태가 읽힌다.
            var lid = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lid.name = "Lid";
            lid.transform.SetParent(barrel, false);
            lid.transform.localPosition = new Vector3(0f, 0.98f, 0f);
            lid.transform.localScale = new Vector3(0.94f, 0.05f, 0.94f);
            StripCollider(lid);
            lid.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Prop_BarrelLid", new Color(0.26f, 0.16f, 0.11f));
        }

        /// <summary>
        /// 나무 상자에 모서리 판자와 윗면 십자 보강대를 붙인다.
        /// 단색 정육면체는 무엇인지 안 읽히는데, 선 몇 줄이면 바로 상자가 된다.
        /// </summary>
        static void DecorateCrate(Transform crate)
        {
            Material plank = MaterialFor("Prop_CratePlank", new Color(0.30f, 0.21f, 0.12f));

            // 위·아래 가로 띠
            foreach (float y in new[] { 0.34f, -0.34f })
            {
                var belt = GameObject.CreatePrimitive(PrimitiveType.Cube);
                belt.name = "Plank";
                belt.transform.SetParent(crate, false);
                belt.transform.localPosition = new Vector3(0f, y, 0f);
                belt.transform.localScale = new Vector3(1.04f, 0.12f, 1.04f);
                StripCollider(belt);
                belt.GetComponent<Renderer>().sharedMaterial = plank;
            }

            // 윗면 십자. 위에서 내려다보는 시점이라 여기가 제일 잘 보인다.
            for (int i = 0; i < 2; i++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "TopBar";
                bar.transform.SetParent(crate, false);
                bar.transform.localPosition = new Vector3(0f, 0.51f, 0f);
                bar.transform.localScale = i == 0
                    ? new Vector3(1.02f, 0.04f, 0.16f)
                    : new Vector3(0.16f, 0.04f, 1.02f);
                StripCollider(bar);
                bar.GetComponent<Renderer>().sharedMaterial = plank;
            }
        }

        /// <summary>들어갈 수 있는 방향(바닥으로 이어지는 이웃 칸)의 인덱스.</summary>
        static int FindOpenNeighbor(StageData stage, int x, int y, int width, int height)
        {
            for (int i = 0; i < Neighbors.Length; i++)
            {
                int nx = x + Neighbors[i].x;
                int ny = y + Neighbors[i].y;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                char c = stage.grid[ny][nx];
                if (c == '#' || c == 'X' || c == 'h') continue;
                return i;
            }

            Debug.LogWarning($"[StageBuilder] ({x},{y}) 은신처에 들어갈 방향이 없습니다. 레이아웃을 확인하세요.");
            return 2;
        }

        /// <summary>
        /// 바닥 타일 눈금. 통짜 회색 판이던 바닥에 격자선을 그어 넓이와 거리가 읽히게 한다.
        ///
        /// ★ 반드시 NavMesh 베이크가 끝난 뒤에 불러야 한다.
        ///   NavMeshSurface 가 RenderMeshes 모드라 콜라이더가 아니라 '보이는 메시'를 읽는다.
        ///   베이크 전에 만들면 이 얇은 판들이 지형으로 잡혀 바닥에 미세한 턱이 생기고,
        ///   경비가 그 턱에 걸려 길을 못 찾는다. StripCollider 로는 막을 수 없다.
        ///
        /// 칸마다 네모를 놓으면 47x25 = 1175개가 되므로, 줄 하나를 통으로 긋는다.
        /// 가로 26줄 + 세로 48줄 = 74개면 끝난다.
        /// </summary>
        static void BuildFloorDetail(Transform parent, int width, int height)
        {
            var root = new GameObject("FloorDetail").transform;
            root.SetParent(parent, false);

            // 바닥(0.17, 0.18, 0.21)과 충분히 차이가 나야 선으로 보인다.
            // 처음엔 0.23 으로 뒀는데 있는 줄도 모를 만큼 흐려서 올렸다.
            Material line = MaterialFor("Greybox_FloorLine", new Color(0.32f, 0.35f, 0.41f));

            float halfW = width * Cell * 0.5f;
            float halfH = height * Cell * 0.5f;
            const float y = 0.012f;      // 바닥(윗면 0)보다 아주 조금 위
            const float thickness = 0.07f;

            // 세로선 — 칸 경계마다 한 줄
            for (int x = 0; x <= width; x++)
            {
                float px = -halfW + x * Cell;
                CreateBox(root, $"GridZ_{x}", new Vector3(px, y, 0f),
                    new Vector3(thickness, 0.01f, height * Cell), line).name = $"GridZ_{x}";
            }

            // 가로선
            for (int z = 0; z <= height; z++)
            {
                float pz = -halfH + z * Cell;
                CreateBox(root, $"GridX_{z}", new Vector3(0f, y, pz),
                    new Vector3(width * Cell, 0.01f, thickness), line).name = $"GridX_{z}";
            }

            // 격자선에 콜라이더가 남아 있으면 동전이 튕기거나 레이캐스트에 걸린다.
            foreach (Transform child in root) StripCollider(child.gameObject);
        }

        /// <summary>
        /// 바닥 표시물: 크롤 통로 안내선과 은신 지점.
        /// NavMesh 베이크 뒤에 만들어 지형으로 잡히지 않게 한다.
        /// </summary>
        static void BuildMarkers(Transform parent, StageData stage, int width, int height)
        {
            Transform markers = null;
            int crawlCount = 0;
            int leverCount = 0;
            int hideCount = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    char c = stage.grid[y][x];
                    if (c != 'c' && c != 'h' && c != 'L') continue;

                    if (markers == null)
                    {
                        markers = new GameObject("Markers").transform;
                        markers.SetParent(parent, false);
                    }

                    Vector3 cell = GridToWorld(x, y, width, height);

                    if (c == 'L')
                    {
                        BuildPowerLever(markers, cell, ++leverCount);
                    }
                    else if (c == 'c')
                    {
                        BuildCrawlMarker(markers, cell, ++crawlCount);
                    }
                    else
                    {
                        // 소품이 트인 방향 = 들어가고 나오는 방향. 소품 배치와 같은 규칙을 쓴다.
                        int opening = FindOpenNeighbor(stage, x, y, width, height);
                        var exitDir = new Vector3(Neighbors[opening].x, 0f, -Neighbors[opening].y);
                        BuildHideSpot(markers, cell, ++hideCount, exitDir);
                    }
                }
            }
        }

        /// <summary>
        /// 환풍구. 그리드에서 같은 숫자('1'~'9')를 단 두 칸이 서로 이어진다.
        /// 숫자 하나당 정확히 두 칸이어야 하고, 그렇지 않으면 경고만 내고 건너뛴다.
        /// </summary>
        static void BuildVents(Transform parent, StageData stage, int width, int height)
        {
            var pairs = new Dictionary<char, List<Vector2Int>>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    char c = stage.grid[y][x];

                    // 문자는 내부적으로 숫자다. '1'~'9' 사이인지 이렇게 비교할 수 있다.
                    if (c < '1' || c > '9') continue;

                    // Dictionary = "열쇠 → 값" 표. 여기서는 숫자 문자 → 그 숫자가 찍힌 칸 목록.
                    // TryGetValue 는 있으면 꺼내고 없으면 false 를 돌려준다.
                    // 없을 때만 새 목록을 만들어 넣는다.
                    if (!pairs.TryGetValue(c, out List<Vector2Int> list))
                    {
                        list = new List<Vector2Int>();
                        pairs[c] = list;
                    }
                    list.Add(new Vector2Int(x, y));
                }
            }

            if (pairs.Count == 0) return;

            var root = new GameObject("Vents").transform;
            root.SetParent(parent, false);

            foreach (KeyValuePair<char, List<Vector2Int>> pair in pairs)
            {
                if (pair.Value.Count != 2)
                {
                    Debug.LogWarning($"[StageBuilder] 환풍구 '{pair.Key}' 가 {pair.Value.Count}칸입니다. " +
                                     "정확히 2칸이어야 짝이 됩니다.");
                    continue;
                }

                Vent a = BuildVent(root, GridToWorld(pair.Value[0].x, pair.Value[0].y, width, height), pair.Key, 'A');
                Vent b = BuildVent(root, GridToWorld(pair.Value[1].x, pair.Value[1].y, width, height), pair.Key, 'B');
                a.linked = b;
                b.linked = a;
            }
        }

        static Vent BuildVent(Transform parent, Vector3 position, char id, char side)
        {
            var vent = new GameObject($"Vent {id}{side}");
            vent.transform.SetParent(parent, false);
            vent.transform.position = position;

            // 바닥에 박힌 격자. 콜라이더가 없어서 순찰도 드론의 시선도 막지 않는다.
            var grate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            grate.name = "Grate";
            grate.transform.SetParent(vent.transform, false);
            grate.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            grate.transform.localScale = new Vector3(1.5f, 0.1f, 1.5f);
            StripCollider(grate);
            grate.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Vent_Grate", new Color(0.45f, 0.55f, 0.68f), "Universal Render Pipeline/Unlit");

            // 살창. 어두운 막대 네 개를 얹으면 '구멍'이 아니라 '환풍구'로 읽힌다.
            Material slat = MaterialFor("Vent_Slat", new Color(0.13f, 0.16f, 0.21f));
            for (int i = 0; i < 4; i++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = $"Slat {i + 1}";
                bar.transform.SetParent(vent.transform, false);
                // -0.45 부터 0.3 간격으로 네 줄
                bar.transform.localPosition = new Vector3(0f, 0.11f, -0.45f + i * 0.3f);
                bar.transform.localScale = new Vector3(1.3f, 0.05f, 0.13f);
                StripCollider(bar);
                bar.GetComponent<Renderer>().sharedMaterial = slat;
            }

            // 테두리 프레임 — 네 변에 얇은 막대를 둘러 금속판 느낌을 준다.
            for (int i = 0; i < 4; i++)
            {
                bool horizontal = i < 2;
                float sign = (i % 2 == 0) ? -1f : 1f;

                var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                edge.name = $"Frame {i + 1}";
                edge.transform.SetParent(vent.transform, false);
                edge.transform.localPosition = horizontal
                    ? new Vector3(0f, 0.11f, sign * 0.72f)
                    : new Vector3(sign * 0.72f, 0.11f, 0f);
                edge.transform.localScale = horizontal
                    ? new Vector3(1.5f, 0.06f, 0.12f)
                    : new Vector3(0.12f, 0.06f, 1.5f);
                StripCollider(edge);
                edge.GetComponent<Renderer>().sharedMaterial = slat;
            }

            var comp = vent.AddComponent<Vent>();
            comp.grateRenderer = grate.GetComponent<Renderer>();
            comp.displayName = $"환풍구 {id}";
            return comp;
        }

        /// <summary>
        /// 두꺼비집. 콜라이더를 달지 않아 순찰 경로를 막지 않는다.
        /// 벽에 붙은 패널이라고 보면 되고, 플레이어는 그 칸까지 걸어와 F를 누른다.
        /// </summary>
        static void BuildPowerLever(Transform parent, Vector3 position, int index)
        {
            var lever = new GameObject($"PowerLever {index}");
            lever.transform.SetParent(parent, false);
            lever.transform.position = position;

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Box";
            box.transform.SetParent(lever.transform, false);
            box.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            box.transform.localScale = new Vector3(0.7f, 1.1f, 0.35f);
            StripCollider(box);
            box.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Prop_LeverBox", new Color(0.26f, 0.28f, 0.33f));

            var handle = new GameObject("Handle");
            handle.transform.SetParent(lever.transform, false);
            handle.transform.localPosition = new Vector3(0f, 0.95f, -0.2f);

            var arm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arm.name = "Arm";
            arm.transform.SetParent(handle.transform, false);
            arm.transform.localPosition = new Vector3(0f, 0f, 0.18f);
            arm.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            arm.transform.localScale = new Vector3(0.09f, 0.2f, 0.09f);
            StripCollider(arm);
            arm.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Prop_LeverArm", new Color(0.75f, 0.68f, 0.25f), "Universal Render Pipeline/Unlit");

            // 배전반 앞판 — 본체보다 밝게 해서 '뚜껑이 달린 상자'로 보이게 한다.
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(lever.transform, false);
            panel.transform.localPosition = new Vector3(0f, 0.55f, -0.19f);
            panel.transform.localScale = new Vector3(0.56f, 0.9f, 0.04f);
            StripCollider(panel);
            panel.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Prop_LeverPanel", new Color(0.38f, 0.41f, 0.47f));

            // 경고 줄무늬. 노랑·검정 사선은 "함부로 만지면 안 되는 것"의 만국 공통 표시다.
            Material warn = MaterialFor("Prop_LeverWarn", new Color(0.92f, 0.76f, 0.18f));
            for (int i = 0; i < 3; i++)
            {
                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = $"Warn {i + 1}";
                stripe.transform.SetParent(lever.transform, false);
                stripe.transform.localPosition = new Vector3(-0.18f + i * 0.18f, 0.22f, -0.21f);
                stripe.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
                stripe.transform.localScale = new Vector3(0.07f, 0.26f, 0.03f);
                StripCollider(stripe);
                stripe.GetComponent<Renderer>().sharedMaterial = warn;
            }

            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lamp.name = "StatusLamp";
            lamp.transform.SetParent(lever.transform, false);
            lamp.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            lamp.transform.localScale = Vector3.one * 0.2f;
            StripCollider(lamp);
            lamp.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Prop_LeverLamp", Color.white, "Universal Render Pipeline/Unlit");

            var comp = lever.AddComponent<PowerLever>();
            comp.handle = handle.transform;
            comp.statusRenderer = lamp.GetComponent<Renderer>();
        }

        /// <summary>
        /// 크롤 통로 바닥 표시. 벽에 뚫린 낮은 구멍이라는 것이 한눈에 읽혀야 한다.
        /// 여기는 NavMesh 베이크가 끝난 뒤(BuildMarkers)라 자유롭게 만들어도 된다.
        /// </summary>
        static void BuildCrawlMarker(Transform parent, Vector3 position, int index)
        {
            var group = new GameObject($"CrawlGap {index}").transform;
            group.SetParent(parent, false);
            group.position = new Vector3(position.x, 0f, position.z);

            var patch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            patch.name = "Patch";
            patch.transform.SetParent(group, false);
            patch.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            patch.transform.localScale = new Vector3(Cell * 0.85f, 0.02f, Cell * 0.85f);
            StripCollider(patch);
            patch.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Marker_Crawl", new Color(0.35f, 0.8f, 1f, 0.28f), "Stealth/FOVCone");

            Material stripe = MaterialFor("Marker_CrawlStripe", new Color(0.55f, 0.9f, 1f),
                "Universal Render Pipeline/Unlit");

            // 통로 양옆 바닥에 짧은 유도선. "이 축으로 지나간다"를 알려준다.
            for (int side = -1; side <= 1; side += 2)
            {
                var guide = GameObject.CreatePrimitive(PrimitiveType.Cube);
                guide.name = side < 0 ? "Guide_L" : "Guide_R";
                guide.transform.SetParent(group, false);
                guide.transform.localPosition = new Vector3(side * Cell * 0.42f, 0.04f, 0f);
                guide.transform.localScale = new Vector3(0.10f, 0.02f, Cell * 0.80f);
                StripCollider(guide);
                guide.GetComponent<Renderer>().sharedMaterial = stripe;
            }

            // 상인방 아래쪽에 매달린 경고 띠. 서서 들어가면 막힌다는 신호다.
            var lip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lip.name = "ClearanceBar";
            lip.transform.SetParent(group, false);
            lip.transform.localPosition = new Vector3(0f, CrawlClearance - 0.06f, 0f);
            lip.transform.localScale = new Vector3(Cell * 0.92f, 0.10f, Cell * 0.92f);
            StripCollider(lip);
            lip.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Marker_CrawlLip", new Color(0.92f, 0.76f, 0.18f));
        }

        static void BuildHideSpot(Transform parent, Vector3 position, int index, Vector3 exitDirection)
        {
            var spot = new GameObject($"HideSpot {index}");
            spot.transform.SetParent(parent, false);
            spot.transform.position = position;

            // 소품 안쪽 바닥에만 작게 깔린다. 가까이 가기 전까지는 알파 0이라 보이지 않는다.
            var patch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            patch.name = "Patch";
            patch.transform.SetParent(spot.transform, false);
            patch.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            patch.transform.localScale = new Vector3(1.0f, 0.02f, 1.0f);
            StripCollider(patch);
            patch.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Marker_HideSpot", new Color(0.35f, 0.75f, 1f, 0f), "Stealth/FOVCone");

            // ↓ 아래는 장식이다. patch 만 HideSpot 이 색을 바꾸므로 나머지는 항상 같은 색이다.

            // 통 더미 사이에 깔린 낡은 자루 — 숨을 자리가 실제로 있다는 인상을 준다.
            var sack = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sack.name = "Sack";
            sack.transform.SetParent(spot.transform, false);
            sack.transform.localPosition = new Vector3(0f, 0.16f, 0f);
            sack.transform.localScale = new Vector3(0.78f, 0.32f, 0.62f);
            sack.transform.localRotation = Quaternion.Euler(0f, 22f, 0f);
            StripCollider(sack);
            sack.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Prop_Sack", new Color(0.36f, 0.31f, 0.22f));

            // 들어가는 방향에 깔린 발판. 어느 쪽이 입구인지 바닥만 봐도 알 수 있다.
            // exitDirection 은 소품이 트인 방향이므로 그대로 쓰면 된다.
            var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.name = "Entry";
            step.transform.SetParent(spot.transform, false);
            step.transform.localPosition = exitDirection.normalized * 0.95f + Vector3.up * 0.035f;
            step.transform.localRotation = Quaternion.LookRotation(exitDirection.normalized, Vector3.up);
            step.transform.localScale = new Vector3(0.95f, 0.02f, 0.34f);
            StripCollider(step);
            step.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Marker_HideEntry", new Color(0.45f, 0.85f, 1f, 0.35f), "Stealth/FOVCone");

            var comp = spot.AddComponent<HideSpot>();
            comp.interactRange = 1.5f;
            comp.exitDirection = exitDirection;
            comp.patchRenderer = patch.GetComponent<Renderer>();
        }

        // ------------------------------------------------------------ 액터

        /// <summary>
        /// 플레이어를 만든다. 프리팹을 쓰지 않고 코드로 조립하는 이유는
        /// 스테이지마다 동전 던지기 유무가 달라지고, 프리팹을 고치면 전 스테이지를
        /// 다시 열어 확인해야 하는데 이 방식은 메뉴 한 번이면 전부 반영되기 때문이다.
        ///
        /// 구조:
        ///   Player                  ← CharacterController + PlayerController
        ///    ├ Body                 ← 보이는 캡슐 (콜라이더 제거)
        ///    ├ FacingMarker         ← 바라보는 방향을 알려주는 코
        ///    └ BlackoutLight        ← 정전 중에만 켜지는 조명
        /// </summary>
        static void BuildPlayer(Transform parent, Vector3 position, bool allowCoin)
        {
            var player = new GameObject("Player");
            player.transform.SetParent(parent, false);
            player.transform.position = position;

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 45f;
            cc.stepOffset = 0.3f;

            // 도둑. 짙은 남색 상하의에 흰 줄무늬, 눈만 내놓은 검은 복면.
            // 너무 어두우면 어두운 바닥에 묻혀서 눈이 없으면 못 찾는다. 도둑 느낌은 유지하되
            // 바닥(0.17)보다는 확실히 밝은 값으로 잡았다.
            Material mat = MaterialFor("Actor_Player", new Color(0.26f, 0.30f, 0.48f));

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(player.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            StripCollider(body);
            body.GetComponent<Renderer>().sharedMaterial = mat;

            // 장식은 전부 몸통의 자식으로 붙인다. 웅크릴 때 몸통이 납작해지면 얼굴도 같이
            // 눌려서 "엎드린" 실루엣이 된다. 몸통 밖에 두면 얼굴만 공중에 떠버린다.
            BuildFaceBand(body.transform, "Mask", 0.62f, 0.26f,
                MaterialFor("Actor_PlayerMask", new Color(0.09f, 0.10f, 0.14f)));
            BuildEyes(body.transform, 0.63f, new Color(0.95f, 0.97f, 1f));

            // 가슴 줄무늬 — 캡슐 사람 스타일의 핵심 요소다. 이것만으로 옷을 입은 것처럼 보인다.
            BuildFaceBand(body.transform, "Stripe", 0.05f, 0.16f,
                MaterialFor("Actor_PlayerStripe", new Color(0.86f, 0.89f, 0.94f)));

            // 등에 멘 가방. 뒤쪽에서 봐도 도둑이라는 것이 읽힌다.
            var pack = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pack.name = "Backpack";
            pack.transform.SetParent(body.transform, false);
            pack.transform.localPosition = new Vector3(0f, 0.18f, -0.52f);
            pack.transform.localScale = new Vector3(0.52f, 0.42f, 0.30f);
            StripCollider(pack);
            pack.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Actor_PlayerPack", new Color(0.13f, 0.14f, 0.19f));

            // 방향 표시. 얼굴이 생겼으므로 눈에 덜 띄게 줄이고 몸 색에 맞춘다.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "FacingMarker";
            nose.transform.SetParent(player.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.25f, 0.42f);
            nose.transform.localScale = new Vector3(0.10f, 0.10f, 0.34f);
            StripCollider(nose);
            nose.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Actor_PlayerStripe", new Color(0.86f, 0.89f, 0.94f));

            // 발밑 링. 쿼터뷰에서 내 캐릭터를 놓치지 않게 하는 가장 싼 장치다.
            // 몸통이 아니라 Player 루트의 자식이라 웅크려도 바닥에 그대로 남는다.
            var footprint = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            footprint.name = "Footprint";
            footprint.transform.SetParent(player.transform, false);
            footprint.transform.localPosition = new Vector3(0f, 0.025f, 0f);
            footprint.transform.localScale = new Vector3(1.25f, 0.01f, 1.25f);
            StripCollider(footprint);
            footprint.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Actor_PlayerRing", new Color(0.45f, 0.85f, 1f, 0.30f), "Stealth/FOVCone");

            // 정전 중 플레이어 주변만 비추는 조명. 평소에는 꺼져 있다.
            var lightObject = new GameObject("BlackoutLight");
            lightObject.transform.SetParent(player.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 2.2f, 0f);

            var pointLight = lightObject.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = new Color(0.85f, 0.92f, 1f);
            pointLight.range = 7f;
            pointLight.intensity = 0f;
            pointLight.shadows = LightShadows.None;
            pointLight.enabled = false;

            // 컴포넌트를 붙이고 인스펙터에서 꽂아줄 참조를 코드로 연결한다.
            // 씬을 다시 만들어도 연결이 끊기지 않는 것이 이 방식의 장점이다.
            var controller = player.AddComponent<PlayerController>();
            controller.body = body.transform;
            controller.facingMarker = nose.transform;

            // 기능은 스테이지마다 하나씩 열린다. 동전은 2스테이지부터.
            if (allowCoin) BuildCoinThrow(player, parent);
        }

        /// <summary>
        /// 던질 동전 하나와 커서 착지 표시.
        /// 동전은 매번 만들지 않고 이 하나를 재사용한다.
        /// </summary>
        static void BuildCoinThrow(GameObject player, Transform stageParent)
        {
            var coinObject = new GameObject("Coin");
            coinObject.transform.SetParent(stageParent, false);

            var coinVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            coinVisual.name = "Visual";
            coinVisual.transform.SetParent(coinObject.transform, false);
            coinVisual.transform.localScale = new Vector3(0.24f, 0.03f, 0.24f);
            StripCollider(coinVisual);
            coinVisual.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Prop_Coin", new Color(1f, 0.82f, 0.28f), "Universal Render Pipeline/Unlit");

            // 실제 물리로 날아가 벽에 튕긴다. 콜라이더는 시각용 원반보다 조금 크게 잡아
            // 얇은 원반이 벽 모서리를 스쳐 지나가는 일이 없게 한다.
            var hitbox = coinObject.AddComponent<SphereCollider>();
            hitbox.radius = 0.14f;

            var rb = coinObject.AddComponent<Rigidbody>();
            rb.mass = 0.05f;
            // 항력을 0으로 둬야 조준점 계산(포물선 역산)이 실제 낙하 지점과 일치한다.
            // 멈추는 것은 바닥 마찰과 반발 손실이 맡는다.
            rb.linearDamping = 0f;
            rb.angularDamping = 0.6f;
            // 빠른 동전이 얇은 벽을 통과해버리지 않도록.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.isKinematic = true;

            var coin = coinObject.AddComponent<Coin>();

            var aim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            aim.name = "CoinAimMarker";
            aim.transform.SetParent(stageParent, false);
            aim.transform.localScale = new Vector3(1.5f, 0.02f, 1.5f);
            StripCollider(aim);
            aim.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Marker_CoinAim", new Color(1f, 0.85f, 0.3f, 0.45f), "Stealth/FOVCone");
            aim.SetActive(false);

            var throwAbility = player.AddComponent<CoinThrow>();
            throwAbility.coin = coin;
            throwAbility.aimMarker = aim.transform;
            throwAbility.aimRenderer = aim.GetComponent<Renderer>();
        }

        /// <summary>
        /// 캡슐 몸통을 가로로 두르는 띠. 복면·줄무늬·벨트를 전부 이걸로 만든다.
        ///
        /// 캡슐 몸통의 자식으로 붙이므로 좌표가 부모 기준이다. 캡슐 원본의 높이가 2라
        /// localY 는 대략 -1(발) ~ +1(머리 끝) 범위를 쓴다.
        /// </summary>
        /// <param name="localY">몸통 기준 높이. 0.6 쯤이 얼굴, 0 이 가슴, -0.1 이 허리.</param>
        /// <param name="thickness">띠의 두께.</param>
        static void BuildFaceBand(Transform body, string name, float localY, float thickness, Material material)
        {
            // 실린더를 아주 납작하게 눌러 띠로 쓴다. 캡슐보다 아주 조금 굵게 해서 파묻히지 않게 한다.
            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            band.name = name;
            band.transform.SetParent(body, false);
            band.transform.localPosition = new Vector3(0f, localY, 0f);
            band.transform.localScale = new Vector3(1.02f, thickness * 0.5f, 1.02f);
            StripCollider(band);
            band.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>
        /// 눈 두 개. 이것 하나로 캡슐이 사람으로 읽힌다 — 가장 비용 대비 효과가 큰 장식이다.
        /// 앞쪽(+Z)에 붙이므로 바라보는 방향도 같이 알려준다.
        /// </summary>
        static void BuildEyes(Transform body, float localY, Color color)
        {
            Material mat = MaterialFor($"Actor_Eye_{ColorUtility.ToHtmlStringRGB(color)}", color,
                "Universal Render Pipeline/Unlit");

            // 좌우 한 쌍. -1 과 +1 을 곱해 대칭으로 놓는다.
            for (int side = -1; side <= 1; side += 2)
            {
                var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eye.name = side < 0 ? "Eye_L" : "Eye_R";
                eye.transform.SetParent(body, false);
                eye.transform.localPosition = new Vector3(side * 0.24f, localY, 0.42f);
                eye.transform.localScale = new Vector3(0.20f, 0.16f, 0.20f);
                StripCollider(eye);
                eye.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        static void BuildGuard(Transform parent, Vector2Int[] route, int index, int width, int height,
            bool roam = false)
        {
            // 배회형은 route 두 점이 사각형의 마주보는 모서리다. 구역 한가운데에서 시작한다.
            Vector3 spawn = roam
                ? Vector3.Lerp(GridToWorld(route[0].x, route[0].y, width, height),
                               GridToWorld(route[1].x, route[1].y, width, height), 0.5f)
                : GridToWorld(route[0].x, route[0].y, width, height);

            var guard = new GameObject(roam ? $"Guard {index} (배회)" : $"Guard {index}");
            guard.transform.SetParent(parent, false);
            guard.transform.position = spawn;

            var capsule = guard.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.35f;
            capsule.center = new Vector3(0f, 0.9f, 0f);

            var agent = guard.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 1.8f;
            agent.baseOffset = 0f;
            agent.acceleration = 20f;
            agent.angularSpeed = 0f;
            agent.stoppingDistance = 0.15f;
            agent.autoBraking = true;

            // 경찰. 남색 제복에 하늘색 견장 줄무늬, 챙 달린 정모.
            // 도둑(짙은 남색 + 흰 줄)과 색이 비슷하지만 모자와 줄 색으로 구분된다.
            Material mat = MaterialFor("Actor_Guard", new Color(0.20f, 0.27f, 0.47f));

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(guard.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            StripCollider(body);
            body.GetComponent<Renderer>().sharedMaterial = mat;

            Material trim = MaterialFor("Actor_GuardTrim", new Color(0.62f, 0.84f, 1f));

            // 얼굴. 경비는 웅크리지 않으므로 위치 계산이 단순하다.
            BuildFaceBand(body.transform, "Face", 0.60f, 0.24f,
                MaterialFor("Actor_GuardSkin", new Color(0.93f, 0.80f, 0.68f)));
            BuildEyes(body.transform, 0.61f, new Color(0.12f, 0.13f, 0.17f));

            // 어깨 줄무늬 — 제복 느낌을 내는 가장 싼 방법.
            BuildFaceBand(body.transform, "Trim", 0.30f, 0.09f, trim);
            BuildFaceBand(body.transform, "Belt", -0.06f, 0.12f,
                MaterialFor("Actor_GuardBelt", new Color(0.10f, 0.11f, 0.15f)));

            // 정모 — 눌러 놓은 구 + 앞챙. 이 둘만으로 '경찰'이 바로 읽힌다.
            var capTop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            capTop.name = "CapTop";
            capTop.transform.SetParent(body.transform, false);
            capTop.transform.localPosition = new Vector3(0f, 0.78f, 0f);
            capTop.transform.localScale = new Vector3(1.03f, 0.42f, 1.03f);
            StripCollider(capTop);
            capTop.GetComponent<Renderer>().sharedMaterial = mat;

            var capBrim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            capBrim.name = "CapBrim";
            capBrim.transform.SetParent(body.transform, false);
            capBrim.transform.localPosition = new Vector3(0f, 0.74f, 0.42f);
            capBrim.transform.localScale = new Vector3(0.76f, 0.06f, 0.42f);
            StripCollider(capBrim);
            capBrim.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Actor_GuardBelt", new Color(0.10f, 0.11f, 0.15f));

            var capBadge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            capBadge.name = "CapBadge";
            capBadge.transform.SetParent(body.transform, false);
            capBadge.transform.localPosition = new Vector3(0f, 0.80f, 0.40f);
            capBadge.transform.localScale = new Vector3(0.20f, 0.14f, 0.12f);
            StripCollider(capBadge);
            capBadge.GetComponent<Renderer>().sharedMaterial = trim;

            // 방향 표시. 모자챙이 앞을 알려주므로 작게 줄인다.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "FacingMarker";
            nose.transform.SetParent(guard.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.25f, 0.42f);
            nose.transform.localScale = new Vector3(0.10f, 0.10f, 0.30f);
            StripCollider(nose);
            nose.GetComponent<Renderer>().sharedMaterial = trim;

            var indicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
            indicator.name = "StateIndicator";
            indicator.transform.SetParent(guard.transform, false);
            indicator.transform.localPosition = new Vector3(0f, 2.35f, 0f);
            indicator.transform.localScale = Vector3.one * 0.3f;
            StripCollider(indicator);
            indicator.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Actor_Indicator", Color.white, "Universal Render Pipeline/Unlit");

            guard.AddComponent<GuardVision>();

            // 순찰 지점
            var routeRoot = new GameObject($"Guard {index} Route");
            routeRoot.transform.SetParent(parent, false);

            var waypoints = new Transform[route.Length];
            for (int i = 0; i < route.Length; i++)
            {
                var wp = new GameObject($"WP {i + 1}");
                wp.transform.SetParent(routeRoot.transform, false);
                wp.transform.position = GridToWorld(route[i].x, route[i].y, width, height);
                waypoints[i] = wp.transform;
            }

            // 각 지점은 다음 지점을 바라보게 해서, 대기 중 시선이 자연스럽게 이어지도록
            for (int i = 0; i < waypoints.Length; i++)
            {
                Vector3 dir = waypoints[(i + 1) % waypoints.Length].position - waypoints[i].position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                    waypoints[i].rotation = Quaternion.LookRotation(dir, Vector3.up);
            }

            guard.transform.rotation = waypoints[0].rotation;

            var ai = guard.AddComponent<GuardAI>();
            ai.waypoints = waypoints;
            ai.indicator = indicator.transform;

            if (roam)
            {
                // 두 모서리에서 사각형을 만든다. 어느 쪽이 좌상단인지 신경 쓰지 않아도 되도록
                // 중심과 크기로 바꿔서 넘긴다.
                Vector3 a = waypoints[0].position;
                Vector3 b = waypoints[1].position;

                ai.roam = true;
                ai.roamCenter = (a + b) * 0.5f;
                ai.roamSize = new Vector3(Mathf.Abs(a.x - b.x), 0f, Mathf.Abs(a.z - b.z));
            }
        }

        const float DroneAltitude = 4.5f;
        const float DroneScanRadius = 4f;

        /// <summary>
        /// 공중 순찰 드론. NavMesh 를 쓰지 않고 웨이포인트 사이를 직선으로 난다.
        /// 바닥에 비추는 원은 드론의 자식이라, 드론이 움직이면 같이 따라간다.
        /// </summary>
        static void BuildDrone(Transform parent, Vector2Int[] route, int index, int width, int height)
        {
            Vector3 spawn = GridToWorld(route[0].x, route[0].y, width, height);
            spawn.y = DroneAltitude;

            var drone = new GameObject($"Drone {index}");
            drone.transform.SetParent(parent, false);
            drone.transform.position = spawn;

            Material mat = MaterialFor("Actor_Drone", new Color(0.26f, 0.32f, 0.44f));
            Material dark = MaterialFor("Actor_DroneDark", new Color(0.12f, 0.14f, 0.19f));

            // 동체를 납작한 구로 만들면 큐브보다 '기계'보다는 '비행체'로 읽힌다.
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Body";
            body.transform.SetParent(drone.transform, false);
            body.transform.localScale = new Vector3(0.68f, 0.34f, 0.68f);
            StripCollider(body);
            body.GetComponent<Renderer>().sharedMaterial = mat;

            // 쿼드콥터의 팔 네 개와 로터. 대각선 네 방향에 같은 것을 놓는다.
            // 회전은 하지 않는다 — 돌리려면 컴포넌트가 필요한데, 쿼터뷰에서는 멀어서 티가 안 난다.
            for (int i = 0; i < 4; i++)
            {
                float angle = 45f + i * 90f;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arm.name = $"Arm {i + 1}";
                arm.transform.SetParent(drone.transform, false);
                arm.transform.localPosition = dir * 0.34f;
                arm.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
                arm.transform.localScale = new Vector3(0.10f, 0.07f, 0.52f);
                StripCollider(arm);
                arm.GetComponent<Renderer>().sharedMaterial = dark;

                var rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                rotor.name = $"Rotor {i + 1}";
                rotor.transform.SetParent(drone.transform, false);
                rotor.transform.localPosition = dir * 0.56f + Vector3.up * 0.06f;
                rotor.transform.localScale = new Vector3(0.40f, 0.012f, 0.40f);
                StripCollider(rotor);
                rotor.GetComponent<Renderer>().sharedMaterial =
                    MaterialFor("Actor_DroneRotor", new Color(0.55f, 0.60f, 0.68f, 0.55f), "Stealth/FOVCone");
            }

            // 위쪽 경광등. 빨간 점 하나로 '감시 중'이라는 인상이 확 올라간다.
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            beacon.name = "Beacon";
            beacon.transform.SetParent(drone.transform, false);
            beacon.transform.localPosition = new Vector3(0f, 0.17f, 0f);
            beacon.transform.localScale = Vector3.one * 0.16f;
            StripCollider(beacon);
            beacon.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Actor_DroneBeacon", new Color(1f, 0.30f, 0.28f), "Universal Render Pipeline/Unlit");

            // 아래를 향한 렌즈. 드론이 무엇을 보고 있는지 한눈에 알 수 있게 한다.
            // 검은 하우징으로 감싸면 카메라처럼 보인다.
            var housing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            housing.name = "LensHousing";
            housing.transform.SetParent(drone.transform, false);
            housing.transform.localPosition = new Vector3(0f, -0.14f, 0f);
            housing.transform.localScale = new Vector3(0.36f, 0.07f, 0.36f);
            StripCollider(housing);
            housing.GetComponent<Renderer>().sharedMaterial = dark;

            var lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lens.name = "Lens";
            lens.transform.SetParent(drone.transform, false);
            lens.transform.localPosition = new Vector3(0f, -0.22f, 0f);
            lens.transform.localScale = Vector3.one * 0.26f;
            StripCollider(lens);
            lens.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Actor_DroneLens", new Color(0.55f, 0.85f, 1f), "Universal Render Pipeline/Unlit");

            // 바닥에 깔리는 탐지 원. 드론은 회전하지 않으므로 로컬 오프셋으로 충분하다.
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "ScanDisc";
            disc.transform.SetParent(drone.transform, false);
            disc.transform.localPosition = new Vector3(0f, -DroneAltitude + 0.05f, 0f);
            disc.transform.localScale = new Vector3(DroneScanRadius * 2f, 0.02f, DroneScanRadius * 2f);
            StripCollider(disc);
            disc.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Drone_Scan", new Color(0.45f, 0.75f, 1f, 0.20f), "Stealth/FOVCone");

            var routeRoot = new GameObject($"Drone {index} Route");
            routeRoot.transform.SetParent(parent, false);

            var waypoints = new Transform[route.Length];
            for (int i = 0; i < route.Length; i++)
            {
                var wp = new GameObject($"WP {i + 1}");
                wp.transform.SetParent(routeRoot.transform, false);
                wp.transform.position = GridToWorld(route[i].x, route[i].y, width, height);
                waypoints[i] = wp.transform;
            }

            var ai = drone.AddComponent<SurveillanceDrone>();
            ai.waypoints = waypoints;
            ai.scanRadius = DroneScanRadius;
            ai.discRenderer = disc.GetComponent<Renderer>();
        }

        /// <summary>
        /// 기밀 자료. points 가 1보다 크면 "고액 자료"로 취급해서 크고 밝게 만들고,
        /// 무한모드에서도 무작위로 옮기지 않고 제자리에서 되살아나게 한다.
        /// 자리 자체가 위험하다는 것이 그 자료의 가치이기 때문이다.
        /// </summary>
        static void BuildObjective(Transform parent, Vector3 position, int index, int points = 1)
        {
            bool prize = points > 1;

            var objective = new GameObject(prize ? $"Objective {index} (고액)" : $"Objective {index}");
            objective.transform.SetParent(parent, false);
            objective.transform.position = position;

            // Visual 은 Objective 가 회전·상하 운동을 시키는 대상이다. 장식은 전부 이 아래에 붙여야
            // 같이 돈다. 바깥에 두면 서류만 돌고 테두리는 가만히 있는 이상한 그림이 된다.
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(objective.transform, false);
            visual.transform.localPosition = new Vector3(0f, prize ? 1.0f : 0.8f, 0f);
            visual.transform.localScale = Vector3.one * (prize ? 0.75f : 0.45f);
            visual.transform.localRotation = Quaternion.Euler(25f, 0f, 25f);
            StripCollider(visual);

            Color coreColor = prize ? new Color(0.45f, 1f, 0.95f) : new Color(1f, 0.82f, 0.25f);

            visual.GetComponent<Renderer>().sharedMaterial = prize
                ? MaterialFor("Objective_Prize", coreColor, "Universal Render Pipeline/Unlit")
                : MaterialFor("Objective_Gold", coreColor, "Universal Render Pipeline/Unlit");

            // 서류철 느낌 — 정육면체 옆구리에 종이 묶음처럼 얇은 판을 겹쳐 붙인다.
            Material paper = MaterialFor("Objective_Paper", new Color(0.96f, 0.95f, 0.90f),
                "Universal Render Pipeline/Unlit");
            for (int i = 0; i < 3; i++)
            {
                var sheet = GameObject.CreatePrimitive(PrimitiveType.Cube);
                sheet.name = $"Sheet {i + 1}";
                sheet.transform.SetParent(visual.transform, false);
                sheet.transform.localPosition = new Vector3(0f, -0.22f + i * 0.22f, 0f);
                sheet.transform.localScale = new Vector3(1.08f, 0.10f, 1.08f);
                StripCollider(sheet);
                sheet.GetComponent<Renderer>().sharedMaterial = paper;
            }

            // 봉인 띠. 고액 자료는 붉은 '1급 기밀' 띠를 둘러 한눈에 구분되게 한다.
            var seal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seal.name = "Seal";
            seal.transform.SetParent(visual.transform, false);
            seal.transform.localScale = new Vector3(1.12f, 0.30f, 0.18f);
            StripCollider(seal);
            seal.GetComponent<Renderer>().sharedMaterial = prize
                ? MaterialFor("Objective_SealPrize", new Color(1f, 0.28f, 0.34f),
                    "Universal Render Pipeline/Unlit")
                : MaterialFor("Objective_Seal", new Color(0.80f, 0.52f, 0.10f),
                    "Universal Render Pipeline/Unlit");

            // 바닥에 떨어지는 빛무리. 어디에 있는지 멀리서도 눈에 띈다.
            var pool = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pool.name = "LightPool";
            pool.transform.SetParent(objective.transform, false);
            pool.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            float poolSize = prize ? 2.6f : 1.5f;
            pool.transform.localScale = new Vector3(poolSize, 0.01f, poolSize);
            StripCollider(pool);
            pool.GetComponent<Renderer>().sharedMaterial = MaterialFor(
                prize ? "Objective_PoolPrize" : "Objective_Pool",
                new Color(coreColor.r, coreColor.g, coreColor.b, prize ? 0.22f : 0.16f),
                "Stealth/FOVCone");

            // 고액 자료는 멀리서도 보이도록 스스로 빛난다. 중앙까지 갈 이유가 눈에 보여야 한다.
            if (prize)
            {
                var glow = new GameObject("Glow");
                glow.transform.SetParent(objective.transform, false);
                glow.transform.localPosition = new Vector3(0f, 1.0f, 0f);

                var light = glow.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.45f, 1f, 0.95f);
                light.range = 9f;
                light.intensity = 2.6f;
                light.shadows = LightShadows.None;
            }

            // 쿨다운을 바닥에 원형으로 표시한다. 자료가 놓인 자리에 직접 보여야 읽힌다.
            if (prize)
            {
                var dial = new GameObject("CooldownDial");
                dial.transform.SetParent(objective.transform, false);
                dial.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                dial.AddComponent<MeshFilter>();
                dial.AddComponent<MeshRenderer>();
                dial.AddComponent<CooldownDial>();
            }

            var comp = objective.AddComponent<Objective>();
            comp.visual = visual.transform;
            comp.displayName = prize ? "1급 기밀" : "기밀 자료";
            comp.points = points;
            comp.fixedSpawn = prize;
            comp.holdDuration = prize ? 2.2f : 1.2f;
            comp.respawnDelay = prize ? 60f : 0f;
        }

        static void BuildExit(Transform parent, Vector3 position)
        {
            var exit = new GameObject("ExitZone");
            exit.transform.SetParent(parent, false);
            exit.transform.position = position;

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            disc.transform.SetParent(exit.transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            disc.transform.localScale = new Vector3(4.4f, 0.02f, 4.4f);
            StripCollider(disc);

            var mat = MaterialFor("Exit_Zone", new Color(0.35f, 1f, 0.6f, 0.35f), "Stealth/FOVCone");
            disc.GetComponent<Renderer>().sharedMaterial = mat;

            // 이 아래는 전부 장식이다. ExitZone 은 disc 의 색만 바꾸므로 나머지는 건드리지 않는다.
            Material frame = MaterialFor("Exit_Frame", new Color(0.35f, 1f, 0.6f),
                "Universal Render Pipeline/Unlit");
            Material post = MaterialFor("Exit_Post", new Color(0.18f, 0.22f, 0.26f));

            // 네 귀퉁이 기둥 + 꼭대기 등. "여기가 출구"라고 멀리서도 알려주는 이정표.
            for (int i = 0; i < 4; i++)
            {
                float angle = 45f + i * 90f;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = $"Post {i + 1}";
                pillar.transform.SetParent(exit.transform, false);
                pillar.transform.localPosition = dir * 1.9f + Vector3.up * 0.6f;
                pillar.transform.localScale = new Vector3(0.16f, 0.6f, 0.16f);
                StripCollider(pillar);
                pillar.GetComponent<Renderer>().sharedMaterial = post;

                var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bulb.name = $"Lamp {i + 1}";
                bulb.transform.SetParent(exit.transform, false);
                bulb.transform.localPosition = dir * 1.9f + Vector3.up * 1.28f;
                bulb.transform.localScale = Vector3.one * 0.22f;
                StripCollider(bulb);
                bulb.GetComponent<Renderer>().sharedMaterial = frame;
            }

            // 바닥 화살표 네 개 — 안쪽을 가리켜 "여기로 들어와라"를 만든다.
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arrow.name = $"Arrow {i + 1}";
                arrow.transform.SetParent(exit.transform, false);
                arrow.transform.localPosition = dir * 1.35f + Vector3.up * 0.06f;
                arrow.transform.localRotation = Quaternion.Euler(0f, angle + 45f, 0f);
                arrow.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
                StripCollider(arrow);
                arrow.GetComponent<Renderer>().sharedMaterial = frame;
            }

            var zone = exit.AddComponent<ExitZone>();
            zone.radius = 2.2f;
            zone.zoneRenderer = disc.GetComponent<Renderer>();
        }

        /// <summary>
        /// 눈에 보이지 않는 관리 컴포넌트들을 한 오브젝트에 모아 붙인다.
        /// 게임 매니저·메뉴·HUD·정전·무한모드가 전부 여기 들어간다.
        /// 한곳에 모아두면 하이어라키에서 "Systems" 하나만 보면 되고,
        /// 씬을 다시 만들 때 빠진 것이 없는지 확인하기도 쉽다.
        /// </summary>
        static void BuildSystems(Transform parent, StageData stage, List<Vector3> walkable)
        {
            var systems = new GameObject("Systems");
            systems.transform.SetParent(parent, false);

            var gm = systems.AddComponent<StealthGameManager>();
            gm.stageName = stage.displayName;
            gm.endlessMode = stage.endless;
            if (stage.endless) gm.maxCaptures = stage.endlessLives;

            systems.AddComponent<StealthMenuUI>();
            systems.AddComponent<StealthHUD>();
            systems.AddComponent<NoisePingVisualizer>();

            var blackout = systems.AddComponent<BlackoutSystem>();
            var playerController = Object.FindFirstObjectByType<PlayerController>();
            if (playerController != null)
                blackout.playerLight = playerController.GetComponentInChildren<Light>(true);

            // 무한모드 전용 컴포넌트는 무한모드에만 붙인다.
            if (!stage.endless) return;

            var endless = systems.AddComponent<EndlessMode>();

            // new List<Vector3>(walkable) 는 복사본을 만든다. 같은 목록을 그대로 넘기면
            // 한쪽에서 고쳤을 때 다른 쪽도 함께 바뀌는 사고가 난다.
            endless.spawnPoints = new List<Vector3>(walkable);
        }

        /// <summary>자료가 리스폰될 수 있는 칸. 벽·선반·오크통·크롤 통로는 제외한다.</summary>
        /// <summary>
        /// 걸어 다닐 수 있는 칸들의 월드 좌표 목록.
        /// 무한모드에서 자료를 되살릴 자리를 고를 때 이 목록에서 뽑는다.
        /// 은신처(h)와 크롤 통로(c)는 제외한다 — 소품이나 상인방이 있어 자료를 놓기 부적절하다.
        /// </summary>
        static List<Vector3> CollectWalkableCells(StageData stage, int width, int height)
        {
            var cells = new List<Vector3>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    char c = stage.grid[y][x];
                    if (c != '.' && c != 'O' && c != 'E' && c != 'L') continue;
                    cells.Add(GridToWorld(x, y, width, height));
                }
            }

            return cells;
        }

        /// <summary>
        /// 서로, 그리고 플레이어 시작 지점에서 충분히 떨어진 자리를 고른다.
        /// 자료가 한쪽에 몰려 생기면 무한모드가 금방 지루해진다.
        /// </summary>
        static List<Vector3> PickSpreadSpots(List<Vector3> candidates, Vector3 playerSpawn, int count)
        {
            var picked = new List<Vector3>();
            if (candidates.Count == 0) return picked;

            // 시작 배치도 같은 이유로 넓게 흩뿌린다. EndlessMode 의 리스폰 조건과 맞춘다.
            const float idealApart = 20f;
            const float idealFromPlayer = 22f;

            for (int i = 0; i < count; i++)
            {
                Vector3 best = candidates[Random.Range(0, candidates.Count)];
                float bestScore = float.MinValue;

                // 무작위 후보 중 가장 한적한 자리를 고른다.
                //
                // 후보를 80번 뽑아서 점수가 제일 높은 것을 쓰는 방식이다. 모든 칸을 다 검사하는
                // 것보다 훨씬 빠르면서, 결과는 거의 차이가 없다. 이런 걸 표본 추출이라고 한다.
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    Vector3 candidate = candidates[Random.Range(0, candidates.Count)];

                    // 점수 = 플레이어에게서 떨어진 정도 + 이미 놓은 자료들에게서 떨어진 정도.
                    // Mathf.Min 으로 상한을 두는 것이 중요하다. 상한이 없으면 "한 곳에서 아주 멀면
                    // 다른 것들과 붙어 있어도 총점이 높은" 결과가 나와 구석에 몰린다.
                    float score = Mathf.Min(idealFromPlayer, Vector3.Distance(candidate, playerSpawn));
                    foreach (Vector3 taken in picked)
                        score += Mathf.Min(idealApart, Vector3.Distance(candidate, taken));

                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = candidate;
                }

                picked.Add(best);
            }

            return picked;
        }

        /// <summary>
        /// 스테이지마다 씬이 하나씩 있다. 없으면 만들고, 있으면 연다.
        /// 저장 안 된 작업이 있으면 사용자에게 먼저 묻는다.
        /// </summary>
        static bool OpenOrCreateScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[StageBuilder] StageData.sceneName 이 비어 있습니다.");
                return false;
            }

            string path = $"Assets/Scenes/{sceneName}.unity";
            if (EditorSceneManager.GetActiveScene().path == path) return true;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

            if (System.IO.File.Exists(path))
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                return true;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");

            var created = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(created, path);
            Debug.Log($"[StageBuilder] 새 씬 생성: {path}");
            return true;
        }

        /// <summary>
        /// 스테이지 선택과 재시작은 SceneManager.LoadScene 을 쓰므로
        /// 해당 씬이 Build Settings 에 등록돼 있어야 한다. 빠뜨리기 쉬워서 자동으로 넣는다.
        /// </summary>
        static void EnsureSceneRegistered()
        {
            string path = EditorSceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(path)) return;

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes)
            {
                if (s.path != path) continue;
                if (!s.enabled) { s.enabled = true; EditorBuildSettings.scenes = scenes.ToArray(); }
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[StageBuilder] Build Settings 에 씬 등록: {path}");
        }

        static void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            cam.orthographic = true;
            cam.orthographicSize = 10f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.04f, 0.06f);

            // ★ 이 체크가 꺼져 있으면 볼륨을 아무리 잘 만들어도 화면에 아무 변화가 없다.
            //   URP 는 카메라마다 포스트 프로세싱 적용 여부를 따로 들고 있다.
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            var follow = cam.GetComponent<IsoCameraFollow>();
            if (follow == null) follow = cam.gameObject.AddComponent<IsoCameraFollow>();
            follow.pitch = 55f;
            follow.yaw = 45f;
            follow.distance = 30f;
        }

        static void SetupLight()
        {
            Light directional = null;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional) { directional = light; break; }
            }

            if (directional == null)
            {
                var go = new GameObject("Directional Light");
                directional = go.AddComponent<Light>();
                directional.type = LightType.Directional;
            }

            // 각도를 낮춰(52 → 44) 그림자를 길게 뽑는다. 위에서 내려다보는 시점이라
            // 그림자 길이가 곧 물체의 높이 정보가 된다.
            directional.transform.rotation = Quaternion.Euler(44f, -38f, 0f);
            directional.intensity = 1.25f;

            // 살짝 따뜻한 키 라이트 + 차가운 주변광. 두 색이 맞서야 회색 덩어리가
            // 입체로 보인다. 같은 색이면 밝기 차이만 남아 밋밋해진다.
            directional.color = new Color(1f, 0.95f, 0.86f);
            directional.shadows = LightShadows.Soft;
            directional.shadowStrength = 0.75f;

            // 주변광(그림자 속 밝기). Flat 으로 고정해야 BlackoutSystem 이 정전 때
            // 이 값을 안전하게 낮췄다 되돌릴 수 있다.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.20f, 0.24f, 0.34f);
            RenderSettings.ambientIntensity = 1f;

            // 옅은 안개. 멀리 있는 벽이 배경으로 가라앉아 깊이감이 생긴다.
            // 맵 대각선이 100m 남짓이라 그보다 짧게 끊어야 효과가 보인다.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.05f, 0.06f, 0.09f);
            RenderSettings.fogStartDistance = 26f;
            RenderSettings.fogEndDistance = 85f;
        }

        /// <summary>
        /// 전 스테이지에 같은 포스트 프로세싱을 깐다.
        ///
        /// 지금까지 볼륨이 SampleScene 에만 있어서 스테이지마다 화면이 달랐다.
        /// 스테이지 루트 안에 만들면 재생성할 때마다 같이 새로 깔린다.
        ///
        /// priority 를 10으로 두는 이유: URP 템플릿이 남긴 "Global Volume"(priority 0)이
        /// SampleScene 에 있는데, 겹칠 때 우리 설정이 이기게 하기 위해서다.
        /// </summary>
        static void SetupPostProcessing(Transform parent)
        {
            var go = new GameObject("Post Process Volume");
            go.transform.SetParent(parent, false);

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;      // 카메라 위치와 무관하게 항상 적용
            volume.priority = 10f;
            volume.weight = 1f;
            volume.sharedProfile = PostProcessProfile();
        }

        /// <summary>
        /// 포스트 프로세싱 프로파일 에셋을 만들거나 갱신한다.
        /// 머티리얼과 같은 방식이다 — 에셋으로 저장해 두면 여섯 스테이지가 하나를 공유하고,
        /// 값을 바꾸면 전부 한 번에 바뀐다.
        /// </summary>
        static VolumeProfile PostProcessProfile()
        {
            const string dir = "Assets/Settings";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Settings");

            string path = $"{dir}/Stealth_PostProcess.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);

            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            // --- 톤매핑 --- 밝기 값을 화면 색으로 옮기는 곡선. 없으면 밝은 곳이 하얗게 날아간다.
            var tone = Effect<Tonemapping>(profile);
            Set(tone.mode, TonemappingMode.Neutral);

            // --- 색 보정 --- 지금 화면이 회색·갈색뿐이라 여기서 가장 크게 달라진다.
            var color = Effect<ColorAdjustments>(profile);
            Set(color.postExposure, 0.35f);                          // 전체를 살짝 밝게
            Set(color.contrast, 14f);                                // 어두운 곳은 더 어둡게
            Set(color.saturation, 10f);                              // 색이 살아나게
            Set(color.colorFilter, new Color(0.96f, 0.97f, 1f));     // 아주 옅은 푸른기

            // --- 블룸 --- 밝은 것이 번진다. 시야 콘·자료 빛무리·탈출구 등이 살아난다.
            var bloom = Effect<Bloom>(profile);
            Set(bloom.threshold, 0.85f);   // 이보다 밝은 픽셀만 번진다. 낮추면 전체가 뿌옇다
            Set(bloom.intensity, 0.9f);
            Set(bloom.scatter, 0.68f);     // 번지는 범위

            // --- 비네트 --- 화면 가장자리를 어둡게. 시선이 가운데(플레이어)로 모인다.
            var vignette = Effect<Vignette>(profile);
            Set(vignette.intensity, 0.32f);
            Set(vignette.smoothness, 0.45f);
            Set(vignette.color, new Color(0.02f, 0.03f, 0.05f));

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>
        /// 프로파일에서 효과를 꺼내고, 없으면 만들어 붙인다.
        /// 효과는 프로파일 에셋의 '자식 에셋'으로 저장해야 파일에 남는다.
        /// AddObjectToAsset 을 빼먹으면 에디터를 껐다 켤 때 설정이 사라진다.
        /// </summary>
        static T Effect<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T existing)) return existing;

            T created = profile.Add<T>(true);
            created.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            AssetDatabase.AddObjectToAsset(created, profile);
            return created;
        }

        /// <summary>
        /// 값을 넣으면서 '이 항목을 덮어쓴다' 체크도 같이 켠다.
        /// overrideState 를 켜지 않으면 값만 바뀌고 화면에는 반영되지 않는다 — 흔한 함정이다.
        /// </summary>
        static void Set<T>(VolumeParameter<T> parameter, T value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        // ------------------------------------------------------------ 유틸

        /// <summary>
        /// 격자가 직사각형인지 검사한다.
        /// 줄 하나의 길이가 어긋나면 벽이 엉뚱한 자리에 생기는데, 원인을 눈으로 찾기가 아주 어렵다.
        /// 생성 전에 미리 알려주는 것이 훨씬 싸다.
        /// </summary>
        static void ValidateGrid(StageData stage)
        {
            int width = stage.grid[0].Length;
            for (int y = 0; y < stage.grid.Length; y++)
            {
                if (stage.grid[y].Length != width)
                {
                    Debug.LogError($"[StageBuilder] '{stage.displayName}' {y}행 길이가 {stage.grid[y].Length}. " +
                                   $"모든 행은 {width}자여야 합니다.");
                }
            }
        }

        /// <summary>
        /// 격자 좌표(x, y) → 월드 좌표.
        ///
        ///   x: 가운데를 0으로 맞추기 위해 (전체너비-1)/2 를 뺀 뒤 칸 크기를 곱한다.
        ///      맵이 원점을 중심으로 놓여야 카메라·조명 설정이 스테이지마다 달라지지 않는다.
        ///   z: 격자의 y 는 아래(남쪽)로 갈수록 커지는데 월드의 z 는 위(북쪽)로 갈수록 커진다.
        ///      부호가 반대라서 빼는 순서를 뒤집었다.
        /// </summary>
        static Vector3 GridToWorld(int x, int y, int width, int height)
        {
            return new Vector3(
                (x - (width - 1) * 0.5f) * Cell,
                0f,
                ((height - 1) * 0.5f - y) * Cell);
        }

        static Vector3 FindCellWorld(StageData stage, char symbol, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (stage.grid[y][x] == symbol) return GridToWorld(x, y, width, height);
                }
            }

            Debug.LogWarning($"[StageBuilder] '{symbol}' 기호를 그리드에서 찾지 못했습니다.");
            return Vector3.zero;
        }

        static GameObject CreateBox(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            return box;
        }

        /// <summary>
        /// 콜라이더를 떼어낸다.
        ///
        /// CreatePrimitive 로 만든 큐브·구에는 콜라이더가 자동으로 붙는다.
        /// 장식용 오브젝트(경비의 코, 상태 표시등, 시야 콘)에 콜라이더가 남아 있으면
        /// 레이캐스트에 걸려서 "경비의 코 때문에 시야가 잘리는" 식의 버그가 생긴다.
        /// </summary>
        static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        /// <summary>
        /// 이름으로 머티리얼 에셋을 찾아 쓰고, 없으면 만들어서 저장한다.
        ///
        /// new Material(...) 로 즉석에서 만들면 씬을 저장할 때마다 머티리얼이 씬 안에 박제되어
        /// 같은 회색 벽이 스테이지마다 다른 머티리얼이 된다. 에셋으로 저장해 두면
        /// 여섯 스테이지가 같은 머티리얼 하나를 공유하고, 색을 바꾸면 전부 한 번에 바뀐다.
        /// </summary>
        /// <summary>
        /// 이 오브젝트에만 텍스처 반복 횟수를 지정한다.
        ///
        /// 머티리얼에 직접 넣으면 안 되는 이유: 머티리얼 하나를 여섯 스테이지가 함께 쓴다.
        /// 25×17 창고와 49×27 아카이브가 같은 반복 횟수를 쓰면 한쪽이 늘어나 보인다.
        /// MaterialPropertyBlock 은 머티리얼을 복제하지 않고 이 렌더러에만 값을 덮어쓴다.
        /// </summary>
        static void SetTiling(GameObject go, float u, float v)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetVector(BaseMapStId, new Vector4(u, v, 0f, 0f));
            renderer.SetPropertyBlock(block);
        }

        // _BaseMap_ST = 텍스처의 (가로 반복, 세로 반복, 가로 밀기, 세로 밀기).
        // 이름으로 매번 찾으면 느려서 정수 ID 로 한 번만 바꿔 둔다.
        static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        /// <summary>
        /// Assets/Textures 아래에서 텍스처를 읽는다. 없으면 null 을 돌려주고,
        /// 그러면 머티리얼은 색만 쓰는 예전 모습 그대로가 된다(텍스처가 없어도 안 깨진다).
        /// </summary>
        static Texture2D TextureAt(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(
                $"Assets/Textures/Kenney_PrototypeTextures/Dark/{fileName}");
        }

        static Material MaterialFor(string name, Color color,
            string shaderName = "Universal Render Pipeline/Lit", string textureFile = null)
        {
            const string dir = "Assets/Materials";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Materials");

            string path = $"{dir}/{name}.mat";

            // LoadAssetAtPath = 그 경로의 에셋을 읽는다. 없으면 null.
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogError($"[StageBuilder] 셰이더를 찾을 수 없습니다: {shaderName}");
                    shader = Shader.Find("Universal Render Pipeline/Lit");
                }

                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.08f);

            // 텍스처는 매번 다시 지정한다. 인자를 빼면 null 이 들어가 예전 모습으로 되돌아간다.
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", TextureAt(textureFile));

            EditorUtility.SetDirty(mat);

            return mat;
        }

        static void BakeNavMesh(NavMeshSurface surface)
        {
            const string dir = "Assets/NavMesh";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "NavMesh");

            string sceneName = EditorSceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(sceneName)) sceneName = "Untitled";
            string path = $"{dir}/{sceneName}_NavMesh.asset";

            surface.BuildNavMesh();

            var data = surface.navMeshData;
            if (data == null)
            {
                Debug.LogWarning("[StageBuilder] NavMesh 베이크 실패. 지오메트리를 확인하세요.");
                return;
            }

            if (!AssetDatabase.Contains(data))
            {
                if (AssetDatabase.LoadAssetAtPath<NavMeshData>(path) != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(data, path);
                AssetDatabase.SaveAssets();
            }

            EditorUtility.SetDirty(surface);
        }
    }

    /// <summary>스테이지 하나를 만드는 데 필요한 모든 정보.</summary>
    //
    // StageEntry(StageCatalog) 와 헷갈리기 쉬운데 역할이 다르다.
    //   StageEntry : 게임이 실행 중에 보는 정보 — 제목, 해금 여부. 빌드에 포함된다
    //   StageData  : 맵을 만들 때만 쓰는 설계도 — 격자, 경비 경로. 에디터 전용이다
    public class StageData
    {
        public string displayName;
        /// <summary>이 스테이지가 저장될 씬 이름. StageCatalog 의 sceneName 과 일치해야 한다.</summary>
        public string sceneName;

        /// <summary>맵 그림. 한 줄이 가로 한 줄이고, 글자 하나가 한 칸이다.</summary>
        public string[] grid;

        /// <summary>
        /// 경비들의 순찰 경로. 바깥 배열이 경비 한 명, 안쪽 배열이 그 경비가 도는 지점들이다.
        /// Vector2Int[][] 처럼 대괄호가 두 겹이면 "배열의 배열"이라는 뜻이다.
        ///   guardRoutes[0] = 1번 경비의 경로
        ///   guardRoutes[0][1] = 1번 경비의 두 번째 순찰 지점
        /// </summary>
        public Vector2Int[][] guardRoutes;

        /// <summary>
        /// 구역을 무작위로 배회하는 경비. 항목 하나가 경비 한 명이고,
        /// <b>Vector2Int 두 개가 사각형의 마주보는 두 모서리</b>다.
        ///
        /// 스테이지마다 한두 명만 둔다. 전원이 배회하면 "관찰해서 빈틈을 찾는다"는
        /// 이 게임의 핵심이 사라지고, 잡혔을 때 내 실수가 아니라 운으로 느껴진다.
        /// </summary>
        public Vector2Int[][] roamRoutes = System.Array.Empty<Vector2Int[]>();

        /// <summary>공중 순찰 드론의 경로. 벽을 무시하므로 격자 위 아무 칸이나 찍어도 된다.</summary>
        public Vector2Int[][] droneRoutes = System.Array.Empty<Vector2Int[]>();

        /// <summary>이 스테이지에서 동전 던지기(Q)를 쓸 수 있는가. 기능은 스테이지마다 하나씩 열린다.</summary>
        public bool allowCoinThrow;

        /// <summary>무한모드: 탈출구가 없고, 자료가 랜덤 위치에 계속 리스폰된다.</summary>
        public bool endless;
        /// <summary>고액 자료(G) 한 개의 점수.</summary>
        public int prizePoints = 5;
        public int endlessObjectiveCount = 5;
        public int endlessLives = 3;
    }

    /// <summary>스테이지 레이아웃 모음. 새 스테이지는 여기에 추가하고 메뉴만 하나 더 달면 된다.</summary>
    public static class StageLibrary
    {
        public static readonly StageData Stage01 = new StageData
        {
            displayName = "STAGE 01 · 창고",
            sceneName = "SampleScene",
            // 시야 커버리지를 계산해서 잡은 배치다.
            //   - 선반으로 긴 직선 시야를 끊어 안전 통로를 만든다
            //   - 경비 4명의 순찰 구역이 서로 겹치지 않게 분리한다
            //   - 그래도 남는 겹침 지점에는 오크통 은신처를 둔다
            //   - 시작 지점(1,14)은 어느 경비에게도 보이지 않아야 한다.
            //     발각 시 여기로 복귀하므로, 보이면 부활하자마자 다시 잡히는 루프가 생긴다
            //   - 동쪽 복도 북쪽 입구(18,5)를 막아, 우측 상단 자료(23,2)로 가려면
            //     반드시 상단을 가로질러야 하게 만들었다. 상단이 통로로만 쓰이던 문제 해결.
            //
            // 결과: 감시받는 칸 99% -> 67%, 2명 이상이 보는 칸 47% -> 2%
            grid = new[]
            {
                "#########################",
                "#.......................#",
                "#..XXXX....XXXX..hXXXX.O#",
                "#..XXXX....XXXX...XXXX.h#",
                "#.......X.......X.......#",
                "#.........###############",
                "#..XXXX...#......###...O#",
                "#..XXXX...c..O...#.c....#",
                "#.........#.h....#.#....#",
                "#.........####.###.#.####",
                "#......X..XXXX.h...#.h..#",
                "#..XXXX...XXXX.....#....#",
                "#XXXXXX...XXXX.....##.###",
                "#............XX...XX....#",
                "#P...XX......XX...XX....#",
                "#....XX................E#",
                "#########################",
            },
            guardRoutes = new[]
            {
                // 1번 · 시작 지점에서 나오는 첫 복도. 서쪽 세로 복도는 1칸짜리 직선이라
                //       거기 세우면 혼자서 좌측 전체를 감시해버리고, 짧게 끊으면
                //       아무도 안 다니는 구석을 지키느라 놀게 된다. 그래서 아예 옮겼다.
                new[] { new Vector2Int(8, 13), new Vector2Int(12, 13) },
                // 2번 · 북동 구역 사각 순찰
                new[] { new Vector2Int(13, 1), new Vector2Int(22, 1), new Vector2Int(22, 4), new Vector2Int(13, 4) },
                // 3번 · 동쪽 방 안에서만. 예전엔 1칸짜리 복도(x=18)를 세로로 훑어서
                //       그 복도에 들어가면 피할 곳이 없었다. 이제 그 복도가 안전한 우회로다.
                new[] { new Vector2Int(21, 7), new Vector2Int(21, 11) },
                // 4번 · 사무실 안만 지킨다. 정문(14,9)은 이쪽이 보므로
                //       서쪽 크롤 통로(10,7)로 들어가는 게 정답 루트가 된다.
                new[] { new Vector2Int(12, 7), new Vector2Int(15, 7) },
            },
            // 배회형 1명 — 시작~탈출 사이 남쪽 통로. 정답 루트 위에 불확실성을 하나 둔다.
            roamRoutes = new[]
            {
                new[] { new Vector2Int(7, 13), new Vector2Int(17, 15) },
            },
        };

        /// <summary>
        /// 저택. 방 6개가 중앙 복도 하나로만 이어져 있어 복도를 반드시 건너야 한다.
        /// 복도는 경비 1명이 끝에서 끝까지 훑으므로, 동전으로 치워내는 게 이 스테이지의 주제다.
        /// </summary>
        public static readonly StageData Stage02 = new StageData
        {
            displayName = "STAGE 02 · 저택",
            sceneName = "Stage02",
            allowCoinThrow = true,
            grid = new[]
            {
                "#########################",
                "#.......#.......#.......#",
                "#..O..h.#.......#.h...O.#",
                "#.......#.......#.......#",
                "#XXX....#XXX.XXX#....XXX#",
                "####.#######.#######.####",
                "#..X...h..X.....X.......#",
                "#.......................#",
                "#..X......X.....X.......#",
                "####.#######.#######.####",
                "#XXX....#XXX.XXX#....XXX#",
                "#.......#.......#.......#",
                "#.......#.h.O...#.......#",
                "#.......#.......#.......#",
                "#P......#.......#.......#",
                "#.......#.......#......E#",
                "#########################",
            },
            guardRoutes = new[]
            {
                // 1번 · 중앙 복도를 끝에서 끝까지. 이 스테이지의 관문.
                new[] { new Vector2Int(2, 7), new Vector2Int(22, 7) },
                // 2번 · 좌상단 방
                new[] { new Vector2Int(2, 3), new Vector2Int(6, 3) },
                // 3번 · 하단 중앙 방
                new[] { new Vector2Int(10, 13), new Vector2Int(14, 13) },
                // 4번 · 우상단 방
                new[] { new Vector2Int(18, 3), new Vector2Int(22, 3) },
            },
            // 배회형 1명 — 북동 자료실. 자료를 지키는 대신 자리가 고정돼 있지 않다.
            roamRoutes = new[]
            {
                new[] { new Vector2Int(17, 1), new Vector2Int(23, 3) },
            },
        };

        /// <summary>
        /// 사무동. 중앙 대공간을 경비 3명이 겹쳐서 훑기 때문에 맨몸으로는 건널 수 없다.
        /// 두꺼비집은 전부 안전한 코너 방에 둔다. 감시 구역 안에 있으면 켜러 가다 잡혀서 무의미해진다.
        /// </summary>
        public static readonly StageData Stage03 = new StageData
        {
            displayName = "STAGE 03 · 사무동",
            sceneName = "Stage03",
            allowCoinThrow = true,
            grid = new[]
            {
                "#########################",
                "#......#.........#......#",
                "#..O...#....h....#...O..#",
                "#L.....#.........#....L.#",
                "#..#####....#....#####..#",
                "#.......................#",
                "#.......................#",
                "#.......................#",
                "#.......................#",
                "#.......................#",
                "#..#####....#....#####..#",
                "#L.h...#.........#...h.L#",
                "#......#....O....#......#",
                "#......#.........#......#",
                "#P.....#.........#......#",
                "#......#.........#.....E#",
                "#########################",
            },
            guardRoutes = new[]
            {
                // 중앙 대공간을 가로/세로로 훑는 3명. 커버리지가 겹치는 게 의도다.
                new[] { new Vector2Int(4, 5), new Vector2Int(20, 5) },
                new[] { new Vector2Int(4, 9), new Vector2Int(20, 9) },
                new[] { new Vector2Int(12, 5), new Vector2Int(12, 9) },
            },
            // 배회형 1명 — 중앙 대합실. 정전으로 건너야 하는 그 공간이다.
            roamRoutes = new[]
            {
                new[] { new Vector2Int(6, 5), new Vector2Int(18, 9) },
            },
        };

        /// <summary>
        /// 스테이지 4. 새 기능은 공중 순찰 드론.
        /// 가로로 긴 복도 두 개가 드론의 영역이고, 방은 경비의 영역이다.
        /// 드론은 벽을 무시하므로 엄폐가 통하지 않고, 머리 위를 막는 크롤 통로 상인방과
        /// 은신처, 정전만이 통한다.
        /// </summary>
        public static readonly StageData Stage04 = new StageData
        {
            displayName = "STAGE 04 · 연구소",
            sceneName = "Stage04",
            allowCoinThrow = true,
            grid = new[]
            {
                "#########################",
                "#P.......#.......#......#",
                "#..XXX...#..O....#.XXXXO#",
                "#..XXX...#.......#.XXXX.#",
                "#.....h..#....h..#......#",
                "###c#########.#######c###",
                "#.......................#",
                "#.###.#######.#######.###",
                "#.#.....#.......#.....###",
                "#.#..O..#..XXX..#.....###",
                "#.#.....#..XXX..#.....###",
                "#.#..h..c.......c..h..###",
                "#.#######.......#########",
                "#.......................#",
                "#######.###.#####.#######",
                "#.....h.#L..h...#.....E.#",
                "#########################",
            },
            guardRoutes = new[]
            {
                new[] { new Vector2Int(10, 1), new Vector2Int(16, 1),
                        new Vector2Int(16, 3), new Vector2Int(10, 3) },   // 상단 자료실
                new[] { new Vector2Int(3, 8), new Vector2Int(7, 8),
                        new Vector2Int(7, 10), new Vector2Int(3, 10) },   // 좌측 실험실
                new[] { new Vector2Int(17, 8), new Vector2Int(21, 8),
                        new Vector2Int(21, 10), new Vector2Int(17, 10) }, // 우측 실험실
                new[] { new Vector2Int(1, 7), new Vector2Int(1, 12) },    // 좌측 세로 연결로
            },
            // 배회형 1명 — 동쪽 실험실. 드론이 복도를 맡고 여기는 사람이 맡는다.
            roamRoutes = new[]
            {
                new[] { new Vector2Int(17, 8), new Vector2Int(21, 11) },
            },
            droneRoutes = new[]
            {
                // 1급 기밀(25,13)과 두꺼비집(26,13) 바로 위는 지나지 않게 비켜 놓았다.
                // 드론까지 겹치면 중앙이 어려운 게 아니라 불가능해진다.
                new[] { new Vector2Int(22, 3),  new Vector2Int(22, 21) },  // 남북 축
                new[] { new Vector2Int(45, 16), new Vector2Int(11, 16) },  // 동서 축
                new[] { new Vector2Int(7, 2),   new Vector2Int(41, 2) },   // 북쪽 순환로
            },
        };

        /// <summary>
        /// 스테이지 5. 새 기능은 환풍구 이동(V).
        /// 맵이 벽 하나로 좌우로 갈리고 통로는 가운데 문 하나뿐이다. 문 앞에는 경비가 붙어 있어서
        /// 정면 돌파 대신 환풍구 두 쌍으로 감시망을 통째로 건너뛰는 것이 정답이 된다.
        /// </summary>
        public static readonly StageData Stage05 = new StageData
        {
            displayName = "STAGE 05 · 금고층",
            sceneName = "Stage05",
            allowCoinThrow = true,
            // 환풍구 4개는 맵 바깥 벽 속에 있다. 안쪽 바닥은 한 칸도 쓰지 않는다.
            //
            //   기존 25x17 을 한 겹 키워 27x19 로 만들고,
            //   예전 바깥 벽 자리를 환풍구 칸으로 뚫었다.
            //   환풍구 바로 앞 한 칸만 크롤 통로(c)로 바꿔서 경비를 막는다.
            //
            // 크롤 통로는 NavMesh 가 높이 부족으로 경로를 잘라내므로,
            // 그 너머는 "경비가 구조적으로 갈 수 없는 곳"이 된다.
            // 그래서 환풍구에서 나오자마자 경비 앞인 상황이 아예 생기지 않는다.
            //
            //   1번 쌍 : 북(7,1)  <-> 남(19,17)   대각선
            //   2번 쌍 : 동(25,5) <-> 서(1,13)    대각선
            //
            // 이 방식 이전에는 환풍구를 맵 안에 두고 주위를 벽으로 둘러쌌는데,
            // 방 한가운데 1칸짜리 벽 기둥이 생겨 맵이 좁아 보였다. 그래서 밖으로 뺐다.
            grid = new[]
            {
                "###########################",
                "#######1###################",
                "##P....c.....#.....O.....##",
                "##..XXX......#.XXX.......##",
                "##..XXX......#.XXX.......##",
                "##...........#..........c2#",
                "######...########...#######",
                "##......h....#...........##",
                "##...........#..XXX...h..##",
                "##L......................##",
                "##...........#...........##",
                "######...##########...#####",
                "##..XXX......#....O......##",
                "#2c.XXX......#..XXXX.....##",
                "##.....O.....#...........##",
                "##...........#..XX....XX.##",
                "##....h......#.....c..E..##",
                "###################1#######",
                "###########################",
            },
            // 맵을 한 겹 키웠으므로 아래 좌표는 전부 예전 값 +1 이다.
            guardRoutes = new[]
            {
                new[] { new Vector2Int(10, 9), new Vector2Int(17, 9) },    // 가운데 문 지킴
                new[] { new Vector2Int(3, 13), new Vector2Int(11, 13),
                        new Vector2Int(11, 15), new Vector2Int(3, 15) },   // 좌하 구역
                new[] { new Vector2Int(18, 2), new Vector2Int(23, 2),
                        new Vector2Int(23, 4), new Vector2Int(18, 4) },    // 우상 금고
                new[] { new Vector2Int(15, 14), new Vector2Int(15, 16) },  // 우하 금고
            },
            // 배회형 1명 — 남서 구역. 환풍구가 전부 맵 밖으로 나가서 이제 겹치는 곳이 없다.
            roamRoutes = new[]
            {
                new[] { new Vector2Int(2, 12), new Vector2Int(12, 16) },
            },
            droneRoutes = new[]
            {
                new[] { new Vector2Int(5, 3), new Vector2Int(5, 15) },     // 좌측 세로
                new[] { new Vector2Int(16, 3), new Vector2Int(16, 15) },   // 우측 세로
            },
        };

        /// <summary>
        /// 무한 모드 전용. 47x25 로 통행 가능 칸이 766개, 일반 스테이지(239칸)의 3.2배다.
        /// 집기 밀도 20.8%, 좁은 통로 비율 21% 로 스테이지 1 창고(29.1% / 27%)보다 헐겁게 잡았다.
        ///
        /// 구조는 "중앙 대공간 + 방사형". 가운데 열람홀이 있고 사방에 3칸짜리 넓은 아치가 뚫려 있으며,
        /// 홀의 네 모서리에서 지도 모서리로 계단 모양 대각선 벽이 뻗어나가 바깥을 네 구역으로 가른다.
        ///
        ///   북 서고    — 긴 책장이 겹겹이 서 있어 사이를 지나야 한다
        ///   남 하역장  — 넓게 트이고 크기가 제각각인 화물 더미만 놓여 있다
        ///   동 사무실  — 작은 방 셋. 홀과 대비되는 좁은 공간이다
        ///   서 기계실  — 길이가 제각각인 설비 덩어리. 두꺼비집이 여기 있다
        ///
        /// 격자를 쓰지 않는 이유는 방이 전부 비슷한 직사각형이면 장소가 아니라 도면처럼 보이기 때문이고,
        /// 복도를 계단형 대각선으로 만든 이유는 일직선 복도만 있으면 어디가 어딘지 구분이 안 되기 때문이다.
        ///
        /// 중앙 열람홀은 드론 두 대가 십자로 가로지르는 가장 위험한 곳이다. 두꺼비집을 홀 밖(기계실)에
        /// 둔 것은, 홀을 건너는 수단인 정전을 켜러 가는 길이 홀을 건너는 일이 되면 안 되기 때문이다.
        /// 환풍구 두 쌍은 대각선 방향 구역끼리 이어서 홀을 통째로 건너뛰는 선택지를 준다.
        ///
        /// 기어서 지나는 통로 여섯 곳은 숨는 자리가 아니라 가로지르는 지름길이다.
        /// 벽 한 칸을 크롤 통로로 바꿨을 때 돌아가는 거리가 얼마나 줄어드는지 전수 조사해서
        /// 가장 많이 단축되는 자리에만 뚫었다. 네 대각선에 하나씩(20~36칸 단축),
        /// 홀 모서리에 둘(8칸 단축) — 아치까지 돌지 않고 바로 홀로 들어갈 수 있다.
        /// </summary>
        public static readonly StageData Endless = new StageData
        {
            displayName = "ENDLESS · 지하 아카이브",
            sceneName = "Endless",
            allowCoinThrow = true,
            grid = new[]
            {
                "#################################################",
                "########################1########################",
                "##...........XX.........c.........XX...........##",
                "##......#..X..........................X...#....##",
                "##......##....XXXXX..XhXXXXXX..hXXXXX.X..##....##",
                "##..Xh....#h#.........................###..hX..##",
                "##..XX......###.....................###....XX..##",
                "##......XX....#c#..XXXXX..XXXXX...#c#..........##",
                "##X.XXX.........#h...............##.........X.X##",
                "##................##c###...h#####...........X..##",
                "##...XX...........#.............#...#.XXX......##",
                "##...XX..XXXXX....#..XX.....XX..#...#..........##",
                "##...XX...........#..XX.....XX..#...#.#######..##",
                "#2c.........h............GL...................c2#",
                "##........XXX.....#..XX.....XX..#...######.##..##",
                "##...XXXXX..X.....#..Xh.....hX..#...#..........##",
                "##................#.............#...#......XX..##",
                "##...XX...........######...###c##..............##",
                "##X....XXX......##...............##...........X##",
                "##............###.................###..XXX.....##",
                "##..........#cXXX....XXX....XXX....XXc#XX......##",
                "##..XX....###.XXX....hXX....XXh....XX.###......##",
                "##..XX..##..X.........................X..##XX..##",
                "##......#....X....XXX.....XXX.........X...#....##",
                "##.P.........XX.........c.........XX...........##",
                "########################1########################",
                "#################################################",
            },
            guardRoutes = new[]
            {
                // 중앙 열람홀 — 1급 기밀이 있는 곳이라 넷이 둘러싼다
                new[] { new Vector2Int(20, 11), new Vector2Int(30, 11) },   // 홀 북쪽
                new[] { new Vector2Int(30, 15), new Vector2Int(20, 15) },   // 홀 남쪽
                new[] { new Vector2Int(20, 11), new Vector2Int(20, 15) },   // 홀 서쪽
                new[] { new Vector2Int(30, 15), new Vector2Int(30, 11) },   // 홀 동쪽
                new[] { new Vector2Int(15, 5),  new Vector2Int(33, 5) },    // 북 서고
                new[] { new Vector2Int(18, 19), new Vector2Int(32, 19) },   // 남 하역장
                new[] { new Vector2Int(37, 13), new Vector2Int(44, 13) },   // 동 사무실
                new[] { new Vector2Int(11, 12), new Vector2Int(17, 12) },   // 서 기계실
                new[] { new Vector2Int(4, 2),   new Vector2Int(12, 2) },    // 북서 순환로
                new[] { new Vector2Int(36, 2),  new Vector2Int(45, 2) },    // 북동 순환로
            },
            // 배회형 4명 — 중앙 열람홀을 동서남북으로 감싼다.
            // 고정 순찰 10명이 홀 안과 주요 축을 맡고, 그 바깥 네 구역은 이쪽이 맡는다.
            roamRoutes = new[]
            {
                // 북↔남은 y=13 에 대해 완전히 대칭이다.
                // 서↔동은 대칭을 조금 깼다 — 동을 x=33 에서 시작하면 북 구역(y2~7)과
                // x33~35 에서 겹쳐서, 같은 자리를 두 명이 동시에 도는 구간이 생긴다.
                new[] { new Vector2Int(17, 2),  new Vector2Int(35, 7) },    // 북 서고    19x6
                new[] { new Vector2Int(17, 19), new Vector2Int(35, 24) },   // 남 하역장  19x6
                new[] { new Vector2Int(2, 6),   new Vector2Int(14, 20) },   // 서 기계실  13x15
                new[] { new Vector2Int(36, 6),  new Vector2Int(46, 20) },   // 동 사무실  11x15
            },
            droneRoutes = new[]
            {
                // 1급 기밀(24,12)과 두꺼비집(25,12) 바로 위는 지나지 않게 비켜 놓았다.
                // 드론까지 겹치면 중앙이 어려운 게 아니라 불가능해진다.
                new[] { new Vector2Int(22, 3),  new Vector2Int(22, 22) },   // 남북 축
                new[] { new Vector2Int(45, 16), new Vector2Int(11, 16) },   // 동서 축
                new[] { new Vector2Int(7, 2),   new Vector2Int(41, 2) },    // 북쪽 순환로
            },
            endless = true,
            endlessObjectiveCount = 5,
            endlessLives = 3,
        };
    }
}
