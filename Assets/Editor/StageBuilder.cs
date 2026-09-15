using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

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
    public static class StageBuilder
    {
        const string RootName = "--- STAGE ---";
        const float Cell = 2f;
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

        public static bool Build(StageData stage)
        {
            if (!OpenOrCreateScene(stage.sceneName)) return false;

            var scene = EditorSceneManager.GetActiveScene();

            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject(RootName);

            ValidateGrid(stage);

            int width = stage.grid[0].Length;
            int height = stage.grid.Length;

            // --- 1. 지오메트리 (NavMesh 베이크 대상) ---
            var geometry = new GameObject("Geometry");
            geometry.transform.SetParent(root.transform, false);

            BuildFloor(geometry.transform, width, height);
            BuildBlocks(geometry.transform, stage, width, height);
            BuildCrawlLintels(geometry.transform, stage, width, height);
            BuildHideProps(geometry.transform, stage, width, height);

            var surface = geometry.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            BakeNavMesh(surface);

            // --- 2. 액터 / 목표물 (NavMesh에 포함되면 안 되므로 베이크 후 생성) ---
            var actors = new GameObject("Actors");
            actors.transform.SetParent(root.transform, false);

            Vector3 playerSpawn = FindCellWorld(stage, 'P', width, height);
            BuildPlayer(actors.transform, playerSpawn, stage.allowCoinThrow);

            for (int i = 0; i < stage.guardRoutes.Length; i++)
                BuildGuard(actors.transform, stage.guardRoutes[i], i + 1, width, height);

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

            BuildMarkers(root.transform, stage, width, height);
            BuildVents(root.transform, stage, width, height);

            // 무한모드에는 탈출 조건이 없다.
            if (!stage.endless) BuildExit(root.transform, FindCellWorld(stage, 'E', width, height));

            // --- 3. 시스템 / 카메라 / 조명 ---
            BuildSystems(root.transform, stage, walkable);
            SetupCamera();
            SetupLight();
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
            var floor = CreateBox(parent, "Floor",
                new Vector3(0f, -0.1f, 0f),
                new Vector3(width * Cell, 0.2f, height * Cell),
                MaterialFor("Greybox_Floor", new Color(0.17f, 0.18f, 0.21f)));
            floor.isStatic = true;
        }

        /// <summary>
        /// 같은 문자가 가로로 이어지면 하나의 박스로 합친다.
        /// 오브젝트 수가 줄고 씬 하이어라키가 읽기 쉬워진다.
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

                    int runStart = x;
                    while (x < width && stage.grid[y][x] == c) x++;
                    int runEnd = x - 1;

                    bool isWall = c == '#';
                    float h = isWall ? WallHeight : ShelfHeight;
                    float length = (runEnd - runStart + 1) * Cell;
                    float centerX = ((runStart + runEnd) * 0.5f - (width - 1) * 0.5f) * Cell;
                    float centerZ = ((height - 1) * 0.5f - y) * Cell;

                    var box = CreateBox(
                        isWall ? wallParent : shelfParent,
                        isWall ? $"Wall_{++wallCount}" : $"Shelf_{++shelfCount}",
                        new Vector3(centerX, h * 0.5f, centerZ),
                        new Vector3(length, h, Cell * (isWall ? 1f : 0.9f)),
                        isWall ? wallMat : shelfMat);
                    box.isStatic = true;
                }
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
                        prop.isStatic = true;
                    }
                }
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
                    if (c < '1' || c > '9') continue;

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

        static void BuildCrawlMarker(Transform parent, Vector3 position, int index)
        {
            var patch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            patch.name = $"CrawlGap {index}";
            patch.transform.SetParent(parent, false);
            patch.transform.localPosition = new Vector3(position.x, 0.03f, position.z);
            patch.transform.localScale = new Vector3(Cell * 0.85f, 0.02f, Cell * 0.85f);
            StripCollider(patch);
            patch.GetComponent<Renderer>().sharedMaterial =
                MaterialFor("Marker_Crawl", new Color(0.35f, 0.8f, 1f, 0.28f), "Stealth/FOVCone");
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

            var comp = spot.AddComponent<HideSpot>();
            comp.interactRange = 1.5f;
            comp.exitDirection = exitDirection;
            comp.patchRenderer = patch.GetComponent<Renderer>();
        }

        // ------------------------------------------------------------ 액터

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

            Material mat = MaterialFor("Actor_Player", new Color(0.25f, 0.78f, 1.00f));

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(player.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            StripCollider(body);
            body.GetComponent<Renderer>().sharedMaterial = mat;

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "FacingMarker";
            nose.transform.SetParent(player.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.25f, 0.42f);
            nose.transform.localScale = new Vector3(0.16f, 0.16f, 0.5f);
            StripCollider(nose);
            nose.GetComponent<Renderer>().sharedMaterial = mat;

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

            var controller = player.AddComponent<PlayerController>();
            controller.body = body.transform;
            controller.facingMarker = nose.transform;

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

        static void BuildGuard(Transform parent, Vector2Int[] route, int index, int width, int height)
        {
            Vector3 spawn = GridToWorld(route[0].x, route[0].y, width, height);

            var guard = new GameObject($"Guard {index}");
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

            Material mat = MaterialFor("Actor_Guard", new Color(0.82f, 0.32f, 0.30f));

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(guard.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            StripCollider(body);
            body.GetComponent<Renderer>().sharedMaterial = mat;

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "FacingMarker";
            nose.transform.SetParent(guard.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.25f, 0.42f);
            nose.transform.localScale = new Vector3(0.16f, 0.16f, 0.5f);
            StripCollider(nose);
            nose.GetComponent<Renderer>().sharedMaterial = mat;

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

            Material mat = MaterialFor("Actor_Drone", new Color(0.30f, 0.42f, 0.58f));

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(drone.transform, false);
            body.transform.localScale = new Vector3(0.7f, 0.25f, 0.7f);
            StripCollider(body);
            body.GetComponent<Renderer>().sharedMaterial = mat;

            // 아래를 향한 렌즈. 드론이 무엇을 보고 있는지 한눈에 알 수 있게 한다.
            var lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lens.name = "Lens";
            lens.transform.SetParent(drone.transform, false);
            lens.transform.localPosition = new Vector3(0f, -0.18f, 0f);
            lens.transform.localScale = Vector3.one * 0.3f;
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

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(objective.transform, false);
            visual.transform.localPosition = new Vector3(0f, prize ? 1.0f : 0.8f, 0f);
            visual.transform.localScale = Vector3.one * (prize ? 0.75f : 0.45f);
            visual.transform.localRotation = Quaternion.Euler(25f, 0f, 25f);
            StripCollider(visual);

            visual.GetComponent<Renderer>().sharedMaterial = prize
                ? MaterialFor("Objective_Prize", new Color(0.45f, 1f, 0.95f),
                    "Universal Render Pipeline/Unlit")
                : MaterialFor("Objective_Gold", new Color(1f, 0.82f, 0.25f),
                    "Universal Render Pipeline/Unlit");

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

            var zone = exit.AddComponent<ExitZone>();
            zone.radius = 2.2f;
            zone.zoneRenderer = disc.GetComponent<Renderer>();
        }

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

            if (!stage.endless) return;

            var endless = systems.AddComponent<EndlessMode>();
            endless.spawnPoints = new List<Vector3>(walkable);
        }

        /// <summary>자료가 리스폰될 수 있는 칸. 벽·선반·오크통·크롤 통로는 제외한다.</summary>
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
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    Vector3 candidate = candidates[Random.Range(0, candidates.Count)];

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

            directional.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            directional.intensity = 1.05f;
            directional.color = new Color(0.95f, 0.96f, 1f);
            directional.shadows = LightShadows.Soft;
        }

        // ------------------------------------------------------------ 유틸

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

        static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        static Material MaterialFor(string name, Color color,
            string shaderName = "Universal Render Pipeline/Lit")
        {
            const string dir = "Assets/Materials";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Materials");

            string path = $"{dir}/{name}.mat";
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

    public class StageData
    {
        public string displayName;
        /// <summary>이 스테이지가 저장될 씬 이름. StageCatalog 의 sceneName 과 일치해야 한다.</summary>
        public string sceneName;
        public string[] grid;
        public Vector2Int[][] guardRoutes;

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
            droneRoutes = new[]
            {
                new[] { new Vector2Int(2, 6), new Vector2Int(22, 6) },    // 상단 복도
                new[] { new Vector2Int(22, 13), new Vector2Int(2, 13) },  // 하단 복도
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
            grid = new[]
            {
                "#########################",
                "#P..........#.....O.....#",
                "#..XXX......#.XXX.......#",
                "#..XXX...1..#.XXX.......#",
                "#...........#...........#",
                "#####...########...######",
                "#......h....#........1..#",
                "#...........#..XXX...h..#",
                "#L......................#",
                "#...........#...........#",
                "#####...##########...####",
                "#..XXX...2..#....O......#",
                "#..XXX......#..XXXX.....#",
                "#.....O.....#........2..#",
                "#...........#..XX....XX.#",
                "#....h......#........E..#",
                "#########################",
            },
            guardRoutes = new[]
            {
                new[] { new Vector2Int(9, 8), new Vector2Int(16, 8) },     // 가운데 문 지킴
                new[] { new Vector2Int(2, 12), new Vector2Int(10, 12),
                        new Vector2Int(10, 14), new Vector2Int(2, 14) },   // 좌하 구역
                new[] { new Vector2Int(17, 1), new Vector2Int(22, 1),
                        new Vector2Int(22, 3), new Vector2Int(17, 3) },    // 우상 금고
                new[] { new Vector2Int(14, 13), new Vector2Int(14, 15) },  // 우하 금고
            },
            droneRoutes = new[]
            {
                new[] { new Vector2Int(4, 2), new Vector2Int(4, 14) },     // 좌측 세로
                new[] { new Vector2Int(15, 2), new Vector2Int(15, 14) },   // 우측 세로
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
                "###############################################",
                "#...........XX...................XX...........#",
                "#......#..X..........................X...#....#",
                "#......##....XXXXX..XhXXXXXX..hXXXXX.X..##....#",
                "#..Xh....#h#.........................###..hX..#",
                "#..XX......###.....................###....XX..#",
                "#......XX....#c#..XXXXX..XXXXX...#c#..........#",
                "#X.XXX.........#h...1...........##.........X.X#",
                "#................##c###...h#####...........X..#",
                "#...XX...........#.............#...#.XXX......#",
                "#...XX..XXXXX....#..XX.....XX..#...#..........#",
                "#...XX...........#..XX.....XX..#...#.#######..#",
                "#..2.......h............GL....................#",
                "#........XXX.....#..XX.....XX..#...######.##..#",
                "#...XXXXX..X.....#..Xh.....hX..#...#..........#",
                "#................#.............#...#......XX..#",
                "#...XX...........######...###c##..............#",
                "#X....XXX......##...............##.........2.X#",
                "#............###.................###..XXX.....#",
                "#..........#cXXX....XXX....XXX....XXc#XX......#",
                "#..XX....###.XXX....hXX....XXh....XX.###......#",
                "#..XX..##..X.........................X..##XX..#",
                "#......#....X....XXX.....XXX.........X.1.#....#",
                "#.P.........XX...................XX...........#",
                "###############################################",
            },
            guardRoutes = new[]
            {
                // 중앙 열람홀 — 1급 기밀이 있는 곳이라 넷이 둘러싼다
                new[] { new Vector2Int(19, 10), new Vector2Int(29, 10) },  // 홀 북쪽
                new[] { new Vector2Int(29, 14), new Vector2Int(19, 14) },  // 홀 남쪽
                new[] { new Vector2Int(19, 10), new Vector2Int(19, 14) },  // 홀 서쪽
                new[] { new Vector2Int(29, 14), new Vector2Int(29, 10) },  // 홀 동쪽
                new[] { new Vector2Int(14, 4), new Vector2Int(32, 4) },    // 북 서고
                new[] { new Vector2Int(17, 18), new Vector2Int(31, 18) },  // 남 하역장
                new[] { new Vector2Int(36, 12), new Vector2Int(43, 12) },  // 동 사무실
                new[] { new Vector2Int(10, 11), new Vector2Int(16, 11) },  // 서 기계실
                new[] { new Vector2Int(3, 1), new Vector2Int(11, 1) },     // 북서 순환로
                new[] { new Vector2Int(35, 1), new Vector2Int(44, 1) },    // 북동 순환로
            },
            droneRoutes = new[]
            {
                // 1급 기밀(24,12)과 두꺼비집(25,12) 바로 위는 지나지 않게 비켜 놓았다.
                // 드론까지 겹치면 중앙이 어려운 게 아니라 불가능해진다.
                new[] { new Vector2Int(21, 2), new Vector2Int(21, 20) },   // 남북 축
                new[] { new Vector2Int(44, 15), new Vector2Int(10, 15) },  // 동서 축
                new[] { new Vector2Int(6, 1), new Vector2Int(40, 1) },     // 북쪽 순환로
            },
            endless = true,
            endlessObjectiveCount = 5,
            endlessLives = 3,
        };
    }
}
