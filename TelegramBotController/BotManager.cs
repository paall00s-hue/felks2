using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using WolfLive.Api;
using WolfLive.Api.Models;
using WolfLive.Api.Delegates;

using System.IO;
using Newtonsoft.Json;
using TelegramBotController.Services;

namespace TelegramBotController
{
    public interface IBot
    {
        string Name { get; }
        string Description { get; }
        bool IsRunning { get; }
        int PlayCount { get; }
        IWolfClient? Client { get; }
        event Action<string>? OnLog;
        
        Task StartAsync(string email, string password, string groupId, string targetUserId);
        Task StopAsync();
        Task<bool> CheckConnectionAsync();
        Task<bool> JoinGroupAsync(string groupId);
        void ResetCounters();
        void SimulateMessage(string content, string userId, string groupId);
        void StartRaceSession(int rounds, bool training, string groupId);
        void StopRaceSession();
    }
    
    public class BotManager : IDisposable
    {
        private ConcurrentDictionary<string, IBot> _activeBots = new ConcurrentDictionary<string, IBot>();
        private ConcurrentDictionary<string, BotStats> _botStats = new ConcurrentDictionary<string, BotStats>();
        private ConcurrentDictionary<string, MessageCarrier<GroupUser>> _deleteHandlers = new ConcurrentDictionary<string, MessageCarrier<GroupUser>>();
        private bool _isDisposed;
        
        public event EventHandler<BotEvent>? OnBotEvent;
        public event EventHandler<NotificationEvent>? OnNotification;
        
        public BotManager()
        {
            Console.WriteLine("✅ مدير البوتات جاهز للعمل");
            Logger.LogEvent("BotManager initialized.");
        }
        
        public int GetUserBotCount(string telegramUserId)
        {
            return _activeBots.Count(b => b.Key.StartsWith(telegramUserId + "_"));
        }

        public List<BotStats> GetUserBots(string telegramUserId)
        {
            return _botStats.Values.Where(b => b.TelegramUserId == telegramUserId).ToList();
        }

        public async Task StopAllBots(string telegramUserId)
        {
            var userBots = _activeBots.Where(b => b.Key.StartsWith(telegramUserId + "_")).ToList();
            foreach (var botEntry in userBots)
            {
                await StopBot(botEntry.Key);
            }
        }
        
        public async Task<string> StartBot(string email, string password)
        {
            // إنشاء بوت مراقبة افتراضي للعمليات الإدارية
            string botType = "مراقبة";
            // استخدمنا admin سابقاً، لكن هذا يسبب مشكلة لأن البوت يسجل باسم admin وليس بمعرف المستخدم الحقيقي
            // ولذلك لا يظهر في قائمة GetUserBots(userId)
            // سنستخدم معرف مؤقت ولكن يجب تحديثه لاحقاً أو تمريره كمعامل
            // الحل الأفضل: إضافة معامل telegramUserId لهذه الدالة
            // ولكن لتجنب كسر الكود القديم، سنبحث عن طريقة أخرى أو نعتمد على StartBot الكاملة
            // بما أن هذا الاستدعاء يأتي من TelegramController، يمكننا تمرير المعرف
            
            // للتوافق السريع: سنفترض أن المستخدم يريد فقط التحقق من الحساب
            // ولكن المشكلة أن البوت يضاف لقائمة activeBots بمعرف admin
            
            // تصحيح: يجب أن نستخدم معرف المستخدم الفعلي. بما أننا لا نملكه هنا،
            // سنقوم بتعديل TelegramController لاستخدام الدالة الأخرى أو تعديل هذه الدالة.
            // الخيار الأسهل: تعديل هذه الدالة لتقبل telegramUserId
            
            throw new InvalidOperationException("Use the overload with telegramUserId instead.");
        }

