using System;
using System.Threading.Tasks;

namespace TelegramBotController
{
    public class RaceBot : MonitorBot
    {
        public new string Name => "🏎️ بوت السباق";
        public new string Description => "بوت متخصص في سباقات ولف";

        public RaceBot() : base()
        {
        }
    }
}
