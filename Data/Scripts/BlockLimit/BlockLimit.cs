using Sandbox.Common.ObjectBuilders;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Game;
using System.Collections.Generic;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Text;
using System.Collections.Concurrent;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using System.Timers;
using VRage.ModAPI;

namespace BlockLimit
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class BlockLimitCore : MySessionComponentBase
    {
        public static int RefineryLimit = 4;
        public static int AssemblerLimit = 4;
        public static int GTurretLimit = 60;
        public static int MTurretLimit = 30;
        public static int ProjectorLimit = 2;

        private Dictionary<long, List<long>> Refineries = new Dictionary<long, List<long>>();
		private Dictionary<long, List<long>> Assemblers = new Dictionary<long, List<long>>();        
        private Dictionary<long, List<long>> MissileTurrets = new Dictionary<long, List<long>>();
        private Dictionary<long, List<long>> GatlingTurrets = new Dictionary<long, List<long>>();
        private Dictionary<long, List<long>> Projectors = new Dictionary<long, List<long>>();
        public ConcurrentQueue<BlockLimitPair> UpdateQueue = new ConcurrentQueue<BlockLimitPair>();
        public static BlockLimitCore Instance;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            Instance = this;
        }

        public override void UpdateAfterSimulation()
        {  
            if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer)
            {
                while (!UpdateQueue.IsEmpty)
                {
                    BlockLimitPair Pair;
                    if (UpdateQueue.TryDequeue(out Pair))
                    {
                        try
                        {
                            if (Pair.Block is IMyRefinery)
                            {
                                UpdateOwner(Pair.Block.OwnerId, ref Refineries);
                                UpdatePair(Pair, Refineries, RefineryLimit);
                            }
                            else if (Pair.Block is IMyAssembler)
                            {
                                UpdateOwner(Pair.Block.OwnerId, ref Assemblers);
                                UpdatePair(Pair, Assemblers, AssemblerLimit);
                            }
                            else if (Pair.Block is IMyLargeMissileTurret)
                            {
                                UpdateOwner(Pair.Block.OwnerId, ref MissileTurrets);
                                UpdatePair(Pair, MissileTurrets, MTurretLimit);
                            }
                            else if (Pair.Block is IMyLargeGatlingTurret)
                            {
                                UpdateOwner(Pair.Block.OwnerId, ref GatlingTurrets);
                                UpdatePair(Pair, GatlingTurrets, GTurretLimit);
                            }
                            else if (Pair.Block is IMyProjector)
                            {
                                UpdateOwner(Pair.Block.OwnerId, ref Projectors);
                                UpdatePair(Pair, Projectors, ProjectorLimit);
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            UpdateQueue.Enqueue(Pair);
                        }
                    }
                    // else nothing
                }
            }          
        }
        
        private void UpdateOwner(long OwnerId, ref Dictionary<long, List<long>> BlockDict)
        {
            List<long> BlockIds;
            if (BlockDict.TryGetValue(OwnerId, out BlockIds))
            {
                List<long> NewBlocks = new List<long>();
                foreach (long BlockId in BlockIds)
                {
                    IMyEntity ThisEntity = MyAPIGateway.Entities.GetEntityById(BlockId);
                    if (ThisEntity != null && ThisEntity is IMyFunctionalBlock)
                    {
                        IMyFunctionalBlock FBlock = ThisEntity as IMyFunctionalBlock;
                        if (FBlock.OwnerId == OwnerId && FBlock.IsWorking)
                        {
                            NewBlocks.Add(BlockId);
                        }                        
                    }
                }
                BlockDict.Remove(OwnerId);
                BlockDict.Add(OwnerId, NewBlocks);
            }
        }     

        private void UpdatePair(BlockLimitPair Pair, Dictionary<long, List<long>> BlockDict, int Limit)
        {
            if (Pair.Block.IsWorking)
            {
                if (BlockDict.ContainsKey(Pair.Block.OwnerId))
                {         
                    List<long> Blocks = BlockDict.GetValueOrDefault(Pair.Block.OwnerId);
                    if (Blocks != null && !Blocks.Contains(Pair.Block.EntityId))
                    {
                        if (Blocks.Count < Limit)
                        {
                            Blocks.Add(Pair.Block.EntityId);
                            Pair.LimitReached = false;
                        }
                        else
                        {
                            Pair.Block.Enabled = false;
                            Pair.LimitReached = true;
                        }                        
                    }
                }
                else
                {
                    List<long> Blocks = new List<long>();
                    Blocks.Add(Pair.Block.EntityId);
                    BlockDict.Add(Pair.Block.OwnerId, Blocks);
                    Pair.LimitReached = false;
                }
            }
            else if (BlockDict.ContainsKey(Pair.Block.OwnerId))
            {
                List<long> Blocks = BlockDict.GetValueOrDefault(Pair.Block.OwnerId);
                Blocks.RemoveAll(x => x == Pair.Block.EntityId);
            }
            Pair.Block.RefreshCustomInfo();
        }
    }

    public class BlockLimitPair
    {
        public IMyFunctionalBlock Block;
        public bool LimitReached;
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_InteriorTurret), false, "LargeInteriorTurret")]
    public class InteriorTurretRestriction : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            this.NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
            IMyLargeInteriorTurret Turret = Entity as IMyLargeInteriorTurret;
            Turret.PropertiesChanged += Lock;
            Turret.EnabledChanged += Lock;
            Turret.AppendingCustomInfo += AppInfo;
        }

        private void AppInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            if (arg1.OwnerId == 0) arg2.AppendLine("Set ownership to enable");
        } 

        private void Lock(IMyCubeBlock obj)
        {
			IMyLargeTurretBase Turret = Entity as IMyLargeTurretBase;
            if (obj.OwnerId == 0 && Turret.IsWorking)
            {
                Turret.Enabled = false;
                Turret.RefreshCustomInfo();
                return;
            }                
            ITerminalAction TargetLarge = Turret.GetActionWithName("TargetLargeShips_Off");
            if (TargetLarge != null)
                TargetLarge.Apply(Turret);
            
            ITerminalAction TargetSmall = Turret.GetActionWithName("TargetSmallShips_Off");
            if (TargetSmall != null)
                TargetSmall.Apply(Turret);

            ITerminalAction TargetStation = Turret.GetActionWithName("TargetStations_Off");
            if (TargetStation != null)
                TargetStation.Apply(Turret);

            ITerminalAction TargetMoving = Turret.GetActionWithName("TargetMoving_Off");
            if (TargetMoving != null)
                TargetMoving.Apply(Turret);
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }
    }
    

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret), false, "", "SmallMissileTurret", "OKI50mmAG", "OKI230mmBAT")]
    public class BlockLimitMTurret : MyGameLogicComponent
    {
        private BlockLimitPair Pair = new BlockLimitPair();
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            this.NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
            IMyFunctionalBlock FBlock = Entity as IMyFunctionalBlock;
            Pair.Block = Entity as IMyFunctionalBlock;
            FBlock.OwnershipChanged += Lock;
            FBlock.IsWorkingChanged += Lock;
            //FBlock.PropertiesChanged += PropertiesChanged;
            FBlock.AppendingCustomInfo += AppInfo;
        }

        private void AppInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            if (arg1.OwnerId == 0) arg2.AppendLine("Set ownership to enable");
            if (Pair.LimitReached) arg2.AppendLine(string.Format("Owner has {0} Missile Turrets on\nTurn another off to turn this one on", BlockLimitCore.MTurretLimit));
        }

        private void Lock(IMyCubeBlock obj)
        {
            if (obj != null)
            {
                if (obj.OwnerId == 0 && Pair.Block.IsWorking)
                {
                    Pair.Block.Enabled = false;
                }
                else if (Pair.Block.IsWorking)
                {
                    BlockLimitCore.Instance.UpdateQueue.Enqueue(Pair);
                }
                Pair.Block.RefreshCustomInfo();
            }
        }
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }
    }
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeGatlingTurret), false, "SmallGatlingTurret", "OKI23mmDG", "OKI23mmSG", "OKI50mmSG", "OKI76mmAG", "OKI76mmBSD", "OKI230mmBC", "OKI230mmBCBRD")]
    public class BlockLimitGTurret : MyGameLogicComponent
    {
        private BlockLimitPair Pair = new BlockLimitPair();
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            this.NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
            IMyFunctionalBlock FBlock = Entity as IMyFunctionalBlock;
            Pair.Block = Entity as IMyFunctionalBlock;
            FBlock.OwnershipChanged += Lock;
            FBlock.IsWorkingChanged += Lock;
            //FBlock.PropertiesChanged += PropertiesChanged;
            FBlock.AppendingCustomInfo += AppInfo;
        }

        private void AppInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            if (arg1.OwnerId == 0) arg2.AppendLine("Set ownership to enable");
            if (Pair.LimitReached) arg2.AppendLine(string.Format("Owner has {0} Gatling Turrets on\nTurn another off to turn this one on", BlockLimitCore.GTurretLimit));
        }

        private void Lock(IMyCubeBlock obj)
        {
            if (obj != null)
            {
                if (obj.OwnerId == 0 && Pair.Block.IsWorking)
                {
                    Pair.Block.Enabled = false;
                }
                else
                {
                    BlockLimitCore.Instance.UpdateQueue.Enqueue(Pair);
                }
                Pair.Block.RefreshCustomInfo();
            }
        }
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }
    }
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Refinery), false, "LargeRefinery")]
    public class BlockLimitRefinery : MyGameLogicComponent
    {
        private BlockLimitPair Pair = new BlockLimitPair();
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            this.NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
            IMyFunctionalBlock FBlock = Entity as IMyFunctionalBlock;
            Pair.Block = Entity as IMyFunctionalBlock;
            FBlock.OwnershipChanged += Lock;
            FBlock.IsWorkingChanged += Lock;
            //FBlock.PropertiesChanged += PropertiesChanged;
            FBlock.AppendingCustomInfo += AppInfo;
        }

        private void AppInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            if (arg1.OwnerId == 0) arg2.AppendLine("Set ownership to enable");
            if (Pair.LimitReached) arg2.AppendLine(string.Format("Owner has {0} Refineries on\nTurn another off to turn this one on", BlockLimitCore.RefineryLimit));
        }

        private void Lock(IMyCubeBlock obj)
        {
            if (obj != null)
            {
                if (obj.OwnerId == 0 && Pair.Block.IsWorking)
                {
                    Pair.Block.Enabled = false;
                }
                else
                {
                    BlockLimitCore.Instance.UpdateQueue.Enqueue(Pair);
                }
                Pair.Block.RefreshCustomInfo();
            }
        }
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }
    }
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Assembler), false, "LargeAssembler", "VCZAssemblerSmall")]
    public class BlockLimitAssembler : MyGameLogicComponent
    {
        private BlockLimitPair Pair;
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Pair = new BlockLimitPair();
            base.Init(objectBuilder);
            this.NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
            IMyFunctionalBlock FBlock = Entity as IMyFunctionalBlock;
            Pair.Block = Entity as IMyFunctionalBlock;
            FBlock.OwnershipChanged += Lock;
            FBlock.IsWorkingChanged += Lock;
            //FBlock.PropertiesChanged += PropertiesChanged;
            FBlock.AppendingCustomInfo += AppInfo;
        }

        private void AppInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            if (arg1.OwnerId == 0) arg2.AppendLine("Set ownership to enable");
            if (Pair.LimitReached) arg2.AppendLine(string.Format("Owner has {0} Assemblers on\nTurn another off to turn this one on", BlockLimitCore.AssemblerLimit));
        }

        private void Lock(IMyCubeBlock obj)
        {
            if (obj != null)
            {
                if (obj.OwnerId == 0 && Pair.Block.IsWorking)
                {
                    Pair.Block.Enabled = false;
                }
                else
                {
                    BlockLimitCore.Instance.UpdateQueue.Enqueue(Pair);
                }
                Pair.Block.RefreshCustomInfo();
            }
        }
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }
    }
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Projector), false, "LargeProjector", "SmallProjector")]
    public class BlockLimitProjector : MyGameLogicComponent
    {
        private BlockLimitPair Pair = new BlockLimitPair();
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            Entity.Save = false;
            this.NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
            IMyFunctionalBlock FBlock = Entity as IMyFunctionalBlock;
            Pair.Block = Entity as IMyFunctionalBlock;
            FBlock.OwnershipChanged += Lock;
            FBlock.IsWorkingChanged += Lock;
            //FBlock.PropertiesChanged += PropertiesChanged;
            FBlock.AppendingCustomInfo += AppInfo;
        }

        private void AppInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            if (arg1.OwnerId == 0) arg2.AppendLine("Set ownership to enable");
            if (Pair.LimitReached) arg2.AppendLine("Owner has a Projector on\nTurn it off to turn this one on");
        }

        private void Lock(IMyCubeBlock obj)
        {
            if (obj != null)
            {
                if (obj.OwnerId == 0 && Pair.Block.IsWorking)
                {
                    Pair.Block.Enabled = false;
                }
                else
                {
                    BlockLimitCore.Instance.UpdateQueue.Enqueue(Pair);
                }
                Pair.Block.RefreshCustomInfo();
            }
        }
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }
    }
}