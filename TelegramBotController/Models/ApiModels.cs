namespace TelegramBotController.Models
{
    public class StartBotRequest
    {
        public string Bot { get; set; }
        public string Account { get; set; }
    }

    public class StopBotRequest
    {
        public string AccountId { get; set; }
    }

    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class AccountDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ActiveBot { get; set; }
    }
}
