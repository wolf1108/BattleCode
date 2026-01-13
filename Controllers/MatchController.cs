using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using BattleCode.Models;
using BattleCode.Services;
using Microsoft.AspNet.SignalR;
using BattleCode.Hubs;
using System.Collections.Generic;
using System.Data.Entity;
using Newtonsoft.Json;
using System.IO;
using System.Web.Http;

namespace BattleCode.Controllers
{
    public class CustomUserIdProvider : IUserIdProvider
    {
        public string GetUserId(IRequest request)
        {
            return request.User?.Identity?.Name;
        }
    }

    public class MatchController : Controller
    {
        private readonly DatabaseEntities db = new DatabaseEntities();
        private readonly MatchService _matchService = new MatchService();
        private readonly AiJudgerService _aiJudgeService = new AiJudgerService();

        [System.Web.Mvc.HttpGet]
        public ActionResult Start()
        {
            return View();
        }

        [System.Web.Mvc.HttpPost]
        public async Task<ActionResult> Start(string language, string difficulty)
        {
            int userId = GetCurrentUserId();
            if (userId == -1)
                return RedirectToAction("Login", "Home");
            //嘗試尋找一場「等待中」的比賽（Match）：
            var existingMatch = db.Matches.FirstOrDefault(m =>
                m.MatchStatus == "Waiting" &&
                m.Mode == difficulty &&
                m.IsPractice == false &&
                m.Player1Id != userId);
            // 若有找到可配對的比賽
            if (existingMatch != null)
            {
                // 設定自己為 Player2，開始比賽
                existingMatch.Player2Id = userId;
                existingMatch.MatchStatus = "Running";
                existingMatch.StartedAt = DateTime.Now;
                db.SaveChanges();

                // （AI 出題）
                await _matchService.GenerateAIProblemsForMatchAsync(existingMatch.MatchId, difficulty, language);
                // 通知雙方比賽已成立，可進入對戰畫面（透過 SignalR）
                NotifyPlayers(existingMatch.MatchId);
                return RedirectToAction("Battle", new { matchId = existingMatch.MatchId });
            }
            else
            {
                // 若沒有找到比賽，表示自己是第一位進入此配對需求的玩家
                // 建立新的比賽並加入資料庫
                var match = new Matches
                {
                    Player1Id = userId,
                    MatchStatus = "Waiting",
                    Mode = difficulty,
                    IsPractice = false,
                    StartedAt = DateTime.Now,
                    Language = language
                };
                db.Matches.Add(match);
                db.SaveChanges();
                // 導向等待頁面，等待另一位玩家加入
                return RedirectToAction("WaitingRoom", new { id = match.MatchId });
            }
        }

        public ActionResult WaitingRoom(int id)
        {
            ViewBag.MatchId = id;
            return View();
        }
        //取消等待資料
        public ActionResult CancelWaiting(int matchId)
        {
            var match = db.Matches.Find(matchId);
            if (match != null && match.MatchStatus == "Waiting")
            {
                // 先刪除該比賽的 Submissions
                var submissions = db.Submissions.Where(s => s.MatchId == matchId).ToList();
                db.Submissions.RemoveRange(submissions);

                // 再刪除該比賽的 Problems
                var problems = db.Problems.Where(p => p.MatchId == matchId).ToList();
                db.Problems.RemoveRange(problems);

                // 最後刪除 Match
                db.Matches.Remove(match);

                db.SaveChanges();
            }
            return RedirectToAction("Start", "Match");
        }

       

        [System.Web.Mvc.HttpGet]
        public JsonResult GetScores(int matchId)
        {
            // 根據傳入的 matchId 從資料庫中尋找對應的比賽
            var match = db.Matches.Find(matchId);
            if (match == null)
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);
            // 若有找到比賽，回傳雙方目前的得分（正確答題數）
            return Json(new
            {
                success = true,
                player1Score = match.Player1CorrectCount ?? 0,
                player2Score = match.Player2CorrectCount ?? 0
            }, JsonRequestBehavior.AllowGet);
        }

