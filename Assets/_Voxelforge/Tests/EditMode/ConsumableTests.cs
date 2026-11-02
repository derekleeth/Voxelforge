using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Voxelforge.Gameplay;
using Object = UnityEngine.Object;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 5.8 steps E and F: potions, their shared cooldown, their action bar slots and elixirs, without any networking.</summary>
    public class ConsumableTests
    {
        const ushort Group = AbilityIds.PotionCooldown;
        readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object made in _made)
                Object.DestroyImmediate(made);
            _made.Clear();
        }

        AbilityDefinition Health(ushort id, float amount) => Keep(AbilityDefinition.Create(id, $"Health {amount}", cooldown: 30f, range: 0f,
            projectileSpeed: 0f, blockDamage: 0f, delivery: AbilityDelivery.Drink, healAmount: amount, cooldownGroup: Group));

        AbilityDefinition Mana(ushort id, float amount) => Keep(AbilityDefinition.Create(id, $"Mana {amount}", cooldown: 30f, range: 0f,
            projectileSpeed: 0f, blockDamage: 0f, delivery: AbilityDelivery.Drink, manaAmount: amount, cooldownGroup: Group));

        AbilityDefinition Elixir(ushort id, GearStats buff) => Keep(AbilityDefinition.Create(id, "Elixir", range: 0f, projectileSpeed: 0f,
            blockDamage: 0f, delivery: AbilityDelivery.Drink, duration: 300f, buff: buff));

        ItemDefinition Item(ushort id, AbilityDefinition drink) =>
            Keep(ItemDefinition.Create(id, $"Item {id}", category: ItemCategory.Consumables, drink: drink));

        T Keep<T>(T made) where T : Object
        {
            _made.Add(made);
            return made;
        }

        // ---- What a drink is ----

        [Test]
        public void KindOf_TellsPotionsAndElixirsApart_AndOnlyDrinksCount()
        {
            Assert.AreEqual(DrinkKind.HealthPotion, ConsumableRules.KindOf(Health(1, 50f)));
            Assert.AreEqual(DrinkKind.ManaPotion, ConsumableRules.KindOf(Mana(2, 50f)));
            Assert.AreEqual(DrinkKind.Elixir, ConsumableRules.KindOf(Elixir(3, new GearStats(20, 0, 0))));
            Assert.AreEqual(DrinkKind.None, ConsumableRules.KindOf(Keep(AbilityDefinition.Create(4, "Bolt"))));
            Assert.AreEqual(DrinkKind.None, ConsumableRules.KindOf(null));
        }

        [Test]
        public void Book_RefusesADrinkThatDoesNothing_AndAnElixirWithNoTime()
        {
            AbilityDefinition nothing = Keep(AbilityDefinition.Create(1, "Water", range: 0f, delivery: AbilityDelivery.Drink));
            AbilityDefinition timeless = Keep(AbilityDefinition.Create(2, "Timeless", range: 0f, delivery: AbilityDelivery.Drink,
                buff: new GearStats(5, 0, 0)));
            Assert.IsNotEmpty(AbilityBook.Check(nothing));
            Assert.IsNotEmpty(AbilityBook.Check(timeless));
            Assert.IsEmpty(AbilityBook.Check(Health(3, 50f)));
            Assert.IsEmpty(AbilityBook.Check(Elixir(4, new GearStats(5, 0, 0))));
        }

        [Test]
        public void Registry_FindsADrinkBothWays_AndRefusesOneThatIsntAConsumable()
        {
            AbilityDefinition potion = Health(1, 50f);
            ItemDefinition good = Item(1340, potion);
            ItemRegistry registry = Keep(ItemRegistry.Create(new[] { good }));
            Assert.IsTrue(registry.TryGetDrink(1340, out AbilityDefinition found));
            Assert.AreEqual(potion, found);
            Assert.IsTrue(registry.TryGetDrinkItem(potion.Id, out ItemDefinition item));
            Assert.AreEqual(good, item);
            Assert.AreEqual(1, registry.Drinks.Count);
            Assert.IsFalse(registry.TryGetDrink(1341, out _));

            ItemDefinition wrong = Keep(ItemDefinition.Create(1341, "Odd", category: ItemCategory.Materials, drink: potion));
            ItemRegistry bad = Keep(ItemRegistry.Create(new[] { wrong }));
            Assert.That(bad.Validate(null), Has.Some.Contains("Consumables"));
        }

        // ---- May they drink? ----

        [Test]
        public void Check_SavesThePotion_WhenItWouldBeWasted()
        {
            AbilityDefinition health = Health(1, 50f);
            AbilityDefinition mana = Mana(2, 50f);
            Assert.AreEqual(CastCheck.NoPotion, ConsumableRules.Check(health, 0, 10f, 100f, 10f, 100f));
            Assert.AreEqual(CastCheck.FullHealth, ConsumableRules.Check(health, 1, 100f, 100f, 10f, 100f));
            Assert.AreEqual(CastCheck.Ok, ConsumableRules.Check(health, 1, 99f, 100f, 100f, 100f));
            Assert.AreEqual(CastCheck.FullMana, ConsumableRules.Check(mana, 1, 10f, 100f, 100f, 100f));
            Assert.AreEqual(CastCheck.Ok, ConsumableRules.Check(mana, 1, 100f, 100f, 99f, 100f));
        }

        [Test]
        public void Check_LetsAnElixirBeDrunkAtFullHealthAndMana()
        {
            AbilityDefinition elixir = Elixir(3, new GearStats(0, 0, 20));
            Assert.AreEqual(CastCheck.Ok, ConsumableRules.Check(elixir, 1, 100f, 100f, 100f, 100f));
            Assert.AreEqual(CastCheck.NoPotion, ConsumableRules.Check(elixir, 0, 100f, 100f, 100f, 100f));
        }

        // ---- The shared cooldown ----

        [Test]
        public void Cooldown_IsSharedByPotions_ButNotBySpellsOrElixirs()
        {
            AbilityDefinition health = Health(1, 50f);
            AbilityDefinition mana = Mana(2, 50f);
            AbilityDefinition spell = Keep(AbilityDefinition.Create(3, "Bolt", cooldown: 5f));
            AbilityDefinition elixir = Elixir(4, new GearStats(5, 0, 0));
            var caster = new AbilityCaster(100f, 0f);
            var finished = new List<AbilityDefinition>();

            Assert.AreEqual(CastCheck.Ok, caster.TryBegin(health, 0f, finished));
            Assert.AreEqual(CastCheck.OnCooldown, caster.Check(mana, 1f), "Drinking one potion puts them all on cooldown.");
            Assert.AreEqual(CastCheck.OnCooldown, caster.Check(health, 29f));
            Assert.AreEqual(CastCheck.Ok, caster.Check(spell, 1f));
            Assert.AreEqual(CastCheck.Ok, caster.Check(elixir, 1f));
            Assert.AreEqual(CastCheck.Ok, caster.Check(mana, 30f), "Ready again after 30 s.");
        }

        // ---- The potion slots ----

        [Test]
        public void BestPotion_IsTheStrongestOwned_OrTheWeakestWithNone()
        {
            AbilityDefinition minor = Health(1, 50f);
            AbilityDefinition greater = Health(2, 200f);
            AbilityDefinition mana = Mana(3, 50f);
            ItemDefinition[] drinks = { Item(1340, greater), Item(1341, minor), Item(1342, mana) };

            var both = new List<ItemStack> { new ItemStack(1341, 3), new ItemStack(1340, 1) };
            Assert.AreEqual(greater, ConsumableRules.BestPotion(drinks, both, DrinkKind.HealthPotion));
            var onlyMinor = new List<ItemStack> { new ItemStack(1341, 3) };
            Assert.AreEqual(minor, ConsumableRules.BestPotion(drinks, onlyMinor, DrinkKind.HealthPotion));
            var none = new List<ItemStack>();
            Assert.AreEqual(minor, ConsumableRules.BestPotion(drinks, none, DrinkKind.HealthPotion), "An empty slot still says what goes there.");
            Assert.AreEqual(mana, ConsumableRules.BestPotion(drinks, none, DrinkKind.ManaPotion));
            Assert.IsNull(ConsumableRules.BestPotion(new ItemDefinition[0], none, DrinkKind.ManaPotion));
        }

        [Test]
        public void WithPotions_FillsSlotsSevenAndEight_AndLeavesTheRest()
        {
            var slots = new ActionBarSlots { Slot1 = 5, Slot7 = 99, Slot9 = 13 };
            ActionBarSlots result = ConsumableRules.WithPotions(slots, 18, 21);
            Assert.AreEqual(18, result.Slot7);
            Assert.AreEqual(21, result.Slot8);
            Assert.AreEqual(5, result.Slot1);
            Assert.AreEqual(13, result.Slot9);
        }

        // ---- Words ----

        [Test]
        public void Text_SaysWhatADrinkDoes()
        {
            Assert.AreEqual("Heals 50 health", GearText.Drink(Health(1, 50f)));
            Assert.AreEqual("Restores 80 mana", GearText.Drink(Mana(2, 80f)));
            Assert.AreEqual("+20 Armor, +5 Power for 5 min", GearText.Drink(Elixir(3, new GearStats(20, 0, 5))));
            Assert.AreEqual("+20 Stamina", GearText.Bonus(new GearStats(0, 20, 0)));
        }

        // ---- The potions and elixirs in the project ----

        static T Load<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>($"Assets/_Voxelforge/Data/{path}");

        [Test]
        public void Project_HasThreeTiersOfEachPotion_AndThreeElixirs_ThatGetStronger()
        {
            var items = Load<ItemRegistry>("Items/ItemRegistry.asset");
            var health = new List<float>();
            var mana = new List<float>();
            int elixirs = 0;
            foreach (ItemDefinition item in items.Drinks)
            {
                switch (ConsumableRules.KindOf(item.Drink))
                {
                    case DrinkKind.HealthPotion:
                        health.Add(item.Drink.HealAmount);
                        Assert.AreEqual(AbilityIds.PotionCooldown, item.Drink.CooldownKey, item.DisplayName);
                        Assert.AreEqual(30f, item.Drink.Cooldown, item.DisplayName);
                        break;
                    case DrinkKind.ManaPotion:
                        mana.Add(item.Drink.ManaAmount);
                        Assert.AreEqual(AbilityIds.PotionCooldown, item.Drink.CooldownKey, item.DisplayName);
                        Assert.AreEqual(30f, item.Drink.Cooldown, item.DisplayName);
                        break;
                    case DrinkKind.Elixir:
                        elixirs++;
                        Assert.AreEqual(300f, item.Drink.Duration, item.DisplayName);
                        break;
                }
            }
            Assert.AreEqual(3, health.Count);
            Assert.AreEqual(3, mana.Count);
            Assert.AreEqual(3, elixirs);
            health.Sort();
            mana.Sort();
            Assert.Less(health[0], health[1]);
            Assert.Less(health[1], health[2]);
            Assert.Less(mana[0], mana[1]);
            Assert.Less(mana[1], mana[2]);
        }

        [Test]
        public void Project_EveryDrink_IsInTheBooks_HasAnIcon_AndIsBrewedAtAnAlchemyTable()
        {
            var items = Load<ItemRegistry>("Items/ItemRegistry.asset");
            var abilities = Load<AbilityBook>("Abilities/AbilityBook.asset");
            var looks = Load<AbilityLookBook>("Abilities/Looks/AbilityLookBook.asset");
            var recipes = Load<RecipeBook>("Items/RecipeBook.asset");
            foreach (ItemDefinition item in items.Drinks)
            {
                Assert.IsTrue(abilities.TryGet(item.Drink.Id, out _), item.DisplayName);
                Assert.IsTrue(looks.TryGet(item.Drink.Id, out AbilityLook look), item.DisplayName);
                Assert.IsNotNull(look.Icon, item.DisplayName);
                Assert.IsNotNull(look.Release, item.DisplayName);
                bool brewed = false;
                foreach (RecipeDefinition recipe in recipes.Recipes)
                    brewed |= recipe.Output.ItemId == item.Id && recipe.Station == CraftStation.AlchemyTable;
                Assert.IsTrue(brewed, $"{item.DisplayName} has no Alchemy recipe.");
            }
        }
    }
}
