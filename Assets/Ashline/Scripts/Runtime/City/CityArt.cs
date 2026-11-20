using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashline.City
{
    /// <summary>Material and prop references for the generated city. Built by the AshlineArt editor tool.</summary>
    [CreateAssetMenu(menuName = "Ashline/City Art")]
    public class CityArt : ScriptableObject
    {
        [Serializable]
        public class Surface
        {
            public string id;
            public Material material;
            /// <summary>Real-world size of one texture repeat, in meters.</summary>
            public Vector2 size = new Vector2(2f, 2f);
        }

        [Serializable]
        public class Prop
        {
            public string id;
            public GameObject prefab;
        }

        public List<Surface> surfaces = new List<Surface>();
        public List<Prop> props = new List<Prop>();

        public Material glass;
        public Material shopGlass;
        public Material windowFrame;
        public Material roadPaint;
        public Material roadPaintYellow;
        public Material metalDark;
        public Material lampEmissive;
        public Material signs;
        public Material[] awnings;
        public Material palmTrunk;
        public Material palmFrond;
        public Material water;
        public Material pool;
        public Material pond;
        public Material paintWhite;
        public Material paintBlue;
        public Material paintRed;
        public Material glowWhite;

        Dictionary<string, Surface> surfaceMap;
        Dictionary<string, GameObject> propMap;

        public Surface Get(string id)
        {
            if (surfaceMap == null)
            {
                surfaceMap = new Dictionary<string, Surface>();
                foreach (var s in surfaces) surfaceMap[s.id] = s;
            }
            return surfaceMap.TryGetValue(id, out var v) ? v : null;
        }

        public GameObject GetProp(string id)
        {
            if (propMap == null)
            {
                propMap = new Dictionary<string, GameObject>();
                foreach (var p in props) propMap[p.id] = p.prefab;
            }
            return propMap.TryGetValue(id, out var v) ? v : null;
        }

        public static CityArt Load() => Resources.Load<CityArt>("CityArt");
    }
}