        public async Task<string> StartBot(string email, string password, string telegramUserId)
        {
             // إنشاء بوت مراقبة افتراضي للعمليات الإدارية
            string botType = "مراقبة";
            string botId = $"{telegramUserId}_{botType}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

            IBot bot = new MonitorBot();
            
            // استخدام groupId="0" و targetUserId="0" كقيم افتراضية لأننا سنستخدم البوت للعمليات الإدارية فقط
            await bot.StartAsync(email, password, "0", "0");
            
            bool connected = await bot.CheckConnectionAsync();
            if (!connected)
            {
                throw new Exception("فشل الاتصال بالسيرفر. تحقق من البريد وكلمة المرور.");
            }

            _activeBots.TryAdd(botId, bot);
            _botStats.TryAdd(botId, new BotStats
            {
                BotId = botId,
                BotType = botType,
                StartTime = DateTime.Now,
                LastUpdate = DateTime.Now,
                TelegramUserId = telegramUserId,
                BotName = bot.Name,
                Email = email,
                Password = password
            });

            return botId;
        }

        public async Task<BotResult> StartBot(string botType, string email, string password, string groupId, string targetUserId, string telegramUserId)
        {
            try
            {
                string botId = $"{telegramUserId}_{botType}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                
                IBot bot = botType.ToLower() switch
                {
                    "أحسب" => new CalculatorBot(),
                    "كتابة" => new WriterBot(),
                    "عكس" => new ReverseBot(),
                    "وقت" => new TimeBot(),
                    "مراقبة" => new MonitorBot(),
                    "join" => new MonitorBot(),
                    "سباق" => new RaceBot(),
                    _ => throw new ArgumentException($"نوع البوت غير معروف: {botType}")
                };
                
                // Subscribe to logs
                bot.OnLog += (message) => 
                {
                    OnNotification?.Invoke(this, new NotificationEvent 
                    { 
                        BotId = botId,
                        TelegramUserId = telegramUserId,
                        Message = message,
                        Count = bot.PlayCount
                    });
                };
                
                // محاولة تسجيل الدخول
                OnBotEvent?.Invoke(this, new BotEvent
                {
                    BotId = botId,
                    Type = BotEventType.Starting,
                    Message = $"جاري تشغيل {bot.Name}..."
                });
                
                await bot.StartAsync(email, password, groupId, targetUserId);
                
                // التحقق من الاتصال
                bool connected = await bot.CheckConnectionAsync();
                if (!connected)
                {
                    Logger.LogError($"Connection failed for bot {botId} ({bot.Name})");
                    OnBotEvent?.Invoke(this, new BotEvent
                    {
                        BotId = botId,
                        Type = BotEventType.Error,
                        Message = "فشل الاتصال بالسيرفر"
                    });
                    return new BotResult { Success = false, Error = "فشل الاتصال بالسيرفر" };
                }
                
                // الانضمام للمجموعة
                bool joined = await bot.JoinGroupAsync(groupId);
                if (!joined)
                {
                    Logger.LogError($"Failed to join group {groupId} for bot {botId}");
                    OnBotEvent?.Invoke(this, new BotEvent
                    {
                        BotId = botId,
                        Type = BotEventType.Error,
                        Message = "فشل الانضمام للمجموعة"
                    });
                    return new BotResult { Success = false, Error = "فشل الانضمام للمجموعة" };
                }
                
                // حفظ البوت النشط
                _activeBots[botId] = bot;
                _botStats[botId] = new BotStats
                {
                    BotId = botId,
                    BotType = botType,
                    StartTime = DateTime.Now,
                    TelegramUserId = telegramUserId,
                    BotName = bot.Name,
                    Email = email, // Store credentials
                    Password = password // Store credentials
                };
                
                // بدء مراقبة العداد
                StartMonitoring(botId, bot);
                
                OnBotEvent?.Invoke(this, new BotEvent
                {
                    BotId = botId,
                    Type = BotEventType.Started,
                    Message = $"✅ تم تشغيل {bot.Name} بنجاح!"
                });
                
                Logger.LogEvent($"Bot started: {botId} ({bot.Name})");

                return new BotResult { Success = true, BotId = botId, BotName = bot.Name };
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error starting bot {botType}", ex);
                OnBotEvent?.Invoke(this, new BotEvent
                {
                    Type = BotEventType.Error,
                    Message = $"❌ خطأ: {ex.Message}"
                });
                return new BotResult { Success = false, Error = ex.Message };
            }
        }
        
