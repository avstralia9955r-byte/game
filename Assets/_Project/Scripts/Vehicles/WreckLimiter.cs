using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Не больше N обломков техники одновременно («Обломки техники (макс.)» в настройках графики, по умолчанию 10):
    /// при появлении лишнего убирается самый старый. Обломки с дымом — заметная нагрузка на CPU/GPU.
    /// </summary>
    public static class WreckLimiter
    {
        static readonly List<GameObject> _wrecks = new();

        public static void Add(GameObject go)
        {
            if (!go) return;
            _wrecks.RemoveAll(w => !w);
            _wrecks.Add(go);
            int max = SaveSystem.Settings != null ? Mathf.Max(0, SaveSystem.Settings.maxWrecks) : 10;
            while (_wrecks.Count > max)
            {
                var old = _wrecks[0]; _wrecks.RemoveAt(0);
                if (old) Object.Destroy(old);
            }
        }
    }
}
