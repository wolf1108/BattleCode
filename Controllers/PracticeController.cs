using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using BattleCode.Models;
using BattleCode.Services;
using System.Collections.Generic;

namespace BattleCode.Controllers
{
    public class PracticeController : Controller
    {
        private readonly DatabaseEntities db = new DatabaseEntities();
        private readonly MatchService _matchService = new MatchService();
        private readonly AiJudgerService _aiJudgeService = new AiJudgerService();

        [HttpGet]
        public ActionResult Start()
        {
            return View();
        }

        [HttpPost]
        public async Task<ActionResult> Start(string language, string difficulty)
        {
            int userId = GetCurrentUserId();
            if (userId == -1)
                return RedirectToAction("Login", "Home");

            // 建立一個練習Match
            var match = new Matches
            {
                Player1Id = userId,
                MatchStatus = "Practice",
                Mode = difficulty,
                IsPractice = true,
                StartedAt = DateTime.Now,
                Language = language
            };
            db.Matches.Add(match);
            db.SaveChanges();

            // 產生三題 AI 題目
            await _matchService.GenerateAIProblemsForMatchAsync(match.MatchId, difficulty, language);

            return RedirectToAction("Practice", new { matchId = match.MatchId });
        }

        public ActionResult Practice(int matchId)
        {
            int userId = GetCurrentUserId();
            var match = db.Matches.Find(matchId);
            if (match == null)
                return HttpNotFound();

            var problems = db.Problems
                .Where(p => p.MatchId == matchId)
                .OrderBy(p => p.ProblemOrder)
                .ToList();

            if (!problems.Any())
                return RedirectToAction("Start");

            ViewBag.CurrentProblemId = problems[0].ProblemId;
            ViewBag.Language = problems[0].Language ?? "python";

            var vm = new PracticeBattleViewModel
            {
                Match = match,
                PlayerId = userId,
                Problems = problems,
                Score = 0
            };

            return View(vm);
        }

        [HttpPost]
        public async Task<JsonResult> SubmitCode(int matchId, int problemId, string code, string language, string aiHint)
        {
            int userId = GetCurrentUserId();
            var match = db.Matches.Find(matchId);
            var problem = db.Problems.FirstOrDefault(p => p.ProblemId == problemId && p.MatchId == matchId);
            if (match == null || problem == null)
                return Json(new { success = false, message = "找不到比賽或題目" });

            bool alreadyCorrect = db.Submissions
                .Any(s => s.MatchId == matchId && s.ProblemId == problemId && s.UserId == userId && s.Result == "Correct");
            if (alreadyCorrect)
            {
                return Json(new
                {
                    success = true,
                    message = "你已經答對此題",
                    isCorrect = true,
                    alreadyAnswered = true
                });
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return Json(new
                {
                    success = false,
                    message = "請輸入程式碼！",
                    isCorrect = false
                });
            }

            try
            {
                var judgeResult = await _aiJudgeService.JudgeCodeAsync(code, problem, language);

                db.Submissions.Add(new Submissions
                {
                    MatchId = matchId,
                    ProblemId = problemId,
                    UserId = userId,
                    Code = code,
                    Language = language,
                    SubmittedAt = DateTime.Now,
                    Result = judgeResult.IsCorrect ? "Correct" : "Wrong",
                    ExecutionTimeMs = judgeResult.ExecutionTimeMs,
                    Output = judgeResult.Output,
                    ErrorMessage = judgeResult.ErrorMessage,
                    AIAnalysis = judgeResult.Analysis,
                    HintText = aiHint
                });

                await db.SaveChangesAsync();

                // ====== 這裡改分數規則 ======
                int addScore = 0;
                switch ((problem.Difficulty ?? "").ToLower())
                {
                    case "easy":
                    case "簡單":
                        addScore = 10;
                        break;
                    case "medium":
                    case "中等":
                        addScore = 20;
                        break;
                    case "hard":
                    case "困難":
                        addScore = 30;
                        break;
                    default:
                        addScore = 10;
                        break;
                }

                if (judgeResult.IsCorrect)
                {
                    match.Player1Score = (match.Player1Score ?? 0) + addScore;
                    db.SaveChanges();
                }

                // ====== 分數規則結束 ======

                return Json(new
                {
                    success = true,
                    message = judgeResult.IsCorrect ? "答對了！請點擊下一題" : "答案錯誤，請再試一次",
                    result = judgeResult.Analysis,
                    isCorrect = judgeResult.IsCorrect,
                    addScore = judgeResult.IsCorrect ? addScore : 0
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "AI 評測失敗：" + ex.Message });
            }
        }

