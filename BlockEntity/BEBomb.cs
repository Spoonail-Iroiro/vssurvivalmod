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
    public class BlockEntityBomb : BlockEntity
    {
        float remainingSeconds = 0;
        bool lit;
        string ignitedByPlayerUid;

        float blastRadius;
        float injureRadius;

        EnumBlastType blastType;

        ILoadedSound fuseSound;
        public static SimpleParticleProperties smallSparks;

        static BlockEntityBomb()
        {
            smallSparks = new SimpleParticleProperties(
                1, 1,
                ColorUtil.ToRgba(255, 255, 233, 0),
                new Vec3d(), new Vec3d(),
                new Vec3f(-3f, 5f, -3f),
                new Vec3f(3f, 8f, 3f),
                0.03f,
                1f,
                0.05f, 0.15f,
                EnumParticleModel.Quad
            );
            smallSparks.VertexFlags = 255;
            smallSparks.AddPos.Set(1 / 16f, 0, 1 / 16f);
            smallSparks.SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.05f);
        }


        public virtual float FuseTimeSeconds
        {
            get { return 4; }
        }


        public virtual EnumBlastType BlastType
        {
            get { return blastType; }
        }

        public virtual float BlastRadius
        {
            get { return blastRadius; }
        }

        public virtual float InjureRadius
        {
            get { return injureRadius; }
        }


        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            RegisterGameTickListener(OnTick, 50);
            

            if (fuseSound == null && api.Side == EnumAppSide.Client)
            {
                fuseSound = ((IClientWorldAccessor)api.World).LoadSound(new SoundParams()
                {
                    Location = new AssetLocation("sounds/effect/fuse"),
                    ShouldLoop = true,
                    Position = Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
                    DisposeOnFinish = false,
                    Volume = 0.1f,
                    Range = 16,
                });
            }

            blastRadius = Block.Attributes["blastRadius"].AsInt(4);
            injureRadius = Block.Attributes["injureRadius"].AsInt(8);
            blastType = (EnumBlastType)Block.Attributes["blastType"].AsInt((int)EnumBlastType.OreBlast);
        }

        private void OnTick(float dt)
        {
            if (lit)
            {
                remainingSeconds -= dt;

                if (Api.Side == EnumAppSide.Server && remainingSeconds <= 0)
                {
                    Combust(dt);
                }

                if (Api.Side == EnumAppSide.Client)
                {
                    smallSparks.MinPos.Set(Pos.X + 0.45, Pos.Y + 0.5, Pos.Z + 0.45);
                    Api.World.SpawnParticles(smallSparks);
                }
            }
        }

        void Combust(float dt)
        {
            if (nearToClaimedLand())
            {
                Api.World.PlaySoundAt(new AssetLocation("sounds/effect/extinguish"), Pos.X + 0.5, Pos.Y, Pos.Z + 0.5, null, false, 16);
                lit = false;
                MarkDirty(true);
                return;
            }

            Api.World.BlockAccessor.SetBlock(0, Pos);
            ((IServerWorldAccessor)Api.World).CreateExplosion(Pos, BlastType, BlastRadius, InjureRadius);
        }

        public bool nearToClaimedLand()
        {
            int rad = (int)Math.Ceiling(BlastRadius);
            Cuboidi exploArea = new Cuboidi(Pos.AddCopy(-rad, -rad, -rad), Pos.AddCopy(rad, rad, rad));
            List<LandClaim> claims = (Api as ICoreServerAPI).WorldManager.SaveGame.LandClaims;
            for (int i = 0; i < claims.Count; i++)
            {
                if (claims[i].Intersects(exploArea)) return true;
            }
            return false;
        }

        internal void OnBlockExploded(BlockPos pos)
        {
            if (Api.Side == EnumAppSide.Server)
            {
                if ((!lit || remainingSeconds > 0.3) && !nearToClaimedLand())
                {
                    Api.World.RegisterCallback(Combust, 250);
                }
                
            }
        }

        public bool IsLit
        {
            get { return lit; }
        }

        internal void OnIgnite(IPlayer byPlayer)
        {
            if (lit) return;

            if (Api.Side == EnumAppSide.Client) fuseSound.Start();
            lit = true;
            remainingSeconds = FuseTimeSeconds;
            ignitedByPlayerUid = byPlayer?.PlayerUID;
            MarkDirty();
        }

        public override void FromTreeAtributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAtributes(tree, worldForResolving);
            remainingSeconds = tree.GetFloat("remainingSeconds", 0);
            lit = tree.GetInt("lit") > 0;
            ignitedByPlayerUid = tree.GetString("ignitedByPlayerUid");

            if (!lit && Api?.Side == EnumAppSide.Client)
            {
                fuseSound.Stop();
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("remainingSeconds", remainingSeconds);
            tree.SetInt("lit", lit ? 1 : 0);
            tree.SetString("ignitedByPlayerUid", ignitedByPlayerUid);
        }



        ~BlockEntityBomb()
        {
            if (fuseSound != null)
            {
                fuseSound.Dispose();
            }
        }


        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            if (fuseSound != null) fuseSound.Stop();
        }
    }
}
