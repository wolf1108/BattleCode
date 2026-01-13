using BattleCode.Models;
using BattleCode.Services;
using Microsoft.AspNet.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BattleCode.Hubs
{
    public class BattleHub : Hub
    {
        // 用戶ID => 連線ID列表 (同一用戶多分頁同時連線)
        private static ConcurrentDictionary<string, HashSet<string>> userConnections =
            new ConcurrentDictionary<string, HashSet<string>>();

        // 房間對應的連線ID清單
        private static ConcurrentDictionary<string, List<string>> rooms =
            new ConcurrentDictionary<string, List<string>>();

        // 等待配對清單 (UserId, UserName, 加入時間)
        private static List<(string UserId, string UserName, DateTime JoinTime)> waitingList = new List<(string, string, DateTime)>();

        // matchId => 準備好的用戶集合
        private static ConcurrentDictionary<string, HashSet<string>> matchReadyUsers =
            new ConcurrentDictionary<string, HashSet<string>>();
        // 當有用戶連線到 SignalR Hub 時呼叫此方法
        public override Task OnConnected()
        {
            // 從連線上下文取得當前用戶的 ID（通常是登入用戶的名稱）
            string userId = Context.User?.Identity?.Name;
            Console.WriteLine($"Hub OnConnected, userId={userId}, connId={Context.ConnectionId}");
            // 若 userId 不為空，代表該連線屬於某個登入用戶
            if (!string.IsNullOrEmpty(userId))
            {
                var connections = userConnections.GetOrAdd(userId, _ => new HashSet<string>());
                // 使用鎖 (lock) 保護 HashSet，避免多執行緒同時存取導致錯誤
                lock (connections)
                {
                    // 新增當前連線 Id 到使用者的連線集合中
                    connections.Add(Context.ConnectionId);
                }
            }
            return base.OnConnected();
        }

        // 當使用者斷開連線時觸發
        public override Task OnDisconnected(bool stopCalled)
        {
            // 取得目前連線使用者的 ID
            string userId = Context.User?.Identity?.Name;
            // 如果 userId 不為空且 userConnections 中有該用戶連線集合
            if (!string.IsNullOrEmpty(userId) && userConnections.TryGetValue(userId, out var connections))
            {
                lock (connections)
                {
                    connections.Remove(Context.ConnectionId);
                    // 如果該使用者已無任何連線，則從字典中移除該使用者
                    if (connections.Count == 0)
                    {
                        userConnections.TryRemove(userId, out _);
                    }
                }
            }

            // 從等待配對清單 waitingList 移除該使用者
            lock (waitingList)
            {
                waitingList.RemoveAll(p => p.UserId == userId);
            }

            // 清理資料庫中該使用者尚在等待狀態的比賽紀錄
            using (var db = new DatabaseEntities())
            {
                // 找出該使用者作為 Player1 或 Player2，且狀態為 "Waiting" 的比賽
                var waitingMatches = db.Matches
                    .Where(m => (m.Player1Id.ToString() == userId || m.Player2Id.ToString() == userId) && m.MatchStatus == "Waiting")
                    .ToList();
                // 將找到的比賽紀錄刪除
                foreach (var match in waitingMatches)
                {
                    db.Matches.Remove(match);
                }
                db.SaveChanges();
            }

            // 從房間移除該連線
            var room = rooms.FirstOrDefault(r => r.Value.Contains(Context.ConnectionId));
            if (!string.IsNullOrEmpty(room.Key))
            {
                lock (rooms[room.Key])
                {
                    rooms[room.Key].Remove(Context.ConnectionId);
                }
                // 嘗試取得房間中剩下的另一個連線 ID（對手）
                var opponentId = rooms[room.Key].FirstOrDefault();
                // 若對手存在，通知對手「對手已離線，對戰結束」
                if (!string.IsNullOrEmpty(opponentId))
                {
                    Clients.Client(opponentId).notify("對手已離線，對戰結束");
                }
                // 從 rooms 字典移除該房間（表示對戰結束）
                rooms.TryRemove(room.Key, out _);
            }
            // 呼叫父類別斷線處理，完成斷線流程
            return base.OnDisconnected(stopCalled);
        }

        /// <summary>
        /// 玩家加入對戰群組（前端呼叫）
        /// </summary>
        /// // 玩家加入特定比賽的 SignalR 群組 (Group)，方便群組內廣播訊息
        public async Task JoinMatchGroup(string matchId)
        {
            // 將當前連線加入 SignalR 群組，群組名稱以 matchId 命名
            await Groups.Add(Context.ConnectionId, $"match_{matchId}");
            Console.WriteLine($"用戶 {Context.ConnectionId} 加入群組 match_{matchId}");
            // 更新 rooms 字典，紀錄群組裡的連線 ID 清單
            rooms.AddOrUpdate($"match_{matchId}",
                // 若沒有此群組，新增一個包含此連線 ID 的清單
                new List<string> { Context.ConnectionId },
                // 若已有此群組，則鎖定清單後新增連線 ID（避免重複）
                (key, list) =>
                {
                    lock (list)
                    {
                        if (!list.Contains(Context.ConnectionId))
                            list.Add(Context.ConnectionId);
                    }
                    return list;
                });
            // 取得目前群組內的連線數
            var clientsInGroup = rooms.TryGetValue($"match_{matchId}", out var connections) ? connections.Count : 0;

            Console.WriteLine($"群組 match_{matchId} 連線數: {clientsInGroup}");
            // 如果有兩位玩家都加入群組，表示雙方都已就緒，開始倒數計時
            if (clientsInGroup == 2)
            {
                Clients.Group($"match_{matchId}").startCountdown();
            }
        }

       // 玩家請求加入等待配對隊列
        public void JoinQueue(string userName)
        {
            string userId = Context.User?.Identity?.Name;
            if (string.IsNullOrEmpty(userId))
            {
                Clients.Caller.notify("請先登入才能加入配對");
                return;
            }

            lock (waitingList)
            {
                // 如果該使用者不在等待清單中，加入並通知
                if (!waitingList.Any(p => p.UserId == userId))
                {
                    waitingList.Add((userId, userName, DateTime.Now));
                    Clients.Caller.notify("已加入配對隊列，請稍候...");
                    TryMatchPlayers();
                }
                else
                {
                    // 已在等待清單，告知使用者
                    Clients.Caller.notify("您已在配對隊列中，請稍候...");
                }
            }
        }

        private void TryMatchPlayers()
        {
            // 鎖定等待隊列，避免多執行緒同時操作造成資料不一致
            lock (waitingList)
            {
                // 清理資料庫中已過期(等待超過1分鐘)的比賽紀錄
                using (var db = new DatabaseEntities())
                {
                    var expire = DateTime.Now.AddMinutes(-1);
                    var expired = db.Matches.Where(m => m.MatchStatus == "Waiting" && m.StartedAt < expire).ToList();
                    if (expired.Count > 0)
                    {
                        db.Matches.RemoveRange(expired);
                        db.SaveChanges();
                    }
                }

                Console.WriteLine($"目前配對隊列人數：{waitingList.Count}");
                // 若等待隊列人數 >= 2，開始配對
                if (waitingList.Count >= 2)
                {
                    // 依加入時間排序，優先配對等待最久的兩位玩家
                    waitingList = waitingList.OrderBy(p => p.JoinTime).ToList();
                    // 從等待隊列移除這兩位玩家
                    var player1 = waitingList[0];
                    var player2 = waitingList[1];

                    waitingList.RemoveAt(0);
                    waitingList.RemoveAt(0);

                    int matchId;
                    string roomId;
                    // 在資料庫建立新的比賽紀錄，狀態設為 "Ongoing" 表示比賽開始
                    using (var db = new DatabaseEntities())
                    {
                        var match = new Matches
                        {
                            Player1Id = int.Parse(player1.UserId),
                            Player2Id = int.Parse(player2.UserId),
                            MatchStatus = "Ongoing",
                            StartedAt = DateTime.Now
                        };

                        db.Matches.Add(match);
                        db.SaveChanges();

                        matchId = match.MatchId;
                        roomId = $"match_{matchId}";

                        Console.WriteLine($"建立 Match {matchId}，房間ID：{roomId}");

                        // 通知玩家導向對戰頁面
                        Clients.User(player1.UserId).redirect($"/Match/Battle?matchId={matchId}");
                        Clients.User(player2.UserId).redirect($"/Match/Battle?matchId={matchId}");
                    }

                    // 建立房間連線清單
                    var player1Conns = userConnections.TryGetValue(player1.UserId, out var conns1) ? conns1.ToList() : new List<string>();
                    var player2Conns = userConnections.TryGetValue(player2.UserId, out var conns2) ? conns2.ToList() : new List<string>();
                    // 合併兩位玩家的所有連線到一個房間連線清單
                    var roomConnections = new List<string>();
                    roomConnections.AddRange(player1Conns);
                    roomConnections.AddRange(player2Conns);
                    // 把這個房間與連線清單存到 rooms 字典
                    rooms[roomId] = roomConnections;

                    // 加入 SignalR 群組
                    foreach (var cId in roomConnections)
                    {
                        Groups.Add(cId, roomId);
                    }

                    // 通知雙方開始對戰
                    foreach (var cId in player1Conns)
                        Clients.Client(cId).startBattle(roomId, player2.UserName);

                    foreach (var cId in player2Conns)
                        Clients.Client(cId).startBattle(roomId, player1.UserName);
                }
            }
        }

        /// <summary>
        /// 玩家準備就緒事件
        /// </summary>
        public void PlayerReady(string matchId)
        {
            string userId = Context.User?.Identity?.Name;
            if (string.IsNullOrEmpty(userId))
                return;

            var readySet = matchReadyUsers.GetOrAdd(matchId, _ => new HashSet<string>());
            lock (readySet)
            {
                readySet.Add(userId);
                // 若兩位玩家都準備好了
                if (readySet.Count == 2)
                {
                    // 透過群組廣播開始倒數
                    Clients.Group($"match_{matchId}").startCountdown();
                    // 清除該 match 的 ready 狀態，準備下一輪
                    matchReadyUsers.TryRemove(matchId, out _);
                }
            }
        }

        public void NextProblem(int matchId, int nextMatchProblemId)
        {
            string userId = Context.User?.Identity?.Name;
            if (string.IsNullOrEmpty(userId))
                return;

            using (var db = new DatabaseEntities())
            {
                var match = db.Matches.Find(matchId);
                if (match == null)
                    return;

                string player1Id = match.Player1Id.ToString();

                if (userId != player1Id)
                {
                    // 不是 player1 的話禁止觸發
                    Clients.Caller.notify("只有房主可以切換題目");
                    return;
                }

                // 廣播下一題給整個群組
                Clients.Group($"match_{matchId}").nextProblem(nextMatchProblemId);
                Console.WriteLine($"Match {matchId}：player1 廣播 nextProblem({nextMatchProblemId})");
            }
        }




        /// <summary>
        /// 用來檢查並通知雙方配對成功 (非立即使用，可做為未來擴充)
        /// </summary>
        public void CheckMatchStatus(string matchId)
        {
            using (var db = new DatabaseEntities())
            {
                var match = db.Matches.Find(int.Parse(matchId));
                if (match != null)
                {
                    Console.WriteLine($"CheckMatchStatus 被呼叫, matchId={matchId}, P1={match.Player1Id}, P2={match.Player2Id}");

                    if (match.Player2Id != null)
                    {
                        Console.WriteLine("Server: 發送 matchFound 給 " + match.Player1Id + " 和 " + match.Player2Id);

                        Clients.User(match.Player1Id.ToString()).matchFound(match.MatchId);
                        Clients.User(match.Player2Id.ToString()).matchFound(match.MatchId);
                    }
                }
                else
                {
                    Console.WriteLine($"找不到 matchId={matchId}");
                }
            }
        }
        // 取得指定題目在比賽中的順序 (從 1 開始計算)
        private int GetCurrentProblemIndex(int matchId, int problemId)
        {
            using (var db = new DatabaseEntities())
            {
                var problems = db.Problems
                    .Where(p => p.MatchId == matchId)
                    .OrderBy(p => p.ProblemOrder)
                    .ToList();

                for (int i = 0; i < problems.Count; i++)
                {
                    if (problems[i].ProblemId == problemId)
                        return i + 1; // 題號從 1 開始
                }

                return 1; // 預設回傳第 1 題（找不到時）
            }
        }


    }
}
