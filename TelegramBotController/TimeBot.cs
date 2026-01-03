using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using WolfLive.Api;
using WolfLive.Api.Models;

namespace TelegramBotController
{
    /// <summary>
    /// بوت الوقت الذي يقوم بإرسال رسالة في وقت محدد بدقة عالية
    /// </summary>
    public class TimeBot : IBot
    {
        private WolfClient? _client;
        private string? _groupId;
        private string? _targetUserId;
        private bool _isRunning;
        private int _playCount;
        
        public string Name => "⏱️ بوت الوقت";
        public string Description => "يرسل كلمة {الان} بدقة عالية";
        public bool IsRunning => _isRunning;
        public int PlayCount => _playCount;
        public IWolfClient? Client => _client;
        public event Action<string>? OnLog;

        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }
        
        /// <summary>
        /// مُنشئ الفئة
        /// </summary>
        public TimeBot()
        {
            _playCount = 0;
            _isRunning = false;
        }
        
        /// <summary>
        /// بدء تشغيل البوت وتسجيل الدخول
        /// </summary>
        public async Task StartAsync(string email, string password, string groupId, string targetUserId)
        {
            if (_isRunning) return;
            
            try
            {
                _client = new WolfClient();
                
                var loginResult = await _client.Login(email, password);
                if (!loginResult) throw new Exception("فشل تسجيل الدخول");
                
                // تثبيت المعرفات حسب طلب المستخدم
                _groupId = groupId;
                _targetUserId = targetUserId; // المعرف المطلوب مراقبته
                _isRunning = true;
                
                _client.Messaging.OnGroupMessage += HandleMessage;
                
                // إرسال رسالة البداية عند التشغيل
                await _client.GroupMessage(_groupId, "!وقت");

                Console.WriteLine($"✅ {Name} يعمل الآن (Group: {_groupId}, Target: {_targetUserId})");
            }
            catch (Exception ex)
            {
                throw new Exception($"فشل بدء {Name}: {ex.Message}");
            }
        }
        
        private async void HandleMessage(IWolfClient client, Message message, GroupUser? groupUser)
        {
            if (!_isRunning) return;
            
            // مراقبة المجموعة المحددة فقط
            if (message.GroupId != _groupId) return;

            // عرض الرسائل في الكونسول للمراقبة (تم الإلغاء)
            if (message.UserId == _targetUserId)
            {
                // Console.WriteLine($"⏱️ [TimeBot] رسالة من الهدف {message.UserId}: {message.Content}");
                
                // تحليل الرسالة: 
                // Arabic: !اكتب {الان} بعد مرور 5 ثانية للفوز
                // English: Type {now} 9 seconds from now to win!
                
                // Arabic Pattern
                var match = Regex.Match(message.Content, @"(?:!|^)\s*اكتب\s*\{(.*?)\}\s*بعد مرور\s*(\d+)\s*ثانية للفوز", RegexOptions.IgnoreCase);
                
                if (!match.Success)
                {
                     // Try Arabic without start anchor
                     match = Regex.Match(message.Content, @"اكتب\s*\{(.*?)\}\s*بعد مرور\s*(\d+)\s*ثانية للفوز", RegexOptions.IgnoreCase);
                }

                if (!match.Success)
                {
                    // English Pattern
                    // Type {now} 9 seconds from now to win!
                    match = Regex.Match(message.Content, @"Type\s*\{(.*?)\}\s*(\d+)\s*seconds from now to win", RegexOptions.IgnoreCase);
                }
                
                if (match.Success)
                {
                    string word = match.Groups[1].Value;
                    if (int.TryParse(match.Groups[2].Value, out int seconds))
                    {
                        // استخدام وقت الوصول المحلي كمرجع أساسي لتجنب مشاكل اختلاف التوقيت مع السيرفر
                        // Using local arrival time as base to avoid clock skew issues
                        await ExecuteResponse(word, seconds);
                    }
                }
            }
        }

