using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WolfLive.Api;
using WolfLive.Api.Models;

namespace TelegramBotController
{
    public class MonitorBot : IBot
    {
        private WolfClient? _client;
        private bool _isRunning;
        private int _playCount;
        private readonly HashSet<string> _monitoredSenders = new HashSet<string>();
        private readonly HashSet<string> _processedMessages = new HashSet<string>();
        private readonly object _processedLock = new object();
        private readonly ConcurrentQueue<(string SenderId, Func<Task> Action)> _globalQueue = new ConcurrentQueue<(string, Func<Task>)>();
        
        private readonly Dictionary<string, string> _knownBotIds = new Dictionary<string, string>
        {
            { "76305584", "صياد" },
            { "32060007", "صيد" },
            { "19121683", "اسرق" },
            { "45578849", "بطل" },
            { "26494626", "وقت" },
            { "75423789", "عكس" },
            { "36828201", "احسب" },
            { "24062011", "كتابة" },
            { "80277459", "سباق" }
        };

        // Race Feature Variables
        private volatile bool _isRaceMode = false;
        private int _totalRaceRounds = 0;
        private int _currentRaceRound = 0;
        private bool _isTrainingEnabled = false;
        private string _raceTargetGroupId = "";
        private const string RaceBotId = "80277459";
        
        // Race Config Commands
        private string _cmdRaceEnergy = "!س طاقه";
        private string _cmdRaceGrind = "!س جلد";
        private string _cmdRaceTrain = "!س تدريب كل";

        // Configuration
        private Dictionary<string, BotConfig> _botConfigs = new Dictionary<string, BotConfig>();
        private int _delaySeconds = 10; // Default delay
        private const string ConfigFileName = "monitor_config.json";

        public string Name => "🦅 بوت المراقبة";
        public string Description => "مراقبة المعززات (صيد، صياد، ...) والمشاركة تلقائياً";
        public bool IsRunning => _isRunning;
        public int PlayCount => _playCount;
        public IWolfClient? Client => _client;
        public event Action<string>? OnLog;

        public MonitorBot()
        {
            _playCount = 0;
            _isRunning = false;
            LoadConfiguration();
        }

        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(ConfigFileName))
                {
                    string json = File.ReadAllText(ConfigFileName);
                    var configData = JsonConvert.DeserializeObject<MonitorConfigData>(json);
                    
                    if (configData != null)
                    {
                        _delaySeconds = configData.DelaySeconds > 0 ? configData.DelaySeconds : 10;
                        
                        _botConfigs.Clear();
                        _monitoredSenders.Clear();
                        
                        if (configData.Phrases != null)
                        {
                            // Load Standard Bot Phrases
                            foreach (var kvp in _knownBotIds)
                            {
                                var id = kvp.Key;
                                var name = kvp.Value;
                                
                                var phrase = configData.Phrases.Find(p => p.Name == name);
                                if (phrase != null)
                                {
                                    _botConfigs[id] = new BotConfig { Name = phrase.Name, Command = phrase.Command };
                                    _monitoredSenders.Add(id);
                                }
                            }

                            // Load Race Phrases
                            var raceEnergy = configData.Phrases.Find(p => p.Name == "سباق_طاقة");
                            if (raceEnergy != null) _cmdRaceEnergy = raceEnergy.Command;

                            var raceGrind = configData.Phrases.Find(p => p.Name == "سباق_جلد");
                            if (raceGrind != null) _cmdRaceGrind = raceGrind.Command;

                            // Training command uses default or remains hardcoded as per request
                        }
                        
                        // Load Race Group ID
                        if (!string.IsNullOrEmpty(configData.TargetGroupId))
                        {
                            _raceTargetGroupId = configData.TargetGroupId;
                            // Console.WriteLine($"✅ تم تحميل مجموعة السباق من الملف: {_raceTargetGroupId}");
                        }
                        else
                        {
                            Console.WriteLine("⚠️ لم يتم العثور على مجموعة السباق في الملف.");
                        }

                        // Console.WriteLine($"✅ تم تحميل إعدادات المراقبة والسباق: {_botConfigs.Count} بوتات، تأخير {_delaySeconds} ثواني.");
                    }
                }
                else
                {
                    // Create default config if not exists
                    SaveDefaultConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ خطأ في تحميل ملف الإعدادات: {ex.Message}");
            }

