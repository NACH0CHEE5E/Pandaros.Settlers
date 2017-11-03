﻿using BlockTypes.Builtin;
using Pipliz.JSON;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pandaros.Settlers.Items.Food
{
    [ModLoader.ModManager]
    public static class BerryPie
    {
        public static ItemTypesServer.ItemTypeRaw Item { get; private set; }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.AfterItemTypesDefined, GameLoader.NAMESPACE + ".Items.Food.RegisterBerryPie")]
        public static void RegisterBerryPie()
        {
            var flour = new InventoryItem(BuiltinBlocks.Flour, 4);
            var Berries = new InventoryItem(BuiltinBlocks.Berry, 4);

            var invItem = new InventoryItem(Item.ItemIndex);
            var recipe = new Recipe(Item.name, new List<InventoryItem>() { flour, Berries }, invItem, 50);

            RecipeStorage.AddDefaultLimitTypeRecipe(ItemFactory.JOB_BAKER, recipe);
        }


        [ModLoader.ModCallback(ModLoader.EModCallbackType.AfterAddingBaseTypes, GameLoader.NAMESPACE + ".Items.Food.AddBerryPie"), ModLoader.ModCallbackDependsOn("pipliz.blocknpcs.addlittypes")]
        public static void AddBerryPie(Dictionary<string, ItemTypesServer.ItemTypeRaw> items)
        {
            var foodName = GameLoader.NAMESPACE + ".BerryPie";
            var foodNode = new JSONNode();
            foodNode["icon"] = new JSONNode(GameLoader.ICON_FOLDER_PANDA.Replace("\\", "/") + "/BerryPie.png");
            foodNode["isPlaceable"] = new JSONNode(false);
            foodNode["NutritionalValue"] = new JSONNode(6);

            Item = new ItemTypesServer.ItemTypeRaw(foodName, foodNode);
            items.Add(foodName, Item);
        }
    }
}
