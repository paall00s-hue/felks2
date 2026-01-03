using System;
using System.Collections.Generic;
using System.Linq;

namespace TelegramBotController.Services
{
    public class AccountService
    {
        private List<AccountInfo> _accounts = new List<AccountInfo>();

        public void AddAccount(string email, string password)
        {
            if (!_accounts.Any(a => a.Email == email))
            {
                _accounts.Add(new AccountInfo
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"Account {_accounts.Count + 1}", // Simple naming like the Swift code
                    Email = email,
                    Password = password
                });
            }
        }

        public AccountInfo GetAccountByName(string name)
        {
            return _accounts.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        
        public AccountInfo GetAccountById(string id)
        {
            return _accounts.FirstOrDefault(a => a.Id == id);
        }

        public List<AccountInfo> GetAll() => _accounts;
    }

    public class AccountInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }
}
