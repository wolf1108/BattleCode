namespace BattleCode.Models
{
    public class User
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string DisplayName { get; set; }
        public string PreferredLanguage { get; set; }
        public string UserRole { get; set; }
        

        public int Rating { get; set; }
        public string AI { get; set; }
        public string pic { get; set; }
   
    }
}