        public ActionResult Battle(int matchId)
        {
            int userId = GetCurrentUserId();
            var match = db.Matches.Find(matchId);
            if (match == null)
                return HttpNotFound();
            // 如果玩家 1 或玩家 2 尚未就緒，表示尚未完成配對
            // 導向等待房畫面
            if (match.Player1Id == 0 || match.Player2Id == 0)
                return RedirectToAction("WaitingRoom", new { id = matchId });

            // 查詢這場比賽的所有題目，並依照出題順序排序
            var problems = db.Problems
                .Where(p => p.MatchId == matchId)
                .OrderBy(p => p.ProblemOrder)
                .ToList();
            // 若尚未生成題目，也導向等待房（可能是 AI 還沒出完題）
            if (!problems.Any())
                return RedirectToAction("WaitingRoom", new { id = matchId });

            // 取得兩位玩家的顯示名稱
            var player1 = db.Users.FirstOrDefault(u => u.UserId == match.Player1Id);
            var player2 = db.Users.FirstOrDefault(u => u.UserId == match.Player2Id);
            // 準備對戰畫面所需的 ViewModel
            var vm = new BattleViewModel
            {
                Match = match,
                PlayerId = userId,
                Problems = problems,
                Player1DisplayName = player1?.DisplayName ?? "玩家1",
                Player2DisplayName = player2?.DisplayName ?? "玩家2",
                Player1Score = match.Player1CorrectCount ?? 0,
                Player2Score = match.Player2CorrectCount ?? 0
            };
            // 將第一題的 ID 傳給前端（初始題目）
            ViewBag.CurrentProblemId = problems[0].ProblemId;
            // 設定前端使用的程式語言
            ViewBag.Language = problems[0].Language ?? "python";

            return View(vm);
        }
        // 取得UserId
        private int GetCurrentUserId()
        {
            if (Session["UserId"] == null)
                return -1;
            return (int)Session["UserId"];
        }

        // 通知指定比賽中的兩位玩家，已成功配對，可以進入對戰畫面
        private void NotifyPlayers(int matchId)
        {
            // 取得 SignalR 的 Hub 上下文，用來發送訊息給前端的 BattleHub
            var hubContext = GlobalHost.ConnectionManager.GetHubContext<BattleHub>();
            var match = db.Matches.Find(matchId);
            if (match == null)
                return;
            // 使用 SignalR 通知 Player1 進入對戰畫面（觸發前端的 matchFound 方法）
            hubContext.Clients.User(match.Player1Id.ToString()).matchFound(matchId);
            // 使用 SignalR 通知 Player2 進入對戰畫面
            hubContext.Clients.User(match.Player2Id.ToString()).matchFound(matchId);

            System.Diagnostics.Debug.WriteLine($"通知玩家 {match.Player1Id} 進入 match_{matchId}");
            System.Diagnostics.Debug.WriteLine($"通知玩家 {match.Player2Id} 進入 match_{matchId}");
        }

