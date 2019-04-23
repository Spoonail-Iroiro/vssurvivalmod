﻿using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class BlockStalagSection : Block
    {
        public string[] Thicknesses = new string[] { "14", "12", "10", "08", "06", "04" };

        public string Thickness => Variant["thickness"];
        public int ThicknessInt;

        public override void OnLoaded(ICoreAPI api)
        {
            ThicknessInt = int.Parse(Variant["thickness"]);
            base.OnLoaded(api);
        }

        public Block GetBlock(IWorldAccessor world, string rocktype, string thickness)
        {
            return world.GetBlock(CodeWithParts(rocktype, thickness));
        }

        public Dictionary<string, int> thicknessIndex = new Dictionary<string, int>()
        {
            { "14", 0 },
            { "12", 1 },
            { "10", 2 },
            { "08", 3 },
            { "06", 4 },
            { "04", 5 }
        };

        Random random = new Random();


        public override void OnNeighourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            if (IsSurroundedByNonSolid(world, pos))
            {
                world.BlockAccessor.BreakBlock(pos, null);
            }
        }


        public bool IsSurroundedByNonSolid(IWorldAccessor world, BlockPos pos)
        {
            foreach (BlockFacing facing in BlockFacing.ALLFACES)
            {
                BlockPos neighborPos = pos.AddCopy(facing.Normali);
                Block neighborBlock = world.BlockAccessor.GetBlock(neighborPos);

                if (neighborBlock.SideSolid[facing.GetOpposite().Index] || neighborBlock is BlockStalagSection) return false;
            }
            return true;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
            //return TryPlaceBlockForWorldGen(world.BlockAccessor, blockSel.Position, blockSel.Face);
        }


        public override bool TryPlaceBlockForWorldGen(IBlockAccessor blockAccessor, BlockPos pos, BlockFacing onBlockFace)
        {
            bool didplace = false;

            if (blockAccessor.GetBlock(pos).Replaceable < 6000) return false;

            pos = pos.Copy();
            for (int i = 0; i < 5 + random.Next(25); i++)
            {
                didplace |= TryGenStalag(blockAccessor, pos, random.Next(4));
                pos.X += random.Next(9) - 4;
                pos.Z += random.Next(9) - 4;
                pos.Y += random.Next(3) - 1;
            }

            return didplace;
        }

        private bool TryGenStalag(IBlockAccessor blockAccessor, BlockPos pos, int thickOff)
        {
            bool didplace = false;

            for (int dy = 0; dy < 5; dy++)
            {
                Block block = blockAccessor.GetBlock(pos.X, pos.Y + dy, pos.Z);
                if (block.SideSolid[BlockFacing.DOWN.Index] && block.BlockMaterial == EnumBlockMaterial.Stone)
                {
                    string rocktype;
                    if (block.Variant.TryGetValue("rock", out rocktype))
                    {
                        GrowDownFrom(blockAccessor, pos.AddCopy(0, dy - 1, 0), rocktype, thickOff);
                        didplace = true;
                    }
                    break;
                }
                else if (block.Id != 0) break;
            }

            if (!didplace) return false;

            for (int dy = 0; dy < 12; dy++)
            {
                Block block = blockAccessor.GetBlock(pos.X, pos.Y - dy, pos.Z);
                if (block.SideSolid[BlockFacing.UP.Index] && block.BlockMaterial == EnumBlockMaterial.Stone)
                {
                    string rocktype;
                    if (block.Variant.TryGetValue("rock", out rocktype))
                    {
                        GrowUpFrom(blockAccessor, pos.AddCopy(0, -dy + 1, 0), rocktype, thickOff);
                        didplace = true;
                    }
                    break;
                }
                else if (block.Id != 0 && !(block is BlockStalagSection)) break;
            }

            return didplace;
        }

        private void GrowUpFrom(IBlockAccessor blockAccessor, BlockPos pos, string rocktype, int thickOff)
        {

            for (int i = thicknessIndex[Thickness] + thickOff; i < Thicknesses.Length; i++)
            {
                BlockStalagSection stalagBlock = (BlockStalagSection)GetBlock(api.World, rocktype, Thicknesses[i]);
                if (stalagBlock == null) continue;

                Block block = blockAccessor.GetBlock(pos);
                if (block.Replaceable >= 6000 || (block as BlockStalagSection)?.ThicknessInt < stalagBlock.ThicknessInt)
                {
                    blockAccessor.SetBlock(stalagBlock.BlockId, pos);
                }
                else break;
                pos.Y++;
            }
        }

        private void GrowDownFrom(IBlockAccessor blockAccessor, BlockPos pos, string rocktype, int thickOff)
        {
            for (int i = thicknessIndex[Thickness] + thickOff + random.Next(2); i < Thicknesses.Length; i++)
            {
                Block stalagBlock = GetBlock(api.World, rocktype, Thicknesses[i]);
                if (stalagBlock == null) continue;

                Block block = blockAccessor.GetBlock(pos);
                if (block.Replaceable >= 6000)
                {
                    blockAccessor.SetBlock(stalagBlock.BlockId, pos);
                }
                else break;
                pos.Y--;

            }
        }
    }
}
