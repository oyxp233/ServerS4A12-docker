using DfoServer.Game.Dungeon;
using DfoServer.Game.Skills;
using DfoServer.GameWorld;
using DfoServer.Infrastructure;
using DfoServer.Network.Builders;
using DfoServer.Network.Parsers.Dungeon;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DfoServer.Network.Handlers
{
    public sealed class DungeonHandler
    {
        public string ProtocolName => "GameProtocol";

        public async Task Handle_ENUM_CMDPACKET_ENTER_SELECT_DUNGEON(EnhancedClientSession session, GamePacketHeader header, byte[] body)
        {
            FileLogger.Log($"[{ProtocolName}] ENTER_SELECT_DUNGEON: cid={session.Player.CharacterId} uid={session.Player.UserId} town={session.Player.CurTownId} area={session.Player.CurAreaId}");
            try
            {
                var snapshot = TownAreaNotificationBuilder.CreateCurrentSnapshot(session.Player);
                snapshot.AreaId = 0xFF;
                await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0017, TownAreaNotificationBuilder.BuildUserArea(snapshot)));

                
                int cid = session.Player.CharacterId;
                if (cid <= 0)
                {
                    FileLogger.Log($"[{ProtocolName}] ENTER_SELECT_DUNGEON ERROR: CharacterId<=0, USERINFO 未发送");
                }
                else
                {
                    var charRepo = new Game.Characters.SqliteCharacterRepository(
                        Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                    var record = charRepo.GetById(cid);
                    var subtype1Repo = new Game.CharacterData.SqliteSubtype1Repository(
                        Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                    var addition = subtype1Repo.HasData(cid) ? subtype1Repo.Load(cid) : null;
                    var skillRepo = new Game.CharacterData.SqliteCharacterProgressRepository(
                        Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                    var skillSnap = skillRepo.LoadSkills(cid);

                    if (record != null && addition != null)
                    {
                        var w = new GamePacketWriter();
                        w.WriteByte(1); 
                        w.WriteUInt16(1);
                        w.WriteUInt16((ushort)record.CharacterId);
                        w.WriteBytes(UserInfoSubtype1Builder.BuildFromSnapshot(addition, skillSnap));
                        await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0002, w.ToArray()));
                        FileLogger.Log($"[{ProtocolName}] ENTER_SELECT_DUNGEON: NOTI 2 type1 dynamic body");
                    }
                    else
                    {
                        FileLogger.Log($"[{ProtocolName}] ENTER_SELECT_DUNGEON ERROR: record={record != null} addition={addition != null}, USERINFO 未发送(不兜底)");
                    }
                }
                await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0003, EnterSelectDungeonStateBuilder.BuildUserState(session.Player)));
                await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x001A, UdpHostBuilder.BuildUnavailable()));
                await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x001B, EnterSelectDungeonStateBuilder.BuildEnterSelectDungeon(session.Player)));
                FileLogger.Log($"[{ProtocolName}] ENTER_SELECT_DUNGEON: 5 packets sent OK");
            }
            catch (System.Exception ex)
            {
                FileLogger.Log($"[{ProtocolName}] ENTER_SELECT_DUNGEON EXCEPTION: {ex}");
            }
        }

        public async Task Handle_ENUM_CMDPACKET_SELECT_DUNGEON(EnhancedClientSession session, GamePacketHeader header, byte[] body)
        {
            var req = SelectDungeonRequest.Parse(body);

            session.Player.CurDungeon = (short)req.DungeonId;
            session.Player.CurDungeonDifficulty = req.Difficulty;
            session.Player.CurDungeonFlag1 = req.Flag1;
            session.Player.CurDungeonFlag2 = req.Flag2;
            session.Player.CurMonsterCnt = 0;
            session.Player.CurLayeredMapIndex = -1;
            session.Player.CurMoveMapU15 = 0;
            session.Player.CurMoveMapU19 = 0;
            session.Player.CurDungeonTotalExp = 0;
            session.Player.CurDungeonTotalGold = 0;
            session.Player.CurSceneSlotCounter = 0;
            session.Player.CurDungeonDrops.Clear();
            session.Player.CurRoomKilledSeqIds.Clear();
            session.Player.DungeonRoomStates.Clear();
            session.Player.CurDungeonRidableObjects.Clear();
            session.Player.CurBossKilled = false;
            session.Player.CurBossCode = 0;

            var selection = Dungeon.SelectDungeonMaze(req.DungeonId);
            session.Player.CurMazeIndex = selection.Index;
            session.Player.CurDungeonRidableObjects = InitRidableObjects(selection.Maze);
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x001C, DungeonNotificationBuilder.BuildDungeonInfo(
                dungeonId: req.DungeonId,
                difficulty: req.Difficulty,
                modeFlag: (byte)selection.Index,
                bossX: (byte)selection.Maze.BossMap[0],
                bossY: (byte)selection.Maze.BossMap[1])));

            await SendStartMapAsync(session, 0xFF, 0xFF, overrideMapId: -1);

            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0117, BitConverter.GetBytes(session.Player.CharacterId)));
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x019F, new byte[] { 0x00, 0x00 }));
        }

        public async Task Handle_ENUM_CMDPACKET_MOVE_MAP(EnhancedClientSession session, GamePacketHeader header, byte[] body)
        {
            var req = MoveMapRequest.Parse(body);
            session.Player.CurMoveMapU15 = req.Unknown15;
            session.Player.CurMoveMapU19 = req.Unknown19;

            int overrideMapId = -1;

            if (req.Unknown23 == 1)
            {
                var layeredIds = Dungeon.GetLayeredMapIds(session.Player.CurDungeon, req.NextX, req.NextY, session.Player.CurMazeIndex);
                if (layeredIds != null && layeredIds.Length > 0)
                {
                    var nextLayer = session.Player.CurLayeredMapIndex + 1;
                    if (nextLayer < layeredIds.Length)
                    {
                        session.Player.CurLayeredMapIndex = nextLayer;
                        overrideMapId = layeredIds[nextLayer];
                    }
                }
            }
            else
            {
                session.Player.CurLayeredMapIndex = -1;
            }

            await SendStartMapAsync(session, req.NextX, req.NextY, overrideMapId);
        }

        private async Task SendStartMapAsync(EnhancedClientSession session, int nextX, int nextY, int overrideMapId)
        {
            var maze = Dungeon.GetDungeonMapMonsterSummaryInformation(session.Player.CurDungeon, nextX, nextY, session.Player.CurMazeIndex, overrideMapId);
            var roomKey = new Game.Dungeon.RoomKey(maze.X, maze.Y, overrideMapId);

            byte[] startMapBody;

            if (session.Player.DungeonRoomStates.TryGetValue(roomKey, out var cached))
            {
                
                session.Player.CurRoomMonsters = cached.Maze.Monsters;
                session.Player.CurRoomStartSequence = cached.FirstSeqId;
                session.Player.CurRoomKilledSeqIds = cached.KilledSeqIds;
                session.Player.CurRoomLcg = cached.Lcg;
                session.Player.CurDungeonSeed = cached.Seed;

                startMapBody = DungeonNotificationBuilder.BuildStartMapRevisit(cached.Maze, cached.Seed);
                FileLogger.Log($"[DungeonHandler] START_MAP revisit: room=({maze.X},{maze.Y}) killed={cached.KilledSeqIds.Count}/{cached.MonsterCount} cleared={cached.IsCleared}");
            }
            else
            {
                
                session.Player.CurRoomMonsters = maze.Monsters;

                var startSequence = session.Player.CurMonsterCnt;
                session.Player.CurRoomStartSequence = (ushort)(startSequence + 1);
                
                
                var seed = (uint)(_seedGen.Next() & ~0x40000);
                session.Player.CurDungeonSeed = seed;
                var lcg = new DnfLcg(seed);
                session.Player.CurRoomLcg = lcg;
                var killedSet = new HashSet<ushort>();
                session.Player.CurRoomKilledSeqIds = killedSet;

                session.Player.DungeonRoomStates[roomKey] = new Game.Dungeon.RoomState
                {
                    Maze = maze,
                    FirstSeqId = session.Player.CurRoomStartSequence,
                    MonsterCount = (ushort)maze.Monsters.Count,
                    KilledSeqIds = killedSet,
                    Seed = seed,
                    Lcg = lcg,
                };

                byte layeredFlag = (byte)(overrideMapId > 0 ? 1 : 0);

                
                var itemSeqCounter = (ushort)_seedGen.Next(60000);
                var extraEntries = GeneratePassiveObjectDrops(
                    session.Player.CurDungeon, session.Player.CurMazeIndex,
                    ref itemSeqCounter);

                if (extraEntries != null)
                {
                    foreach (var e in extraEntries)
                        session.Player.CurDungeonDrops[e.GlobalSeq] = new DropInfo
                        {
                            SceneSlot = e.GlobalSeq,
                            TemplateId = e.ItemId,
                            StackCount = e.StackCount,
                            Endurance = e.Endurance,
                        };
                }

                var ridableForRoom = GetRidableEntriesForRoom(session, maze.X, maze.Y);
                startMapBody = DungeonNotificationBuilder.BuildStartMap(maze, startSequence, (int)seed,
                    fogOrModeFlag: layeredFlag, extraEntries: extraEntries, ridableEntries: ridableForRoom);
                session.Player.CurMonsterCnt += (ushort)maze.Monsters.Count;
            }

            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x001D, startMapBody));
        }

        private static List<Game.Dungeon.RidableObjectSpawnEntry> InitRidableObjects(PvfLib.MazeInfo maze)
        {
            var result = new List<Game.Dungeon.RidableObjectSpawnEntry>();
            if (maze.RidableScript == null || maze.RidableScript.Objects.Count == 0)
                return result;

            var script = maze.RidableScript;
            var candidates = new List<PvfLib.RidableObject>(script.Objects);

            if (script.SelectCount > 0 && script.SelectCount < candidates.Count)
            {
                lock (_seedGen)
                {
                    for (int i = candidates.Count - 1; i > 0; i--)
                    {
                        int j = _seedGen.Next(i + 1);
                        var tmp = candidates[i];
                        candidates[i] = candidates[j];
                        candidates[j] = tmp;
                    }
                }
                candidates = candidates.GetRange(0, script.SelectCount);
            }

            foreach (var obj in candidates)
            {
                result.Add(new Game.Dungeon.RidableObjectSpawnEntry
                {
                    ObjectIndex = obj.ObjectIndex,
                    MonsterIndex = 0,
                    PosX = obj.PosX,
                    PosY = obj.PosY,
                    Faction = obj.Faction,
                    MapX = (byte)obj.MapX,
                    MapY = (byte)obj.MapY,
                });
            }

            if (result.Count > 0)
                FileLogger.Log($"[DungeonHandler] RIDABLE: selected {result.Count}/{script.Objects.Count} objects (select={script.SelectCount})");

            return result;
        }

        private static List<Game.Dungeon.RidableObjectSpawnEntry> GetRidableEntriesForRoom(
            EnhancedClientSession session, int roomX, int roomY)
        {
            var all = session.Player.CurDungeonRidableObjects;
            if (all == null || all.Count == 0) return null;
            var result = new List<Game.Dungeon.RidableObjectSpawnEntry>();
            foreach (var r in all)
            {
                if (r.MapX == roomX && r.MapY == roomY)
                    result.Add(r);
            }
            return result.Count > 0 ? result : null;
        }

        private static readonly System.Random _seedGen = new System.Random();

        private static List<Game.Dungeon.PassiveObjectDropEntry> GeneratePassiveObjectDrops(
            int dungeonId, int mazeIndex, ref ushort itemSeqCounter)
        {
            try
            {
                var dgnlst = Dungeon.LoadDungeonLstFile();
                var dgnPath = dgnlst.GetById(dungeonId);
                if (dgnPath == null || string.IsNullOrEmpty(dgnPath.FilePath)) return null;

                var dgnText = GameWorld.PvfArchiveAccessor.ReadText(
                    System.IO.Path.Combine("dungeon", dgnPath.FilePath));
                var dgn = PvfLib.DungeonFile.Parse(dgnText);
                if (dgn.SpecialPassiveObjectItems.Count == 0) return null;

                var result = new List<Game.Dungeon.PassiveObjectDropEntry>();
                var rng = new System.Random();

                foreach (var item in dgn.SpecialPassiveObjectItems)
                {
                    int roll = rng.Next(10000);
                    if (roll >= item.DropRate) continue;

                    itemSeqCounter++;
                    result.Add(new Game.Dungeon.PassiveObjectDropEntry
                    {
                        ObjectIndex = (byte)item.Index,
                        GlobalSeq = itemSeqCounter,
                        ItemId = (uint)item.ItemId,
                        StackCount = 1,
                    });
                }

                if (result.Count > 0)
                    FileLogger.Log($"[DungeonHandler] PASSIVE_OBJ_DROP: {result.Count} items generated for dungeon={dungeonId}");
                return result.Count > 0 ? result : null;
            }
            catch (System.Exception ex)
            {
                FileLogger.Log($"[DungeonHandler] GeneratePassiveObjectDrops ERROR: {ex.Message}");
                return null;
            }
        }

        public async Task Handle_ENUM_CMDPACKET_DIE_MONSTER(EnhancedClientSession session, GamePacketHeader header, byte[] body)
        {
            var req = DieMonsterRequest.Parse(body);

            if (req.IsPassiveObject)
            {
                FileLogger.Log($"[DungeonHandler] DIE_MONSTER: passive object code={req.LocalIndex}, ignored (client-side drops)");
                return;
            }

            if (!session.Player.CurRoomKilledSeqIds.Add(req.LocalIndex))
            {
                FileLogger.Log($"[DungeonHandler] DIE_MONSTER: duplicate seqId={req.LocalIndex}, ignored");
                await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0026,
                    DungeonNotificationBuilder.BuildMonsterDie(req.LocalIndex, null, session.Player.UserId)));
                return;
            }

            var roomLocalIndex = req.LocalIndex - session.Player.CurRoomStartSequence;
            var monsters = session.Player.CurRoomMonsters;

            List<DropInfo> drops = null;
            if (roomLocalIndex >= 0 && roomLocalIndex < monsters.Count)
            {
                var monster = monsters[roomLocalIndex];
                var monsterLevel = monster.Level;

                var weight = Dungeon.GetExperienceWeight(session.Player.CurDungeon);
                var gainedExp = (uint)MonsterRewardTable.CalcExp(monsterLevel, weight);

                var dropPool = MonsterDropTable.GetDropPool(monster.Code);
                var generator = new DropGenerator(session.Player.CurRoomLcg);
                var slotCounter = session.Player.CurSceneSlotCounter;
                int dungeonBasisLevel = monsterLevel;
                try { dungeonBasisLevel = Dungeon.GetDungeonBasicLv(session.Player.CurDungeon); } catch { }
                var (goldGained, generatedDrops) = generator.GenerateMonsterDrops(monsterLevel, monster.Type, monster.Code, session.Player.CurDungeonDifficulty, dungeonBasisLevel, ref slotCounter, dropPool);
                session.Player.CurSceneSlotCounter = slotCounter;

                session.Player.Exp += gainedExp;
                session.Player.CurDungeonTotalExp += gainedExp;
                session.Player.CurDungeonTotalGold += goldGained;

                if (generatedDrops.Count > 0)
                {
                    foreach (var drop in generatedDrops)
                        session.Player.CurDungeonDrops[drop.SceneSlot] = drop;
                    drops = generatedDrops;
                    FileLogger.Log($"[DungeonHandler] DROP: {generatedDrops.Count} items, seqId={req.LocalIndex} seed={session.Player.CurRoomLcg.Seed:X8}");
                }

                var prevLevel = session.Player.Level;
                while (session.Player.Level < 86 && session.Player.Exp >= (uint)ExpTableProvider.GetLevelThreshold(session.Player.Level))
                    session.Player.Level++;

                var leveledUp = session.Player.Level > prevLevel;

                ushort spTree0 = 0, spTree1 = 0;
                try
                {
                    var charRepo2 = new Game.Characters.SqliteCharacterRepository(
                        Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                    var rec2 = charRepo2.GetById(session.Player.CharacterId);
                    var skillRepo2 = new Game.CharacterData.SqliteCharacterProgressRepository(
                        Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                    var skillSnap2 = skillRepo2.LoadSkills(session.Player.CharacterId);
                    if (rec2 != null && skillSnap2 != null)
                    {
                        var sp2 = Game.Skills.SkillPointCalculator.Calculate(
                            rec2.Job, session.Player.Level, rec2.BonusSp, rec2.BonusTp, skillSnap2);
                        spTree0 = (ushort)sp2.RemainingSp;
                        spTree1 = (ushort)sp2.RemainingSp;
                    }
                }
                catch { }

                await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0025,
                    ExpNotificationBuilder.Build(session.Player.Level, session.Player.Exp, spTree0, spTree1)));

                if (leveledUp)
                {
                    FileLogger.Log($"[DungeonHandler] LEVEL UP: cid={session.Player.CharacterId} {prevLevel}→{session.Player.Level} exp={session.Player.Exp}");
                    PersistLevelAndExp(session.Player.CharacterId, session.Player.Level, session.Player.Exp);
                    await SendUserInfoBroadcast(session);
                }

                if (monster.Type == 3)
                {
                    session.Player.CurBossKilled = true;
                    session.Player.CurBossCode = monster.Code;
                }
            }

            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0026,
                DungeonNotificationBuilder.BuildMonsterDie(req.LocalIndex, drops, session.Player.UserId)));

            if (session.Player.CurBossKilled && roomLocalIndex >= 0 && roomLocalIndex < monsters.Count && monsters[roomLocalIndex].Type == 3)
                await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x001F, DungeonNotificationBuilder.BuildEnableClearDungeon()));

            if (session.Player.CurRoomKilledSeqIds.Count >= monsters.Count && monsters.Count > 0)
                FileLogger.Log($"[DungeonHandler] ROOM CLEARED: dungeon={session.Player.CurDungeon} killed={session.Player.CurRoomKilledSeqIds.Count}/{monsters.Count}");
        }

        private void PersistLevelAndExp(int characterId, byte level, uint exp)
        {
            try
            {
                var repo = new Game.Characters.SqliteCharacterRepository(
                    Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                repo.UpdateLevelAndExp(characterId, level, exp);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[DungeonHandler] PersistLevelAndExp ERROR: {ex.Message}");
            }
        }

        private async Task SendUserInfoBroadcast(EnhancedClientSession session)
        {
            try
            {
                int cid = session.Player.CharacterId;
                var charRepo = new Game.Characters.SqliteCharacterRepository(
                    Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                var record = charRepo.GetById(cid);
                var subtype1Repo = new Game.CharacterData.SqliteSubtype1Repository(
                    Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                var addition = subtype1Repo.HasData(cid) ? subtype1Repo.Load(cid) : null;
                var skillRepo = new Game.CharacterData.SqliteCharacterProgressRepository(
                    Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                var skillSnap = skillRepo.LoadSkills(cid);

                if (record != null && addition != null)
                {
                    var w = new GamePacketWriter();
                    w.WriteByte(1);
                    w.WriteUInt16(1);
                    w.WriteUInt16((ushort)record.CharacterId);
                    w.WriteBytes(UserInfoSubtype1Builder.BuildFromSnapshot(addition, skillSnap));
                    await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0002, w.ToArray()));
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[DungeonHandler] SendUserInfoBroadcast ERROR: {ex.Message}");
            }
        }

        public async Task Handle_ENUM_CMDPACKET_GET_ITEM(EnhancedClientSession session, GamePacketHeader header, byte[] body)
        {
            var req = GetItemRequest.Parse(body);
            FileLogger.Log($"[{ProtocolName}] GET_ITEM: cid={session.Player.CharacterId} srcSlot={req.SrcSlot}");

            DropInfo matchedDrop;
            if (!session.Player.CurDungeonDrops.TryGetValue(req.SrcSlot, out matchedDrop))
            {
                FileLogger.Log($"[{ProtocolName}] GET_ITEM: no pending drop for srcSlot={req.SrcSlot}, ignored");
                return;
            }

            if (matchedDrop.IsGold)
            {
                var goldAmount = (int)matchedDrop.StackCount;
                PersistGold(session.Player.CharacterId, goldAmount);
                session.Player.CurDungeonDrops.Remove(req.SrcSlot);
                await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0027,
                    DropItemBuilder.BuildPickupGold(req.SrcSlot, session.Player.UserId, goldAmount)));
                FileLogger.Log($"[{ProtocolName}] GET_ITEM: gold pickup srcSlot={req.SrcSlot} amount={goldAmount}");
            }
            else
            {
                short invSlot;
                if (!TryPickupItemToInventory(session.Player.CharacterId, (int)matchedDrop.TemplateId, (int)matchedDrop.StackCount, out invSlot))
                {
                    FileLogger.Log($"[{ProtocolName}] GET_ITEM: FAILED to insert item templateId={matchedDrop.TemplateId} — inventory full or special, drop preserved for retry");
                    return;
                }
                session.Player.CurDungeonDrops.Remove(req.SrcSlot);
                await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0027,
                    DropItemBuilder.BuildPickupItem(req.SrcSlot, session.Player.UserId, (ushort)invSlot, 7)));
                FileLogger.Log($"[{ProtocolName}] GET_ITEM: item pickup srcSlot={req.SrcSlot} templateId={matchedDrop.TemplateId} invSlot={invSlot}");
            }
        }

        public async Task Handle_ENUM_CMDPACKET_DIE_CHARACTER(EnhancedClientSession session, GamePacketHeader header, byte[] body)
        {
            FileLogger.Log($"[{ProtocolName}] DIE_CHARACTER: uid={session.Player.UserId} body={BitConverter.ToString(body)}");
            
            var w = new GamePacketWriter();
            w.WriteUInt16(session.Player.UserId);
            w.WriteByte(0x00);  
            w.WriteByte(0x00);
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0020, w.ToArray()));
        }

        public async Task Handle_ENUM_CMDPACKET_USE_COIN(EnhancedClientSession session, GamePacketHeader header, byte[] body)
        {
            
            ushort targetId = body.Length >= 2 ? BitConverter.ToUInt16(body, 0) : session.Player.UserId;
            FileLogger.Log($"[{ProtocolName}] USE_COIN: uid={session.Player.UserId} target={targetId}");

            
            
            var noti = new GamePacketWriter();
            noti.WriteUInt16(targetId);
            noti.WriteByte(0x01);  
            noti.WriteByte(0x00);  
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0020, noti.ToArray()));

            
            var ack = new GamePacketWriter();
            ack.WriteByte(0x01);           
            ack.WriteUInt16(targetId);     
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x01, 0x0029, ack.ToArray()));
        }

        
        
        
        
        public async Task Handle_ENUM_CMDPACKET_SELECT_CARD(EnhancedClientSession session, GamePacketHeader header, byte[] body)
        {
            if (body.Length < 2) return;
            byte cardType = body[0];
            byte cardIndex = body[1];

            
            
            if (cardType >= 2 || (cardType == 1 && session.Player.CurCardRewards == null))
            {
                FileLogger.Log($"[{ProtocolName}] EPLP: state={cardType} option={cardIndex}");
                await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x01, 0x0048,
                    new byte[] { 0x01, cardType, cardIndex }));

                if (cardType == 1)
                {
                    
                    
                    int delayMs = cardIndex == 2 ? 1000 : 3000;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(delayMs);
                        try
                        {
                            session.Player.CurDungeon = 0;
                            session.Player.CurDungeonClearState = 0;
                            session.Player.CurDungeonTotalExp = 0;
                            session.Player.CurDungeonTotalGold = 0;
                            session.Player.CurDungeonDifficulty = 0;
                            session.Player.CurDungeonFlag1 = 0;
                            session.Player.CurDungeonFlag2 = 0;
                            session.Player.CurMazeIndex = -1;
                            session.Player.CurLayeredMapIndex = -1;
                            session.Player.CurMap = 0;
                            session.Player.CurMonsterCnt = 0;
                            session.Player.CurRoomStartSequence = 0;
                            session.Player.CurRoomMonsters = System.Array.Empty<GameWorld.Dungeon.MonsterSumInfo>();
                            session.Player.CurRoomKilledSeqIds.Clear();
                            session.Player.CurBossKilled = false;
                            session.Player.CurBossCode = 0;
                            session.Player.CurSceneSlotCounter = 0;
                            session.Player.CurDungeonSeed = 0;
                            session.Player.CurRoomLcg = null;
                            session.Player.CurMoveMapU15 = 0;
                            session.Player.CurMoveMapU19 = 0;
                            session.Player.CurDungeonDrops.Clear();
                            session.Player.DungeonRoomStates.Clear();
                            session.Player.CurCardRewards = null;
                            session.Player.CurCardFlipCount = 0;
                            session.Player.CurFreeCardSlots = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
                            session.Player.CurPaidCardSlots = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
                            session.Player.UserState = 0x01;

                            
                            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0003,
                                EnterSelectDungeonStateBuilder.BuildUserState(session.Player)));

                            
                            var snapshot = TownAreaNotificationBuilder.CreateCurrentSnapshot(session.Player);
                            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0017,
                                TownAreaNotificationBuilder.BuildUserArea(snapshot)));
                            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0018,
                                TownAreaNotificationBuilder.BuildAreaUsers(snapshot)));

                            FileLogger.Log($"[{ProtocolName}] EPLP ReturnToVillage: STATE+AREA+USERS sent");
                        }
                        catch (Exception ex) { FileLogger.Log($"[{ProtocolName}] EPLP ReturnToVillage ERROR: {ex.Message}"); }
                    });
                }
                return;
            }

            
            if (cardIndex > 3) return;
            session.Player.CurCardFlipCount++;
            FileLogger.Log($"[{ProtocolName}] SELECT_CARD: flip#{session.Player.CurCardFlipCount} type={cardType} index={cardIndex}");

            if (cardType == 0)
                session.Player.CurFreeCardSlots[cardIndex] = 0x00;
            else
                session.Player.CurPaidCardSlots[cardIndex] = 0x00;

            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x01, 0x0047,
                BuildCardInfoAck(session)));

            bool freeSelected = session.Player.CurFreeCardSlots[0] != 0xFF;
            bool paidSelected = session.Player.CurPaidCardSlots[0] != 0xFF;

            if (freeSelected && paidSelected)
            {
                var cards = session.Player.CurCardRewards;
                if (cards != null)
                {
                    var entries = new System.Collections.Generic.List<byte[]>();

                    
                    if (cards.Count > 0 && cards[0].IsGold && cards[0].GoldAmount > 0)
                    {
                        PersistGold(session.Player.CharacterId, cards[0].GoldAmount);
                        int totalGold = ReadGold(session.Player.CharacterId);
                        entries.Add(BuildItemEntry(0, 0, (uint)totalGold));
                    }

                    
                    if (cards.Count > 1 && !cards[1].IsGold && cards[1].ItemId > 0)
                    {
                        short slot;
                        if (TryPickupItemToInventory(session.Player.CharacterId, cards[1].ItemId, cards[1].StackCount, out slot))
                            entries.Add(BuildEquipEntry(slot, (uint)cards[1].ItemId));
                    }

                    
                    if (cards.Count > 4 && cards[4].IsGold && cards[4].GoldAmount > 0)
                    {
                        PersistGold(session.Player.CharacterId, cards[4].GoldAmount);
                        int totalGold = ReadGold(session.Player.CharacterId);
                        entries.Add(BuildItemEntry(0, 0, (uint)totalGold));
                    }

                    
                    if (cards.Count > 5 && !cards[5].IsGold && cards[5].ItemId > 0)
                    {
                        short slot;
                        if (TryPickupItemToInventory(session.Player.CharacterId, cards[5].ItemId, cards[5].StackCount, out slot))
                            entries.Add(BuildEquipEntry(slot, (uint)cards[5].ItemId));
                    }

                    
                    if (entries.Count > 0)
                    {
                        var w = new GamePacketWriter();
                        w.WriteByte(0);                     
                        w.WriteUInt16((ushort)entries.Count);
                        foreach (var e in entries)
                            w.WriteBytes(e);
                        await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x000E, w.ToArray()));
                    }

                    FileLogger.Log($"[{ProtocolName}] Card flip complete — {entries.Count} entries sent via NOTI 14");
                }
                session.Player.CurCardRewards = null;
            }
        }

        
        
        
        private byte[] BuildCardInfoAck(EnhancedClientSession session)
        {
            var w = new GamePacketWriter();
            w.WriteByte(0x01);  

            for (int i = 0; i < 8; i++)
            {
                if (i >= 4)
                {
                    
                    w.WriteByte(0xFF);
                    w.WriteByte(0xFF);
                    w.WriteByte(0xFF);
                    w.WriteByte(0xFF);
                    continue;
                }

                bool freeSelected = session.Player.CurFreeCardSlots[i] != 0xFF;
                bool paidSelected = session.Player.CurPaidCardSlots[i] != 0xFF;

                if (i != 0)
                {
                    
                    w.WriteByte(0xFF);
                    w.WriteByte(0xFF);
                    w.WriteByte(0x00);
                    w.WriteByte(0x00);
                    continue;
                }

                
                w.WriteByte(freeSelected ? (byte)0x00 : (byte)0xFF);  
                w.WriteByte(paidSelected ? (byte)0x00 : (byte)0xFF);  

                if (paidSelected)
                {
                    
                    
                    var cards = session.Player.CurCardRewards;
                    int paidGoldAmt = (cards != null && cards.Count > 4 && cards[4].IsGold) ? cards[4].GoldAmount : 0;
                    int paidItemId = (cards != null && cards.Count > 5 && !cards[5].IsGold) ? cards[5].ItemId : 0;
                    int paidItemCnt = (cards != null && cards.Count > 5 && !cards[5].IsGold) ? cards[5].StackCount : 0;

                    w.WriteByte(2);                         
                    w.WriteUInt32(0);                       
                    w.WriteInt32(paidGoldAmt);              
                    w.WriteUInt32((uint)paidItemId);        
                    w.WriteInt32(paidItemCnt);              
                }
                else
                {
                    w.WriteByte(0x00);  
                }

                w.WriteByte(0x00);  
            }

            return w.ToArray();
        }

        
        
        private static byte[] HexToBytes(string hex) {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        private static byte[] BuildCardLayoutAck()
        {
            var w = new GamePacketWriter();
            w.WriteByte(0x01);
            w.WriteUInt16(0x0001);
            for (int i = 1; i < 8; i++)
                w.WriteUInt16(0xFFFF);
            return w.ToArray();
        }

        public async Task Handle_SET_PLAY_RESULT(EnhancedClientSession session, GamePacketHeader header, byte[] body)
        {
            if (!session.Player.CurBossKilled) return;
            session.Player.CurBossKilled = false;
            PersistLevelAndExp(session.Player.CharacterId, session.Player.Level, session.Player.Exp);

            
            int dungeonLevel = 85;
            try { dungeonLevel = GameWorld.Dungeon.GetDungeonBasicLv(session.Player.CurDungeon); } catch { }
            var lcg = session.Player.CurRoomLcg ?? new Game.Dungeon.DnfLcg(session.Player.CurDungeonSeed);
            var freeGold = Game.Dungeon.ClearRewardGenerator.GenerateGoldCard(
                dungeonLevel, session.Player.CurDungeonDifficulty, lcg);
            var freeItem = Game.Dungeon.ClearRewardGenerator.GenerateItemCard(
                dungeonLevel, session.Player.CurDungeonDifficulty, lcg);
            var paidGold = Game.Dungeon.ClearRewardGenerator.GenerateGoldCard(
                dungeonLevel, session.Player.CurDungeonDifficulty, lcg);
            var paidItem = Game.Dungeon.ClearRewardGenerator.GenerateItemCard(
                dungeonLevel, session.Player.CurDungeonDifficulty, lcg);
            session.Player.CurCardRewards = new System.Collections.Generic.List<Game.Dungeon.ClearRewardGenerator.CardReward>
            {
                freeGold, freeItem, default, default,  
                paidGold, paidItem, default, default    
            };

            
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0022,
                DungeonNotificationBuilder.BuildPlayResult(
                    session.Player.UserId, session.Player.CurBossCode,
                    session.Player.CurDungeonTotalExp, allKill: true)));
            ushort spTree0 = 0, spTree1 = 0;
            try
            {
                var charRepo = new Game.Characters.SqliteCharacterRepository(
                    Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                var rec = charRepo.GetById(session.Player.CharacterId);
                var skillRepo = new Game.CharacterData.SqliteCharacterProgressRepository(
                    Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                var skillSnap = skillRepo.LoadSkills(session.Player.CharacterId);
                if (rec != null && skillSnap != null)
                {
                    var sp = Game.Skills.SkillPointCalculator.Calculate(
                        rec.Job, rec.Level, rec.BonusSp, rec.BonusTp, skillSnap);
                    spTree0 = (ushort)sp.RemainingSp;
                    spTree1 = (ushort)sp.RemainingSp;
                }
            }
            catch { }
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0025,
                DungeonNotificationBuilder.BuildExp(session.Player.Level, session.Player.Exp, spTree0, spTree1)));
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0023,
                DungeonNotificationBuilder.BuildClearDungeonReward(
                    session.Player.CurDungeonTotalExp, session.Player.CurDungeonTotalGold,
                    goldCardCost: 10180, freeCardGold: freeGold.GoldAmount,
                    freeCardItemId: freeItem.ItemId, freeCardItemCount: freeItem.StackCount)));
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x01, 0x0045, new byte[] { 0x01 }));
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x01, 0x0046, BuildCardLayoutAck()));

            session.Player.CurDungeonClearState = 4;
            session.Player.CurCardFlipCount = 0;
            session.Player.CurFreeCardSlots = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
            session.Player.CurPaidCardSlots = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
        }

        private bool TryPickupItemToInventory(int characterId, int itemTemplateId, int stackCount, out short assignedSlot)
        {
            assignedSlot = -1;
            try
            {
                var store = new Game.Inventory.SqliteInventoryStore(
                    Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                using (store.BeginScope(characterId, 1))
                {
                    return store.TryPickupItem(itemTemplateId, stackCount, out assignedSlot);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[DungeonHandler] TryPickupItemToInventory ERROR: {ex.Message}");
                return false;
            }
        }

        private void PersistGold(int characterId, int goldGained)
        {
            if (goldGained <= 0) return;
            try
            {
                var connStr = Infrastructure.SqliteDatabaseBootstrap.Initialize(
                    Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                using (var conn = new SqliteConnection(connStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        var wallet = Game.Inventory.CurrencyService.LoadWallet(conn, tx, characterId);
                        Game.Inventory.CurrencyService.UpdateGold(conn, tx, characterId, wallet.Gold + goldGained);
                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[DungeonHandler] PersistGold ERROR: {ex.Message}");
            }
        }

        private int ReadGold(int characterId)
        {
            try
            {
                var connStr = Infrastructure.SqliteDatabaseBootstrap.Initialize(
                    Infrastructure.ServerPaths.DatabasePath, Infrastructure.ServerPaths.SchemaFilePath);
                using (var conn = new SqliteConnection(connStr))
                {
                    conn.Open();
                    var wallet = Game.Inventory.CurrencyService.LoadWallet(conn, null, characterId);
                    return wallet.Gold;
                }
            }
            catch { return 0; }
        }

        
        
        private static byte[] BuildItemEntry(short slotIndex, uint itemId, uint instanceValue)
        {
            var buf = new byte[84];
            BitConverter.GetBytes(slotIndex).CopyTo(buf, 0);
            BitConverter.GetBytes(itemId).CopyTo(buf, 2);
            BitConverter.GetBytes(instanceValue).CopyTo(buf, 6);
            return buf;
        }

        private static byte[] BuildEquipEntry(short slotIndex, uint itemId,
            uint qualitySeed = 999999998, ushort durability = 32)
        {
            var buf = new byte[84];
            BitConverter.GetBytes(slotIndex).CopyTo(buf, 0);    
            BitConverter.GetBytes(itemId).CopyTo(buf, 2);        
            BitConverter.GetBytes(qualitySeed).CopyTo(buf, 6);   
            
            BitConverter.GetBytes(durability).CopyTo(buf, 11);   
            
            BitConverter.GetBytes(0xFFFFFFFF).CopyTo(buf, 22);   
            return buf;
        }

        
        
        public async Task Handle_BACK_2_VILLAGE(EnhancedClientSession session, GamePacketHeader header, byte[] body)
        {
            FileLogger.Log($"[{ProtocolName}] BACK_2_VILLAGE: returning to town");

            
            session.Player.CurDungeon = 0;
            session.Player.CurDungeonClearState = 0;
            session.Player.CurDungeonTotalExp = 0;
            session.Player.CurDungeonTotalGold = 0;
            session.Player.CurMonsterCnt = 0;
            session.Player.CurBossKilled = false;
            session.Player.CurBossCode = 0;
            session.Player.CurSceneSlotCounter = 0;
            session.Player.CurDungeonDrops.Clear();
            session.Player.DungeonRoomStates.Clear();
            session.Player.CurCardRewards = null;
            session.Player.UserState = 0x00;

            var snapshot = TownAreaNotificationBuilder.CreateCurrentSnapshot(session.Player);

            
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0003,
                EnterSelectDungeonStateBuilder.BuildUserState(session.Player)));
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0017,
                TownAreaNotificationBuilder.BuildUserArea(snapshot)));
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x0018,
                TownAreaNotificationBuilder.BuildAreaUsers(snapshot)));
            await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x00CA,
                new byte[] { 0x00 }));

            FileLogger.Log($"[{ProtocolName}] BACK_2_VILLAGE: 4 town packets sent");
        }
    }
}