        [System.Web.Mvc.HttpPost]// 處理使用者提交程式碼的請求（由前端透過 POST 傳送 JSON 資料）
        public async Task<JsonResult> SubmitCode([FromBody] SubmitCodeRequest data)
        {
            
            if (data == null || data.MatchId <= 0 || data.ProblemId <= 0 || string.IsNullOrWhiteSpace(data.Language))
            {
                return Json(new { success = false, message = "提交資料不完整" });
            }

            int userId = GetCurrentUserId();
            // 使用 using 確保 db 資源正確釋放
            using (var db = new DatabaseEntities())
            {
                // 取得比賽與題目
                var match = db.Matches.FirstOrDefault(m => m.MatchId == data.MatchId);
                var problem = db.Problems.FirstOrDefault(p => p.ProblemId == data.ProblemId && p.MatchId == data.MatchId);
                if (match == null || problem == null)
                    return Json(new { success = false, message = "找不到比賽或題目" });

                // 檢查該使用者是否已經正確回答過此題，若有就直接回傳
                bool alreadyCorrect = db.Submissions
                    .Any(s => s.MatchId == data.MatchId && s.ProblemId == data.ProblemId && s.UserId == userId && s.Result == "Correct");

                if (alreadyCorrect)
                {
                    return Json(new
                    {
                        success = true,
                        message = "你已經答對此題，請等待對手完成或時間結束。",
                        isCorrect = true,
                        alreadyAnswered = true
                    });
                }

                // 如果程式碼為空（未作答），但時間到了或強制送出
                if (string.IsNullOrWhiteSpace(data.Code))
                {
                    // 檢查是否已經送出過空答案，避免重複記錄
                    bool hasSubmitted = db.Submissions
                        .Any(s => s.MatchId == data.MatchId && s.ProblemId == data.ProblemId && s.UserId == userId);

                    if (!hasSubmitted)
                    {
                        // 記錄一筆未作答的提交紀錄
                        db.Submissions.Add(new Submissions
                        {
                            MatchId = data.MatchId,
                            ProblemId = data.ProblemId,
                            UserId = userId,
                            Code = "",
                            Language = data.Language,
                            SubmittedAt = DateTime.Now,
                            Result = "NoAnswer",
                            ExecutionTimeMs = 0,
                            Output = "",
                            ErrorMessage = "",
                            HintText = data.AiHint,
                            AIAnalysis = "時間到未作答"
                        });

                        await db.SaveChangesAsync();
                    }
                    // 檢查是否該進入下一題（若雙方皆作答或時間到）
                    await CheckAndGotoNextProblem(db, data.MatchId, data.ProblemId, isTimeUp: true);

                    return Json(new
                    {
                        success = false,
                        message = "時間到未作答",
                        isCorrect = false
                    });
                }

                try
                {
                    // 呼叫 AI 判題服務進行評測與分析
                    var judgeResult = await _aiJudgeService.JudgeCodeAsync(data.Code, problem, data.Language);
                    // 若答案正確，更新對應玩家的分數
                    if (judgeResult.IsCorrect)
                    {
                        if (userId == match.Player1Id)
                        {
                            match.Player1CorrectCount = (match.Player1CorrectCount ?? 0) + 1;
                            match.Player1Score = (match.Player1Score ?? 0) + 1;
                        }
                        else if (userId == match.Player2Id)
                        {
                            match.Player2CorrectCount = (match.Player2CorrectCount ?? 0) + 1;
                            match.Player2Score = (match.Player2Score ?? 0) + 1;
                        }
                        db.SaveChanges();
                    }
                    // 儲存此次提交的詳細結果
                    db.Submissions.Add(new Submissions
                    {
                        MatchId = data.MatchId,
                        ProblemId = data.ProblemId,
                        UserId = userId,
                        Code = data.Code,
                        Language = data.Language,
                        SubmittedAt = DateTime.Now,
                        Result = judgeResult.IsCorrect ? "Correct" : "Wrong",
                        ExecutionTimeMs = judgeResult.ExecutionTimeMs,
                        Output = judgeResult.Output,
                        ErrorMessage = judgeResult.ErrorMessage,
                        AIAnalysis = judgeResult.Analysis,
                        HintText = data.AiHint,
                    });

                    await db.SaveChangesAsync();
                    // 檢查是否該進入下一題（若雙方皆作答或時間到）
                    await CheckAndGotoNextProblem(db, data.MatchId, data.ProblemId, isTimeUp: false);
                    // 回傳結果與 AI 分析
                    return Json(new
                    {
                        success = judgeResult.IsCorrect,
                        message = judgeResult.IsCorrect ? "答對了！請等待對手完成或時間結束。" : "答案錯誤，可以再次嘗試。",
                        result = judgeResult.Analysis,
                        isCorrect = judgeResult.IsCorrect
                    });
                }
                catch (Exception ex)
                {
                    // 若 AI 評測過程發生例外，回傳錯誤訊息
                    return Json(new { success = false, message = "AI 評測失敗：" + ex.Message });
                }
            }
        }
        // 檢查是否應該切換到下一題（由 SubmitCode 呼叫）
        private async Task CheckAndGotoNextProblem(DatabaseEntities db, int matchId, int problemId, bool isTimeUp)
        {
            // 從資料庫取得對應的比賽紀錄
            var match = db.Matches.Find(matchId);
            if (match == null) return;// 若找不到比賽，則不處理

            int p1 = match.Player1Id;
            int p2 = match.Player2Id ?? 0;
            // 取得此題目下所有提交，按照使用者分組，取出每位使用者「最新一次」提交的紀錄
            var submissions = db.Submissions
                .Where(s => s.MatchId == matchId && s.ProblemId == problemId)
                .GroupBy(s => s.UserId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.SubmittedAt).FirstOrDefault());
            // 判斷兩位玩家是否「都已經答對」
            bool p1Correct = submissions.ContainsKey(p1) && submissions[p1].Result == "Correct";
            bool p2Correct = submissions.ContainsKey(p2) && submissions[p2].Result == "Correct";

