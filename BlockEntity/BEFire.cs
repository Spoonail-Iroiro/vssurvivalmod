﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class BlockEntityFire : BlockEntity
    {
        public float startDuration;
        public float remainingBurnDuration;
        public BlockFacing fromFacing = BlockFacing.NORTH;

        Block fireBlock;
        Block neibBlock;

        string startedByPlayerUid;

        ILoadedSound ambientSound;


        public float TimePassed
        {
            get { return startDuration - remainingBurnDuration; }
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            RegisterGameTickListener(OnTick, 25);
            RegisterGameTickListener(OnSlowTick, 1000);

            fireBlock = api.World.GetBlock(new AssetLocation("fire"));
            if (fireBlock == null) fireBlock = new Block();

            neibBlock = api.World.BlockAccessor.GetBlock(pos.AddCopy(fromFacing.GetOpposite()));


            if (ambientSound == null && api.Side == EnumAppSide.Client)
            {
                RegisterDelayedCallback((dt) => {
                    // When the world loads with a lot of fire they'll all start at the same millisecond, so lets delay a bit
                    ambientSound = ((IClientWorldAccessor)api.World).LoadSound(new SoundParams()
                    {
                        Location = new AssetLocation("sounds/environment/fire.ogg"),
                        ShouldLoop = true,
                        Position = pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
                        DisposeOnFinish = false,
                        Volume = 1f
                    });
                    ambientSound?.Start();

                }, api.World.Rand.Next(200));


            }
        }

        CollisionTester collTester = new CollisionTester();
        Cuboidf fireCuboid = new Cuboidf(0, 0, 0, 1, 1, 1);

        private void OnSlowTick(float dt)
        {
            if (api.Side == EnumAppSide.Client) return;

            BlockPos neibPos = pos.AddCopy(fromFacing.GetOpposite());
            neibBlock = api.World.BlockAccessor.GetBlock(neibPos);
            if (!canBurn(neibBlock, neibPos))
            {
                api.World.BlockAccessor.SetBlock(0, pos);
                api.World.BlockAccessor.RemoveBlockEntity(pos); // Sometimes block entities don't get removed properly o.O
                api.World.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
                return;
            }

            Entity[] entities = api.World.GetEntitiesAround(pos.ToVec3d().Add(0.5, 0.5, 0.5), 3, 3, (e) => e.Alive);
            Vec3d ownPos = pos.ToVec3d();
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (CollisionTester.AabbIntersect(entity.CollisionBox, entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z, fireCuboid, ownPos))
                {
                    entity.ReceiveDamage(new DamageSource() { Source = EnumDamageSource.Block, sourceBlock = fireBlock, sourcePos = ownPos, Type = EnumDamageType.Fire }, 2f);
                }
            }
        }

        private void OnTick(float dt)
        {
            if (api.Side == EnumAppSide.Server)
            {
                remainingBurnDuration -= dt;

                if (remainingBurnDuration <= 0)
                {
                    BlockPos fuelPos = pos.AddCopy(fromFacing.GetOpposite());
                    Block fuelBlock = api.World.BlockAccessor.GetBlock(fuelPos);

                    if (canBurn(fuelBlock, fuelPos))
                    {
                        TrySpreadTo(fuelPos, fromFacing);
                    }

                    api.World.BlockAccessor.SetBlock(0, pos);
                    api.World.BlockAccessor.RemoveBlockEntity(pos); // Sometimes block entities don't get removed properly o.O
                    api.World.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
                    return;
                }

                float spreadChance = (TimePassed - 2.5f) / 450f;

                if (((ICoreServerAPI)api).Server.Config.AllowFireSpread && spreadChance > api.World.Rand.NextDouble())
                {
                    TrySpreadFire();
                }
            }

            if (api.Side == EnumAppSide.Client)
            {
                int index = Math.Min(fireBlock.ParticleProperties.Length-1, api.World.Rand.Next(fireBlock.ParticleProperties.Length + 1));
                AdvancedParticleProperties particles = fireBlock.ParticleProperties[index];
                particles.basePos = RandomBlockPos(api.World.BlockAccessor, pos.AddCopy(fromFacing.GetOpposite()), neibBlock, fromFacing);

                particles.Quantity.avg = 0.75f;
                particles.TerrainCollision = false;
                api.World.SpawnParticles(particles);
                particles.Quantity.avg = 0;
            }
        }


        private void TrySpreadFire()
        {
            BlockPos opos = pos.AddCopy(fromFacing.GetOpposite());

            foreach (BlockFacing facing in BlockFacing.ALLFACES)
            {
                BlockPos npos = opos.AddCopy(facing);
                Block nBlock = api.World.BlockAccessor.GetBlock(npos);

                if (canBurn(nBlock, npos))
                {
                    if (api.World.BlockAccessor.GetBlock(npos.AddCopy(fromFacing)).BlockId == 0 && TrySpreadTo(npos.AddCopy(fromFacing), fromFacing))
                    {
                        break;
                    }

                    bool dobreak = false;
                    foreach (BlockFacing firefacing in BlockFacing.ALLFACES)
                    {
                        BlockPos nnpos = npos.AddCopy(firefacing);
                        Block nnblock = api.World.BlockAccessor.GetBlock(nnpos);
                        if (canBurn(nnblock, nnpos) && TrySpreadTo(nnpos, firefacing))
                        {
                            dobreak = true;
                            break;
                        }
                    }

                    if (dobreak) break;
                }
            }
        }


        public bool TrySpreadTo(BlockPos pos, BlockFacing facing)
        {
            IPlayer player = api.World.PlayerByUid(startedByPlayerUid);
            
            if (player != null && api.World.Claims.TestAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak) != EnumWorldAccessResponse.Granted) {
                return false;
            }

            api.World.BlockAccessor.SetBlock(fireBlock.BlockId, pos);

            BlockEntityFire befire = api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityFire;
            if (befire != null) befire.Init(facing, startedByPlayerUid);

            return true;
        }


        bool canBurn(Block block, BlockPos pos)
        {
            if (block?.CombustibleProps == null || block.CombustibleProps.BurnDuration <= 0) return false;

            return api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.IsReinforced(pos) != true;
        }

        public void Init(BlockFacing fromFacing, string startedByPlayerUid)
        {
            this.fromFacing = fromFacing;
            this.startedByPlayerUid = startedByPlayerUid;

            BlockPos neibPos = pos.AddCopy(fromFacing.GetOpposite());
            neibBlock = api.World.BlockAccessor.GetBlock(neibPos);

            if (!canBurn(neibBlock, neibPos))
            {
                foreach (BlockFacing facing in BlockFacing.ALLFACES)
                {
                    BlockPos nnpos = pos.AddCopy(facing);
                    neibBlock = api.World.BlockAccessor.GetBlock(nnpos);
                    if (canBurn(neibBlock, nnpos))
                    {
                        this.fromFacing = facing.GetOpposite();
                        startDuration = remainingBurnDuration = neibBlock.CombustibleProps.BurnDuration;
                        return;
                    }
                }

                startDuration = 1;
                remainingBurnDuration = 1;
            } else
            {
                startDuration = remainingBurnDuration = neibBlock.CombustibleProps.BurnDuration;
            }
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();

            if (ambientSound != null)
            {
                ambientSound?.Stop();
                ambientSound?.Dispose();
                ambientSound = null;
            }
        }

        ~BlockEntityFire()
        {
            if (ambientSound != null)
            {
                ambientSound?.Dispose();
            }
        }

        public override void FromTreeAtributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAtributes(tree, worldForResolving);
            remainingBurnDuration = tree.GetFloat("remainingBurnDuration");
            startDuration = tree.GetFloat("startDuration");
            fromFacing = BlockFacing.ALLFACES[tree.GetInt("fromFacing")];

            startedByPlayerUid = tree.GetString("startedByPlayerUid");
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("remainingBurnDuration", remainingBurnDuration);
            tree.SetFloat("startDuration", startDuration);
            tree.SetInt("fromFacing", fromFacing.Index);

            if (startedByPlayerUid != null)
            {
                tree.SetString("startedByPlayerUid", startedByPlayerUid);
            }
        }


        static Random rand = new Random();
        public static Vec3d RandomBlockPos(IBlockAccessor blockAccess, BlockPos pos, Block block, BlockFacing facing = null)
        {
            if (facing == null)
            {
                Cuboidf[] selectionBoxes = block.GetSelectionBoxes(blockAccess, pos);
                Cuboidf box = (selectionBoxes != null && selectionBoxes.Length > 0) ? selectionBoxes[0] : Block.DefaultCollisionBox;

                return new Vec3d(
                    pos.X + box.X1 + rand.NextDouble() * (box.X2 - box.X1),
                    pos.Y + box.Y1 + rand.NextDouble() * (box.Y2 - box.Y1),
                    pos.Z + box.Z1 + rand.NextDouble() * (box.Z2 - box.Z1)
                );
            }
            else
            {
                Vec3i face = facing.Normali;

                Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccess, pos);

                bool haveCollisionBox = collisionBoxes != null && collisionBoxes.Length > 0;

                Vec3d basepos = new Vec3d(
                    pos.X + 0.5f + face.X / 1.95f + (haveCollisionBox && facing.Axis == EnumAxis.X ? (face.X > 0 ? collisionBoxes[0].X2 - 1 : collisionBoxes[0].X1) : 0),
                    pos.Y + 0.5f + face.Y / 1.95f + (haveCollisionBox && facing.Axis == EnumAxis.Y ? (face.Y > 0 ? collisionBoxes[0].Y2 - 1 : collisionBoxes[0].Y1) : 0),
                    pos.Z + 0.5f + face.Z / 1.95f + (haveCollisionBox && facing.Axis == EnumAxis.Z ? (face.Z > 0 ? collisionBoxes[0].Z2 - 1 : collisionBoxes[0].Z1) : 0)
                );

                Vec3d posVariance = new Vec3d(
                    1f * (1 - face.X),
                    1f * (1 - face.Y),
                    1f * (1 - face.Z)
                );

                return new Vec3d(
                    basepos.X + (rand.NextDouble() - 0.5) * posVariance.X,
                    basepos.Y + (rand.NextDouble() - 0.5) * posVariance.Y,
                    basepos.Z + (rand.NextDouble() - 0.5) * posVariance.Z
                );
            }
        }

    }
}