            // Ensure we have at least the default bots if config failed or was empty
            if (_botConfigs.Count == 0)
            {
                foreach (var kvp in _knownBotIds)
                {
                    var id = kvp.Key;
                    var name = kvp.Value;
                    string defaultCommand = "!صياد 3";
                    
                    if (name == "صيد") defaultCommand = "!صياد جنوبية ٣";
                    else if (name == "اسرق") defaultCommand = "!اسرق 5";
                    else if (name == "بطل") defaultCommand = "!بطل 5";
                    
                    _botConfigs[id] = new BotConfig { Name = name, Command = defaultCommand };
                    _monitoredSenders.Add(id);
                }
            }
        }

        private void SaveDefaultConfig()
        {
            try
            {
                var data = new MonitorConfigData
                {
                    DelaySeconds = 10,
                    Phrases = new List<PhraseConfig>
                    {
                        new PhraseConfig { Name = "صياد", Command = "!صياد 3" },
                        new PhraseConfig { Name = "صيد", Command = "!صياد جنوبية ٣" },
                        new PhraseConfig { Name = "اسرق", Command = "!اسرق 5" },
                        new PhraseConfig { Name = "بطل", Command = "!بطل 5" }
                    }
                };
                
                File.WriteAllText(ConfigFileName, JsonConvert.SerializeObject(data, Formatting.Indented));
                Console.WriteLine("✅ تم إنشاء ملف إعدادات افتراضي.");
            }
            catch { }
        }

        public async Task StartAsync(string email, string password, string groupId, string targetUserId)
        {
            if (_isRunning) return;

            // Reload config on start to pick up any changes
            LoadConfiguration();

            try
            {
                _client = new WolfClient();
                var loginResult = await _client.Login(email, password);
                if (!loginResult) throw new Exception("فشل تسجيل الدخول");

                _isRunning = true;

                // Set target group if provided
                if (!string.IsNullOrEmpty(groupId) && groupId != "0")
                {
                    _raceTargetGroupId = groupId;
                    // Try to join the group in background
                    _ = Task.Run(async () => 
                    {
                        try 
                        {
                            if (int.TryParse(groupId, out int gid))
                                await _client.Emit(new Packet("group join", new { id = gid, password = "" }));
                        }
                        catch { }
                    });
                }
                
                try 
                {
                    _client.On<WolfMessage>("message send", OnMessageReceived);
                }
                catch
                {
                     _client.Messaging.OnPrivateMessage += HandlePrivateMessage;
                }

                // بدء معالجة الطابور
                _ = Task.Run(ProcessQueue);

                Console.WriteLine($"✅ {Name} - جاهز للعمل");
            }
            catch (Exception ex)
            {
                throw new Exception($"فشل بدء {Name}: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            _isRunning = false;
            if (_client != null)
            {
                try {
                     await _client.Connection.DisconnectAsync();
                } catch {}
                _client = null;
            }
        }

        public Task<bool> CheckConnectionAsync()
        {
            return Task.FromResult(_client != null && _client.Connection.Connected);
        }

        public Task<bool> JoinGroupAsync(string groupId)
        {
            return Task.FromResult(true);
        }

        public void StartRaceSession(int rounds, bool training, string groupId)
        {
            if (!_isRunning) return;
            
            _isRaceMode = true;
            _totalRaceRounds = rounds;
            _currentRaceRound = 0;
            _isTrainingEnabled = training;
            
            // If groupId is provided (not null/empty/"0"), update the target.
            // Otherwise, keep the one loaded from config.
            if (!string.IsNullOrEmpty(groupId) && groupId != "0")
            {
                _raceTargetGroupId = groupId;
            }
            
            Console.WriteLine($"🏁 بدء جلسة سباق: {rounds} جولات، تدريب: {training}، المجموعة: {_raceTargetGroupId}");
            
            if (string.IsNullOrEmpty(_raceTargetGroupId) || _raceTargetGroupId == "0")
            {
                Console.WriteLine("⚠️ تحذير: لم يتم تحديد مجموعة للسباق!");
            }
            
            // Start sequence: Check Energy via PM
            _globalQueue.Enqueue((RaceBotId, new Func<Task>(async () =>
            {
                Console.WriteLine("⚡ التحقق من الطاقة...");
                await _client.PrivateMessage(RaceBotId, _cmdRaceEnergy);
            })));
        }

        public void StopRaceSession()
        {
            _isRaceMode = false;
            _isWaitingForRaceEnd = false;
            Console.WriteLine("🛑 تم إيقاف وضع السباق.");
        }

        public void ResetCounters()
        {
            _playCount = 0;
            _processedMessages.Clear();
            _isRaceMode = false;
            _isWaitingForRaceEnd = false;
        }

        public void SimulateMessage(string content, string userId, string groupId)
        {
            ProcessMessageContent(content, userId, false);
        }
        
        private void HandlePrivateMessage(IWolfClient client, Message message, User user)
        {
             if (!_isRunning) return;

             // Handle Race Bot Messages
             if (_isRaceMode && message.UserId == RaceBotId)
             {
                 HandleRacePrivateMessage(message.Content);
                 return;
             }

             ProcessMessageContent(message.Content, message.UserId, message.IsGroup);
        }

        private void HandleRacePrivateMessage(string content)
        {
            // Check for Energy: "طاقة F35: 100%"
            if (content.Contains("100%")) 
            {
                 Console.WriteLine("🔋 الطاقة كاملة (100%). بدء الجولة...");
                 StartRaceRound();
            }
            // Check for Training Complete: "عاد حيوانك لطاقته الكاملة!"
            else if (content.Contains("عاد حيوانك لطاقته الكاملة"))
            {
                Console.WriteLine("💪 اكتمل التدريب. بدء دورة جديدة...");
                _currentRaceRound = 0; // Reset rounds for new loop
                StartRaceRound();
            }
        }

        private void StartRaceRound()
        {
            if (!_isRaceMode) return;
            
            // Fallback if empty
            if (string.IsNullOrEmpty(_raceTargetGroupId) || _raceTargetGroupId == "0")
            {
                 Console.WriteLine("⚠️ مجموعة السباق غير محددة! محاولة إعادة التحميل...");
                 LoadConfiguration();
                 // if (string.IsNullOrEmpty(_raceTargetGroupId)) _raceTargetGroupId = "18822804"; // Hard fallback removed
            }

            if (string.IsNullOrEmpty(_raceTargetGroupId))
            {
                 Console.WriteLine("❌ فشل تحديد مجموعة السباق. يرجى التحقق من monitor_config.json");
                 return;
            }

            _globalQueue.Enqueue((_raceTargetGroupId, new Func<Task>(async () =>
            {
                Console.WriteLine($"🏎️ إرسال أمر السباق للمجموعة {_raceTargetGroupId}...");
                try 
                {
                    if (int.TryParse(_raceTargetGroupId, out int gid))
                    {
                        await _client.Emit(new Packet("group join", new { id = gid, password = "" }));
                        await Task.Delay(500);
                        await _client.GroupMessage(_raceTargetGroupId, _cmdRaceGrind);
                    }
                    else
                    {
                         Console.WriteLine($"❌ معرف المجموعة غير صالح: {_raceTargetGroupId}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ خطأ أثناء بدء جولة السباق: {ex.Message}");
                }
            })));
        }

        // Race State
        private bool _isWaitingForRaceEnd = false;

        private void OnMessageReceived(WolfMessage wolfMsg)
        {
            if (!_isRunning) return;
            
            try 
            {
                var msg = new Message(wolfMsg);

                // Handle Race Group Messages
                if (_isRaceMode && msg.IsGroup && msg.GroupId == _raceTargetGroupId)
                {
                    // Check for "Cannot use command during race" error
                    if (msg.Content.Contains("لا يمكنك استخدام هذا الأمر أثناء السباق"))
                    {
                        Console.WriteLine("⚠️ سباق جارٍ بالفعل. انتظار انتهاء السباق الحالي...");
                        _isWaitingForRaceEnd = true;
                        return;
                    }

                    if (msg.Content.Contains("انتهى السباق وهذه النتائج النهائية"))
                    {
                        Console.WriteLine("🏁 انتهت جولة السباق.");

                        if (_isWaitingForRaceEnd)
                        {
                            Console.WriteLine("🔄 إعادة محاولة بدء السباق...");
                            _isWaitingForRaceEnd = false;
                            
                            // Retry the race command immediately
                            _globalQueue.Enqueue((_raceTargetGroupId, new Func<Task>(async () =>
                            {
                                await Task.Delay(2000); // Wait a bit
                                await _client.GroupMessage(_raceTargetGroupId, _cmdRaceGrind);
                            })));
                            return; // Don't process as a completed round yet
                        }

                        _currentRaceRound++;
                        _playCount++;

                        if (_currentRaceRound < _totalRaceRounds)
                        {
                            Console.WriteLine($"🔄 الجولة {_currentRaceRound + 1} من {_totalRaceRounds}. تكرار السباق...");
                            // Repeat race command
                             _globalQueue.Enqueue((_raceTargetGroupId, new Func<Task>(async () =>
                            {
                                await Task.Delay(2000); // Wait a bit
                                await _client.GroupMessage(_raceTargetGroupId, _cmdRaceGrind);
                            })));
                        }
                        else
                        {
                            Console.WriteLine("🛑 انتهت جميع الجولات.");
                            
                            // Training Logic
                            if (_isTrainingEnabled && _totalRaceRounds < 5)
                            {
                                int percentageNeeded = 100 - (_totalRaceRounds * 20);
                                string trainCmd = $"{_cmdRaceTrain} {percentageNeeded}";
                                
                                Console.WriteLine($"🏋️ إرسال أمر التدريب: {trainCmd}");
                                
                                _globalQueue.Enqueue((RaceBotId, new Func<Task>(async () =>
                                {
                                    await _client.PrivateMessage(RaceBotId, trainCmd);
                                })));
                            }
                            else
                            {
                                Console.WriteLine("⚠️ لا يوجد تدريب (إما غير مفعل أو الجولات = 5). انتظار دورة جديدة...");
                                // Note: Without training, we don't get the "Full Energy" message to trigger the loop.
                                // If the user wants to loop even without training (e.g. relying on natural regen), 
                                // we might need a timer, but the prompt says "wait until message arrives".
                            }
                        }
                    }
                }

                if (msg.UserId == RaceBotId && !msg.IsGroup && _isRaceMode)
                {
                     HandleRacePrivateMessage(msg.Content);
                     return;
                }

                ProcessMessageContent(msg.Content, msg.UserId, msg.IsGroup);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting message: {ex.Message}");
            }
        }

        private void ProcessMessageContent(string content, string userId, bool isGroup)
        {
            // If in Race Mode, do NOT process monitor messages
            if (_isRaceMode) return;

            try
            {
                if (isGroup) return;
                if (!_monitoredSenders.Contains(userId)) return;

                var contentHash = content?.GetHashCode() ?? 0;
                var uniqueKey = $"{userId}_{DateTime.Now.Ticks}_{contentHash}";

                lock (_processedLock)
                {
                    if (_processedMessages.Contains(uniqueKey)) return;
                    _processedMessages.Add(uniqueKey);

                    if (_processedMessages.Count > 10000) _processedMessages.Clear();
                }

                var match = Regex.Match(content ?? "", @"\[(.*?)\] \((\d+)\)");

                if (match.Success)
                {
                    var groupName = match.Groups[1].Value;
                    var targetGroupId = match.Groups[2].Value;

                    if (targetGroupId == "9677")
                    {
                        Console.WriteLine($"تم تجاهل رسالة للمجموعة المستثناة {targetGroupId}");
                        return;
                    }

                    if (_botConfigs.TryGetValue(userId, out var config))
                    {
                        Console.WriteLine($"⚡ تم رصد رسالة من {config.Name}: المجموعة {targetGroupId}");

                        OnLog?.Invoke($"قناة [{groupName}]  بوت {config.Name}");

                        _globalQueue.Enqueue((userId, new Func<Task>(async () =>
                        {
                            await PerformAction(userId, targetGroupId, config.Command);
                        })));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"خطأ في معالجة الرسالة: {ex.Message}");
            }
        }

        private async Task PerformAction(string senderId, string groupId, string command)
        {
            try
            {
                await _client.Emit(new Packet("group join", new { id = int.Parse(groupId), password = "" }));
                await Task.Delay(500);
                await _client.GroupMessage(groupId, command);
                _playCount++;
                
                Console.WriteLine($"رساله من المجموعه {groupId} {command} تم بنجااح");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ فشل تنفيذ العملية للبوت {senderId}: {ex.Message}");
            }
        }

        private async Task ProcessQueue()
        {
            while (_isRunning)
            {
                if (_globalQueue.TryDequeue(out var item))
                {
                    try
                    {
                        await item.Action();
                        
                        // تطبيق التأخير فقط في وضع المراقبة (وليس السباق)
                        if (!_isRaceMode)
                        {
                            Console.WriteLine($"⏳ انتظار {_delaySeconds} ثواني قبل العملية التالية...");
                            await Task.Delay(_delaySeconds * 1000); 
                        }
                        else
                        {
                            // تأخير بسيط جداً في وضع السباق لمنع الضغط الزائد
                            await Task.Delay(100);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error executing queued action: {ex.Message}");
                    }
                }
                else
                {
                    await Task.Delay(100); // Check queue every 100ms if empty
                }
            }
        }
        
        
        private class BotConfig
        {
            public string Name { get; set; } = "";
            public string Command { get; set; } = "";
        }
    }
}
