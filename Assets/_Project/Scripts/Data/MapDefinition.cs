using System;
using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    [CreateAssetMenu(menuName = "Frontline/Map", fileName = "MAP_")]
    public class MapDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public string sceneName;
        public MapTheme theme;
        public CamoPalette camo = new();
        public TimeOfDay[] supportedTimes = { TimeOfDay.Day, TimeOfDay.Evening };
        public Weather defaultWeather = Weather.Clear;
        public int recommendedBots = 128;
        public Sprite minimapImage;
        public Vector2 minimapWorldMin, minimapWorldMax;
    }
}