        public Task<bool> StartRaceMode(string botId, int rounds, bool training, string groupId)
        {
            if (!_activeBots.TryGetValue(botId, out IBot bot)) return Task.FromResult(false);
            
            try
            {
                bot.StartRaceSession(rounds, training, groupId);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error starting race for bot {botId}", ex);
                return Task.FromResult(false);
            }
        }

        public BotStats GetBotStats(string botId)
        {
            _botStats.TryGetValue(botId, out var stats);
            return stats;
        }

        public async Task<BotResult> StopBot(string botId)
        {
            try
            {
                // تنظيف معالج الحذف التلقائي إذا وجد
                if (_deleteHandlers.TryRemove(botId, out var handler))
                {
                    if (_activeBots.TryGetValue(botId, out var b) && b.Client != null)
                    {
                        b.Client.Messaging.OnGroupMessage -= handler;
                    }
                }

                if (!_activeBots.TryGetValue(botId, out IBot bot))
                {
                    return new BotResult { Success = false, Error = "البوت غير موجود" };
                }
                
                await bot.StopAsync();
                _activeBots.TryRemove(botId, out _);
                _botStats.TryRemove(botId, out _);
                
                OnBotEvent?.Invoke(this, new BotEvent
                {
                    BotId = botId,
                    Type = BotEventType.Stopped,
                    Message = "⏹️ تم إيقاف البوت بنجاح"
                });
                
                Logger.LogEvent($"Bot stopped: {botId}");

                return new BotResult { Success = true };
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error stopping bot {botId}", ex);
                OnBotEvent?.Invoke(this, new BotEvent
                {
                    BotId = botId,
                    Type = BotEventType.Error,
                    Message = $"❌ خطأ في الإيقاف: {ex.Message}"
                });
                return new BotResult { Success = false, Error = ex.Message };
            }
        }
        
        public BotStatus? GetBotStatus(string botId)
        {
            if (_activeBots.TryGetValue(botId, out var bot))
            {
                if (!_botStats.TryGetValue(botId, out var stats))
                {
                    stats = new BotStats { BotName = bot.Name, StartTime = DateTime.Now };
                    _botStats.TryAdd(botId, stats);
                }
                
                return new BotStatus
                {
                    BotId = botId,
                    BotName = bot.Name,
                    IsRunning = bot.IsRunning,
                    PlayCount = bot.PlayCount,
                    StartTime = stats.StartTime,
                    RunningTime = DateTime.Now - stats.StartTime,
                    BotType = stats.BotType
                };
            }
            return null;
        }
        
        public int GetUserActiveBotsCount(string telegramUserId)
        {
            int count = 0;
            foreach (var stats in _botStats.Values)
            {
                if (stats.TelegramUserId == telegramUserId)
                {
                    count++;
                }
            }
            return count;
        }
        
        public void SimulateMessageToAll(string content)
        {
            foreach (var bot in _activeBots.Values)
            {
                // استخدام معرفات ثابتة للاختبار (يمكن تعديلها لتكون ديناميكية)
                // نستخدم المعرفات التي يتوقعها كل بوت (مثل 36828201 للحاسبة)
                // لكن هنا سنرسل معرف "TEST_USER" وسنعتمد على البوت في قبولها أو لا
                // ولكن لتسهيل الاختبار، سنجعل البوت يقبل الرسائل من "TEST_USER" أو نرسل المعرف الصحيح
                
                // الأفضل: إرسال الرسالة كما لو كانت من الهدف
                // سنقوم بتنفيذ ذلك في كل بوت
                bot.SimulateMessage(content, "TARGET_USER", "GROUP_ID");
            }
        }
        