            // ✅ 雙方答對 → 立即切題
            if (p1Correct && p2Correct)
            {
                await GoToNextProblemOrFinish(matchId, problemId);
                return;
            }

            // ✅ 時間到時，不管有沒有提交都切題
            if (isTimeUp)
            {
                await GoToNextProblemOrFinish(matchId, problemId);
                return;
            }
        }

        // 在一題作答完成（雙方都答對或時間到）後，
        // 通知前端切換至下一題或結束比賽。
        public static async Task GoToNextProblemOrFinish(int matchId, int problemId)
        {
            var matchService = new MatchService();// 建立 MatchService 實例，用來查詢下一題 ID
            var nextProblemId = await matchService.GetNextMatchProblemId(problemId);// 取得此題之後的下一題 ProblemId（若無則回傳 null）
            var context = GlobalHost.ConnectionManager.GetHubContext<BattleHub>();// 取得 SignalR Hub 的上下文（用來推送通知到指定比賽群組）

            if (nextProblemId != null)
            {
                // 若還有下一題，則取得其在整體題目順序中的 index
                int currentIndex = GetCurrentProblemIndex(matchId, nextProblemId.Value);
                // 對 match_{matchId} 群組內所有用戶推送「切題事件」與下一題 ID
                context.Clients.Group($"match_{matchId}").nextProblem(nextProblemId, currentIndex);
                // 等待 1 秒後再發送「開始倒數事件」，讓畫面先切換再倒數
                await Task.Delay(1000);
                context.Clients.Group($"match_{matchId}").startCountdown();
            }
            else
            {
                // 若沒有下一題，通知前端比賽已結束
                context.Clients.Group($"match_{matchId}").matchFinished();
            }
        }


        [System.Web.Mvc.HttpPost]
        public async Task<ActionResult> ForceGotoNext(int matchId, int problemId)
        {
            try
            {
                await GoToNextProblemOrFinish(matchId, problemId);
                return Json(new { success = true, message = "已跳至下一題。" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "跳題失敗：" + ex.Message });
            }
        }
        // 取得指定題目在對戰中的出題順序
        public static int GetCurrentProblemIndex(int matchId, int problemId)
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
                        return i + 1;
                }

                return 1; // 預設回傳第一題
            }
        }

        // 取得指定題目的詳細資訊
        [System.Web.Mvc.HttpGet]
        public async Task<JsonResult> GetProblemDetail(int problemId)
        {
            var problem = await _matchService.GetProblemDetail(problemId);
            if (problem == null)
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);
            // 找到題目，回傳題目相關資訊供前端渲染
            return Json(new
            {
                success = true,
                title = problem.Title,
                description = problem.Description,
                inputFormat = problem.InputFormat,
                outputFormat = problem.OutputFormat,
                sampleInput = problem.SampleInputs,
                sampleOutput = problem.SampleOutputs
            }, JsonRequestBehavior.AllowGet);
        }
        //對戰結果頁面
        public ActionResult Result(int matchId)
        {
            var match = db.Matches.Find(matchId);
            if (match == null)
                return HttpNotFound("找不到此對戰紀錄");

            var player1 = db.Users.Find(match.Player1Id);
            var player2 = db.Users.Find(match.Player2Id);
            if (player1 == null || player2 == null)
                return HttpNotFound("找不到玩家資料");

            int userId = GetCurrentUserId();
            // 查出此比賽所有題目並依順序排序
            var problems = db.Problems
                .Where(p => p.MatchId == matchId)
                .OrderBy(p => p.ProblemOrder)
                .ToList();
            // 取得此比賽所有提交資料
            var submissions = db.Submissions
                .Where(s => s.MatchId == matchId)
                .ToList();

            var problemResults = new List<ProblemResultViewModel>();

            int player1CorrectCount = 0;
            int player2CorrectCount = 0;

            // [Rating 計算]
            int player1RatingGain = 0;
            int player2RatingGain = 0;

            // 逐題分析雙方答題狀況與使用者個人回饋
            foreach (var prob in problems)
            {
                // 找雙方對該題的最新提交
                var player1Submission = submissions
                    .Where(s => s.ProblemId == prob.ProblemId && s.UserId == player1.UserId)
                    .OrderByDescending(s => s.SubmittedAt)
                    .FirstOrDefault();

                var player2Submission = submissions
                    .Where(s => s.ProblemId == prob.ProblemId && s.UserId == player2.UserId)
                    .OrderByDescending(s => s.SubmittedAt)
                    .FirstOrDefault();

                // 找當前登入使用者對該題的最新提交（用來顯示個人反饋）
                var userSubmission = submissions
                    .Where(s => s.ProblemId == prob.ProblemId && s.UserId == userId)
                    .OrderByDescending(s => s.SubmittedAt)
                    .FirstOrDefault();

                bool isCorrect = userSubmission?.Result == "Correct";
                string feedback;
                // 組合個人題目反饋訊息（答對或錯誤說明）
                if (userSubmission != null)
                {
                    if (isCorrect)
                        feedback = "👍 做得好！這題你答對了，繼續保持！";
                    else
                        feedback = $"❌ 解釋：{userSubmission.AIAnalysis ?? userSubmission.ErrorMessage ?? "邏輯可能有誤，建議檢查輸入與邊界條件"}";
                }
                else
                {
                    feedback = "未作答";
                }
                // 將題目結果封裝成 ViewModel 供前端顯示
                problemResults.Add(new ProblemResultViewModel
                {
                    Title = prob.Title,
                    Description = prob.Description,
                    Difficulty = prob.Difficulty,
                    IsCorrect = isCorrect,
                    ExplanationOrFeedback = feedback,
                    AIAnalysis = userSubmission?.AIAnalysis,
                    ErrorMessage = userSubmission?.ErrorMessage,
                    Code = userSubmission?.Code
                });
                // 計算雙方正確題數
                if (player1Submission?.Result == "Correct")
                {
                    player1CorrectCount++;
                }

                if (player2Submission?.Result == "Correct")
                {
                    player2CorrectCount++;
                }
            }


            // [勝負與 Rating]
            // 假設每場比賽題目難度一致，取第一題的難度即可
            string difficulty = problems.FirstOrDefault()?.Difficulty.ToLower() ?? "easy";

            // 計算加分
            if (player1CorrectCount > player2CorrectCount)
            {
                match.WinnerId = player1.UserId;
                match.IsDraw = false;
                match.MatchStatus = "Finished";
                match.Player1Rank = 1;
                match.Player2Rank = 2;

                player1RatingGain = GetRatingByDifficulty(difficulty, true);
                player2RatingGain = 0;
            }
            else if (player2CorrectCount > player1CorrectCount)
            {
                match.WinnerId = player2.UserId;
                match.IsDraw = false;
                match.MatchStatus = "Finished";
                match.Player1Rank = 2;
                match.Player2Rank = 1;

                player1RatingGain = 0;
                player2RatingGain = GetRatingByDifficulty(difficulty, true);
            }
            else
            {
                match.WinnerId = null;
                match.IsDraw = true;
                match.MatchStatus = "Draw";
                match.Player1Rank = null;
                match.Player2Rank = null;

                int drawScore = GetRatingByDifficulty(difficulty, null);
                player1RatingGain = drawScore;
                player2RatingGain = drawScore;
            }

            // 更新比賽統計資料
            match.Player1CorrectCount = player1CorrectCount;
            match.Player2CorrectCount = player2CorrectCount;
            match.Player1Score = player1RatingGain;
            match.Player2Score = player2RatingGain;
            match.EndedAt = DateTime.Now;

            // 更新玩家積分
            player1.Rating += player1RatingGain;
            player2.Rating += player2RatingGain;

            // 設定資料庫狀態為已修改，並存檔
            db.Entry(player1).State = EntityState.Modified;
            db.Entry(player2).State = EntityState.Modified;
            db.SaveChanges();

            // 決定勝利者名稱顯示文字
            var winnerName = match.IsDraw == true ? "平手"
                            : match.WinnerId == player1.UserId ? player1.DisplayName
                            : player2.DisplayName;
            // 組成 ViewModel 傳給 View 用來渲染結果頁
            var viewModel = new ResultViewModel
            {
                MatchId = matchId,
                Player1Name = player1.DisplayName,
                Player2Name = player2.DisplayName,
                Player1Score = match.Player1Score ?? 0,
                Player2Score = match.Player2Score ?? 0,
                WinnerName = winnerName,
                ProblemResults = problemResults
            };

            return View(viewModel);
        }

        // 幫助方法：根據難度與是否獲勝，計算加分
        private int GetRatingByDifficulty(string difficulty, bool? isWin)
        {
            // 平手加分
            if (isWin == null)
            {
                switch (difficulty.ToLower())
                {
                    case "easy":
                        return 25;
                    case "medium":
                        return 35;
                    case "hard":
                        return 50;
                    default:
                        return 0;
                }
            }
            // 勝利才加分，失敗不加
            if (isWin == true)
            {
                switch (difficulty.ToLower())
                {
                    case "easy":
                        return 50;
                    case "medium":
                        return 70;
                    case "hard":
                        return 100;
                    default:
                        return 0;
                }
            }
            // isWin == false → 輸家，不加分
            return 0;
        }


        // 透過 AI 助理取得題目的提示
        [System.Web.Mvc.HttpGet]
        public async Task<JsonResult> GetAiHint(int problemId, int? matchId = null, int? userId = null)
        {
            //先查找指定的題目，找不到就回傳失敗
            var prob = db.Problems.Find(problemId);
            if (prob == null)
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);

            string hint;
            string code = null;
            // 若有提供 matchId 和 userId，嘗試取得該用戶最近一次提交的程式碼
            if (matchId.HasValue && userId.HasValue)
            {
                var last = db.Submissions
                    .Where(s => s.MatchId == matchId && s.ProblemId == problemId && s.UserId == userId && !string.IsNullOrEmpty(s.Code))
                    .OrderByDescending(s => s.SubmittedAt)
                    .FirstOrDefault();

                if (last != null)
                    code = last.Code;// 取最新程式碼
            }
            // 建立 AI 助理服務實例
            var aiService = new OpenAIAssistantService();
            // 若有程式碼則用程式碼產生提示，否則用題目描述產生提示
            if (!string.IsNullOrWhiteSpace(code))
                hint = await aiService.GenerateHintFromCodeAsync(prob, code);
            else
                hint = await aiService.GenerateHintFromProblemAsync(prob);
            // 回傳成功與提示文字
            return Json(new { success = true, hint = hint }, JsonRequestBehavior.AllowGet);
        }


        [System.Web.Mvc.HttpPost]//沒作用的吧
        public async Task<ActionResult> StartMatch(string difficulty, string language)
        {
            var match = new Matches
            {
                StartedAt = DateTime.Now,
                MatchStatus = "Waiting",
                IsPractice = false,
                Mode = difficulty,
                Language = language
            };

            db.Matches.Add(match);
            await db.SaveChangesAsync();

            await _matchService.GenerateAIProblemsForMatchAsync(match.MatchId, difficulty, language);
            return RedirectToAction("Battle", new { matchId = match.MatchId });
        }

        public ActionResult RequestMatch(string difficulty)
        {
            int userId = GetCurrentUserId();
            var match = new Matches
            {
                Player1Id = userId,
                MatchStatus = "Waiting",
                Mode = difficulty,
                IsPractice = false,
                StartedAt = DateTime.Now
            };

            db.Matches.Add(match);
            db.SaveChanges();

            var context = GlobalHost.ConnectionManager.GetHubContext<BattleHub>();
            context.Clients.User(userId.ToString()).matchFound(match.MatchId);

            return RedirectToAction("WaitingRoom", new { id = match.MatchId });
        }

    }

    public class JudgeRequest
    {
        public string Code { get; set; }
        public string Language { get; set; }
        public string Input { get; set; }
        public string ExpectedOutput { get; set; }
    }
}