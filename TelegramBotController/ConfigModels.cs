using System.Collections.Generic;

namespace TelegramBotController
{
    public class MonitorConfigData
    {
        public int DelaySeconds { get; set; }
        public List<PhraseConfig>? Phrases { get; set; }
        public string? TargetGroupId { get; set; }
    }

    public class PhraseConfig
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
    }
}
