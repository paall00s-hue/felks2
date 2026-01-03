using System;
using System.Threading.Tasks;

namespace TelegramBotController
{
    public class RaceBot : MonitorBot
    {
        public override string Name => "🏎️ سباق";
        public override string Description => "بوت متخصص في سباقات ولف";

        public RaceBot() : base()
        {
        }
    }
}
