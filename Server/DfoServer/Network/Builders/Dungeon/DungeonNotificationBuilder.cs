using System;
using System.Collections.Generic;
using DfoServer.Game.Dungeon;
using DfoServer.GameWorld;
using DfoServer.Network;

namespace DfoServer.Network.Builders
{
    public static class DungeonNotificationBuilder
    {
        
        
        
        
        public static byte[] BuildDungeonInfo(
            int dungeonId,
            byte difficulty,
            byte modeFlag = 0,
            byte bossX = 0,
            byte bossY = 0,
            byte hellPartyFlag0 = 0xFF,
            byte hellPartyFlag1 = 0xFF,
            byte dungeonMode = 0,
            IReadOnlyList<IReadOnlyList<(byte, byte)>> extraPairGroups = null,
            ushort value0 = 0x0000,
            ushort value1 = 0x000C,
            byte value2 = 0,
            byte flagA = 0,
            uint packetSeed = 0xFFFFFFFFu,
            byte paramA = 0,
            byte paramB = 0,
            byte paramC = 0,
            byte tailFlag0 = 0,
            byte tailFlag1 = 0,
            byte tailFlag2 = 0,
            uint tailReserved = 0)
        {
            var writer = new GamePacketWriter();

            writer.WriteInt16((short)dungeonId);
            writer.WriteByte(difficulty);
            writer.WriteByte(modeFlag);
            writer.WriteByte(bossX);
            writer.WriteByte(bossY);
            writer.WriteByte(hellPartyFlag0);
            writer.WriteByte(hellPartyFlag1);
            writer.WriteByte(dungeonMode);

            var groupCount = extraPairGroups == null ? 0 : extraPairGroups.Count;
            writer.WriteByte((byte)groupCount);
            for (var gi = 0; gi < groupCount; gi++)
            {
                var group = extraPairGroups[gi];
                writer.WriteByte((byte)group.Count);
                for (var pi = 0; pi < group.Count; pi++)
                {
                    var pair = group[pi];
                    writer.WriteByte(pair.Item1);
                    writer.WriteByte(pair.Item2);
                }
            }

            writer.WriteUInt16(value0);
            writer.WriteUInt16(value1);
            writer.WriteByte(value2);
            writer.WriteByte(flagA);
            writer.WriteInt32(unchecked((int)packetSeed));
            writer.WriteByte(paramA);
            writer.WriteByte(paramB);
            writer.WriteByte(paramC);
            writer.WriteByte(tailFlag0);
            writer.WriteByte(tailFlag1);
            writer.WriteByte(tailFlag2);
            writer.WriteInt32(unchecked((int)tailReserved));
            return writer.ToArray();
        }

        
        
        
        
        public static byte[] BuildStartMap(
            Dungeon.MazeSumInfo maze,
            ushort firstMonsterSequence,
            int randomSeed = 0,
            byte fogOrModeFlag = 0,
            byte abyssGuardianType = 0,
            byte reserved0 = 0,
            uint stateValue0 = 1,
            byte stateValue1 = 1,
            byte fogFlag = 0,
            byte partyMemberIndex = 0xFF,
            IReadOnlyList<Game.Dungeon.PassiveObjectDropEntry> extraEntries = null,
            IReadOnlyList<Game.Dungeon.RidableObjectSpawnEntry> ridableEntries = null)
        {
            var writer = new GamePacketWriter();

            writer.WriteByte((byte)maze.X);
            writer.WriteByte((byte)maze.Y);
            writer.WriteByte(fogOrModeFlag);
            writer.WriteInt32(randomSeed);
            writer.WriteByte(abyssGuardianType);
            writer.WriteByte(reserved0);
            writer.WriteInt32(unchecked((int)stateValue0));
            writer.WriteByte(stateValue1);

            writer.WriteUInt16((ushort)maze.Index);
            writer.WriteByte((byte)maze.Monsters.Count);

            
            for (var index = 0; index < maze.Monsters.Count; index++)
            {
                var monster = maze.Monsters[index];

                writer.WriteUInt16(0x0000);
                writer.WriteInt32(index);
                writer.WriteUInt16((ushort)(firstMonsterSequence + index + 1));
                writer.WriteInt32(monster.Code);
                writer.WriteByte(monster.Level);
                writer.WriteByte(monster.Type);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteInt32(0x00000000);
            }

            
            var extraCount = extraEntries?.Count ?? 0;
            writer.WriteByte((byte)extraCount);
            for (int i = 0; i < extraCount; i++)
            {
                var e = extraEntries[i];
                writer.WriteByte(e.ObjectIndex);     
                writer.WriteUInt16(e.GlobalSeq);     
                writer.WriteUInt32(e.ItemId);        
                writer.WriteUInt32(e.StackCount);    
                writer.WriteUInt16(e.Endurance);     
                writer.WriteByte(0);                 
                writer.WriteUInt16(0);               
                writer.WriteUInt16(0);               
                writer.WriteByte(0);                 
            }

            writer.WriteByte(fogFlag);

            
            
            var ridableForThisRoom = new System.Collections.Generic.List<Game.Dungeon.RidableObjectSpawnEntry>();
            if (ridableEntries != null)
                foreach (var r in ridableEntries)
                    ridableForThisRoom.Add(r);

            if (ridableForThisRoom.Count > 0)
            {
                writer.WriteByte(1);                                     
                writer.WriteByte((byte)ridableForThisRoom.Count);        
                foreach (var r in ridableForThisRoom)
                {
                    
                    
                    writer.WriteInt32(r.PosX);
                    writer.WriteInt32(r.PosY);
                    writer.WriteInt32(r.ObjectIndex);   
                    writer.WriteInt32(r.Faction);        
                    writer.WriteInt32(0);                
                }
            }
            else
            {
                writer.WriteByte(0);                                     
            }

            writer.WriteByte(partyMemberIndex);

            return writer.ToArray();
        }

        
        
