using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DfoServer.Game.Session;
using Microsoft.Data.Sqlite;

namespace DfoServer.Game.Quests
{
    public sealed class QuestManager
    {
        private readonly ISessionPacketSender _sender;
        private readonly string _connStr;

        public QuestManager(ISessionPacketSender sender, string connStr)
        {
            _sender = sender;
            _connStr = connStr;
        }

        private static byte[] StripEcho(byte[] body)
        {
            if (body == null || body.Length <= 2) return body;
            var stripped = new byte[body.Length - 2];
            Buffer.BlockCopy(body, 2, stripped, 0, stripped.Length);
            return stripped;
        }

        public async Task HandleAcceptQuestAsync(ushort wireType, byte[] body)
        {
            var qBody = StripEcho(body);
            FileLogger.Log($"[GameProtocol] ACCEPT_QUEST payload: {(qBody != null ? BitConverter.ToString(qBody) : "null")} ({qBody?.Length ?? 0}B)");
            int cid = _sender.CharacterId;
            if (cid <= 0) return;
            var ack = QuestService.HandleAcceptQuest(_connStr, cid, qBody);
            await _sender.SendCmdAckAsync(wireType, ack);
        }

        public async Task HandleGiveupQuestAsync(ushort wireType, byte[] body)
        {
            var qBody = StripEcho(body);
            int cid = _sender.CharacterId;
            if (cid <= 0) return;
            var ack = QuestService.HandleGiveupQuest(_connStr, cid, qBody);
            await _sender.SendCmdAckAsync(wireType, ack);
        }

        public async Task HandleSetTriggerAsync(ushort wireType, byte[] body)
        {
            var qBody = StripEcho(body);
            int cid = _sender.CharacterId;
            if (cid <= 0) return;
            var ack = QuestService.HandleSetTrigger(_connStr, cid, qBody);
            await _sender.SendCmdAckAsync(wireType, ack);
        }

        public async Task HandleFinishQuestAsync(ushort wireType, byte[] body)
        {
            var qBody = StripEcho(body);
            int cid = _sender.CharacterId;
            if (cid <= 0) return;
            var ack = QuestService.HandleFinishQuest(_connStr, cid, qBody);
            await _sender.SendCmdAckAsync(wireType, ack);

            if (ack != null && ack.Length > 1 && ack[0] == 0x01)
            {
                var noti = BuildAcceptedQuestNoti(cid);
                await _sender.SendNotiAsync(0x023F, noti);
                await SendAcceptableQuestListAsync();
            }
        }

        public async Task SendActiveQuestListAsync()
        {
            int cid = _sender.CharacterId;
            if (cid <= 0) return;
            var noti = BuildAcceptedQuestNoti(cid);
            await _sender.SendNotiAsync(0x023F, noti);
        }

        private async Task SendAcceptableQuestListAsync()
        {
            int cid = _sender.CharacterId;
            if (cid <= 0) return;
            var character = _sender.Player;
            int level = character != null ? character.Level : 1;
            int job = character != null ? character.Job : 0;
            int growType = character != null ? character.GrowType : -1;

            var clearedSet = new System.Collections.Generic.HashSet<int>();
            var clearedFlags = new System.Collections.Generic.Dictionary<int, int>();
            using (var conn = new SqliteConnection(_connStr))
            {
                conn.Open();
                using (var cmd = new SqliteCommand(
                    "SELECT slot_index, flag_value FROM character_invisible_falgs WHERE character_id=@cid", conn))
                {
                    cmd.Parameters.AddWithValue("@cid", cid);
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            int si = r.GetInt32(0), fv = r.GetInt32(1);
                            if (fv != 0) { clearedSet.Add(si); clearedFlags[si] = fv; }
                        }
                }
            }
            var questIds = GameWorld.QuestData.ComputeAcceptableQuests(level, job, growType, clearedSet, clearedFlags);
            var w = new Network.GamePacketWriter();
            w.WriteByte((byte)level);
            w.WriteUInt16((ushort)questIds.Count);
            foreach (var qid in questIds)
                w.WriteUInt16(qid);
            await _sender.SendNotiAsync(0x0015, w.ToArray());
        }

        private byte[] BuildAcceptedQuestNoti(int characterId)
        {
            var active = QuestService.LoadActiveQuests(_connStr, characterId);
            var w = new Network.GamePacketWriter();
            w.WriteUInt32((uint)active.Count);
            foreach (var q in active)
            {
                w.WriteUInt16(q.QuestId);
                w.WriteUInt32(q.TriggerValue);
            }
            return w.ToArray();
        }
    }
}
