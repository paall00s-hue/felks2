using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramBotController.Services
{
    public class TelegramBackgroundService : BackgroundService
    {
        private readonly TelegramController _telegramController;

        public TelegramBackgroundService(TelegramController telegramController)
        {
            _telegramController = telegramController;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _telegramController.StartAsync();
        }
    }
}
