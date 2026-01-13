using System;

namespace BattleCode.Models
{
    public class MatchViewModel
    {
        public int MatchId { get; set; }
        public bool IsPractice { get; set; }
        public string Mode { get; set; }
        public string Player1Name { get; set; }
        public string Player2Name { get; set; }
        public int? Player1Score { get; set; }
        public int? Player2Score { get; set; }
        public string WinnerName { get; set; }
        public DateTime? StartedAt { get; set; }
        public string Language { get; set; }


        public string Player1Pic { get; set; }
        public string Player2Pic { get; set; }

    }
}
