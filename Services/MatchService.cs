using System;
using System.Linq;
using System.Threading.Tasks;
using BattleCode.Models;
using BattleCode.Services;

namespace BattleCode.Services
{
    public class MatchService
    {
        private readonly DatabaseEntities _db;
        private readonly OpenAIAssistantService _aiService;
        // 建構子：初始化資料庫物件和 AI 助理服務物件
        public MatchService()
        {
            _db = new DatabaseEntities();
            _aiService = new OpenAIAssistantService();
        }

        // 產生 AI 題目並加入到 Problems（直接帶有 MatchId 和 ProblemOrder）
        public async Task GenerateAIProblemsForMatchAsync(int matchId, string difficulty, string language)
        {
            // 呼叫 AI 服務，產生 3 題符合難度和語言的題目列表
            var generatedProblems = await _aiService.GenerateProblemsAsync(3, difficulty, language);
            // 將產生的題目逐題加入資料庫，並綁定 MatchId 及題目順序 ProblemOrder
            for (int i = 0; i < generatedProblems.Count; i++)
            {
                var generated = generatedProblems[i];
                var problem = new Problems
                {
                    MatchId = matchId,
                    Title = generated.Title,
                    TagName = generated.TagName,
                    Description = generated.Description,
                    InputFormat = generated.InputFormat,
                    OutputFormat = generated.OutputFormat,
                    SampleInputs = generated.SampleInputs,
                    SampleOutputs = generated.SampleOutputs,
                    Difficulty = generated.Difficulty,
                    Language = language,
                    CreatedAt = DateTime.Now,
                    ProblemOrder = i + 1
                };
                // 將此題加入 EF 追蹤的集合中
                _db.Problems.Add(problem);
            }

            await _db.SaveChangesAsync();
        }


        // 根據目前題目的 ProblemId，取得下一題的 ProblemId
        public async Task<int?> GetNextMatchProblemId(int currentProblemId)
        {
            // 注意這裡使用了 using，建立新的資料庫上下文，避免跨執行緒問題
            using (var db = new DatabaseEntities())
            {
                // 取得目前題目物件
                var current = db.Problems.FirstOrDefault(p => p.ProblemId == currentProblemId);
                if (current == null || current.MatchId == null) return null;
                // 找到同一場比賽中，題目順序比目前題目大的最小題目（也就是下一題）
                var next = db.Problems
                    .Where(p => p.MatchId == current.MatchId && p.ProblemOrder > current.ProblemOrder)
                    .OrderBy(p => p.ProblemOrder)
                    .FirstOrDefault();

                return next?.ProblemId;// 若有下一題則回傳題目 ID，沒有則回傳 null
            }
        }

        // 根據 ProblemId 取得詳細資訊
        public async Task<Problems> GetProblemDetail(int problemId)
        {
            using (var db = new DatabaseEntities())
            {
                return db.Problems.FirstOrDefault(p => p.ProblemId == problemId);
            }
        }
    }
}
