using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Loot.EditorTools
{
    /// <summary>
    /// Brings the 19 collectible curios (Blender models from output/collectible-models) into the game as world finds
    /// that also stand on the shop shelf: import settings, materials, look-only prefabs, LootItem assets in the
    /// «Диковины» category, the three find pools (find / find-hard / crystal) and the find prefabs that use them.
    /// Idempotent: run again after replacing an FBX or texture. Tools → Hakaton → Лут → Подключить диковины.
    /// </summary>
    static class CurioSetup
    {
        const string ModelsRoot = "Assets/Loot/Models/Curios";
        const string PrefabRoot = "Assets/Loot/Prefabs/Models";
        const string ItemRoot = "Assets/Loot/Items";
        const string PoolRoot = "Assets/Loot/Pools";
        const string Template = "Assets/ShopArt/Materials/M_Stock.mat";
        const string CatalogPath = "Assets/Loot/Loot catalog.asset";
        const string Category = "Диковины";
        const string CommonFind = "Assets/Loot/Prefabs/Finds/Find common_find.prefab";
        const string SpecialFind = "Assets/Loot/Prefabs/Finds/Find special_find.prefab";
        const string CrystalFind = "Assets/Loot/Prefabs/Finds/Find crystal.prefab";
        const float WorldScale = 2;
        const string WorldContentPath = "Assets/WorldAssembly/World content.asset";

        struct Spec
        {
            public string folder, model, id, name; public LootRarity rarity; public int price;
            public Spec(string folder, string model, string id, string name, LootRarity rarity, int price)
            { this.folder = folder; this.model = model; this.id = id; this.name = name; this.rarity = rarity; this.price = price; }
        }

        static readonly Spec[] Curios =
        {
            new Spec("01-fly-agaric-eyes", "CI01_FlyAgaricEyes", "fly_agaric_eyes", "Мухомор с глазами", LootRarity.Uncommon, 90),
            new Spec("02-hypercube", "CI02_Hypercube", "hypercube", "Гиперкуб", LootRarity.Legendary, 400),
            new Spec("03-poop", "CI03_Poop", "poop", "Говешка", LootRarity.Common, 15),
            new Spec("04-straw-doll", "CI04_StrawDoll", "straw_doll", "Соломенная кукла", LootRarity.Common, 45),
            new Spec("05-diamond-apple", "CI05_DiamondApple", "diamond_apple", "Алмазное яблоко", LootRarity.Legendary, 350),
            new Spec("06-golden-jug", "CI06_GoldenJug", "golden_jug", "Золотой кувшин", LootRarity.Rare, 260),
            new Spec("07-silver-ruby-goblet", "CI07_SilverRubyGoblet", "silver_ruby_goblet", "Серебряный кубок с рубинами", LootRarity.Rare, 300),
            new Spec("08-living-bonsai", "CI08_LivingBonsai", "living_bonsai", "Глазастый бонсай", LootRarity.Rare, 220),
            new Spec("09-ipod-earbuds", "CI09_IpodEarbuds", "ipod_earbuds", "iPod с наушниками", LootRarity.Uncommon, 140),
            new Spec("10-school-trophy", "CI10_SchoolTrophy", "school_trophy", "Школьный кубок", LootRarity.Uncommon, 80),
            new Spec("11-juice-carton", "CI11_JuiceCarton", "juice_carton", "Пакет сока", LootRarity.Common, 20),
            new Spec("12-buddha", "CI12_Buddha", "buddha_statuette", "Статуэтка Будды", LootRarity.Rare, 200),
            new Spec("13-strange-teddy", "CI13_StrangeTeddy", "strange_teddy", "Странный мишка", LootRarity.Common, 50),
            new Spec("14-ornate-mantel-clock", "CI14_OrnateMantelClock", "mantel_clock", "Пафосные часы", LootRarity.Uncommon, 160),
            new Spec("15-film-camera", "CI15_FilmCamera", "film_camera", "Плёночный фотоаппарат", LootRarity.Uncommon, 130),
            new Spec("16-antique-magnifying-glass", "CI16_AntiqueMagnifyingGlass", "magnifying_glass", "Старая лупа", LootRarity.Uncommon, 100),
            new Spec("17-baby-bottle", "CI17_BabyBottle", "baby_bottle", "Бутылочка", LootRarity.Common, 25),
            new Spec("18-battered-case", "CI18_BatteredCase", "battered_case", "Потрёпанный кейс", LootRarity.Common, 60),
            new Spec("19-kerosene-lamp", "CI19_KeroseneLamp", "kerosene_lamp", "Керосиновая лампа", LootRarity.Common, 55),
        };

        // Pool weights per rarity: what each kind of find spot turns out to be.
        static readonly (string file, string label, Dictionary<LootRarity, float> weights)[] Pools =
        {
            ("Pool find", "find", new Dictionary<LootRarity, float> { { LootRarity.Common, 1f }, { LootRarity.Uncommon, .25f } }),
            ("Pool find-hard", "find-hard", new Dictionary<LootRarity, float> { { LootRarity.Uncommon, 1f }, { LootRarity.Rare, .6f } }),
            ("Pool crystal", "crystal", new Dictionary<LootRarity, float> { { LootRarity.Rare, 1f }, { LootRarity.Legendary, .8f } }),
        };

        [MenuItem("Tools/Hakaton/Лут/Подключить диковины")]
        static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            var log = new StringBuilder();
            var template = AssetDatabase.LoadAssetAtPath<Material>(Template);
            if (!template) return "FAILED no material template " + Template;
            Directory.CreateDirectory(PoolRoot);
            var items = new List<LootItem>();
            foreach (var spec in Curios)
            {
                string dir = $"{ModelsRoot}/{spec.folder}";
                string fbx = $"{dir}/{spec.model}.fbx", png = $"{dir}/T_{spec.model}.png";
                if (!File.Exists(fbx) || !File.Exists(png)) { log.AppendLine($"MISSING {fbx} or {png}"); continue; }
                SetupTexture(png);
                SetupModel(fbx);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(png);
                var material = SaveMaterial($"{dir}/M_{spec.model}.mat", template, texture);
                var prefab = SavePrefab(spec, fbx, material, out Bounds bounds);
                var item = SaveItem(spec, prefab);
                items.Add(item);
                log.AppendLine($"{spec.id}: size {bounds.size.x:0.000}x{bounds.size.y:0.000}x{bounds.size.z:0.000} min.y {bounds.min.y:0.000} tex {texture.width}");
            }
            AddToCatalog(items, log);
            var pools = Pools.Select(p => SavePool(p.file, p.label, p.weights, items)).ToArray();
            SetFindPool(CommonFind, pools[0], log);
            SetFindPool(SpecialFind, pools[1], log);
            if (!File.Exists(CrystalFind)) AssetDatabase.CopyAsset(SpecialFind, CrystalFind);
            SetFindPool(CrystalFind, pools[2], log);
            PointWorldContent("crystal", CrystalFind, log);
            AssetDatabase.SaveAssets();
            foreach (var pool in pools)
                log.AppendLine(pool.name + ": " + string.Join(", ", pool.Chances().Select(c => $"{c.item.id} {c.chance * 100:0.#}%")));
            return log.ToString();
        }

        static void SetupTexture(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }

        static void SetupModel(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importNormals = ModelImporterNormals.Import;
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        static Material SaveMaterial(string path, Material template, Texture2D texture)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            var material = existing ? existing : new Material(template);
            if (existing) material.CopyPropertiesFromMaterial(template);
            material.shader = template.shader;
            material.name = Path.GetFileNameWithoutExtension(path);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (!existing) AssetDatabase.CreateAsset(material, path);
            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject SavePrefab(Spec spec, string fbx, Material material, out Bounds bounds)
        {
            string path = $"{PrefabRoot}/{spec.id} model.prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var root = new GameObject($"{spec.id} model");
            var mesh = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            mesh.name = spec.model;
            // Models face +Z (Blender front). The display slots look along the case's +X towards the customer camera B,
            // so the look is turned to face -X there; on the ground the pickup spins anyway.
            mesh.transform.localRotation = Quaternion.Euler(0, -90, 0);
            foreach (var r in mesh.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
            foreach (var c in mesh.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            // Base at zero: the shelf and the pickup stand the model on its pivot.
            mesh.transform.localPosition -= new Vector3(0, bounds.min.y, 0);
            bounds.center -= new Vector3(0, bounds.min.y, 0);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        static LootItem SaveItem(Spec spec, GameObject model)
        {
            string path = $"{ItemRoot}/{spec.id}.asset";
            var item = AssetDatabase.LoadAssetAtPath<LootItem>(path);
            if (!item)
            {
                item = ScriptableObject.CreateInstance<LootItem>();
                AssetDatabase.CreateAsset(item, path);
            }
            item.id = spec.id;
            item.displayName = spec.name;
            item.category = Category;
            item.price = spec.price;
            item.rarity = spec.rarity;
            item.model = model;
            item.shelfModel = null;
            item.notes = $"Диковина: находка в мире (пулы находок), стоит и на витрине. Модель: output/collectible-models/{spec.folder}.";
            EditorUtility.SetDirty(item);
            return item;
        }

        static void AddToCatalog(List<LootItem> items, StringBuilder log)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LootCatalog>(CatalogPath);
            if (!catalog) { log.AppendLine("FAILED no catalog"); return; }
            Undo.RecordObject(catalog, "Curios");
            if (!catalog.categories.Contains(Category)) catalog.categories.Add(Category);
            int added = 0;
            foreach (var item in items) if (!catalog.items.Contains(item)) { catalog.items.Add(item); added++; }
            EditorUtility.SetDirty(catalog);
            log.AppendLine($"catalog: +{added} items, category «{Category}»");
        }

        static LootPool SavePool(string file, string label, Dictionary<LootRarity, float> weights, List<LootItem> items)
        {
            string path = $"{PoolRoot}/{file}.asset";
            var pool = AssetDatabase.LoadAssetAtPath<LootPool>(path);
            if (!pool)
            {
                pool = ScriptableObject.CreateInstance<LootPool>();
                AssetDatabase.CreateAsset(pool, path);
            }
            pool.entries = items.Where(i => weights.ContainsKey(i.rarity))
                .Select(i => new LootPool.Entry { item = i, weight = weights[i.rarity] }).ToList();
            EditorUtility.SetDirty(pool);
            return pool;
        }

        static void SetFindPool(string prefabPath, LootPool pool, StringBuilder log)
        {
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            int set = 0;
            foreach (var behaviour in contents.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!behaviour) continue;
                var so = new SerializedObject(behaviour);
                var property = so.FindProperty("pool");
                if (property == null || so.FindProperty("spawnChance") == null) continue;
                property.objectReferenceValue = pool;
                // Curios lie on the ground twice their shelf size so they read from afar.
                var scale = so.FindProperty("modelScale");
                if (scale != null) scale.floatValue = WorldScale;
                so.ApplyModifiedPropertiesWithoutUndo();
                set++;
            }
            if (prefabPath == CrystalFind) contents.name = "Find crystal";
            PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
            log.AppendLine($"{Path.GetFileName(prefabPath)}: pool {pool.name}, model scale {WorldScale} on {set} pickup(s)");
        }

        static void PointWorldContent(string role, string prefabPath, StringBuilder log)
        {
            var content = AssetDatabase.LoadAssetAtPath<ScriptableObject>(WorldContentPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!content || !prefab) { log.AppendLine("FAILED world content or " + prefabPath); return; }
            var so = new SerializedObject(content);
            var loot = so.FindProperty("loot");
            for (int i = 0; i < loot.arraySize; i++)
            {
                var entry = loot.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("id").stringValue != role) continue;
                var field = entry.FindPropertyRelative("prefab");
                var component = prefab.GetComponents<MonoBehaviour>().FirstOrDefault(b => b && new SerializedObject(b).FindProperty("spawnChance") != null);
                field.objectReferenceValue = component;
                so.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine($"world content: {role} -> {prefabPath}");
                return;
            }
            log.AppendLine("FAILED no world content entry " + role);
        }

        /// <summary>
        /// Development check (bridge: invoke ...PreviewShelf 0): puts six curios on the display slots of the open shop
        /// scene, renders them from the display camera (B) into Logs/bridge/shots/curios-shelf-N.png and removes them
        /// again. The scene is not modified. Reports the slot anchors' facing.
        /// </summary>
        public static string PreviewShelf(string argument)
        {
            int start = 0;
            int.TryParse(argument, out start);
            var log = new StringBuilder();
            Transform[] anchors = null;
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!behaviour || behaviour.GetType().Name != "TradingSceneBindings") continue;
                var field = behaviour.GetType().GetField("SlotAnchors");
                anchors = field?.GetValue(behaviour) as Transform[];
                break;
            }
            if (anchors == null || anchors.Length == 0 || anchors.Any(a => !a)) return "FAILED no slot anchors in the open scene";
            Transform root = anchors[0].parent;
            while (root && !anchors.All(a => a.IsChildOf(root))) root = root.parent;
            log.AppendLine($"display root {root.name} pos {root.position} rot {root.rotation.eulerAngles}");
            foreach (var a in anchors) log.AppendLine($"  {a.name}: local fwd {root.InverseTransformDirection(a.forward)} pos {root.InverseTransformPoint(a.position)}");
            var made = new List<GameObject>();
            for (int i = 0; i < anchors.Length; i++)
            {
                var spec = Curios[(start + i) % Curios.Length];
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/{spec.id} model.prefab");
                if (!model) continue;
                var o = Object.Instantiate(model);
                o.hideFlags = HideFlags.DontSave;
                o.transform.SetPositionAndRotation(anchors[i].position, anchors[i].rotation);
                made.Add(o);
            }
            var camGo = new GameObject("CurioPreviewCam") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 80;
            cam.nearClipPlane = .02f;
            camGo.transform.SetPositionAndRotation(root.TransformPoint(new Vector3(-1.075f, 1.6f, 0)), root.rotation * Quaternion.Euler(23, 90, 0));
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "bridge", "shots", $"curios-shelf-{start}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGo);
            foreach (var o in made) Object.DestroyImmediate(o);
            log.AppendLine("shot " + path);
            return log.ToString();
        }

        // Slight crooked spread so a full shelf does not look machine-aligned; every slot still faces camera B.
        static readonly float[] SlotYaw = { -7, 5, 9, -4, 3, -9 };
        const string DisplayCasePath = "Assets/ShopBlockout/Prefabs/DisplayCase.prefab";

        /// <summary>
        /// Turns the six display anchors («Item visual») of the DisplayCase prefab to face the customer camera B (the
        /// case's -X), with a small spread, and drops scene overrides of their rotation in the open scene.
        /// </summary>
        public static string FaceShelfToCamera()
        {
            var log = new StringBuilder();
            var contents = PrefabUtility.LoadPrefabContents(DisplayCasePath);
            var anchors = contents.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Item visual").ToArray();
            if (anchors.Length != 6) { PrefabUtility.UnloadPrefabContents(contents); return "FAILED anchors: " + anchors.Length; }
            // Slot order as in the scene bindings: sorted by height, then by side.
            anchors = anchors.OrderBy(a => Mathf.Round(contents.transform.InverseTransformPoint(a.position).y * 10))
                .ThenBy(a => contents.transform.InverseTransformPoint(a.position).z).ToArray();
            for (int i = 0; i < anchors.Length; i++)
            {
                anchors[i].rotation = contents.transform.rotation * Quaternion.Euler(0, SlotYaw[i], 0);
                log.AppendLine($"{anchors[i].parent.name}: yaw {SlotYaw[i]}");
            }
            PrefabUtility.SaveAsPrefabAsset(contents, DisplayCasePath);
            PrefabUtility.UnloadPrefabContents(contents);
            int reverted = 0;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "Item visual" || !PrefabUtility.IsPartOfPrefabInstance(t)) continue;
                    var so = new SerializedObject(t);
                    var rotation = so.FindProperty("m_LocalRotation");
                    if (rotation != null && rotation.prefabOverride)
                    {
                        PrefabUtility.RevertPropertyOverride(rotation, InteractionMode.AutomatedAction);
                        reverted++;
                    }
                }
            if (reverted > 0) UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            log.AppendLine($"scene {scene.path}: reverted {reverted} rotation override(s)");
            return log.ToString();
        }
    }
}
