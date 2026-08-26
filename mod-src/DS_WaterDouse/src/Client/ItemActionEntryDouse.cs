using System;
using Audio;
using UnityEngine;
using UnityEngine.Scripting;

namespace DSWaterDouse
{
    /// <summary>
    /// "Douse" context-menu entry for water items (a separate option next to the
    /// vanilla Use/Drink entry). Consumes one unit of the item, cuts the local scent
    /// radius (dedicated client) and reports the douse to the server, which applies
    /// the authoritative cut for zombie AI and the smell display.
    /// </summary>
    [Preserve]
    public class ItemActionEntryDouse : BaseItemActionEntry
    {
        private readonly XUiC_ItemStack stackControl;

        public ItemActionEntryDouse(XUiC_ItemStack _controller)
            : base(_controller, "lblContextActionDouse", "ui_game_symbol_water", GamepadShortCut.DPadLeft)
        {
            stackControl = _controller;
            RefreshEnabled();
        }

        public override void RefreshEnabled()
        {
            Enabled = false;
            if (stackControl == null || stackControl.xui == null || stackControl.xui.playerUI == null) return;
            EntityPlayerLocal player = stackControl.xui.playerUI.entityPlayer as EntityPlayerLocal;
            if (player == null || player.IsDead()) return;
            if ((bool)player.AttachedToEntity) return; // no dousing from a vehicle
            ItemStack stack = stackControl.ItemStack;
            if (stack == null || stack.IsEmpty() || stack.count <= 0) return;
            if (!DouseConfig.IsDouseable(stack.itemValue.ItemClass)) return;
            // Anything to wash off? On a dedicated server the client's own
            // PlayerStealth.smellRadius never moves (vanilla: only the server copy
            // decays), so use the server-synced "smell" cvar — the same value that
            // drives the "N M" smell display.
            if (player.Buffs.GetCustomVar("smell") < 1f) return;
            Enabled = true;
        }

        public override void OnActivated()
        {
            try
            {
                if (stackControl == null || stackControl.xui == null || stackControl.xui.playerUI == null) return;
                EntityPlayerLocal player = stackControl.xui.playerUI.entityPlayer as EntityPlayerLocal;
                if (player == null) return;
                ItemStack stack = stackControl.ItemStack;
                if (stack == null || stack.IsEmpty() || stack.count <= 0) return;
                ItemClass itemClass = stack.itemValue.ItemClass;
                if (!DouseConfig.IsDouseable(itemClass)) return;

                bool fullClear = DouseConfig.IsFullClear(itemClass);
                float meters = DouseConfig.MetersFor(itemClass);

                // Consume one unit (same pattern as ItemActionEntryUse).
                ItemStack newStack = new ItemStack(stack.itemValue.Clone(), stack.count - 1);
                stackControl.ItemStack = newStack.count > 0 ? newStack : ItemStack.Empty;
                stackControl.WindowGroup.Controller.SetAllChildrenDirty();

                // The vanilla drink refunds the empty jar via the item's Eat action
                // (Create_item -> drinkJarEmpty). The douse skips the action entirely,
                // so refund it here or every douse destroys the jar. Goes through
                // XUiM_PlayerInventory.AddItem so it lands in the backpack or any of
                // the 15 toolbelt slots (incl. the extended ones), like a normal pickup.
                if (DouseConfig.Instance.RefundEmptyJar)
                {
                    RefundJar(player, itemClass);
                }

                float removed = DouseClient.ApplyDouse(player, fullClear, meters);

                Manager.Play(player, DouseConfig.Instance.SoundName);
                if (fullClear)
                {
                    GameManager.ShowTooltip(player, Localization.Get("douseFeedbackFull"));
                }
                else
                {
                    GameManager.ShowTooltip(player, string.Format(Localization.Get("douseFeedbackPartial"), removed));
                }
                ParentActionList?.RefreshActionList();
            }
            catch (Exception e)
            {
                Log.Error("[DSDouse] ItemActionEntryDouse.OnActivated error: " + e);
            }
        }

        /// <summary>
        /// Refunds the item's empty container (the Eat action's Create_item, e.g.
        /// drinkJarEmpty), mirroring the vanilla drink's jar refund: added via
        /// XUiM_PlayerInventory (backpack first, then any toolbelt slot), dropped at
        /// the player's feet when there is no room (same as the vanilla behavior).
        /// </summary>
        private void RefundJar(EntityPlayerLocal player, ItemClass itemClass)
        {
            try
            {
                if (itemClass == null || itemClass.Actions == null || itemClass.Actions.Length == 0) return;
                ItemActionEat eat = itemClass.Actions[0] as ItemActionEat;
                if (eat == null || string.IsNullOrEmpty(eat.CreateItem)) return;
                int count = eat.CreateItemCount > 0 ? eat.CreateItemCount : 1;
                ItemStack jar = new ItemStack(ItemClass.GetItem(eat.CreateItem), count);
                if (!stackControl.xui.PlayerInventory.AddItem(jar, false))
                {
                    player.world.gameManager.ItemDropServer(jar, player.GetPosition(), Vector3.zero);
                }
            }
            catch (Exception e)
            {
                Log.Error("[DSDouse] Jar refund error: " + e);
            }
        }

        public override void OnDisabledActivate()
        {
            try
            {
                if (stackControl == null || stackControl.xui == null || stackControl.xui.playerUI == null) return;
                EntityPlayerLocal player = stackControl.xui.playerUI.entityPlayer as EntityPlayerLocal;
                if (player == null) return;
                if (player.Buffs.GetCustomVar("smell") < 1f)
                {
                    GameManager.ShowTooltip(player, Localization.Get("douseNoScent"));
                }
                else
                {
                    GameManager.ShowTooltip(player, Localization.Get("isBusy"));
                }
            }
            catch (Exception e)
            {
                Log.Error("[DSDouse] ItemActionEntryDouse.OnDisabledActivate error: " + e);
            }
        }
    }
}
