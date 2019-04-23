﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityButterfly : EntityAgent
    {
        double sitHeight = 1;

        static EntityButterfly() {
            AiTaskRegistry.Register<AiTaskButterflyWander>("butterflywander");
            AiTaskRegistry.Register<AiTaskButterflyFeedOnFlowers>("butterflyfeedonflowers");
        }

        public override bool IsInteractable => false;

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            if (api.Side == EnumAppSide.Server)
            {
                int a = 1;
            }
            base.Initialize(properties, api, InChunkIndex3d);
            
            if (api.Side == EnumAppSide.Client)
            {
                WatchedAttributes.RegisterModifiedListener("sitHeight", () =>
                {
                    (Properties.Client.Renderer as EntityShapeRenderer).WindWaveIntensity = WatchedAttributes.GetDouble("sitHeight");
                });
            }
        }

        public override void OnGameTick(float dt)
        {
            if (World.Side == EnumAppSide.Server)
            {
                base.OnGameTick(dt);
                return;
            }

            if (!AnimManager.ActiveAnimationsByAnimCode.ContainsKey("feed"))
            {
                if (ServerPos.Y < Pos.Y - 0.25 && !Collided)
                {
                    SetAnimation("glide", 1);
                }
                else
                {
                    SetAnimation("fly", 2);
                }
            }

            
            base.OnGameTick(dt);

            if (ServerPos.SquareDistanceTo(Pos.XYZ) > 0.01)
            {
                float desiredYaw = (float)Math.Atan2(ServerPos.X - Pos.X, ServerPos.Z - Pos.Z);

                float yawDist = GameMath.AngleRadDistance(LocalPos.Yaw, desiredYaw);
                Pos.Yaw += GameMath.Clamp(yawDist, -35 * dt, 35 * dt);
                Pos.Yaw = Pos.Yaw % GameMath.TWOPI;
            }
        }


        private void SetAnimation(string animCode, float speed)
        {
            AnimationMetaData animMeta = null;
            if (!AnimManager.ActiveAnimationsByAnimCode.TryGetValue(animCode, out animMeta))
            {
                animMeta = new AnimationMetaData()
                {
                    Code = animCode,
                    Animation = animCode,
                    AnimationSpeed = speed,                   
                };

                AnimManager.ActiveAnimationsByAnimCode.Clear();
                AnimManager.ActiveAnimationsByAnimCode[animMeta.Animation] = animMeta;
                return;
            }

            animMeta.AnimationSpeed = speed;
            SetDebugAnimsInfo();
        }

        public override void OnReceivedServerAnimations(int[] activeAnimations, int activeAnimationsCount, float[] activeAnimationSpeeds)
        {
            // We control glide and fly animations entirely client side

            if (activeAnimationsCount == 0)
            {
                AnimManager.ActiveAnimationsByAnimCode.Clear();
                AnimManager.StartAnimation("fly");
            }

            string active = "";

            bool found = false;

            for (int i = 0; i < activeAnimationsCount; i++)
            {
                int crc32 = activeAnimations[i];
                for (int j = 0; j < Properties.Client.LoadedShape.Animations.Length; j++)
                {
                    Animation anim = Properties.Client.LoadedShape.Animations[j];
                    int mask = ~(1 << 31); // Because I fail to get the sign bit transmitted correctly over the network T_T
                    if ((anim.CodeCrc32 & mask) == (crc32 & mask))
                    {
                        if (AnimManager.ActiveAnimationsByAnimCode.ContainsKey(anim.Code)) break;
                        if (anim.Code == "glide" || anim.Code == "fly") continue;

                        string code = anim.Code == null ? anim.Name.ToLowerInvariant() : anim.Code;
                        active += ", " + code;
                        AnimationMetaData animmeta = null;
                        Properties.Client.AnimationsByMetaCode.TryGetValue(code, out animmeta);

                        if (animmeta == null)
                        {
                            animmeta = new AnimationMetaData()
                            {
                                Code = code,
                                Animation = code,
                                CodeCrc32 = anim.CodeCrc32
                            };
                        }

                        animmeta.AnimationSpeed = activeAnimationSpeeds[i];

                        AnimManager.ActiveAnimationsByAnimCode[anim.Code] = animmeta;

                        found = true;
                    }
                }
            }

            if (found)
            {
                AnimManager.StopAnimation("fly");
                AnimManager.StopAnimation("glide");

                (Properties.Client.Renderer as EntityShapeRenderer).AddRenderFlags = VertexFlags.WindWaveBitMask;
            } else
            {
                (Properties.Client.Renderer as EntityShapeRenderer).AddRenderFlags = 0;
            }

            

        }
    }
}
