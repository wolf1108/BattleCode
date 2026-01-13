using BattleCode.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using Microsoft.Owin.Security;
using System.IO;
using System.Threading.Tasks;
using Google.Apis.Auth;
using Newtonsoft.Json;
using BattleCode.Services;
using BattleCode.Models.ViewModels;
using System.Data.Entity.Validation;



namespace BattleCode.Controllers
{
    public class HomeController : Controller
    {
        private readonly DatabaseEntities db = new DatabaseEntities();
        //private readonly DatabaseEntities db;
        //private readonly AIAnalysisService _aiAnalysisService = new AIAnalysisService();
        //private readonly AIAnalysisService _aiAnalysisService;
        private readonly AIAnalysisService _aiAnalysisService;

        public HomeController()
        {
            var db = new DatabaseEntities();
            _aiAnalysisService = new AIAnalysisService(db);
        }

        public ActionResult AIAnalysis()
        {
            //我改這
            int userId = GetCurrentUserId();
            if (userId == -1)
                return RedirectToAction("Login", "Home");


            // 錯題統計資料給前端圖表用
            // 先把所有錯題的 TagName 查出來（一次到記憶體）
            var tagNamesList = db.Submissions
                .Where(s => s.UserId == userId && s.Result != "Correct")
                .Join(db.Problems, s => s.ProblemId, p => p.ProblemId, (s, p) => p.TagName)
                .ToList();  // 先把資料拉出來

            // 再在記憶體中拆分、分組計數
            var wrongStats = tagNamesList
                .SelectMany(tagNames => tagNames.Split(',')) // 拆成多個標籤
                .Select(tag => tag.Trim())
                .GroupBy(tag => tag)
                .Select(g => new { Tag = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();


            var user = db.Users.Find(userId);

            ViewBag.AIResult = user?.AI ?? "尚未執行分析";
            ViewBag.StatsJson = JsonConvert.SerializeObject(wrongStats);
            return View();
        }

        [HttpPost]
        public async Task<JsonResult> RunAnalysis()
        {
            int userId = GetCurrentUserId();

            var analysisText = await _aiAnalysisService.AnalyzeUserMistakesAsync(userId);

            var user = db.Users.Find(userId);
            if (user != null)
            {
                user.AI = analysisText;
                await db.SaveChangesAsync();
            }

            return Json(new { success = true, analysis = analysisText });
        }

        public ActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Register(string Username, string Email, string Password, string ConfirmPassword, string DisplayName)
        {
            if (ModelState.IsValid)
            {
                if (Password != ConfirmPassword)
                {
                    ModelState.AddModelError("ConfirmPassword", "兩次密碼輸入不一致");
                    return View();
                }

                if (db.Users.Any(u => u.Username == Username))
                {
                    ModelState.AddModelError("Username", "此帳號已被使用");
                    return View();
                }

                if (db.Users.Any(u => u.Email == Email))
                {
                    ModelState.AddModelError("Email", "此 Email 已被註冊");
                    return View();
                }

                var newUser = new Users
                {
                    Username = Username,
                    Email = Email,
                    PasswordHash = Password,
                    DisplayName = DisplayName,
                    UserRole = "User",
                    PreferredLanguage = null,
                    Rating = 0,
                    CreatedAt = DateTime.Now,
                    LoginProvider = "註冊"
                };

                db.Users.Add(newUser);
                db.SaveChanges();

                TempData["RegisterSuccess"] = "註冊成功，請登入帳號";
                return RedirectToAction("Login");
            }

            return View();
        }


        public ActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Login(string UsernameOrEmail, string Password)
        {
            if (ModelState.IsValid)
            {
                var user = db.Users.FirstOrDefault(u =>
                    (u.Username == UsernameOrEmail || u.Email == UsernameOrEmail)
                    && u.PasswordHash == Password);

                if (user != null)
                {
                    Session["UserId"] = user.UserId;
                    Session["UserName"] = user.Username;
                    Session["UserRole"] = user.UserRole;
                    Session["DisplayName"] = user.DisplayName;

                    FormsAuthentication.SetAuthCookie(user.UserId.ToString(), false);
                    TempData["LoginSuccess"] = $"歡迎 {user.DisplayName} 回來！";

                    if (user.UserRole == "Admin")
                    {
                        return RedirectToAction("Dashboard", "Home");
                    }
                    else if (user.UserRole == "User")
                    {
                        return RedirectToAction("Index", "Home");
                    }
                    else
                    {
                        // 預設導向（如果角色不明）
                        return RedirectToAction("Index", "Home");
                    }
                }

                TempData["LoginError"] = "帳號或密碼錯誤，請再試一次。";
            }

            return View();
        }

        [HttpPost]
        //[ValidateAntiForgeryToken]
        public async Task<ActionResult> GoogleLogin()
        {
            System.Diagnostics.Debug.WriteLine("收到 GoogleLogin 請求"); // Debug

            try
            {
                // 讀取 raw body
                string requestBody;
                using (var reader = new StreamReader(Request.InputStream))
                {
                    requestBody = reader.ReadToEnd();
                }
                System.Diagnostics.Debug.WriteLine("GoogleLogin requestBody: " + requestBody); // Debug

                dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(requestBody);
                string id_token = data.id_token;
                System.Diagnostics.Debug.WriteLine("id_token: " + id_token); // Debug

                var payload = await Google.Apis.Auth.GoogleJsonWebSignature.ValidateAsync(id_token, new Google.Apis.Auth.GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { "433291994708-4bkvmuv7fs3c9p5rkctlje3lmbtb6j1t.apps.googleusercontent.com" }
                });

                System.Diagnostics.Debug.WriteLine($"Google payload: email={payload.Email}, name={payload.Name}, picture={payload.Picture}"); // Debug

                string email = payload.Email;
                string displayName = payload.Name;
                string avatarUrl = payload.Picture;
                string providerKey = payload.Subject;

                var user = db.Users.FirstOrDefault(u => u.Email == email);

                if (user == null)
                {
                    System.Diagnostics.Debug.WriteLine("建立新用戶：" + email); // Debug
                                                                          // 新增帳號
                    user = new Users
                    {
                        Username = email,
                        Email = email,
                        PasswordHash = Guid.NewGuid().ToString(),
                        DisplayName = displayName ?? email,
                        UserRole = "User",
                        PreferredLanguage = null,
                        Rating = 0,
                        CreatedAt = DateTime.Now,
                        LoginProvider = "Google"

                    };
                    db.Users.Add(user);
                    db.SaveChanges();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("已存在用戶：" + email); // Debug
                }

                // 登入
                Session["UserId"] = user.UserId;
                Session["UserName"] = user.Username;
                Session["UserRole"] = user.UserRole;
                Session["DisplayName"] = user.DisplayName;

                var identity = new System.Security.Claims.ClaimsIdentity(
                    new[] {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, user.DisplayName ?? user.Username),
                new System.Security.Claims.Claim("http://schemas.microsoft.com/accesscontrolservice/2010/07/claims/identityprovider", "Google")
                    },
                    "ApplicationCookie"
                );
                HttpContext.GetOwinContext().Authentication.SignIn(new AuthenticationProperties { IsPersistent = false }, identity);

                return Json(new { success = true, message = $"歡迎 {user.DisplayName} 回來！" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GoogleLogin 發生錯誤: " + ex); // Debug
                return Json(new { success = false, message = "Google 登入失敗：" + ex.Message });
            }
        }

        public ActionResult Logout()
        {
            HttpContext.GetOwinContext().Authentication.SignOut("ApplicationCookie");
            Session.Clear();
            return RedirectToAction("Login");
        }

        public ActionResult Index()
        {
            return View();
        }
        public ActionResult Chat()
        {
            return View();
        }

        private int GetCurrentUserId()
        {
            if (Session["UserId"] == null)
                return -1;
            return (int)Session["UserId"];
        }
        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }

        public ActionResult all_His()
        {
            using (var db = new DatabaseEntities())
            {
                var matches = db.Matches
                    .Select(m => new
                    {
                        Match = m,
                        Player1Name = db.Users.FirstOrDefault(u => u.UserId == m.Player1Id).DisplayName,
                        Player2Name = db.Users.FirstOrDefault(u => u.UserId == m.Player2Id).DisplayName
                    })
                    .ToList()
                    .Select(m => new MatchViewModel
                    {
                        MatchId = m.Match.MatchId,
                        StartedAt = m.Match.StartedAt,
                        IsPractice = (bool)m.Match.IsPractice,
                        Mode = m.Match.Mode,
                        Language = m.Match.Language,
                        Player1Name = m.Player1Name,
                        Player2Name = m.Player2Name,
                        Player1Score = m.Match.Player1Score,
                        Player2Score = m.Match.Player2Score,
                        WinnerName = m.Match.WinnerId == m.Match.Player1Id ? m.Player1Name :
                                     m.Match.WinnerId == m.Match.Player2Id ? m.Player2Name : "平手"
                    }).ToList();

                return View(matches);
            }
        }

        public ActionResult A_details(int matchId)
        {
            using (var db = new DatabaseEntities())
            {
                var match = db.Matches.Find(matchId);
                if (match == null) return HttpNotFound();

                var problemIds = db.Submissions
                                   .Where(s => s.MatchId == matchId)
                                   .Select(s => s.ProblemId)
                                   .Distinct()
                                   .ToList();

                var problems = db.Problems
                                 .Where(p => problemIds.Contains(p.ProblemId))
                                 .ToList();

                var submissions = db.Submissions
                                    .Where(s => s.MatchId == matchId)
                                    .ToList();

                var users = db.Users.ToDictionary(u => u.UserId, u => u.DisplayName);
              

                var problemVMs = problems.Select(p => new ProblemDetailViewModel
                {
                    Problem = p,
                    Submissions = submissions
                        .Where(s => s.ProblemId == p.ProblemId)
                        .Select(s => new SubmissionDetailViewModel
                        {
                            UserId = s.UserId,
                            PlayerName = users.ContainsKey(s.UserId) ? users[s.UserId] : $"User {s.UserId}",
                            Code = s.Code,
                            Result = s.Result,
                            ExecutionTimeMs = s.ExecutionTimeMs,
                            Output = s.Output,
                            ErrorMessage = s.ErrorMessage,
                            AIAnalysis = s.AIAnalysis,
                            HintText = s.HintText,
                        }).ToList()
                }).ToList();

                var vm = new MatchDetailViewModel
                {
                    Match = match,
                    Problems = problemVMs
                };

                return View(vm);
            }
        }

        public ActionResult History()
        {
            int currentUserId = Convert.ToInt32(Session["UserId"]);
            using (var db = new DatabaseEntities())
            {
                var matches = db.Matches
                    .Where(m => m.Player1Id == currentUserId || m.Player2Id == currentUserId)
                    .Select(m => new
                    {
                        Match = m,
                        Player1 = db.Users.FirstOrDefault(u => u.UserId == m.Player1Id),
                        Player2 = db.Users.FirstOrDefault(u => u.UserId == m.Player2Id)
                    })
                    .ToList()
                    .Select(m => new MatchViewModel
                    {
                        MatchId = m.Match.MatchId,
                        StartedAt = m.Match.StartedAt,
                        IsPractice = (bool)m.Match.IsPractice,
                        Mode = m.Match.Mode,
                        Language = m.Match.Language,
                        Player1Name = m.Player1?.DisplayName ?? "未知玩家",
                        Player2Name = m.Player2?.DisplayName ?? "未知玩家",
                        Player1Score = m.Match.Player1Score,
                        Player2Score = m.Match.Player2Score,
                        Player1Pic = m.Player1?.pic ?? "root.png",  // ✅ 加這行
                        Player2Pic = m.Player2?.pic ?? "root.png",  // ✅ 加這行
                        WinnerName = m.Match.WinnerId == m.Match.Player1Id ? m.Player1?.DisplayName :
                                     m.Match.WinnerId == m.Match.Player2Id ? m.Player2?.DisplayName : "平手"
                    }).ToList();

                return View(matches);
            }
        }


        public ActionResult details(int matchId)
        {
            using (var db = new DatabaseEntities())
            {
                var match = db.Matches.Find(matchId);
                if (match == null) return HttpNotFound();

                var problemIds = db.Submissions
                                   .Where(s => s.MatchId == matchId)
                                   .Select(s => s.ProblemId)
                                   .Distinct()
                                   .ToList();

                var problems = db.Problems
                                 .Where(p => problemIds.Contains(p.ProblemId))
                                 .ToList();

                var submissions = db.Submissions
                                    .Where(s => s.MatchId == matchId)
                                    .ToList();

                var users = db.Users.ToDictionary(u => u.UserId, u => u.DisplayName);
                int currentUserId = (int)(Session["UserId"] ?? 0);

                var problemVMs = problems.Select(p => new ProblemDetailViewModel
                {
                    Problem = p,
                    Submissions = submissions
                        .Where(s => s.ProblemId == p.ProblemId && s.UserId == currentUserId)
                        .Select(s => new SubmissionDetailViewModel
                        {
                            UserId = s.UserId,
                            PlayerName = users.ContainsKey(s.UserId) ? users[s.UserId] : $"User {s.UserId}",
                            Code = s.Code,
                            Result = s.Result,
                            ExecutionTimeMs = s.ExecutionTimeMs,
                            Output = s.Output,
                            ErrorMessage = s.ErrorMessage,
                            AIAnalysis = s.AIAnalysis,
                            HintText= s.HintText,
                        }).ToList()
                }).ToList();

                var vm = new MatchDetailViewModel
                {
                    Match = match,
                    Problems = problemVMs
                };

                return View(vm);
            }
        }


        public ActionResult Rank()
        {
            using (var db = new DatabaseEntities())
            {
                var rankings = db.Users
                                 .Where(u => u.Rating != null)
                                 .OrderByDescending(u => u.Rating)
                                 .ToList(); // 👈 保留整個 Users 型別

                return View(rankings);
            }
        }


        [HttpGet]
        public ActionResult Person()
        {
        
            if (Session["UserId"] == null)
                return RedirectToAction("Login", "Home");

            int userId = Convert.ToInt32(Session["UserId"]);
            var user = db.Users
                         .AsNoTracking()
                         .FirstOrDefault(u => u.UserId == userId);

            if (user == null)
                return RedirectToAction("Login", "Home");

            string picFileName = string.IsNullOrEmpty(user.pic) ? "root.png" : user.pic;
            string imagePath = Server.MapPath("~/Content/img/head_up/" + picFileName);
            if (!System.IO.File.Exists(imagePath))
            {
                picFileName = "root.png";
            }

            // ✅ 把處理過的圖片檔名放進 ViewModel.pic
            var viewModel = new User
            {
                UserId = user.UserId,
                Username = user.Username,
                Email = user.Email,
                PasswordHash = user.PasswordHash,
                DisplayName = user.DisplayName,
                PreferredLanguage = user.PreferredLanguage,
                Rating = (int)user.Rating,
                AI = user.AI,
                UserRole = user.UserRole,
                pic = picFileName // ✅ 這邊塞處理過後的檔名
            };

            if (TempData["SuccessMessage"] != null)
                ViewBag.Message = TempData["SuccessMessage"].ToString();

            return View(viewModel);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Person(Users updatedUser, HttpPostedFileBase picFile)
        {
            if (Session["UserId"] == null)
                return RedirectToAction("Login", "Home");

            int userId = Convert.ToInt32(Session["UserId"]);
            var user = db.Users.FirstOrDefault(u => u.UserId == userId);
            if (user == null)
                return RedirectToAction("Login", "Home");

            // ✅ 更新欄位（限本人）
            user.DisplayName = updatedUser.DisplayName ?? user.DisplayName;
            user.PasswordHash = updatedUser.PasswordHash ?? user.PasswordHash;
            user.PreferredLanguage = updatedUser.PreferredLanguage ?? user.PreferredLanguage;


            // ✅ 頭像上傳處理
            if (picFile != null && picFile.ContentLength > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
                var ext = Path.GetExtension(picFile.FileName).ToLower();

                if (string.IsNullOrEmpty(ext) || !allowedExtensions.Contains(ext))
                {
                    ModelState.AddModelError("", "僅支援 jpg、jpeg、png 格式");
                    return View(updatedUser);
                }

                string folder = Server.MapPath("~/Content/img/head_up");
                string fileName = $"{userId}{ext}";
                string fullPath = Path.Combine(folder, fileName);

                // 刪除其他副檔名舊圖
                foreach (var oldExt in allowedExtensions)
                {
                    string oldPath = Path.Combine(folder, $"{userId}{oldExt}");
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                // 儲存新圖片
                picFile.SaveAs(fullPath);
                user.pic = fileName;
            }
            else
            {
                // ✅ 如果沒上傳圖且資料庫也沒 pic，就給預設圖
                if (string.IsNullOrEmpty(user.pic))
                {
                    user.pic = "root.png";
                }
            }

                try
            {
                db.SaveChanges();
                TempData["SuccessMessage"] = "個人資料已成功更新！";
                return RedirectToAction("Person");
            }
            catch (DbEntityValidationException ex)
            {
                foreach (var e in ex.EntityValidationErrors.SelectMany(ev => ev.ValidationErrors))
                {
                    System.Diagnostics.Debug.WriteLine($"欄位: {e.PropertyName}, 錯誤: {e.ErrorMessage}");
                }
                ModelState.AddModelError("", "資料儲存失敗，請檢查欄位內容。");
                return View(updatedUser);
            }
        }



        public ActionResult Dashboard()
        {
            return View();
        }
        public JsonResult GetOverallStats()
        {
            using (var db = new DatabaseEntities())
            {
                var matchCount = db.Matches.Count();
                var problemCount = db.Problems.Count();
                var submissionCount = db.Submissions.Count();
                var userCount = db.Users.Count();

                return Json(new
                {
                    matchCount,
                    problemCount,
                    submissionCount,
                    userCount
                }, JsonRequestBehavior.AllowGet);
            }
        }

        public JsonResult GetMatchUserStats()
        {
            var startDate = DateTime.Today.AddDays(-30);

            var matchStats = db.Matches
                .Where(m => m.StartedAt >= startDate)
                .GroupBy(m => DbFunctions.TruncateTime(m.StartedAt))
                .Select(g => new
                {
                    Date = g.Key.Value,
                    MatchCount = g.Count()
                }).ToList();

            var userStats = db.Users
                .Where(u => u.CreatedAt >= startDate)
                .GroupBy(u => DbFunctions.TruncateTime(u.CreatedAt))
                .Select(g => new
                {
                    Date = g.Key.Value,
                    UserCount = g.Count()
                }).ToList();

            return Json(new { matchStats, userStats }, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetLanguageStats()
        {
            var data = db.Submissions
                .GroupBy(s => s.Language)
                .Select(g => new
                {
                    Language = g.Key,
                    Count = g.Count()
                }).ToList();

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetDifficultyStats()
        {
            // Step 1: 取得每個題目難度對應的 ProblemId
            var problemDifficultyMap = db.Problems
                .Select(p => new { p.ProblemId, p.Difficulty })
                .ToList();

            // Step 2: 取得正確的提交對應的 ProblemId（直接從 Submissions）
            var correctProblemIds = db.Submissions
                .Where(s => s.Result == "Correct")
                .Select(s => s.ProblemId)
                .Distinct()
                .ToList();

            // Step 3: 平均得分（從 Matches 抓 Mode + Player1Score + Player2Score）
            var avgScores = db.Matches
                .Where(m => m.MatchStatus == "Finished")
                .Select(m => new
                {
                    m.Mode,
                    Player1Score = (int?)m.Player1Score ?? 0,
                    Player2Score = (int?)m.Player2Score ?? 0
                })
                .ToList()
                .GroupBy(x => x.Mode)
                .Select(g => new
                {
                    Difficulty = g.Key,
                    AvgScore = g.Average(x => (x.Player1Score + x.Player2Score) / 2.0)
                })
                .ToList();

            // Step 4: 整合為統計資料
            var result = problemDifficultyMap
                .GroupBy(p => p.Difficulty)
                .Select(g =>
                {
                    string difficulty = g.Key;
                    int total = g.Count();
                    int correct = g.Count(p => correctProblemIds.Contains(p.ProblemId));
                    double avg = avgScores.FirstOrDefault(a => a.Difficulty == difficulty)?.AvgScore ?? 0;

                    return new
                    {
                        Difficulty = difficulty,
                        TotalProblems = total,
                        CorrectSubmissions = correct,
                        AvgScore = Math.Round(avg, 2)
                    };
                })
                .ToList();

            return Json(result, JsonRequestBehavior.AllowGet);
        }



        public JsonResult GetHeatMapStats()
        {
            var startDate = DateTime.Today.AddDays(-7);
            var data = db.Matches
                .Where(m => m.StartedAt.HasValue && m.StartedAt >= startDate)
                .GroupBy(m => new
                {
                    DayOfWeek = DbFunctions.DiffDays(DateTime.MinValue, m.StartedAt) % 7,
                    Hour = m.StartedAt.Value.Hour
                })
                .Select(g => new
                {
                    g.Key.DayOfWeek,
                    g.Key.Hour,
                    Count = g.Count()
                }).ToList();

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult UserList()
        {
            using (var db = new DatabaseEntities())
            {
                var users = db.Users.ToList();
                return View(users);  
            }
        }
        [HttpPost]
        public ActionResult UpdateUser(int userId, string Username, string Email, string PasswordHash,
                               string DisplayName, string UserRole, string PreferredLanguage,
                               int Rating)
        {
            using (var db = new DatabaseEntities())
            {
                var user = db.Users.FirstOrDefault(u => u.UserId == userId);
                if (user != null)
                {
                    user.Username = Username;
                    user.Email = Email;
                    user.PasswordHash = PasswordHash;
                    user.DisplayName = DisplayName;
                    user.UserRole = UserRole;
                    user.PreferredLanguage = PreferredLanguage;
                    user.Rating = Rating;

                    db.SaveChanges();
                }
            }
            return Json(new { success = true });
        }

    }
}