        private async Task ExecuteResponse(string word, int seconds)
        {
            try
            {
                // وقت الوصول (الآن)
                DateTime arrivalTime = DateTime.UtcNow;
                long arrivalTimeMs = new DateTimeOffset(arrivalTime).ToUnixTimeMilliseconds();

                // حساب وقت الهدف: وقت الوصول + الثواني المطلوبة
                long targetTime = arrivalTimeMs + (seconds * 1000);
                
                // تعويض زمن الوصول + هامش لضمان الوصول قبل النهاية
                // المستخدم طلب: "قبل انتهاء الوقت ب جزء من الثانية" (مثلاً 0.01)
                // نقلل الهامش إلى 150ms ليكون أقرب للهدف
                int networkLatencyBuffer = 150; 
                
                // الوقت الذي يجب أن نرسل فيه
                long sendTime = targetTime - networkLatencyBuffer;
                
                long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long delayMs = sendTime - currentTime;

                // Console.WriteLine($"📊 حساب التوقيت: Arrival={arrivalTime:HH:mm:ss.fff}, Target={seconds}s, Buffer={networkLatencyBuffer}ms, Delay={delayMs}ms");

                if (delayMs <= 0)
                {
                    // Console.WriteLine($"⚠️ الوقت ضيق جداً ({delayMs}ms)، إرسال فوري!");
                }
                else
                {
                    // Console.WriteLine($"⏳ مؤقت دقيق: الانتظار {delayMs}ms...");
                    
                    // انتظار مبدئي (Task.Delay)
                    if (delayMs > 200)
                    {
                        await Task.Delay((int)delayMs - 200);
                    }
                    
                    // انتظار دقيق (SpinWait)
                    while (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < sendTime)
                    {
                        Thread.SpinWait(100);
                    }
                }
                
                // Console.WriteLine($"🚀 إرسال الإجابة: {word} (الوقت الفعلي: {DateTime.UtcNow:HH:mm:ss.fff})");
                await _client.GroupMessage(_groupId, word);
                _playCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ خطأ في تنفيذ الاستجابة: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            _client.Messaging.OnGroupMessage -= HandleMessage;
            
            try
            {
                 // محاولة تسجيل خروج نظامي قبل قطع الاتصال
                 await _client.Emit(new Packet("private logout", null));
                 await Task.Delay(500);
            }
            catch { }
            
            try 
            {
                 await _client.Connection.DisconnectAsync();
            }
            catch { }
            
            Console.WriteLine($"⏹️ {Name} متوقف");
        }
        
        public Task<bool> CheckConnectionAsync()
        {
            try
            {
                return Task.FromResult(_client != null && _client.Connection.Connected);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
        
        public async Task<bool> JoinGroupAsync(string groupId)
        {
            try
            {
                await _client.Messaging.GroupMessageSubscribe(groupId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task SimulateMessage(WolfMessage message)
        {
             // محاكاة لاستقبال رسالة (لأغراض الاختبار)
             var msg = new Message(message);
             await Task.Run(() => HandleMessage(_client!, msg, null));
        }

        public void ResetCounters()
        {
            _playCount = 0;
        }

        public void SimulateMessage(string content, string userId, string groupId)
        {
            if (!_isRunning) return;
            
            string finalGroupId = (groupId == "GROUP_ID") ? _groupId : groupId;
            string finalUserId = (userId == "TARGET_USER") ? _targetUserId : userId;
            
            var wolfMsg = new WolfMessage
            {
                Recipient = new IdHash { Id = finalGroupId },
                Originator = new IdHash { Id = finalUserId },
                IsGroup = true,
                ByteData = Encoding.UTF8.GetBytes(content),
                Timestamp = DateTime.UtcNow.Ticks,
                MimeType = "text/plain"
            };
            
            var msg = new Message(wolfMsg);
            
            HandleMessage(_client!, msg, null);
        }

        public void StartRaceSession(int rounds, bool training, string groupId) { /* Not supported */ }
        public void StopRaceSession() { /* Not supported */ }
    }
}