        private async void StartMonitoring(string botId, IBot bot)
        {
            int lastNotificationCount = 0;
            
            while (_activeBots.ContainsKey(botId) && bot.IsRunning)
            {
                try
                {
                    await Task.Delay(10000); // كل 10 ثواني
                    
                    var stats = _botStats[botId];
                    int currentCount = bot.PlayCount;
                    
                    // إرسال إشعار كل 100 مرة
                    if (currentCount >= lastNotificationCount + 100)
                    {
                        lastNotificationCount = currentCount - (currentCount % 100);
                        
                        OnNotification?.Invoke(this, new NotificationEvent
                        {
                            BotId = botId,
                            TelegramUserId = stats.TelegramUserId,
                            Message = $"🎉 وصل {bot.Name} إلى {currentCount} عملية!",
                            Count = currentCount
                        });
                    }
                    
                    // تحديث الإحصائيات
                    stats.PlayCount = currentCount;
                    stats.LastUpdate = DateTime.Now;
                }
                catch
                {
                    // تجاهل الأخطاء في المراقبة
                }
            }
        }
        
        public async Task<string> StartAutoDelete(string botId, string targetGroupId, string targetUserId, int delaySeconds)
        {
            if (!_activeBots.TryGetValue(botId, out var bot) || bot.Client == null)
                return "❌ البوت غير متصل.";

            // 1. التحقق من الانضمام للمجموعة أو الانضمام إليها
            bool isMember = false;
            try
            {
                var groups = bot.Client.Groups();
                if (groups != null && groups.Any(g => g.Id == targetGroupId))
                {
                    isMember = true;
                }
                else
                {
                    // محاولة الانضمام
                    isMember = await bot.Client.JoinGroup(targetGroupId);
                }
            }
            catch 
            {
                 // تجاهل الأخطاء، سنفترض الفشل أو المحاولة لاحقاً
            }

            if (!isMember)
                return "❌ فشل الانضمام للمجموعة (قد تكون مغلقة أو البوت محظور).";

            // قراءة إعدادات الرسائل من ملف JSON
            string? spamMsg = null;
            string? adminMsg = null;

            try 
            {
                if (File.Exists("auto_delete_config.json"))
                {
                    var json = File.ReadAllText("auto_delete_config.json", System.Text.Encoding.UTF8);
                    dynamic config = JsonConvert.DeserializeObject(json);
                    // استخدام القيم من الملف إذا وجدت، وإلا الاحتفاظ بالافتراضي
                    if (config?.SpamMessage != null) spamMsg = config.SpamMessage;
                    if (config?.AdminSuccessMessage != null) adminMsg = config.AdminSuccessMessage;
                }
            }
            catch {}

            // 2. إرسال رسالة "انا بوت حذف..." 3 مرات (مهمة خلفية)
            _ = Task.Run(async () => 
            {
                try 
                {
                    if (!string.IsNullOrEmpty(spamMsg))
                    {
                        for(int i=0; i<3; i++)
                        {
                            await bot.Client.GroupMessage(targetGroupId, spamMsg);
                            await Task.Delay(1500); // فاصل زمني قصير
                        }
                    }
                }
                catch {}
            });

            // 3. مراقبة صلاحيات الأدمن وإرسال الشكر (مهمة خلفية)
            _ = Task.Run(async () => 
            {
                int checks = 0;
                // المحاولة لمدة ساعة تقريباً (360 محاولة * 10 ثواني)
                while(checks < 360) 
                {
                    try
                    {
                        await Task.Delay(10000); // فحص كل 10 ثواني
                        checks++;

                        var me = bot.Client.CurrentUser();
                        if (me == null) continue;

                        // جلب بيانات العضو في المجموعة للتأكد من الصلاحيات
                        var groupUsers = await bot.Client.GetGroupUsers(targetGroupId, me.Id.ToString());
                        var groupUser = groupUsers?.FirstOrDefault();
                        
                        // (int)GroupUserType.User = 0, Admin/Mod/Owner > 0
                        if (groupUser != null && (int)groupUser.Capabilities > 0) 
                        {
                            // أصبح أدمن!
                            if (!string.IsNullOrEmpty(adminMsg))
                            {
                                await bot.Client.GroupMessage(targetGroupId, adminMsg);
                            }
                            break; // إنهاء الحلقة
                        }
                    }
                    catch 
                    {
                        // تجاهل الأخطاء في الخلفية
                    }
                }
            });

            // 4. تفعيل معالج الحذف (Logic as before)
            // تقسيم معرفات المستخدمين المستهدفين (يدعم التعدد)
            var targetUserIds = targetUserId.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            
            MessageCarrier<GroupUser> handler = (client, msg, user) =>
            {
                // التحقق من المجموعة والمستخدم
                if (msg.IsGroup && msg.GroupId == targetGroupId && targetUserIds.Contains(msg.UserId))
                {
                    // استخدام المتغير المهمل (_) لإخبار المترجم أننا لا نريد انتظار هذه المهمة عمداً
                    _ = Task.Run(async () =>
                    {
                        if (delaySeconds > 0)
                        {
                            await Task.Delay(delaySeconds * 1000);
                        }
                        
                        try
                        {
                            await client.Delete(msg);
                        }
                        catch { }
                    });
                }
                return;
            };

            bot.Client.Messaging.OnGroupMessage += handler;
            
            // تخزين المعالج
            _deleteHandlers[botId] = handler;
            
            return $"✅ تم تفعيل الحذف التلقائي للمستخدم {targetUserId} في المجموعة {targetGroupId} بواسطة {bot.Name} (التأخير: {delaySeconds} ثواني)\n\nجاري طلب الصلاحيات...";
        }

