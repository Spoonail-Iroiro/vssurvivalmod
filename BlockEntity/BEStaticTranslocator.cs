﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class BlockEntityStaticTranslocator : BlockEntityAnimatable
    {
        public int MinTeleporterRangeInBlocks = 400;
        public int MaxTeleporterRangeInBlocks = 6000;

        BlockPos tpLocation;
        Dictionary<long, TeleportingEntity> tpingEntities = new Dictionary<long, TeleportingEntity>();

        BlockStaticTranslocator block;
        Vec3d posvec;
        long lastCollideMsOwnPlayer;

        TeleporterManager manager;

        ICoreServerAPI sapi;

        int repairState = 0;
        bool activated;
        bool canTeleport = false;
        bool findNextChunk = true;

        ItemStack temporalGearStack;
        NatFloat rndPos = NatFloat.create(EnumDistribution.INVERSEGAUSSIAN, 0, 0.5f);

        public bool Activated
        {
            get { return activated; }
        }

        public bool FullyRepaired
        {
            get { return repairState >= 4; }
        }

        public BlockEntityStaticTranslocator()
        {
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            manager = api.ModLoader.GetModSystem<TeleporterManager>();

            if (FullyRepaired) setupGameTickers();

            block = api.World.BlockAccessor.GetBlock(pos) as BlockStaticTranslocator;
            posvec = new Vec3d(pos.X + 0.5, pos.Y, pos.Z + 0.5);

            if (api.World.Side == EnumAppSide.Client)
            {
                float rotY = block.Shape.rotateY;
                animUtil.InitializeAnimator("translocator", new Vec3f(0, rotY, 0));

                temporalGearStack = new ItemStack(api.World.GetItem(new AssetLocation("gear-temporal")));
            }
        }

        public void DoActivate()
        {
            activated = true;
            MarkDirty(true);
        }

        public void DoRepair()
        {
            if (FullyRepaired) return;

            repairState++;
            MarkDirty(true);

            if (FullyRepaired)
            {
                activated = true;
                setupGameTickers();
            }
        }
        
        public void setupGameTickers()
        {
            if (api.Side == EnumAppSide.Server)
            {
                sapi = api as ICoreServerAPI;
                RegisterGameTickListener(OnServerGameTick, 250);
            }
            else
            {
                RegisterGameTickListener(OnClientGameTick, 50);
            }
        }

        internal void OnEntityCollide(Entity entity)
        {
            if (!FullyRepaired || !Activated) return;

            TeleportingEntity tpe = null;
            if (!tpingEntities.TryGetValue(entity.EntityId, out tpe))
            {
                tpingEntities[entity.EntityId] = tpe = new TeleportingEntity()
                {
                    Entity = entity
                };
            }

            tpe.LastCollideMs = api.World.ElapsedMilliseconds;
            
            
            if (api.Side == EnumAppSide.Client)
            {
                if ((api as ICoreClientAPI).World.Player.Entity == entity)
                {
                    lastCollideMsOwnPlayer = api.World.ElapsedMilliseconds;
                    manager.lastTranslocateCollideMsOwnPlayer = lastCollideMsOwnPlayer;
                }
            }
        }

        
        private void OnClientGameTick(float dt)
        {
            if (block == null || api?.World == null || !canTeleport || !Activated) return;

            bool playerInside = (api.World.ElapsedMilliseconds > 100 && api.World.ElapsedMilliseconds - lastCollideMsOwnPlayer < 100);

            SimpleParticleProperties currentParticles = playerInside ? 
                block.insideParticles : 
                block.idleParticles
            ;

            if (playerInside)
            {
                animUtil.StartAnimation(new AnimationMetaData() { Animation = "idle", Code = "idle", AnimationSpeed = 1, EaseInSpeed = 100, EaseOutSpeed = 100, BlendMode = EnumAnimationBlendMode.Average });
                animUtil.StartAnimation(new AnimationMetaData() { Animation = "teleport", Code = "teleport", AnimationSpeed = 1, EaseInSpeed = 8, EaseOutSpeed = 8, BlendMode = EnumAnimationBlendMode.Add });
            }
            else
            {
                animUtil.StopAnimation("teleport");
            }

            if (api.World.ElapsedMilliseconds - lastCollideMsOwnPlayer > 3000)
            {
                animUtil.StopAnimation("idle");
            }

            //int color = temporalGearStack.Collectible.GetRandomColor(api as ICoreClientAPI, temporalGearStack); - not working o.O

            int r = 53;
            int g = 221;
            int b = 172;
            currentParticles.color = (r << 16) | (g << 8) | (b << 0) | (50 << 24);
            
            currentParticles.addPos.Set(0, 0, 0);
            currentParticles.BlueEvolve = null;
            currentParticles.RedEvolve = null;
            currentParticles.GreenEvolve = null;
            currentParticles.minSize = 0.1f;
            currentParticles.maxSize = 0.2f;
            currentParticles.SizeEvolve = null;
            currentParticles.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 100f);

            double xpos = rndPos.nextFloat();
            double ypos = 1.9 + api.World.Rand.NextDouble() * 0.2;
            double zpos = rndPos.nextFloat();

            currentParticles.lifeLength = GameMath.Sqrt(xpos*xpos + zpos*zpos) / 10;
            currentParticles.minPos.Set(posvec.X + xpos, posvec.Y + ypos, posvec.Z + zpos);
            currentParticles.minVelocity.Set(-(float)xpos, -1 - (float)api.World.Rand.NextDouble()/2, -(float)zpos);
            currentParticles.minQuantity = playerInside ? 2 : 0.25f;
            currentParticles.addQuantity = 0;
            
            api.World.SpawnParticles(currentParticles);
        }


        private void OnServerGameTick(float dt)
        {            
            if (findNextChunk)
            {
                findNextChunk = false;

                int dx = (int)(MinTeleporterRangeInBlocks + sapi.World.Rand.NextDouble() * (MaxTeleporterRangeInBlocks - MinTeleporterRangeInBlocks));
                int dz = (int)(MinTeleporterRangeInBlocks + sapi.World.Rand.NextDouble() * (MaxTeleporterRangeInBlocks - MinTeleporterRangeInBlocks));

                int chunkX = (pos.X + dx) / sapi.World.BlockAccessor.ChunkSize;
                int chunkZ = (pos.Z + dz) / sapi.World.BlockAccessor.ChunkSize;
                
                ChunkPeekOptions opts = new ChunkPeekOptions()
                {
                    OnGenerated = (chunks) => TestForExitPoint(chunks, chunkX, chunkZ),
                    UntilPass = EnumWorldGenPass.TerrainFeatures,
                    ChunkGenParams = chunkGenParams()
                };

                sapi.WorldManager.PeekChunkColumn(chunkX, chunkZ, opts);
            }

            if (canTeleport && activated)
            {
                HandleTeleporting(dt);
            }
        }


        ITreeAttribute chunkGenParams()
        {
            TreeAttribute tree = new TreeAttribute();
            TreeAttribute subtree;
            tree["structureChanceModifier"] = subtree = new TreeAttribute();
            subtree.SetFloat("gates", 10);

            tree["structureMaxCount"] = subtree = new TreeAttribute();
            subtree.SetInt("gates", 1);

            return tree;
        }


        private void TestForExitPoint(Dictionary<Vec2i, IServerChunk[]> columnsByChunkCoordinate, int centerCx, int centerCz)
        {
            BlockPos pos = HasExitPoint(columnsByChunkCoordinate, centerCx, centerCz);

            if (pos == null)
            {
                findNextChunk = true;
            }
            else
            {
                sapi.WorldManager.LoadChunkColumnFast(centerCx, centerCz, new ChunkLoadOptions() { ChunkGenParams = chunkGenParams(), OnLoaded = () => exitChunkLoaded(pos) });
            }             
        }

        private void exitChunkLoaded(BlockPos exitPos)
        {
            BlockStaticTranslocator exitBlock = api.World.BlockAccessor.GetBlock(exitPos) as BlockStaticTranslocator;

            if (exitBlock == null)
            {
                // Cheap hax: Pre v1.10 chunks do not have translocators at the same location and maybe future versions will also have a changed location
                // So let's still try to find something useful in the chunk we generated, with any luck we come across an old one. 
                exitPos = HasExitPoint(pos);
                if (exitPos != null)
                {
                    exitBlock = api.World.BlockAccessor.GetBlock(exitPos) as BlockStaticTranslocator;
                }
            }
            
            if (exitBlock != null && !exitBlock.Repaired)
            {
                // Repair it
                api.World.BlockAccessor.SetBlock(block.Id, exitPos);
                BlockEntityStaticTranslocator beExit = api.World.BlockAccessor.GetBlockEntity(exitPos) as BlockEntityStaticTranslocator;

                // Connect
                beExit.tpLocation = pos.Copy();
                beExit.canTeleport = true;
                beExit.repairState = 4;
                beExit.findNextChunk = false;
                beExit.activated = true;
                MarkDirty(true);
                api.World.BlockAccessor.MarkBlockEntityDirty(exitPos);
                api.World.BlockAccessor.MarkBlockDirty(exitPos);

                tpLocation = exitPos;
                canTeleport = true;
            } else
            {
                api.World.Logger.Warning("Translocator: Regen chunk but broken translocator is gone. Structure generation perhaps seed not consistent? May also just be pre-v1.10 chunk, so probably nothing to worry about. Searching again...");
                findNextChunk = true;
            }
        }

        private BlockPos HasExitPoint(BlockPos nearpos)
        {
            IServerChunk chunk = api.World.BlockAccessor.GetChunkAtBlockPos(nearpos) as IServerChunk;          
            List<GeneratedStructure> structures = chunk?.MapChunk?.MapRegion?.GeneratedStructures;

            if (structures == null) return null;

            foreach (var structure in structures)
            {
                if (structure.Code.Contains("gates"))
                {
                    Cuboidi loc = structure.Location;
                    BlockPos foundPos = null;
                    api.World.BlockAccessor.WalkBlocks(loc.Start.AsBlockPos, loc.End.AsBlockPos, (block, pos) =>
                    {
                        BlockStaticTranslocator transBlock = block as BlockStaticTranslocator;
                        
                        if (transBlock != null && !transBlock.Repaired)
                        {
                            foundPos = pos.Copy();
                        }
                    });

                    if (foundPos != null) return foundPos;
                }
            }

            return null;
        }

        private BlockPos HasExitPoint(Dictionary<Vec2i, IServerChunk[]> columnsByChunkCoordinate, int centerCx, int centerCz)
        {
            IMapRegion mapregion = columnsByChunkCoordinate[new Vec2i(centerCx, centerCz)][0].MapChunk.MapRegion;

            List<GeneratedStructure> structures = mapregion.GeneratedStructures;
            foreach (var structure in structures)
            {
                if (structure.Code.Contains("gates"))
                {
                    BlockPos pos = FindTranslocator(structure.Location, columnsByChunkCoordinate, centerCx, centerCz);
                    if (pos != null) return pos;
                }
            }

            return null;
        }

        private BlockPos FindTranslocator(Cuboidi location, Dictionary<Vec2i, IServerChunk[]> columnsByChunkCoordinate, int centerCx, int centerCz)
        {
            int chunksize = api.World.BlockAccessor.ChunkSize;
            
            for (int x = location.X1; x < location.X2; x++)
            {
                for (int y = location.Y1; y < location.Y2; y++)
                {
                    for (int z = location.Z1; z < location.Z2; z++)
                    {
                        int cx = x / chunksize;
                        int cy = y / chunksize;
                        int cz = z / chunksize;

                        IServerChunk[] chunks = null;
                        if (!columnsByChunkCoordinate.TryGetValue(new Vec2i(cx, cz), out chunks))
                        {
                            continue;
                        }

                        IServerChunk chunk = chunks[y / chunksize];

                        int lx = x % chunksize;
                        int ly = y % chunksize;
                        int lz = z % chunksize;

                        int index3d = (ly * chunksize + lz) * chunksize + lx;
                        Block block = api.World.Blocks[chunk.Blocks[index3d]];

                        BlockStaticTranslocator transBlock = block as BlockStaticTranslocator;
                        if (transBlock != null && !transBlock.Repaired)
                        {
                            return new BlockPos(x, y, z);
                        }
                    }
                }
            }

            return null;
        }

        public long MapRegionIndex2D(int regionX, int regionZ)
        {
            return ((long)regionZ) * (sapi.WorldManager.MapSizeX / sapi.WorldManager.RegionSize) + regionX;
        }



        List<long> toremove = new List<long>();
        void HandleTeleporting(float dt)
        { 
            toremove.Clear();

            foreach (var val in tpingEntities)
            {
                if (val.Value.Entity.Teleporting) continue;

                val.Value.SecondsPassed += Math.Min(0.5f, dt);

                if (api.World.ElapsedMilliseconds - val.Value.LastCollideMs > 100)
                {
                    toremove.Add(val.Key);
                    continue;
                }

                if (val.Value.SecondsPassed > 1.5 && tpLocation != null)
                {
                    // Preload the chunk
                    sapi.WorldManager.LoadChunkColumnFast((int)tpLocation.X / api.World.BlockAccessor.ChunkSize, (int)tpLocation.Z / api.World.BlockAccessor.ChunkSize, new ChunkLoadOptions() { KeepLoaded = true });
                }

                if (val.Value.SecondsPassed > 5 && tpLocation != null)
                {
                    val.Value.Entity.TeleportTo(tpLocation.ToVec3d().Add(0.5, 1, 0.5));

                    Entity e = val.Value.Entity;
                    if (e is EntityPlayer)
                    {
                        api.World.Logger.Debug("Teleporting player {0} to {1}", (e as EntityPlayer).GetBehavior<EntityBehaviorNameTag>().DisplayName, tpLocation);
                        manager.DidTranslocateServer((e as EntityPlayer).Player as IServerPlayer);
                    } else
                    {
                        api.World.Logger.Debug("Teleporting entity {0} to {1}", e.Code, tpLocation);
                    }

                    toremove.Add(val.Key);

                    activated = false;
                    
                    MarkDirty();
                }
            }

            foreach(long entityid in toremove)
            {
                tpingEntities.Remove(entityid);
            }
        }


        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();

            if (api.Side == EnumAppSide.Server)
            {
                ICoreServerAPI sapi = api as ICoreServerAPI;
                sapi.ModLoader.GetModSystem<TeleporterManager>().DeleteLocation(pos);
            }
        }


        public override void FromTreeAtributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAtributes(tree, worldAccessForResolve);

            canTeleport = tree.GetBool("canTele");
            repairState = tree.GetInt("repairState");
            findNextChunk = tree.GetBool("findNextChunk", true);

            bool wasActivated = activated;

            activated = tree.GetBool("activated");

            if (canTeleport) {
                tpLocation = new BlockPos(tree.GetInt("teleX"), tree.GetInt("teleY"), tree.GetInt("teleZ"));

                if (tpLocation.X == 0 && tpLocation.Z == 0) tpLocation = null; // For safety
            }
            
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetBool("canTele", canTeleport);
            tree.SetInt("repairState", repairState);
            tree.SetBool("findNextChunk", findNextChunk);
            tree.SetBool("activated", activated);

            if (tpLocation != null)
            {
                tree.SetInt("teleX", tpLocation.X);
                tree.SetInt("teleY", tpLocation.Y);
                tree.SetInt("teleZ", tpLocation.Z);
            }
        }


        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (animUtil.activeAnimationsByAnimCode.Count > 0)
            {
                return true;
            }

            if (!FullyRepaired)
            {
                MeshData mesh = ObjectCacheUtil.GetOrCreate(api, "statictranslocator-" + repairState + "-" + block.Shape.rotateY, () =>
                {
                    float rotY = block.Shape.rotateY;

                    ICoreClientAPI capi = api as ICoreClientAPI;

                    string shapeCode = "normal";
                    switch (repairState)
                    {
                        case 0: shapeCode = "broken"; break;
                        case 1: shapeCode = "repairstate1"; break;
                        case 2: shapeCode = "repairstate2"; break;
                        case 3: shapeCode = "repairstate3"; break;
                    }

                    MeshData meshdata;
                    IAsset asset = api.Assets.TryGet(new AssetLocation("shapes/block/machine/statictranslocator/" + shapeCode + ".json"));
                    Shape shape = asset.ToObject<Shape>();

                    capi.Tesselator.TesselateShape(block, shape, out meshdata, new Vec3f(0, rotY, 0));

                    return meshdata;
                });

                mesher.AddMeshData(mesh);
                return true;
            }


            return false;
        }



        public override string GetBlockInfo(IPlayer forPlayer)
        {
            if (!FullyRepaired)
            {
                return Lang.Get("Seems to be missing a couple of gears. I think I've seen such gears before.");
            }
            else
            {
                if (tpLocation == null)
                {
                    string[] lines = new string[] { Lang.Get("Warping spacetime."), Lang.Get("Warping spacetime.."), Lang.Get("Warping spacetime...") };

                    return lines[(int)(api.World.ElapsedMilliseconds / 1000f) % 3];
                }
            }

            if (forPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                return Lang.Get("Teleports to {0}", tpLocation.Copy().Sub(api.World.DefaultSpawnPosition.AsBlockPos));
            }
            else
            {
                return Lang.Get("Spacetime subduction completed.");
            }
        }
    }
}
