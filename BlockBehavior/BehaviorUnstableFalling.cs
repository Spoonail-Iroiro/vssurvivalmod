﻿using System;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Spawns an EntityBlockFalling when the user places a block that has air underneath it or if a neighbor block is
    /// removed and causes air to be underneath it.
    /// </summary>
    public class BlockBehaviorUnstableFalling : BlockBehavior
    {
        bool ignorePlaceTest;
        AssetLocation[] exceptions;
        public bool fallSideways;
        bool dustyFall;
        float fallSidewaysChance = 0.25f;

        AssetLocation fallSound;
        float impactDamageMul;

        public BlockBehaviorUnstableFalling(Block block) : base(block)
        {
        }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);

            ignorePlaceTest = properties["ingorePlaceTest"].AsBool(false);
            exceptions = properties["exceptions"].AsObject<AssetLocation[]>(new AssetLocation[0], block.Code.Domain);
            fallSideways = properties["fallSideways"].AsBool(false);
            dustyFall = properties["dustyFall"].AsBool(false);

            fallSidewaysChance = properties["fallSidewaysChance"].AsFloat(0.25f);
            string sound = properties["fallSound"].AsString(null);
            if (sound != null)
            {
                fallSound = AssetLocation.Create(sound, block.Code.Domain);
            }
            impactDamageMul = properties["impactDamageMul"].AsFloat(1f);
        }

        public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling, ref string failureCode)
        {
            handling = EnumHandling.PassThrough;
            if (ignorePlaceTest) return true;

            Block onBlock = world.BlockAccessor.GetBlock(blockSel.Position.DownCopy());
            if (blockSel != null && !onBlock.SideSolid[BlockFacing.UP.Index] && block.Attributes?["allowUnstablePlacement"].AsBool() != true && !exceptions.Contains(onBlock.Code))
            {
                handling = EnumHandling.PreventSubsequent;
                failureCode = "requiresolidground";
                return false;
            }

            return TryFalling(world, blockSel.Position, ref handling, ref failureCode);
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos, ref EnumHandling handling)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos, ref handling);

            EnumHandling bla = EnumHandling.PassThrough;
            string bla2 = "";
            TryFalling(world, pos, ref bla, ref bla2);
        }

        private bool TryFalling(IWorldAccessor world, BlockPos pos, ref EnumHandling handling, ref string failureCode)
        {
            if (world.Side == EnumAppSide.Server)
            {
                ICoreServerAPI sapi = (world as IServerWorldAccessor).Api as ICoreServerAPI;
                if (!sapi.Server.Config.AllowFallingBlocks) return false;
            }

            if (IsReplacableBeneath(world, pos) || (fallSideways && world.Rand.NextDouble() < fallSidewaysChance && IsReplacableBeneathAndSideways(world, pos)))
            {
                // Prevents duplication
                Entity entity = world.GetNearestEntity(pos.ToVec3d().Add(0.5, 0.5, 0.5), 1, 1.5f, (e) =>
                {
                    return e is EntityBlockFalling && ((EntityBlockFalling)e).initialPos.Equals(pos);
                });

                if (entity == null)
                {
                    EntityBlockFalling entityblock = new EntityBlockFalling(block, world.BlockAccessor.GetBlockEntity(pos), pos, fallSound, impactDamageMul, true, dustyFall);
                    world.SpawnEntity(entityblock);
                } else
                {
                    handling = EnumHandling.PreventDefault;
                    failureCode = "entityintersecting";
                    return false;
                }

                handling = EnumHandling.PreventSubsequent;
                return true;
            }

            handling = EnumHandling.PreventSubsequent;
            return true;
        }

        private bool IsReplacableBeneathAndSideways(IWorldAccessor world, BlockPos pos)
        {
            for (int i = 0; i < 4; i++)
            {
                BlockFacing facing = BlockFacing.HORIZONTALS[i];
                if (
                    world.BlockAccessor.GetBlock(pos.X + facing.Normali.X, pos.Y + facing.Normali.Y, pos.Z + facing.Normali.Z).Replaceable >= 6000 &&
                    world.BlockAccessor.GetBlock(pos.X + facing.Normali.X, pos.Y + facing.Normali.Y - 1, pos.Z + facing.Normali.Z).Replaceable >= 6000)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsReplacableBeneath(IWorldAccessor world, BlockPos pos)
        {
            Block bottomBlock = world.BlockAccessor.GetBlock(pos.X, pos.Y - 1, pos.Z);
            return (bottomBlock != null && bottomBlock.Replaceable > 6000);
        }
    }
}
