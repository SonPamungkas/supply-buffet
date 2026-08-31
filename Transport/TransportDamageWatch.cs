using System;
using System.Collections.Generic;
using UnityEngine;
namespace SupplyBuffetMod
{
    public class TransportDamageWatch
    {
        private int hits;
        private int engineHits;
        private int detaches;
        private int engineLosses;
        private readonly List<Action> unsubscribers = new List<Action>();
        private bool attached;
        public void Attach(Aircraft aircraft)
        {
            Detach();
            hits = engineHits = detaches = engineLosses = 0;
            if (aircraft == null) return;
            List<UnitPart> parts = aircraft.GetAllParts();
            if (parts == null) return;
            for (int i = 0; i < parts.Count; i++) Subscribe(parts[i]);
            attached = true;
        }
        private void Subscribe(UnitPart part)
        {
            if (part == null) return;
            void OnDamage(UnitPart.OnApplyDamage e)
            {
                float total = e.impactDamage + e.pierceDamage + e.fireDamage + e.blastDamage;
                float floor = Plugin.Cfg(Plugin.RTBMinHitDamage, 1f);
                if (total > floor) hits++;
            }
            void OnDetached(UnitPart p) { detaches++; }
            void OnEngineDamage() { engineHits++; }
            void OnEngineDisable() { engineLosses++; }
            part.onApplyDamage += OnDamage;
            part.onPartDetached += OnDetached;
            unsubscribers.Add(() =>
            {
                if (part == null) return;
                part.onApplyDamage -= OnDamage;
                part.onPartDetached -= OnDetached;
            });
            IEngine engine = null;
            try { part.TryGetComponent(out engine); } catch { }
            if (engine != null)
            {
                engine.OnEngineDamage += OnEngineDamage;
                engine.OnEngineDisable += OnEngineDisable;
                unsubscribers.Add(() =>
                {
                    engine.OnEngineDamage -= OnEngineDamage;
                    engine.OnEngineDisable -= OnEngineDisable;
                });
            }
        }
        public void Detach()
        {
            for (int i = 0; i < unsubscribers.Count; i++)
            {
                try { unsubscribers[i](); } catch { }
            }
            unsubscribers.Clear();
            attached = false;
        }
        public float Score()
        {
            if (!attached) return 0f;
            float hitCount = Plugin.Cfg(Plugin.RTBHitCount, 5f);
            float engineCount = Plugin.Cfg(Plugin.RTBEngineHitCount, 3f);
            float score = 0f;
            if (hitCount > 0f) score += hits / hitCount;
            if (engineCount > 0f) score += engineHits / engineCount;
            if (Plugin.RTBDetachTriggers == null || Plugin.RTBDetachTriggers.Value) score += detaches;
            if (Plugin.RTBEngineLossTriggers == null || Plugin.RTBEngineLossTriggers.Value) score += engineLosses;
            return score;
        }
        public bool ShouldReturn()
        {
            if (Plugin.RTBOnDamage == null || !Plugin.RTBOnDamage.Value) return false;
            return Score() >= 1f;
        }
        public string Describe()
        {
            return $"hits {hits} engineDmg {engineHits} detach {detaches} engineLost {engineLosses} (score {Score():F2})";
        }
    }
}