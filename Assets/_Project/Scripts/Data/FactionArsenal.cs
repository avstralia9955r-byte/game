using System;
using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    [CreateAssetMenu(menuName = "Frontline/Faction Arsenal", fileName = "ARS_")]
    public class FactionArsenal : ScriptableObject
    {
        public Faction faction;
        public string displayName;
        public List<WeaponDefinition> weapons = new();  // по одному образцу в каждой категории
        public List<ClassLoadout> classes = new();
        public GameObject soldierViewHandsPrefab;
        public Mesh botMeshLOD0, botMeshLOD1, botMeshLOD2; // VAT-меши для ECS-ботов
        public Material botMaterial;

        public WeaponDefinition Get(WeaponCategory c) => weapons.Find(w => w && w.category == c);
        public ClassLoadout GetClass(SoldierClass c) => classes.Find(x => x.soldierClass == c);
    }
}
