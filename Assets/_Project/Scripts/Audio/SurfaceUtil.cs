using UnityEngine;

namespace Frontline
{
    /// <summary>Определение материала поверхности по коллайдеру — для шагов и звуков попаданий.</summary>
    public static class SurfaceUtil
    {
        public static bool Winter => ProceduralModels.Theme == MapTheme.Winter;

        public static SoundSynth.ImpactKind Impact(Collider c)
        {
            if (!c) return Winter ? SoundSynth.ImpactKind.Snow : SoundSynth.ImpactKind.Dirt;
            if (c is TerrainCollider) return Winter ? SoundSynth.ImpactKind.Snow : SoundSynth.ImpactKind.Dirt;
            if (c.GetComponent<Brick>()) return SoundSynth.ImpactKind.Stone;
            string n = Root(c).name;
            if (n.Contains("Wreck") || n.Contains("Hedgehog") || n.Contains("Locomotive") || n.Contains("Wagon") || n.Contains("Bunker")) return SoundSynth.ImpactKind.Metal;
            if (n.Contains("Barn") || n.Contains("Crate") || n.Contains("Supplies") || n.Contains("Cart") || n.Contains("Watchtower") || n.Contains("LogPile") || n.Contains("Haystack") || n.Contains("Hedge")) return SoundSynth.ImpactKind.Wood;
            return SoundSynth.ImpactKind.Stone;
        }

        public static SoundSynth.Surface Step(Collider c)
        {
            if (!c || c is TerrainCollider) return Winter ? SoundSynth.Surface.Snow : SoundSynth.Surface.Grass;
            string n = Root(c).name;
            if (n.Contains("Watchtower") || n.Contains("Wagon") || n.Contains("Cart")) return SoundSynth.Surface.Wood;
            if (n.StartsWith("Ballast")) return SoundSynth.Surface.Gravel;
            if (n.Contains("FrozenRiver") || n.Contains("FrozenLake")) return SoundSynth.Surface.Stone;
            return SoundSynth.Surface.Stone;
        }

        static Transform Root(Collider c)
        {
            var t = c.transform;
            while (t.parent && t.parent.name != "Structures" && t.parent.parent) t = t.parent;
            return t;
        }
    }
}
