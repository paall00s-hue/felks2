using System;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using Newtonsoft.Json;
using WolfLive.Api;
using WolfLive.Api.Models;

namespace TelegramBotController
{
    public class ReverseBot : IBot
    {
        private WolfClient? _client;
        private string? _groupId;
        private string? _targetUserId;
        private bool _isRunning;
        private int _playCount;
        
        public string Name => "🔄 عكس";
        public string Description => "يعكس الكلمات العربية والإنجليزية";
        public bool IsRunning => _isRunning;
        public int PlayCount => _playCount;
        public IWolfClient? Client => _client;
        public event Action<string>? OnLog;

        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }
        
        public ReverseBot()
        {
            _playCount = 0;
            _isRunning = false;
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
                        _groupId = "18822804"; // Default fallback
                    }
                }

                _targetUserId = "75423789";

                _isRunning = true;
                
                _client.Messaging.OnGroupMessage += HandleMessage;

                // إرسال رسالة التأكيد عند الدخول
                if (int.TryParse(_groupId, out _))
                {
                    await _client.JoinGroup(_groupId);
                    await _client.GroupMessage(_groupId, "!bw");
                    Console.WriteLine($"✅ {Name} - قناة: {_groupId} - نوع: عكس");
                }
                else
                {
                    Console.WriteLine($"⚠️ {Name} - معرف المجموعة غير صالح: {_groupId}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"فشل بدء {Name}: {ex.Message}");
            }
        }
        
        private async void HandleMessage(IWolfClient client, Message message, GroupUser? groupUser)
        {
            if (!_isRunning) return;
            
            var groupId = message.GroupId;
            var userId = message.UserId;
            
            // تم إيقاف عرض الرسائل في الكونسول بناءً على طلب المستخدم لتقليل الضوضاء
            // if (groupId == _groupId)
            // {
            //     Console.WriteLine($"💬 [{Name}] رسالة من {userId}: {message.Content}");
            // }
            
            if (groupId != _groupId || userId != _targetUserId)
                return;
            
            try
            {
                var content = message.Content;

                // تجاهل رسائل الفوز والنتائج لتجنب التكرار (عربي وإنجليزي)
                if (content.Contains("مُبارك") || content.Contains("أجبت خلال") || content.Contains("نقطة") ||
                    content.Contains("Congrats") || content.Contains("figured out") || content.Contains("gained"))
                {
                    return;
                }

                var reversedText = ReverseWords(content);
                
                if (!string.IsNullOrEmpty(reversedText))
                {
                    await _client.GroupMessage(_groupId, reversedText);
                    _playCount++;
                }
            }
            catch (Exception)
            {
                // تجاهل الأخطاء في الكونسول
            }
        }
        
        private string ReverseWords(string text)
        {
            // تنظيف النص من الرموز والأسهم قبل العكس
            var cleanedText = Regex.Replace(text, @"[|><-]", "");
            
            var words = cleanedText.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var reversedWords = words.Select(w => new string(w.Reverse().ToArray()));
            return string.Join(" ", reversedWords);
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
            
            // استخدام القيم الحقيقية إذا تم تمرير قيم افتراضية
            string finalGroupId = (groupId == "GROUP_ID") ? _groupId : groupId;
            string finalUserId = (userId == "TARGET_USER") ? _targetUserId : userId;
            
             // إنشاء رسالة وهمية
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