        private async Task CheckAndGotoNextProblem(DatabaseEntities db, int matchId, int problemId)
        {
            var match = db.Matches.Find(matchId);
            if (match == null) return;

            int userId = match.Player1Id;

            // 只檢查自己是否有交卷
            bool userSubmitted = db.Submissions
                .Any(s => s.MatchId == matchId && s.ProblemId == problemId && s.UserId == userId);

            if (userSubmitted)
            {
                // 找下一題
                var problems = db.Problems
                    .Where(p => p.MatchId == matchId)
                    .OrderBy(p => p.ProblemOrder)
                    .ToList();
                int currIndex = problems.FindIndex(p => p.ProblemId == problemId);
                var next = currIndex >= 0 && currIndex + 1 < problems.Count ? problems[currIndex + 1] : null;
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetProblemDetail(int problemId)
        {
            var problem = await _matchService.GetProblemDetail(problemId);
            if (problem == null)
                return Json(new { success = false }, JsonRequestBehavior.AllowGet);

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

        public ActionResult Result(int matchId)
        {
            var match = db.Matches.Find(matchId);
            int userId = GetCurrentUserId();

            var user = db.Users.FirstOrDefault(u => u.UserId == match.Player1Id);

            var problems = db.Problems
                .Where(p => p.MatchId == matchId)
                .OrderBy(p => p.ProblemOrder)
                .ToList();

            var submissions = db.Submissions
                .Where(s => s.MatchId == matchId && s.UserId == userId)
                .ToList();

            var problemResults = new List<ProblemResultViewModel>();

            // 2. 若尚未加過分，則加分
            if (match != null && user != null)
            {
                // 所有練習累計分數同步到 Rating
                var totalPracticeScore = db.Matches
                    .Where(m => m.Player1Id == userId && m.MatchStatus == "Practice")
                    .Sum(m => m.Player1Score ?? 0);

                user.Rating = totalPracticeScore;
                db.SaveChanges();
            }

            foreach (var prob in problems)
            {
                var userSubmission = submissions
                    .Where(s => s.ProblemId == prob.ProblemId)
                    .OrderByDescending(s => s.SubmittedAt)
                    .FirstOrDefault();

                bool isCorrect = userSubmission?.Result == "Correct";
                string feedback;

                if (userSubmission != null)
                {
                    if (isCorrect)
                    {
                        feedback = "👍 做得好！這題你答對了，繼續保持！";
                    }
                    else
                    {
                        feedback = $"❌ 解釋：{userSubmission.AIAnalysis ?? userSubmission.ErrorMessage ?? "邏輯可能有誤，建議檢查輸入與邊界條件"}";
                    }
                }
                else
                {
                    feedback = "未作答";
                }

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
            }

            var viewModel = new ResultViewModel
            {
                MatchId = matchId,
                Player1Name = "你",
                Player2Name = "",
                Player1Score = match.Player1Score ?? 0,
                Player2Score = 0,
                WinnerName = "練習",
                ProblemResults = problemResults
            };

            return View(viewModel);
        }

        private int GetCurrentUserId()
        {
            if (Session["UserId"] == null)
                return -1;
            return (int)Session["UserId"];
        }
    }
}