        public static byte[] BuildStartMapRevisit(Dungeon.MazeSumInfo maze, uint seed)
        {
            var writer = new GamePacketWriter();
            writer.WriteByte((byte)maze.X);
            writer.WriteByte((byte)maze.Y);
            writer.WriteByte(0);                      
            writer.WriteInt32(unchecked((int)seed));
            writer.WriteByte(0);                      
            writer.WriteByte(0);                      
            writer.WriteInt32(1);                     
            writer.WriteByte(0);                      
            writer.WriteByte(0x00);                   
            writer.WriteByte(0xFF);                   
            return writer.ToArray();
        }

        
        
        
        public static byte[] BuildMonsterDie(ushort monsterSeqId, IReadOnlyList<DropInfo> drops, ushort ownerActorId)
        {
            var w = new GamePacketWriter();

            
            w.WriteUInt16(monsterSeqId);
            var dropCount = drops?.Count ?? 0;
            w.WriteByte((byte)dropCount);

            for (int i = 0; i < dropCount; i++)
            {
                var d = drops[i];
                w.WriteUInt16(d.SceneSlot);     
                w.WriteUInt32(d.TemplateId);    
                w.WriteByte(d.UpgradeLevel);    
                w.WriteUInt32(d.StackCount);    
                w.WriteUInt16(d.Endurance);     
                w.WriteUInt32(0);               
                w.WriteByte(0);                 
                w.WriteByte(0);                 
                w.WriteUInt16(0);               
                w.WriteUInt32(0);               
                w.WriteByte(0);                 
                w.WriteUInt16(0);               
                w.WriteByte(0);                 
                w.WriteZeroBytes(8);            
                w.WriteUInt16(ownerActorId);    
            }

            
            w.WriteByte(0x00);
            w.WriteByte(0x00);
            w.WriteByte(0xFF);
            w.WriteByte(0x00);

            return w.ToArray();
        }

        
        public static byte[] BuildEnableClearDungeon()
        {
            return System.Array.Empty<byte>();
        }

        
        
        public static byte[] BuildPlayResult(ushort userId, int bossCode, uint totalExp, bool allKill)
        {
            var writer = new GamePacketWriter();
            writer.WriteByte(0x63);              
            writer.WriteUInt32(totalExp);         
            writer.WriteByte(0x00);              
            writer.WriteByte(0x63);              
            writer.WriteByte(allKill ? (byte)1 : (byte)0);  
            if (bossCode > 0)
            {
                writer.WriteByte(0x01);                      
                writer.WriteUInt16((ushort)bossCode);        
                writer.WriteUInt32(totalExp);                 
                writer.WriteByte(0x01);                      
            }
            else
            {
                writer.WriteByte(0x00);                      
            }
            return writer.ToArray();
        }

        
        
        public static byte[] BuildExp(byte level, uint totalExp, ushort spTree0 = 0, ushort spTree1 = 0)
        {
            var writer = new GamePacketWriter();
            writer.WriteByte(level);             
            writer.WriteUInt32(totalExp);         
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteUInt16(spTree0);          
            writer.WriteUInt16(spTree1);          
            writer.WriteUInt16(0);               
            writer.WriteUInt16(0);               
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteByte(0x00);              
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteByte(0x00);              
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteUInt32(0);               
            writer.WriteZeroBytes(8);            
            return writer.ToArray();
        }

        
        
        
        
        
        
        
        
        
        
        
        public static byte[] BuildClearDungeonReward(uint totalExp, int totalGold,
            int goldCardCost = 0, int freeCardGold = 0, int freeCardItemId = 0, int freeCardItemCount = 0)
        {
            var w = new GamePacketWriter();

            
            w.WriteUInt32(totalExp);        
            w.WriteInt32(totalGold);        
            w.WriteUInt32(0);               
            w.WriteUInt32(0);               
            w.WriteByte(0);                 
            for (int i = 0; i < 25; i++)    
                w.WriteInt32(0);

            
            w.WriteByte(0);
            w.WriteByte(0);

            
            for (int i = 0; i < 8; i++)
                w.WriteInt32(0);

            
            
            
            
            
            w.WriteInt32(0);                
            w.WriteInt32(goldCardCost);     
            w.WriteInt32(0);                
            w.WriteInt32(goldCardCost);     

            
            w.WriteUInt32(0);

            
            w.WriteByte(0);

            
            
            
            byte freeCnt = (byte)(freeCardItemId > 0 ? 2 : 1);
            w.WriteByte(freeCnt);           
            w.WriteInt32(0);                
            w.WriteInt32(freeCardGold);     
            if (freeCardItemId > 0)
            {
                w.WriteInt32(freeCardItemId);   
                w.WriteInt32(freeCardItemCount); 
            }
            for (int i = 1; i < 8; i++)
                w.WriteByte(0);

            
            w.WriteInt32(0);

            
            for (int i = 0; i < 8; i++)
                w.WriteByte(0);

            
            for (int i = 0; i < 8; i++)
                w.WriteByte(0);

            
            w.WriteInt32(0);                
            w.WriteByte(0);                 
            w.WriteByte(0);                 
            w.WriteUInt32(0);               
            w.WriteInt32(0);                

            return w.ToArray();             
        }
    }
}