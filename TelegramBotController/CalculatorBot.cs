using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;
using System.IO;
using Newtonsoft.Json;
using WolfLive.Api;
using WolfLive.Api.Models;

namespace TelegramBotController
{
    public class CalculatorBot : IBot
    {
        private WolfClient? _client;
        private string? _groupId;
        private string? _targetUserId;
        private bool _isRunning;
        private int _playCount;
        private bool _waitingForRoundEnd;
        
        public string Name => "🧮 بوت العمليات الحسابية";
        public string Description => "يحل العمليات الرياضية تلقائياً";
        public bool IsRunning => _isRunning;
        public int PlayCount => _playCount;
        public IWolfClient? Client => _client;
        public event Action<string>? OnLog;
        
        public CalculatorBot()
        {
            _playCount = 0;
            _isRunning = false;
            _waitingForRoundEnd = false;
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

                _targetUserId = "36828201";

                _isRunning = true;
                _waitingForRoundEnd = false;
                
                // تسجيل معالج الرسائل
                _client.Messaging.OnGroupMessage += HandleMessage;

                // إرسال رسالة التأكيد عند الدخول
                await _client.GroupMessage(_groupId, "!احسب");
                
                Console.WriteLine($"✅ {Name} - قناة: {_groupId} - نوع: حساب");
            }
            catch (Exception ex)
            {
                throw new Exception($"فشل بدء {Name}: {ex.Message}");
            }
        }
        
        private void HandleMessage(IWolfClient client, Message message, GroupUser? groupUser)
        {
            if (!_isRunning) return;
            
            var groupId = message.GroupId;
            var userId = message.UserId;
            
            // تم إيقاف عرض الرسائل في الكونسول
            // if (groupId == _groupId)
            // {
            //    Console.WriteLine($"💬 [{Name}] رسالة من {userId}: {message.Content}");
            // }
            
            if (groupId != _groupId || userId != _targetUserId)
                return;
            
            HandleMessageLogic(message.Content);
        }

        private async void HandleMessageLogic(string content)
        {
            try
            {
                // التحقق من رسالة الفوز لإعادة تعيين الحالة
                if (content.Contains("الفائز:") && content.Contains("استعد، اللعبة الجديدة ستبدأ!"))
                {
                    _waitingForRoundEnd = false;
                    // Console.WriteLine("🔄 انتهت الجولة، مستعد للجولة القادمة.");
                    return;
                }
                
                if (_waitingForRoundEnd) return;
                
                if (content.Contains("أوجد الناتج"))
                {
                    var mathResult = ProcessMathExpression(content);
                    
                    if (!string.IsNullOrEmpty(mathResult))
                    {
                        // تم إلغاء الطباعة في الموجه بناءً على طلب المستخدم
                        // Console.WriteLine($"🧮 تم حل المعادلة: {mathResult}");
                        await _client.GroupMessage(_groupId, mathResult);
                        _playCount++;
                        _waitingForRoundEnd = true; 
                    }
                    else 
                    {
                         Console.WriteLine($"⚠️ فشل استخراج معادلة من النص: {content}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ خطأ في {Name}: {ex.Message}");
            }
        }
        
        private string ProcessMathExpression(string text)
        {
            try
            {
                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    string cleaned = CleanMathString(line);
                    
                    // Console.WriteLine($"[DEBUG] Line: '{line}' -> Cleaned: '{cleaned}'");

                    if (IsMathEquation(cleaned))
                    {
                        return EvaluateMathExpression(cleaned);
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
        
        private string CleanMathString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            // استبدال الرموز البصرية بالرموز القياسية
            // نستخدم Unicode escapes لضمان الدقة
            string text = input.Replace("\u00D7", "*")  // × Multiplication Sign
                               .Replace("×", "*")       // Literal just in case
                               .Replace("\u2715", "*")  // ✕ Multiplication X
                               .Replace("\u2716", "*")  // ✖ Heavy Multiplication X
                               .Replace("x", "*")       // Letter x (lowercase) - risky but users might use it
                               .Replace("X", "*")       // Letter X (uppercase)
                               .Replace("÷", "/")
                               .Replace("\u00F7", "/")  // ÷ Division Sign
                               .Replace(":", "/") 
                               .Replace("−", "-")       // Minus sign
                               .Replace("\u2212", "-"); // Minus sign unicode

            // تنظيف النص من أي أحرف غير مرئية أو تحكم
            // السماح فقط بالأرقام والعمليات والنقطة العشرية والمسافات
            string allowedChars = "0123456789+-*/. ";
            string result = "";
            foreach (char c in text)
            {
                if (allowedChars.Contains(c))
                {
                    result += c;
                }
            }
            return result.Trim();
        }

        private bool IsMathEquation(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            
            // يجب أن يحتوي على رقم واحد على الأقل وعملية حسابية واحدة
            bool hasDigit = Regex.IsMatch(text, @"\d");
            bool hasOp = text.Contains("+") || text.Contains("-") || text.Contains("*") || text.Contains("/");
            
            return hasDigit && hasOp;
        }
        
        private string EvaluateMathExpression(string expression)
        {
            try
            {
                // تقييم تعبير رياضي بسيط (بدون أقواس معقدة للدقة)
                // نستخدم طريقة بسيطة للأولويات: الضرب والقسمة أولاً، ثم الجمع والطرح
                
                // 1. تقسيم النص إلى رموز
                var tokens = Tokenize(expression);
                if (tokens.Count == 0) return null;

                // 2. معالجة الضرب والقسمة
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (tokens[i] == "*" || tokens[i] == "/")
                    {
                        double left = double.Parse(tokens[i - 1], CultureInfo.InvariantCulture);
                        double right = double.Parse(tokens[i + 1], CultureInfo.InvariantCulture);
                        double res = tokens[i] == "*" ? left * right : left / right;
                        
                        tokens[i - 1] = res.ToString(CultureInfo.InvariantCulture);
                        tokens.RemoveAt(i);
                        tokens.RemoveAt(i);
                        i--;
                    }
                }

                // 3. معالجة الجمع والطرح
                double finalResult = double.Parse(tokens[0], CultureInfo.InvariantCulture);
                for (int i = 1; i < tokens.Count; i += 2)
                {
                    string op = tokens[i];
                    double val = double.Parse(tokens[i + 1], CultureInfo.InvariantCulture);
                    
                    if (op == "+") finalResult += val;
                    else if (op == "-") finalResult -= val;
                }

                return finalResult.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private List<string> Tokenize(string expression)
        {
            var tokens = new List<string>();
            string currentNumber = "";
            
            foreach (char c in expression)
            {
                if (char.IsDigit(c) || c == '.')
                {
                    currentNumber += c;
                }
                else if ("+-*/".Contains(c))
                {
                    if (!string.IsNullOrEmpty(currentNumber))
                    {
                        tokens.Add(currentNumber);
                        currentNumber = "";
                    }
                    tokens.Add(c.ToString());
                }
            }
            
            if (!string.IsNullOrEmpty(currentNumber))
            {
                tokens.Add(currentNumber);
            }
            
            return tokens;
        }

        public async Task StopAsync()
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            _client.Messaging.OnGroupMessage -= HandleMessage;
            
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
            _waitingForRoundEnd = false;
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