        public bool IsAutoDeleteActive(string telegramUserId)
        {
            var userBots = _activeBots.Where(b => b.Key.StartsWith(telegramUserId + "_")).Select(b => b.Key);
            return userBots.Any(id => _deleteHandlers.ContainsKey(id));
        }

        public string StopAutoDelete(string botId)
        {
            if (_activeBots.TryGetValue(botId, out var bot) && bot.Client != null)
            {
                if (_deleteHandlers.TryRemove(botId, out var handler))
                {
                    bot.Client.Messaging.OnGroupMessage -= handler;
                    return "✅ تم إيقاف الحذف التلقائي.";
                }
            }
            return "⚠️ لم يتم تفعيل الحذف مسبقاً.";
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                foreach (var bot in _activeBots.Values)
                {
                    try { bot.StopAsync().Wait(5000); } catch { }
                }
                _activeBots.Clear();
                _botStats.Clear();
                _isDisposed = true;
            }
        }
        
        // فئات البيانات
        public class BotResult
        {
            public bool Success { get; set; }
            public string? BotId { get; set; }
            public string? BotName { get; set; }
            public string? Error { get; set; }
        }
        
        public class BotStatus
        {
            public string? BotId { get; set; }
            public string? BotName { get; set; }
            public string? BotType { get; set; }
            public bool IsRunning { get; set; }
            public int PlayCount { get; set; }
            public DateTime StartTime { get; set; }
            public TimeSpan RunningTime { get; set; }
        }
        
        public class BotStats
        {
            public string? BotId { get; set; }
            public string? BotType { get; set; }
            public string? TelegramUserId { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? LastUpdate { get; set; }
            public int PlayCount { get; set; }
            public string BotName { get; set; }
        public string Email { get; set; } // Added to store credentials
        public string Password { get; set; } // Added to store credentials
    }
        
    public class BotEvent
        {
            public string? BotId { get; set; }
            public BotEventType Type { get; set; }
            public string? Message { get; set; }
        }
        
        public class NotificationEvent
        {
            public string? BotId { get; set; }
            public string? TelegramUserId { get; set; }
            public string? Message { get; set; }
            public int Count { get; set; }
        }
        
        public enum BotEventType
        {
            Starting,
            Started,
            Stopping,
            Stopped,
            Error,
            Warning,
            Info
        }
    }
}
