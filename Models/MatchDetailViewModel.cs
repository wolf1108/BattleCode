using System.Collections.Generic;
using BattleCode.Models;  // 請確認你的 EDMX 自動產生的命名空間

namespace BattleCode.Models.ViewModels
{
    public class MatchDetailViewModel
    {
        public Matches Match { get; set; }
        public List<ProblemDetailViewModel> Problems { get; set; }
    }

    public class ProblemDetailViewModel
    {
        public Problems Problem { get; set; }
        public List<SubmissionDetailViewModel> Submissions { get; set; }
    }

    public class SubmissionDetailViewModel
    {
        public string PlayerName { get; set; }
        public string Code { get; set; }
        public string Result { get; set; }
        public int? ExecutionTimeMs { get; set; }
        public string Output { get; set; }
        public string ErrorMessage { get; set; }
        public string AIAnalysis { get; set; }
        public int UserId { get; set; }
        public string HintText { get; set; }
    }
}
