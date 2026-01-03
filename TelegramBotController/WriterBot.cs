using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using Newtonsoft.Json;
using WolfLive.Api;
using WolfLive.Api.Models;

namespace TelegramBotController
{
    public class WriterBot : IBot
    {
        private WolfClient? _client;
        private string? _groupId;
        private string? _targetUserId;
        private bool _isRunning;
        private int _playCount;
        private readonly Regex _cleanPattern;
        
        public string Name => "📝 كتابة";
        public string Description => "ينظف الكلمات من الرموز ويرسلها";
        public bool IsRunning => _isRunning;
        public int PlayCount => _playCount;
        public IWolfClient? Client => _client;
        public event Action<string>? OnLog;

        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }
        
        public WriterBot()
        {
            _playCount = 0;
            _isRunning = false;
            // Clean everything except letters and marks
            _cleanPattern = new Regex(@"[^\p{L}\p{M}\s]", RegexOptions.Compiled);
        }
        
        public async Task StartAsync(string email, string password, string groupId, string targetUserId)
        {
            if (_isRunning) return;
            
            try
            {
                _client = new WolfClient();
                
                var loginResult = await _client.Login(email, password);
                if (!loginResult)
                {
                    throw new Exception("فشل تسجيل الدخول");
                }
                
                // تثبيت المعرفات حسب طلب المستخدم
                // _groupId = "18822804"; // Removed hardcode
                _groupId = null;

                // تحميل الإعدادات من الملف
                try
                {
                    if (File.Exists("monitor_config.json"))
                    {
                        var json = File.ReadAllText("monitor_config.json");
                        var config = JsonConvert.DeserializeObject<MonitorConfigData>(json);
                        if (config != null && !string.IsNullOrEmpty(config.TargetGroupId))
                        {
                            _groupId = config.TargetGroupId;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ فشل تحميل إعدادات المجموعة لـ {Name}: {ex.Message}");
                }

                if (string.IsNullOrEmpty(_groupId))
                {
                    if (!string.IsNullOrEmpty(groupId) && groupId != "0")
                    {
                        _groupId = groupId;
                    }
                    else
                    {
                        // Fallback logic or error if no group ID is found
                        _groupId = "18822804"; // Default fallback if needed, or handle error
                    }
                }
                
                _targetUserId = "24062011"; // المعرف المطلوب للبوت الكتابي
                _isRunning = true;
                
                _client.Messaging.OnGroupMessage += HandleMessage;
                
                // إرسال رسالة البداية عند التشغيل
                if (int.TryParse(_groupId, out _))
                {
                    await _client.JoinGroup(_groupId);
                    await _client.GroupMessage(_groupId, "!كتابه");
                    Console.WriteLine($"✅ {Name} - قناة: {_groupId} - نوع: كتابة");
                }
                else
                {
                     // Don't send if group ID is invalid (though JoinGroupAsync might catch this later)
                     Console.WriteLine($"⚠️ {Name} - معرف المجموعة غير صالح: {_groupId}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"فشل بدء {Name}: {ex.Message}");
            }
        }
        
        private async void HandleMessage(IWolfClient client, Message message, GroupUser groupUser)
        {
            if (!_isRunning) return;
            
            var groupId = message.GroupId;
            var userId = message.UserId;
            
            // مراقبة المجموعة المحددة والهدف المحدد
            if (groupId != _groupId || userId != _targetUserId)
                return;
            
            try
            {
                var content = message.Content;

                // تجاهل رسائل الفوز والنتائج
                if (content.Contains("مُبارك") || content.Contains("أجبت خلال"))
                {
                    return;
                }

                // استخراج النص من النمط: |--> النص <--|
                var match = Regex.Match(content, @"\|-->\s*(.*?)\s*<--\|");
                
                if (match.Success)
                {
                    var extractedText = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(extractedText))
                    {
                        await _client.GroupMessage(_groupId, extractedText);
                        _playCount++;
                    }
                }
                // Support English Pattern: Type {now} 8 seconds from now to win!
                else if (content.Contains("Type {") && content.Contains("} 8 seconds from now to win!"))
                {
                     var matchEn = Regex.Match(content, @"Type \{(.*?)\} 8 seconds from now to win!");
                     if (matchEn.Success)
                     {
                         var extractedText = matchEn.Groups[1].Value.Trim();
                         if (!string.IsNullOrEmpty(extractedText))
                         {
                             await _client.GroupMessage(_groupId, extractedText);
                             _playCount++;
                         }
                     }
                }
            }
            catch (Exception)
            {
                // تجاهل الأخطاء لعدم إزعاج المستخدم في الكونسول
            }
        }
        
        private string CleanText(string text)
        {
            // لم نعد بحاجة لهذه الدالة بالشكل القديم، الاستخراج يتم عبر Regex في HandleMessage
            return text;
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
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MimeType = "text/plain"
            };
            
            var msg = new Message(wolfMsg);
            
            HandleMessage(_client!, msg, null);
        }

        public void StartRaceSession(int rounds, bool training, string groupId) { /* Not supported */ }
        public void StopRaceSession() { /* Not supported */ }
    }
}