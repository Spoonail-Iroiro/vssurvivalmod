﻿using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class ClutterTypeProps : IShapeTypeProps
    {
        public ModelTransformNoDefaults GuiTf { get; set; }
        public ModelTransformNoDefaults FpTf { get; set; }
        public ModelTransformNoDefaults TpTf { get; set; }
        public ModelTransformNoDefaults GroundTf { get; set; }
        public string Code { get; set; }
        public Vec3f Rotation { get; set; } = new Vec3f();
        public Cuboidf[] ColSelBoxes { get; set; }
        public ModelTransform GuiTransform { get; set; }
        public ModelTransform FpTtransform { get; set; }
        public ModelTransform TpTransform { get; set; }
        public ModelTransform GroundTransform { get; set; }
        public string RotInterval { get; set; } = "22.5deg";
        public string FirstTexture { get; set; }
        public TextureAtlasPosition TexPos { get; set; }
        public Dictionary<int, Cuboidf[]> ColSelBoxesByDeg { get; set; } = new Dictionary<int, Cuboidf[]>();
        public AssetLocation ShapePath { get; set; }
        public Shape ShapeResolved { get; set; }
        public string HashKey => Code;
        public bool Randomize { get; set; } = true;
        public bool Climbable { get; set; }
        public byte[] LightHsv { get; set; }
        public Dictionary<string, CompositeTexture> Textures { get; set; }
        public string TextureFlipCode { get; set; }
        public string TextureFlipGroupCode { get; set; }
        public Dictionary<string, bool> SideAttachable { get; set; }
        public BlockDropItemStack[] Drops { get; set; }

        public bool CanAttachBlockAt(Vec3f blockRot, BlockFacing blockFace, Cuboidi attachmentArea = null)
        {
            if (SideAttachable != null)
            {
                SideAttachable.TryGetValue(blockFace.Code, out var val);
                return val;
            }
            return false;
        }

        public ClutterTypeProps Clone()
        {
            return new ClutterTypeProps
            {
                GuiTf = this.GuiTransform?.Clone(),
                FpTf = this.FpTtransform?.Clone(),
                TpTf = this.TpTransform?.Clone(),
                GroundTf = this.GroundTransform?.Clone(),
                Code = this.Code,
                Rotation = new Vec3f { X = this.Rotation.X, Y = this.Rotation.Y, Z = this.Rotation.Z },
                ColSelBoxes = this.ColSelBoxes?.Select(box => box?.Clone()).ToArray(),
                GuiTransform = this.GuiTransform?.Clone(),
                FpTtransform = this.FpTtransform?.Clone(),
                TpTransform = this.TpTransform?.Clone(),
                GroundTransform = this.GroundTransform?.Clone(),
                RotInterval = this.RotInterval,
                FirstTexture = this.FirstTexture,
                TexPos = this.TexPos?.Clone(),
                ColSelBoxesByDeg = this.ColSelBoxesByDeg.ToDictionary(kv => kv.Key, kv => kv.Value?.Select(box => box?.Clone()).ToArray()),
                ShapePath = this.ShapePath?.Clone(),
                ShapeResolved = this.ShapeResolved?.Clone(),
                Randomize = this.Randomize,
                Climbable = this.Climbable,
                LightHsv = this.LightHsv?.ToArray(),
                Textures = this.Textures?.ToDictionary(kv => kv.Key, kv => kv.Value?.Clone()),
                TextureFlipCode = this.TextureFlipCode,
                TextureFlipGroupCode = this.TextureFlipGroupCode,
                SideAttachable = this.SideAttachable?.ToDictionary(kv => kv.Key, kv => kv.Value),
                Drops = this.Drops?.Select(drop => drop.Clone()).ToArray()
            };
        }
    }

    public class BlockClutter : BlockShapeFromAttributes
    {
        public override string ClassType => "clutter";

        public Dictionary<string, ClutterTypeProps> clutterByCode = new Dictionary<string, ClutterTypeProps>();
        public override IEnumerable<IShapeTypeProps> AllTypes => clutterByCode.Values;
        string basePath;


        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            api.Event.RegisterEventBusListener(onExpClang, 0.5, "expclang");
        }

        private void onExpClang(string eventName, ref EnumHandling handling, IAttribute data)
        {
            var tree = data as ITreeAttribute;

            foreach (var val in clutterByCode)
            {
                string langKey = (Code.Domain == "game" ? "" : Code.Domain + ":") + ClassType + "-" + val.Key?.Replace("/", "-");
                if (!Lang.HasTranslation(langKey))
                {
                    tree[langKey] = new StringAttribute("\t\"" + langKey + "\": \"" + Lang.GetNamePlaceHolder(new AssetLocation(val.Key)) + "\",");
                }
            }
        }

        public override void LoadTypes()
        {
            var cluttertypes = Attributes["types"].AsObject<ClutterTypeProps[]>();
            basePath = Attributes["shapeBasePath"].AsString();

            List<JsonItemStack> stacks = new List<JsonItemStack>();

            var defaultGui = ModelTransform.BlockDefaultGui();
            var defaultFp = ModelTransform.BlockDefaultFp();
            var defaultTp = ModelTransform.BlockDefaultTp();
            var defaultGround = ModelTransform.BlockDefaultGround();

            foreach (var ct in cluttertypes)
            {
                clutterByCode[ct.Code] = ct;
                
                if (ct.GuiTf != null) ct.GuiTransform = new ModelTransform(ct.GuiTf, defaultGui);
                if (ct.FpTf != null) ct.FpTtransform = new ModelTransform(ct.FpTf, defaultFp);
                if (ct.TpTf != null) ct.TpTransform = new ModelTransform(ct.TpTf, defaultTp);
                if (ct.GroundTf != null) ct.GroundTransform = new ModelTransform(ct.GroundTf, defaultGround);

                if (ct.ShapePath == null)
                {
                    ct.ShapePath = AssetLocation.Create("shapes/" + basePath + "/" + ct.Code + ".json", Code.Domain);
                } else
                {
                    if (ct.ShapePath.Path.StartsWith("/"))
                    {
                        ct.ShapePath.Path = ct.ShapePath.Path.Substring(1);
                        ct.ShapePath.WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
                    }
                    else
                    {
                        ct.ShapePath.WithPathPrefixOnce("shapes/" + basePath + "/").WithPathAppendixOnce(".json");
                    }
                }
                
                var jstack = new JsonItemStack()
                {
                    Code = this.Code,
                    Type = EnumItemClass.Block,
                    Attributes = new JsonObject(JToken.Parse("{ \"type\": \"" + ct.Code + "\" }"))
                };

                jstack.Resolve(api.World, ClassType + " type");
                stacks.Add(jstack);
            }

            this.CreativeInventoryStacks = new CreativeTabAndStackList[]
            {
                new CreativeTabAndStackList() { Stacks = stacks.ToArray(), Tabs = new string[] { "general", "clutter" } }
            };
        }

        public static string Remap(IWorldAccessor worldAccessForResolve, string type)
        {
            if (type.StartsWithFast("pipes/"))
            {
                return "pipe-veryrusted-" + type.Substring(6);
            }

            return type;
        }

        public override bool IsClimbable(BlockPos pos)
        {
            var bec = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
            if (bec?.Type != null && clutterByCode.TryGetValue(bec.Type, out var props))
            {
                return props.Climbable;
            }

            return Climbable;
        }

        public override IShapeTypeProps GetTypeProps(string code, ItemStack stack, BEBehaviorShapeFromAttributes be)
        {
            if (code == null) return null;
            clutterByCode.TryGetValue(code, out var cprops);
            return cprops;
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            var bec = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);

            if ((bec == null || !bec.Collected) && world.Rand.NextDouble() < 0.5)
            {
                world.PlaySoundAt(new AssetLocation("sounds/effect/toolbreak"), pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, false, 12);
                return new ItemStack[0];
            }

            var stack = OnPickBlock(world, pos);
            stack.Attributes.SetBool("collected", true);
            return new ItemStack[] { stack };
        }

        public override BlockDropItemStack[] GetDropsForHandbook(ItemStack handbookStack, IPlayer forPlayer)
        {
            return new BlockDropItemStack[] { new BlockDropItemStack(handbookStack) };
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            dsc.AppendLine(Lang.Get("Unusable clutter"));

            string type = inSlot.Itemstack.Attributes.GetString("type", "");
            if (type.StartsWithFast("banner-"))
            {
                string[] parts = type.Split('-');
                dsc.AppendLine(Lang.Get("Pattern: {0}", Lang.Get("bannerpattern-" + parts[1])));
                dsc.AppendLine(Lang.Get("Segment: {0}", Lang.Get("bannersegment-" + parts[3])));
            }
        }
    }

}
