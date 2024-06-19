using BepInEx;
using R2API;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace EarlyAccessBands
{
    [BepInDependency(LanguageAPI.PluginGUID)]
    [BepInPlugin("com.Moffein.EarlyAccessBands", "EarlyAccessBands", "1.0.0")]
    public class EarlyAccessBandsPlugin : BaseUnityPlugin
    {
        public static float iceBaseDamage = 2.5f;
        public static float iceStackDamage = 2.5f;

        public static float fireBaseDamage = 3f;
        public static float fireStackDamage = 3f;

        public static float procChance = 8f;

        public static GameObject iceRingExplosionEffectPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/ElementalRings/IceRingExplosion.prefab").WaitForCompletion();
        public static GameObject fireTornadoProjectilePrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/ElementalRings/FireTornado.prefab").WaitForCompletion();

        public void Awake()
        {
            ReadConfig();
            SetLanguage();

            On.RoR2.CharacterBody.ElementalRingsBehavior.FixedUpdate += RemoveRingBuff;
            GlobalEventManager.onServerDamageDealt += GlobalEventManager_onServerDamageDealt;
        }

        private void ReadConfig()
        {
            procChance = Config.Bind("Stats", "Proc Chance", 8f, "Chance for this item to proc.").Value;

            iceBaseDamage = Config.Bind("Stats", "Runalds Band - Initial Damage", 2.5f, "Damage of the first stack of this item.").Value;
            iceStackDamage = Config.Bind("Stats", "Runalds Band - Stack Damage", 2.5f, "Damage of extra stacks of this item. Set to 1.25 for the original.").Value;

            fireBaseDamage = Config.Bind("Stats", "Kjaros Band - Initial Damage", 3f, "Damage of the first stack of this item. Set to 5 for the original. Default is set lower since the original Fire Tornado was a lot smaller than the current one.").Value;
            fireStackDamage = Config.Bind("Stats", "Kjaros Band - Stack Damage", 3f, "Damage of extra stacks of this item. Set to 2.5 for the original.").Value;
        }

        private void SetLanguage()
        {
            string iceString = $"<style=cIsDamage>{procChance}%</style> chance on hit to strike an enemy with a <style=cIsDamage>runic ice blast</style>, <style=cIsUtility>slowing</style> them by <style=cIsUtility>80%</style> and dealing <style=cIsDamage>{iceBaseDamage * 100f}%</style> <style=cStack>(+{iceStackDamage * 100f}% per stack)</style> TOTAL damage.";
            string fireString = $"<style=cIsDamage>{procChance}%</style> chance on hit to strike an enemy with a <style=cIsDamage>runic flame tornado</style>, dealing <style=cIsDamage>{fireBaseDamage * 100f}%</style> <style=cStack>(+{fireStackDamage * 100f}% per stack)</style> TOTAL damage.";

            LanguageAPI.Add("ITEM_ICERING_DESC", iceString);
            LanguageAPI.Add("ITEM_FIRERING_DESC", fireString);

            LanguageAPI.Add("ITEM_ICERING_PICKUP", "Chance to blast enemies with runic ice.");
            LanguageAPI.Add("ITEM_FIRERING_PICKUP", "Chance to blast enemies with a runic flame tornado.");
        }

        private void GlobalEventManager_onServerDamageDealt(DamageReport damageReport)
        {
            if (damageReport.damageInfo.rejected || damageReport.damageInfo.procCoefficient <= 0f || !damageReport.attackerBody || !damageReport.victimBody || damageReport.damageInfo.procChainMask.HasProc(ProcType.Rings)) return;

            CharacterBody attackerBody = damageReport.attackerBody;

            int iceCount = attackerBody.inventory.GetItemCount(RoR2Content.Items.IceRing);
            int fireCount = attackerBody.inventory.GetItemCount(RoR2Content.Items.FireRing);
            if ((iceCount + fireCount <= 0) || !Util.CheckRoll(procChance, attackerBody.master)) return;

            DamageInfo damageInfo = damageReport.damageInfo;
            damageInfo.procChainMask.AddProc(ProcType.Rings);

            CharacterBody victimBody = damageReport.victimBody;
            
            //Actual code here is mostly copypasted from current RoR2. It's just the proc condition that changed.
            if (iceCount > 0)
            {
                float damageCoefficient = iceBaseDamage + (iceCount - 1) * iceStackDamage;
                float damage2 = Util.OnHitProcDamage(damageInfo.damage, attackerBody.damage, damageCoefficient);
                DamageInfo iceDamageInfo = new DamageInfo
                {
                    damage = damage2,
                    damageColorIndex = DamageColorIndex.Item,
                    damageType = DamageType.Generic,
                    attacker = damageInfo.attacker,
                    crit = damageInfo.crit,
                    force = Vector3.zero,
                    inflictor = null,
                    position = damageInfo.position,
                    procChainMask = damageInfo.procChainMask,
                    procCoefficient = 1f
                };

                EffectManager.SimpleImpactEffect(iceRingExplosionEffectPrefab, damageInfo.position, Vector3.up, true);

                victimBody.AddTimedBuff(RoR2Content.Buffs.Slow80, 3f * iceCount);
                victimBody.healthComponent.TakeDamage(iceDamageInfo);
            }

            if (fireCount > 0)
            {
                GameObject projectilePrefab = fireTornadoProjectilePrefab;
                float resetInterval = projectilePrefab.GetComponent<ProjectileOverlapAttack>().resetInterval;
                float lifetime = projectilePrefab.GetComponent<ProjectileSimple>().lifetime;
                float damageCoefficient9 = fireBaseDamage + (fireCount - 1) * fireStackDamage;
                float damage3 = Util.OnHitProcDamage(damageInfo.damage, attackerBody.damage, damageCoefficient9) / lifetime * resetInterval;
                float speedOverride = 0f;
                Quaternion rotation2 = Quaternion.identity;
                Vector3 vector = damageInfo.position - attackerBody.aimOrigin;
                vector.y = 0f;
                if (vector != Vector3.zero)
                {
                    speedOverride = -1f;
                    rotation2 = Util.QuaternionSafeLookRotation(vector, Vector3.up);
                }
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                {
                    damage = damage3,
                    crit = damageInfo.crit,
                    damageColorIndex = DamageColorIndex.Item,
                    position = damageInfo.position,
                    procChainMask = damageInfo.procChainMask,
                    force = 0f,
                    owner = damageInfo.attacker,
                    projectilePrefab = projectilePrefab,
                    rotation = rotation2,
                    speedOverride = speedOverride,
                    target = null
                });
            }
        }

        private void RemoveRingBuff(On.RoR2.CharacterBody.ElementalRingsBehavior.orig_FixedUpdate orig, RoR2.CharacterBody.ElementalRingsBehavior self)
        {
            if (NetworkServer.active)
            {
                if (self.body.HasBuff(RoR2Content.Buffs.ElementalRingsReady)) self.body.RemoveBuff(RoR2Content.Buffs.ElementalRingsReady);
                if (self.body.HasBuff(RoR2Content.Buffs.ElementalRingsCooldown)) self.body.ClearTimedBuffs(RoR2Content.Buffs.ElementalRingsCooldown);
                Destroy(this);
            }
        }
    }
}
