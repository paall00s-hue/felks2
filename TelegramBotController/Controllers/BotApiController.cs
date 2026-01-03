using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using TelegramBotController.Models;
using TelegramBotController.Services;
using System;
using System.Linq;

namespace TelegramBotController.Controllers
{
    [ApiController]
    [Route("")]
    public class BotApiController : ControllerBase
    {
        private readonly BotManager _botManager;
        private readonly AccountService _accountService;

        public BotApiController(BotManager botManager, AccountService accountService)
        {
            _botManager = botManager;
            _accountService = accountService;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            _accountService.AddAccount(request.Email, request.Password);
            return Ok(new { message = "Logged in", accountName = $"Account {_accountService.GetAll().Count}" });
        }

        [HttpGet("accounts")]
        public IActionResult GetAccounts()
        {
            var accounts = _accountService.GetAll().Select(a => new AccountDto 
            {
                Id = a.Id,
                Name = a.Name,
                ActiveBot = _botManager.GetUserBots(a.Id).FirstOrDefault()?.BotType // Simplified active bot view
            });
            return Ok(accounts);
        }

        [HttpPost("startBot")]
        public async Task<IActionResult> StartBot([FromBody] StartBotRequest request)
        {
            var account = _accountService.GetAccountByName(request.Account);
            if (account == null)
                return NotFound("Account not found. Please login first.");

            // Default settings for API-started bots
            string groupId = "18822804"; // Default or load from config
            string targetUserId = "0";
            
            // Map Swift bot names to internal names
            string botType = MapBotType(request.Bot);

            try 
            {
                await _botManager.StartBot(botType, account.Email, account.Password, groupId, targetUserId, account.Id);
                return Ok(new { message = "Bot started" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        private string MapBotType(string swiftBotType)
        {
            return swiftBotType.ToLower() switch
            {
                "race" => "سباق",
                "calculator" => "أحسب",
                "time" => "وقت",
                "writer" => "كتابة",
                "reverse" => "عكس",
                "monitor" => "مراقبة",
                "join" => "مراقبة", // Assuming join uses monitor logic or similar
                _ => swiftBotType // Fallback
            };
        }

        [HttpPost("stopBot")]
        public async Task<IActionResult> StopBot([FromBody] StopBotRequest request)
        {
            // The Swift code sends "accountId" UUID string
            await _botManager.StopAllBots(request.AccountId);
            return Ok(new { message = "Bot stopped" });
        }
    }
}
