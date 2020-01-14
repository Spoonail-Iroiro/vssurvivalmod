﻿using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public abstract class BlockEntityItemPile : BlockEntity
    {
        public InventoryGeneric inventory;
        public object inventoryLock = new object(); // Because OnTesselation runs in another thread

        public abstract AssetLocation SoundLocation { get; }
        public bool RandomizeSoundPitch;
        public abstract string BlockCode { get; }
        public abstract int MaxStackSize { get; }

        

        public virtual int DefaultTakeQuantity { get { return 1; } }
        public virtual int BulkTakeQuantity { get { return 4; } }

        public int OwnStackSize
        {
            get { return inventory[0]?.StackSize ?? 0; }
        }

        public Size2i AtlasSize
        {
            get { return ((ICoreClientAPI)Api).BlockTextureAtlas.Size; }
        }

        public BlockEntityItemPile()
        {
            inventory = new InventoryGeneric(1, BlockCode, null, null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            inventory.LateInitialize(BlockCode + "-" + Pos.ToString(), api);
            inventory.ResolveBlocksOrItems();
        }

        public override void OnBlockBroken()
        {
            if (Api.World is IServerWorldAccessor)
            {
                ItemSlot slot = inventory[0];
                while (slot.StackSize > 0)
                {
                    ItemStack split = slot.TakeOut(GameMath.Clamp(slot.StackSize, 1, System.Math.Max(1, slot.Itemstack.Collectible.MaxStackSize / 4)));
                    Api.World.SpawnItemEntity(split, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }
        }


        public override void FromTreeAtributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAtributes(tree, worldForResolving);
            inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
            if (Api != null)
            {
                inventory.Api = Api;
                inventory.ResolveBlocksOrItems();
            }

            if (Api is ICoreClientAPI)
            {
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            ITreeAttribute invtree = new TreeAttribute();
            inventory.ToTreeAttributes(invtree);
            tree["inventory"] = invtree;
        }



        public virtual bool OnPlayerInteract(IPlayer byPlayer)
        {
            BlockPos abovePos = Pos.UpCopy();

            BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(abovePos);
            if (be is BlockEntityItemPile)
            {
                return ((BlockEntityItemPile)be).OnPlayerInteract(byPlayer);
            }

            bool sneaking = byPlayer.Entity.Controls.Sneak;

          
            ItemSlot hotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;

            bool equalStack = hotbarSlot.Itemstack != null && hotbarSlot.Itemstack.Equals(Api.World, inventory[0].Itemstack, GlobalConstants.IgnoredStackAttributes);

            if (sneaking && !equalStack)
            {
                return false;
            }

            if (sneaking && equalStack && OwnStackSize >= MaxStackSize)
            {
                Block pileblock = Api.World.BlockAccessor.GetBlock(Pos);
                Block aboveblock = Api.World.BlockAccessor.GetBlock(abovePos);

                if (aboveblock.IsReplacableBy(pileblock))
                {
                    if (Api.World is IServerWorldAccessor)
                    {
                        Api.World.BlockAccessor.SetBlock((ushort)pileblock.Id, abovePos);
                        BlockEntityItemPile bep = Api.World.BlockAccessor.GetBlockEntity(abovePos) as BlockEntityItemPile;
                        if (bep != null) bep.TryPutItem(byPlayer);
                    }
                    return true;
                }

                return false;
            }

            lock (inventoryLock)
            {
                if (sneaking)
                {
                    return TryPutItem(byPlayer);
                }
                else
                {
                    return TryTakeItem(byPlayer);
                }
            }
        }


        public virtual bool TryPutItem(IPlayer player)
        {
            if (OwnStackSize >= MaxStackSize) return false;

            ItemSlot hotbarSlot = player.InventoryManager.ActiveHotbarSlot;

            if (hotbarSlot.Itemstack == null) return false;

            ItemSlot invSlot = inventory[0];

            if (invSlot.Itemstack == null)
            {
                invSlot.Itemstack = hotbarSlot.Itemstack.Clone();
                invSlot.Itemstack.StackSize = 0;
                Api.World.PlaySoundAt(SoundLocation, Pos.X, Pos.Y, Pos.Z, null, 0.88f + (float)Api.World.Rand.NextDouble() * 0.24f, 16);
            }

            if (invSlot.Itemstack.Equals(Api.World, hotbarSlot.Itemstack, GlobalConstants.IgnoredStackAttributes))
            {
                bool putBulk = player.Entity.Controls.Sprint;

                int q = GameMath.Min(hotbarSlot.StackSize, putBulk ? BulkTakeQuantity : DefaultTakeQuantity, MaxStackSize - OwnStackSize);

                invSlot.Itemstack.StackSize += q;
                if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                {
                    hotbarSlot.TakeOut(q);
                    hotbarSlot.OnItemSlotModified(null);
                }

                Api.World.PlaySoundAt(SoundLocation, Pos.X, Pos.Y, Pos.Z, player, 0.88f + (float)Api.World.Rand.NextDouble() * 0.24f, 16);

                MarkDirty();

                Cuboidf[] collBoxes = Api.World.BlockAccessor.GetBlock(Pos).GetCollisionBoxes(Api.World.BlockAccessor, Pos);
                if (collBoxes != null && collBoxes.Length > 0 && CollisionTester.AabbIntersect(collBoxes[0], Pos.X, Pos.Y, Pos.Z, player.Entity.CollisionBox, player.Entity.LocalPos.XYZ))
                {
                    player.Entity.LocalPos.Y += collBoxes[0].Y2 - (player.Entity.LocalPos.Y - (int)player.Entity.LocalPos.Y);
                }

                (player as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);

                return true;
            }

            return false;
        }

        public bool TryTakeItem(IPlayer player)
        {
            bool takeBulk = player.Entity.Controls.Sprint;
            int q = GameMath.Min(takeBulk ? BulkTakeQuantity : DefaultTakeQuantity, OwnStackSize);

            if (inventory[0]?.Itemstack != null)
            {
                ItemStack stack = inventory[0].TakeOut(q);
                player.InventoryManager.TryGiveItemstack(stack);

                if (stack.StackSize > 0)
                {
                    Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }

            if (OwnStackSize == 0)
            {
                Api.World.BlockAccessor.SetBlock(0, Pos);
            }

            Api.World.PlaySoundAt(SoundLocation, Pos.X, Pos.Y, Pos.Z, player, 0.88f + (float)Api.World.Rand.NextDouble() * 0.24f, 16);

            MarkDirty();

            (player as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);

            return true;
        }


        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            ItemStack stack = inventory[0].Itemstack;
            if (stack == null) return;

            dsc.AppendLine(stack.StackSize + "x " + stack.GetName());
        }


        public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping)
        {
            ItemStack stack = inventory?[0]?.Itemstack;
            if (stack?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve) == false)
            {
                inventory[0].Itemstack = null;
            }
        }

        public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
        {
            ItemStack stack = inventory?[0]?.Itemstack;
            if (stack != null)
            {
                stack.Collectible.OnStoreCollectibleMappings(Api.World, inventory[0], blockIdMapping, itemIdMapping);
            }
        }
    }
}
