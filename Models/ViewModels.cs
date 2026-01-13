using System.Collections.Generic;

namespace BattleCode.Models
{
    public class ResultViewModel
    {
        public int MatchId { get; set; }
        public string Player1Name { get; set; }
        public string Player2Name { get; set; }
        public int Player1Score { get; set; }
        public int Player2Score { get; set; }
        public string WinnerName { get; set; }
        public List<ProblemResultViewModel> ProblemResults { get; set; }
    }

    public class SubmitCodeRequest
    {
        public int MatchId { get; set; }
        public int ProblemId { get; set; }
        public string Code { get; set; }
        public string Language { get; set; }
        public string AiHint { get; set; }
    }

    public class ProblemResultViewModel
    {
        public string Title { get; set; }
        public bool IsCorrect { get; set; }

        public string Description { get; set; }
        
        public string Difficulty { get; set; }
        public string ExplanationOrFeedback { get; set; }
        public string AIAnalysis { get; set; }    // AI 分析建議
        public string ErrorMessage { get; set; }  // 錯誤訊息
        public string Code { get; set; }          // 使用者提交程式碼
    }

    public class BattleViewModel
    {
        public Matches Match { get; set; }
        public int PlayerId { get; set; }
        public List<Problems> Problems { get; set; }
        public string Player1DisplayName { get; set; }
        public string Player2DisplayName { get; set; }
        public int Player1Score { get; set; }
        public int Player2Score { get; set; }
    }

    public class PracticeBattleViewModel
    {
        public Matches Match { get; set; }
        public int PlayerId { get; set; }
        public List<Problems> Problems { get; set; }
        public int Score { get; set; }
    }
    
}