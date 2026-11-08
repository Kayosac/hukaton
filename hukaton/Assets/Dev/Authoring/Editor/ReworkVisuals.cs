using Hakaton.Feel;
using System;
using System.Linq;
using Hakaton.Expeditions;
using UnityEditor;
using UnityEngine;
using static Hakaton.Authoring.Kit;

namespace Hakaton.Authoring
{
    /// <summary>Materials, effect prefabs and card prefabs for the expedition rework.</summary>
    static class ReworkVisuals
    {
        const string Fx = "Assets/Expedition/Art/Fx/";
        const string Ui = "Assets/Expedition/Art/Ui/";
        const string Mats = "Assets/Game/Materials/";
        const string VfxFolder = "Assets/Game/Prefabs/Effects/";
        const string CardFolder = "Assets/Game/Prefabs/Cards/";

        public static void Textures()
        {
            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Expedition/Art/Fx" }).Select(AssetDatabase.GUIDToAssetPath))
                ImportTexture(path, false, true, 512);
            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Expedition/Art/Ui" }).Select(AssetDatabase.GUIDToAssetPath))
                ImportTexture(path, true, false, 512);
            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Decks/Art/Backs" }).Select(AssetDatabase.GUIDToAssetPath))
                ImportTexture(path, false, true, 1024);
            // Card fronts are seen small in the hand: mip-maps keep the art readable instead of shimmering.
            var config = Load<Hakaton.Decks.Unity.DeckPrototypeConfig>("Assets/Game/Data/Cards.asset");
            foreach (var sprite in config.cards.Select(c => c.artwork).Append(config.returnArtwork).Where(s => s))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite));
                if (importer.mipmapEnabled && importer.filterMode == FilterMode.Trilinear) continue;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.SaveAndReimport();
            }
            Say("textures imported");
        }

        public static Material ParticleMaterial(string name, string texture, bool additive)
            => Material(Mats + "Fx " + name + ".mat", "Hakaton/Particle", m =>
            {
                m.SetTexture("_MainTex", Load<Texture2D>(Fx + texture));
                m.SetFloat("_SrcBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.renderQueue = 3000;
            });

        public static Material CardViewmodel => Material(Mats + "Card Viewmodel.mat", "Hakaton/Card", m =>
        {
            m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_Cutoff", .5f);
            m.renderQueue = 3100;
        });

        public static Material CardWorld => Material(Mats + "Card World.mat", "Hakaton/Card", m =>
        {
            m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            m.SetFloat("_ZWrite", 1);
            m.SetFloat("_Cutoff", .5f);
            m.renderQueue = 2450;
        });

        // ---------------- particles ----------------

        sealed class Spec
        {
            public string name; public Material material; public int count = 10;
            public Vector2 lifetime = new Vector2(.4f, .7f), speed = new Vector2(2, 4), size = new Vector2(.2f, .4f);
            public Color colour = Color.white; public float gravity, radius = .1f, delay, spin = 360, drag;
            public ParticleSystemShapeType shape = ParticleSystemShapeType.Sphere; public float arc = 360, angle = 25;
            public bool tinted, flat, stretch, grow, hemisphereUp; public Vector3 shapeRotation;
            public bool looping; public float rate;
        }

        static ParticleSystem Add(Transform parent, Spec s)
        {
            var go = Child(parent, s.name, typeof(ParticleSystem));
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = s.looping;
            main.loop = s.looping;
            main.duration = s.looping ? 2 : 1;
            main.startDelay = s.delay;
            main.startLifetime = new ParticleSystem.MinMaxCurve(s.lifetime.x, s.lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(s.speed.x, s.speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(s.size.x, s.size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = s.colour;
            main.gravityModifier = s.gravity;
            main.simulationSpace = s.looping ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = Mathf.Max(8, s.count * 2);
            var emission = ps.emission;
            emission.rateOverTime = s.looping ? s.rate : 0;
            emission.SetBursts(s.looping ? new ParticleSystem.Burst[0] : new[] { new ParticleSystem.Burst(0, (short)s.count) });
            var shape = ps.shape;
            shape.shapeType = s.shape;
            shape.radius = s.radius;
            shape.arc = s.arc;
            shape.angle = s.angle;
            shape.rotation = s.shapeRotation;
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .55f), new GradientAlphaKey(0, 1) });
            colour.color = gradient;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, s.grow
                ? new AnimationCurve(new Keyframe(0, .15f, 0, 4), new Keyframe(1, 1, 0, 0))
                : new AnimationCurve(new Keyframe(0, .5f), new Keyframe(.15f, 1), new Keyframe(1, .35f)));
            if (s.spin > 0)
            {
                var rotation = ps.rotationOverLifetime;
                rotation.enabled = true;
                rotation.z = new ParticleSystem.MinMaxCurve(-s.spin * Mathf.Deg2Rad, s.spin * Mathf.Deg2Rad);
            }
            if (s.drag > 0)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.drag = s.drag;
                limit.multiplyDragByParticleSize = false;
            }
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = s.material;
            renderer.renderMode = s.stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (s.stretch) { renderer.velocityScale = .06f; renderer.lengthScale = 1.5f; }
            renderer.alignment = s.flat ? ParticleSystemRenderSpace.Local : ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = s.flat ? 10 : 0;
            return ps;
        }

        static VfxBurst Burst(string name, float lifetime, Color? light, params Spec[] specs)
        {
            var root = new GameObject(name);
            var burst = root.AddComponent<VfxBurst>();
            var systems = specs.Select(s => (spec: s, ps: Add(root.transform, s))).ToArray();
            burst.systems = systems.Select(x => x.ps).ToArray();
            burst.tinted = systems.Where(x => x.spec.tinted).Select(x => x.ps).ToArray();
            burst.lifetime = lifetime;
            if (light.HasValue)
            {
                var lamp = Child(root.transform, "Flash", typeof(Light)).GetComponent<Light>();
                lamp.type = LightType.Point;
                lamp.range = 6;
                lamp.intensity = 0;
                lamp.color = light.Value;
                lamp.shadows = LightShadows.None;
                lamp.transform.localPosition = new Vector3(0, 0, .4f);
                burst.flash = lamp;
                burst.flashIntensity = 3;
                burst.flashSeconds = .22f;
            }
            return SavePrefab(root, VfxFolder + name + ".prefab").GetComponent<VfxBurst>();
        }

        public static ExpeditionVfx Effects(out VfxBurst impulse, out VfxBurst ice, out VfxBurst fire, out VfxBurst strike, out VfxBurst heavy, out VfxBurst heal, out GameObject glintPrefab)
        {
            Material puff = ParticleMaterial("Puff", "fx_puff_0.png", false), puff2 = ParticleMaterial("Puff B", "fx_puff_1.png", false);
            Material flame = ParticleMaterial("Flame", "fx_flame.png", false), shard = ParticleMaterial("Shard", "fx_shard.png", false);
            Material scrap = ParticleMaterial("Scrap", "fx_scrap_0.png", false), scrap2 = ParticleMaterial("Scrap B", "fx_scrap_1.png", false);
            Material spark = ParticleMaterial("Spark", "fx_spark.png", false), glow = ParticleMaterial("Glow", "fx_soft.png", true);
            Material ring = ParticleMaterial("Ink ring", "fx_inkring.png", false), softRing = ParticleMaterial("Soft ring", "fx_ring.png", true);
            Material plus = ParticleMaterial("Plus", "fx_plus.png", false), heart = ParticleMaterial("Heart", "fx_heart.png", false);
            Material slash = ParticleMaterial("Slash", "fx_slash.png", false);
            Color dust = new Color(.86f, .78f, .64f), paper = new Color(.96f, .9f, .74f);

            impulse = Burst("Impulse burst", 1.2f, new Color(1, .9f, .6f),
                new Spec { name = "Shock ring", material = ring, count = 1, lifetime = new Vector2(.32f, .32f), speed = Vector2.zero, size = new Vector2(5.5f, 5.5f), tinted = true, flat = true, grow = true, spin = 0, shape = ParticleSystemShapeType.Sphere, radius = .01f },
                new Spec { name = "Dust", material = puff, count = 12, lifetime = new Vector2(.45f, .75f), speed = new Vector2(5, 8), size = new Vector2(.5f, .9f), colour = dust, drag = 6, shape = ParticleSystemShapeType.Circle, radius = .3f },
                new Spec { name = "Scraps", material = scrap, count = 8, lifetime = new Vector2(.6f, 1f), speed = new Vector2(4, 7), size = new Vector2(.12f, .2f), colour = paper, gravity = 1, shape = ParticleSystemShapeType.Hemisphere, radius = .2f, spin = 720 });
            ice = Burst("Ice burst", 1.6f, new Color(.6f, .9f, 1),
                new Spec { name = "Shards", material = shard, count = 16, lifetime = new Vector2(.5f, .9f), speed = new Vector2(4, 8), size = new Vector2(.18f, .35f), colour = new Color(.75f, .95f, 1), gravity = 1.2f, shape = ParticleSystemShapeType.Hemisphere, radius = .15f, spin = 540 },
                new Spec { name = "Frost", material = puff2, count = 7, lifetime = new Vector2(.8f, 1.3f), speed = new Vector2(1, 2.5f), size = new Vector2(.6f, 1f), colour = new Color(.85f, .96f, 1, .9f), drag = 2 },
                new Spec { name = "Glint", material = glow, count = 14, lifetime = new Vector2(.4f, .8f), speed = new Vector2(2, 5), size = new Vector2(.1f, .25f), colour = new Color(.6f, .9f, 1), tinted = true },
                new Spec { name = "Ring", material = softRing, count = 1, lifetime = new Vector2(.4f, .4f), speed = Vector2.zero, size = new Vector2(4, 4), tinted = true, flat = true, grow = true, spin = 0 });
            fire = Burst("Fire burst", 2.2f, new Color(1, .55f, .2f),
                new Spec { name = "Flames", material = flame, count = 12, lifetime = new Vector2(.45f, .8f), speed = new Vector2(1.5f, 3.5f), size = new Vector2(.5f, .9f), colour = new Color(1, .62f, .2f), tinted = true, gravity = -.6f, shape = ParticleSystemShapeType.Hemisphere, radius = .35f, spin = 60 },
                new Spec { name = "Embers", material = glow, count = 24, lifetime = new Vector2(.7f, 1.4f), speed = new Vector2(2, 5), size = new Vector2(.06f, .14f), colour = new Color(1, .6f, .2f), gravity = -.25f, shape = ParticleSystemShapeType.Hemisphere, radius = .3f },
                new Spec { name = "Smoke", material = puff, count = 6, lifetime = new Vector2(1, 1.6f), speed = new Vector2(.6f, 1.4f), size = new Vector2(.7f, 1.2f), colour = new Color(.32f, .29f, .28f, .85f), gravity = -.15f, delay = .15f, drag = 1 });
            strike = Burst("Strike hit", 1f, new Color(1, .8f, .7f),
                new Spec { name = "Slash", material = slash, count = 2, lifetime = new Vector2(.14f, .2f), speed = Vector2.zero, size = new Vector2(1.4f, 1.9f), tinted = true, spin = 0 },
                new Spec { name = "Sparks", material = spark, count = 10, lifetime = new Vector2(.2f, .4f), speed = new Vector2(5, 9), size = new Vector2(.15f, .3f), colour = new Color(1, .92f, .6f), drag = 4 },
                new Spec { name = "Puff", material = puff, count = 4, lifetime = new Vector2(.3f, .5f), speed = new Vector2(1, 2.5f), size = new Vector2(.4f, .6f), colour = paper });
            heavy = Burst("Heavy hit", 1.4f, new Color(1, .7f, .5f),
                new Spec { name = "Shock ring", material = ring, count = 1, lifetime = new Vector2(.28f, .28f), speed = Vector2.zero, size = new Vector2(3.2f, 3.2f), tinted = true, grow = true, spin = 0 },
                new Spec { name = "Sparks", material = spark, count = 16, lifetime = new Vector2(.25f, .5f), speed = new Vector2(7, 12), size = new Vector2(.2f, .38f), colour = new Color(1, .85f, .5f), drag = 4 },
                new Spec { name = "Dust", material = puff, count = 10, lifetime = new Vector2(.5f, .8f), speed = new Vector2(3, 6), size = new Vector2(.6f, 1f), colour = dust, drag = 5 },
                new Spec { name = "Slash", material = slash, count = 1, lifetime = new Vector2(.18f, .18f), speed = Vector2.zero, size = new Vector2(2.4f, 2.4f), tinted = true, spin = 0 });
            heal = Burst("Heal burst", 1.8f, new Color(.5f, 1, .6f),
                new Spec { name = "Plus", material = plus, count = 10, lifetime = new Vector2(.8f, 1.2f), speed = new Vector2(1, 2.5f), size = new Vector2(.22f, .38f), colour = new Color(.5f, .95f, .55f), gravity = -.4f, shape = ParticleSystemShapeType.Circle, radius = 1.2f, spin = 90 },
                new Spec { name = "Hearts", material = heart, count = 5, lifetime = new Vector2(.9f, 1.3f), speed = new Vector2(1, 2), size = new Vector2(.28f, .4f), colour = new Color(1, .45f, .45f), gravity = -.5f, shape = ParticleSystemShapeType.Circle, radius = .8f, spin = 40 },
                new Spec { name = "Ring", material = softRing, count = 1, lifetime = new Vector2(.5f, .5f), speed = Vector2.zero, size = new Vector2(5, 5), colour = new Color(.4f, 1, .55f), flat = true, grow = true, spin = 0 });

            var vfx = Asset<ExpeditionVfx>("Assets/Game/Data/Expedition Effects.asset");
            vfx.paperPoof = Burst("Paper poof", 1.2f, null,
                new Spec { name = "Scraps", material = scrap, count = 9, lifetime = new Vector2(.5f, .9f), speed = new Vector2(1.5f, 3.5f), size = new Vector2(.08f, .15f), colour = paper, gravity = .8f, spin = 720 },
                new Spec { name = "Scraps B", material = scrap2, count = 6, lifetime = new Vector2(.5f, .9f), speed = new Vector2(1.5f, 3.5f), size = new Vector2(.08f, .14f), colour = Color.white, tinted = true, gravity = .8f, spin = 720 },
                new Spec { name = "Puff", material = puff, count = 3, lifetime = new Vector2(.3f, .45f), speed = new Vector2(.5f, 1.2f), size = new Vector2(.25f, .4f), colour = paper });
            vfx.cardBurn = Burst("Card burn", 1.2f, null,
                new Spec { name = "Flare", material = glow, count = 6, lifetime = new Vector2(.2f, .35f), speed = new Vector2(.5f, 1.5f), size = new Vector2(.15f, .3f), tinted = true },
                new Spec { name = "Scraps", material = scrap, count = 8, lifetime = new Vector2(.5f, .8f), speed = new Vector2(1, 2.5f), size = new Vector2(.06f, .12f), colour = paper, gravity = .6f, spin = 720 },
                new Spec { name = "Puff", material = puff2, count = 2, lifetime = new Vector2(.3f, .4f), speed = new Vector2(.3f, .8f), size = new Vector2(.25f, .35f), tinted = true });
            vfx.pickupSparkle = Burst("Pickup sparkle", 1f, null,
                new Spec { name = "Sparks", material = spark, count = 10, lifetime = new Vector2(.3f, .5f), speed = new Vector2(1.5f, 3.5f), size = new Vector2(.12f, .22f), colour = new Color(1, .9f, .5f), drag = 3 },
                new Spec { name = "Glow", material = glow, count = 1, lifetime = new Vector2(.25f, .25f), speed = Vector2.zero, size = new Vector2(1.2f, 1.2f), colour = new Color(1, .85f, .4f, .7f), grow = true, spin = 0 });
            vfx.creatureDeath = Burst("Creature poof", 1.6f, null,
                new Spec { name = "Puffs", material = puff, count = 12, lifetime = new Vector2(.5f, .9f), speed = new Vector2(2, 4.5f), size = new Vector2(.6f, 1.1f), colour = Color.white, drag = 4 },
                new Spec { name = "Fur", material = scrap2, count = 12, lifetime = new Vector2(.7f, 1.1f), speed = new Vector2(2, 5), size = new Vector2(.1f, .2f), tinted = true, gravity = .8f, spin = 540 },
                new Spec { name = "Sparks", material = spark, count = 8, lifetime = new Vector2(.3f, .5f), speed = new Vector2(3, 6), size = new Vector2(.18f, .3f), colour = new Color(1, .95f, .7f), drag = 3 },
                new Spec { name = "Ring", material = ring, count = 1, lifetime = new Vector2(.3f, .3f), speed = Vector2.zero, size = new Vector2(2.6f, 2.6f), colour = Color.white, grow = true, spin = 0 });
            vfx.dustPuff = Burst("Dust puff", 1f, null,
                new Spec { name = "Dust", material = puff, count = 6, lifetime = new Vector2(.35f, .6f), speed = new Vector2(1, 3), size = new Vector2(.35f, .6f), colour = dust, tinted = true, drag = 4 });
            vfx.landingDust = Burst("Landing dust", 1f, null,
                new Spec { name = "Dust", material = puff, count = 10, lifetime = new Vector2(.4f, .65f), speed = new Vector2(2.5f, 4.5f), size = new Vector2(.35f, .6f), colour = dust, tinted = true, drag = 5, shape = ParticleSystemShapeType.Circle, radius = .3f, shapeRotation = new Vector3(90, 0, 0) });
            vfx.returnPoof = Burst("Return poof", 1.6f, new Color(1, .85f, .4f),
                new Spec { name = "Sparks", material = spark, count = 30, lifetime = new Vector2(.4f, .9f), speed = new Vector2(3, 8), size = new Vector2(.18f, .35f), colour = new Color(1, .86f, .45f), drag = 2 },
                new Spec { name = "Puffs", material = puff2, count = 10, lifetime = new Vector2(.5f, .8f), speed = new Vector2(1.5f, 3), size = new Vector2(.5f, .9f), colour = new Color(1, .95f, .8f) },
                new Spec { name = "Ring", material = ring, count = 1, lifetime = new Vector2(.4f, .4f), speed = Vector2.zero, size = new Vector2(4, 4), colour = new Color(1, .82f, .35f), grow = true, spin = 0 });
            EditorUtility.SetDirty(vfx);

            // Looping glint so loot reads from a distance.
            var glintRoot = new GameObject("Glint");
            var glintSystem = Add(glintRoot.transform, new Spec { name = "Sparkle", material = spark, looping = true, rate = 1.6f, lifetime = new Vector2(.5f, .8f), speed = new Vector2(0, .2f), size = new Vector2(.14f, .26f), colour = new Color(1, .93f, .6f), radius = .25f, spin = 180 });
            glintSystem.transform.localPosition = Vector3.up * .45f;
            glintPrefab = SavePrefab(glintRoot, VfxFolder + "Loot glint.prefab");
            Say("effects built");
            return vfx;
        }

        // ---------------- cards ----------------

        static CardView Quad(Transform parent, string name, Material material)
        {
            var go = Child(parent, name, typeof(MeshFilter), typeof(MeshRenderer), typeof(CardView));
            go.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var view = go.GetComponent<CardView>();
            view.meshRenderer = renderer;
            return view;
        }

        public static CardView HandQuad(Transform parent, string name) => Quad(parent, name, CardViewmodel);

        public static void Cards(out CardProjectile projectile, out CardDebris debris)
        {
            var root = new GameObject("Thrown card");
            int cards = LayerMask.NameToLayer("Cards");
            if (cards >= 0) root.layer = cards;
            var thrown = root.AddComponent<CardProjectile>();
            thrown.view = Quad(root.transform, "Card", CardWorld);
            var trail = root.AddComponent<TrailRenderer>();
            trail.time = .16f;
            trail.minVertexDistance = .04f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, .09f), new Keyframe(1, 0));
            trail.sharedMaterial = ParticleMaterial("Trail", "fx_soft.png", true);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            thrown.trail = trail;
            projectile = SavePrefab(root, CardFolder + "Thrown card.prefab").GetComponent<CardProjectile>();

            var debrisRoot = new GameObject("Card debris");
            var flying = debrisRoot.AddComponent<CardDebris>();
            flying.view = Quad(debrisRoot.transform, "Card", CardWorld);
            debris = SavePrefab(debrisRoot, CardFolder + "Card debris.prefab").GetComponent<CardDebris>();
            Say("card prefabs built");
        }

        public static Sprite UiSprite(string file) => Load<Sprite>(Ui + file);
    }
}
