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
            { "39369782", "اسرق" },
            { "45578849", "بطل" },
            { "26494626", "وقت" },
            { "75423789", "عكس" },
            { "36828201", "احسب" },
            { "24062011", "كتابة" },
            { "80277459", "سباق" }
        };

        // Race Feature - Session Logic
        private RaceSession? _raceSession;
        private const string RaceBotId = "80277459";
        
        // Race Config Commands
        private string _cmdRaceEnergy = "!س طاقه";
        private string _cmdRaceGrind = "!س جلد";
        private string _cmdRaceTrain = "!س تدريب كل";
        private string _cmdRaceAlert = "!س تنبية طاقة";

        // Configuration
        private Dictionary<string, BotConfig> _botConfigs = new Dictionary<string, BotConfig>();
        private int _delaySeconds = 10; // Default delay
        private string _targetGroupId = "0"; // Default invalid group
        private const string ConfigFileName = "monitor_config.json";

        public virtual string Name => "👁️ المراقب";
        public virtual string Description => "مراقبة المعززات (صيد، صياد، ...) والمشاركة تلقائياً";
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
                        _targetGroupId = !string.IsNullOrEmpty(configData.TargetGroupId) ? configData.TargetGroupId : "0";
                        
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
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config: {ex.Message}");
            }
        }
        
        // Helper to update config
        public static void UpdateTargetGroupId(string newGroupId)
        {
            try
            {
                MonitorConfigData configData;
                if (File.Exists(ConfigFileName))
                {
                    string json = File.ReadAllText(ConfigFileName);
                    configData = JsonConvert.DeserializeObject<MonitorConfigData>(json) ?? new MonitorConfigData();
                }
                else
                {
                    configData = new MonitorConfigData();
                }

                configData.TargetGroupId = newGroupId;

                string output = JsonConvert.SerializeObject(configData, Formatting.Indented);
                File.WriteAllText(ConfigFileName, output);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving config: {ex.Message}");
            }
        }
        
        // Helper to check if Target Group ID is missing or invalid
        public static bool IsTargetGroupMissing()
        {
            try
            {
                if (File.Exists(ConfigFileName))
                {
                    string json = File.ReadAllText(ConfigFileName);
                    var configData = JsonConvert.DeserializeObject<MonitorConfigData>(json);
                    
                    if (configData != null && !string.IsNullOrEmpty(configData.TargetGroupId) && configData.TargetGroupId != "0")
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return true;
            }
        }

        public async Task StartAsync(string email, string password, string groupId, string targetUserId)
        {
            _client = new WolfClient();
            await _client.Login(email, password);
            
            await StartAsync(email, password, _client);
            
            // If groupId is explicitly provided (and not "0"), use it
            if (!string.IsNullOrEmpty(groupId) && groupId != "0")
            {
                _targetGroupId = groupId;
                await _client.JoinGroup(_targetGroupId);
            }
        }
        
        
        private void OnGroupMessage(IWolfClient client, Message msg, GroupUser user)
        {
            if (!_isRunning) return;

            // Race Logic
            if (_raceSession != null)
            {
                 _raceSession.HandleGroupMessage(msg);
            }

            ProcessMessageContent(msg.Content, msg.UserId, msg.IsGroup);
        }

        private void OnPrivateMessage(IWolfClient client, Message msg, User user)
        {
             if (!_isRunning) return;

             // Delegate to Race Session if active and message is from Race Bot
             if (_raceSession != null && msg.UserId == RaceBotId)
             {
                 _raceSession.HandlePrivateMessage(msg.Content);
                 return;
             }

             ProcessMessageContent(msg.Content, msg.UserId, msg.IsGroup);
        }

        public async Task StartAsync(string email, string password, WolfClient client)
        {
            _client = client;
            
            // Re-load config to ensure fresh data
            LoadConfiguration();

            _isRunning = true;
            _processedMessages.Clear();

            _client.Messaging.OnGroupMessage += OnGroupMessage;
            _client.Messaging.OnPrivateMessage += OnPrivateMessage;

            // Start processing queue
            _ = Task.Run(ProcessQueue);
            
            // Join target group if set
            if (_targetGroupId != "0" && !string.IsNullOrEmpty(_targetGroupId))
            {
                await _client.JoinGroup(_targetGroupId);
            }

            OnLog?.Invoke($"Monitor Bot Started for {email}");
        }

        public async Task StopAsync()
        {
            _isRunning = false;
            StopRaceSession(); // Ensure session is cleared
            if (_client != null)
            {
                try {
                     // محاولة تسجيل خروج نظامي قبل قطع الاتصال
                     await _client.Emit(new Packet("private logout", null));
                     await Task.Delay(500); // مهلة قصيرة لإرسال الباكت
                } catch {}

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
            if (!_isRunning || _client == null) return;
            
            // Create a completely new, isolated session
            _raceSession = new RaceSession(
                _client, 
                (action) => _globalQueue.Enqueue((RaceBotId, action)), 
                rounds, 
                training, 
                groupId,
                _cmdRaceAlert,
                _cmdRaceEnergy,
                _cmdRaceGrind,
                _cmdRaceTrain
            );

            Console.WriteLine($"🏁 بدء جلسة سباق جديدة (معزولة): {rounds} جولات، تدريب: {training}");
            _raceSession.Start();
        }

        public void StopRaceSession()
        {
            if (_raceSession != null)
            {
                _raceSession = null; // Dispose/Clear session
                Console.WriteLine("🛑 تم إيقاف وضع السباق.");
            }
        }

        public void ResetCounters()
        {
            _playCount = 0;
            _processedMessages.Clear();
            StopRaceSession();
        }

        public void SimulateMessage(string content, string userId, string groupId)
        {
            ProcessMessageContent(content, userId, false);
        }
        


        private void ProcessMessageContent(string content, string userId, bool isGroup)
        {
            // If in Race Mode, do NOT process monitor messages
            if (_raceSession != null) return;

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
                        
                        if (_raceSession == null)
                        {
                            Console.WriteLine($"⏳ انتظار {_delaySeconds} ثواني قبل العملية التالية...");
                            await Task.Delay(_delaySeconds * 1000); 
                        }
                        else
                        {
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
                    await Task.Delay(100);
                }
            }
        }

        private class BotConfig
        {
            public string Name { get; set; } = "";
            public string Command { get; set; } = "";
        }
        
        // --- Isolated Race Session Class ---
        private class RaceSession
        {
            private readonly WolfClient _client;
            private readonly Action<Func<Task>> _enqueueAction;
            
            // State
            private readonly int _totalRounds;
            private readonly bool _isTrainingEnabled;
            private readonly string _targetGroupId;
            private int _currentRound;
            private bool _isWaitingForRaceEnd;
            
            // Commands
            private readonly string _cmdAlert;
            private readonly string _cmdEnergy;
            private readonly string _cmdGrind;
            private readonly string _cmdTrain;
            
            private const string RaceBotId = "80277459";

            public RaceSession(
                WolfClient client, 
                Action<Func<Task>> enqueueAction, 
                int rounds, 
                bool training, 
                string groupId,
                string cmdAlert,
                string cmdEnergy,
                string cmdGrind,
                string cmdTrain)
            {
                _client = client;
                _enqueueAction = enqueueAction;
                _totalRounds = rounds;
                _isTrainingEnabled = training;
                _targetGroupId = (string.IsNullOrEmpty(groupId) || groupId == "0") ? "" : groupId;
                
                _cmdAlert = cmdAlert;
                _cmdEnergy = cmdEnergy;
                _cmdGrind = cmdGrind;
                _cmdTrain = cmdTrain;
                
                _currentRound = 0;
                _isWaitingForRaceEnd = false;
            }

            public void Start()
            {
                if (string.IsNullOrEmpty(_targetGroupId))
                {
                    Console.WriteLine("⚠️ تحذير: لم يتم تحديد مجموعة للسباق!");
                    return;
                }

                // Initial Check: Alert Settings
                _enqueueAction(async () =>
                {
                    Console.WriteLine("🔔 التحقق من إعدادات التنبيه...");
                    await _client.PrivateMessage(RaceBotId, _cmdAlert);
                });
            }

            public void HandlePrivateMessage(string content)
            {
                // 1. Alert Status Check
                if (content.Contains("ستصلك تنبيهات"))
                {
                    Console.WriteLine("✅ التنبيهات مفعلة. التحقق من الطاقة...");
                    _enqueueAction(async () =>
                    {
                        await Task.Delay(1000);
                        await _client.PrivateMessage(RaceBotId, _cmdEnergy);
                    });
                    return;
                }
                else if (content.Contains("لن تصلك تنبيهات"))
                {
                    Console.WriteLine("⚠️ التنبيهات غير مفعلة. جاري التفعيل...");
                    _enqueueAction(async () =>
                    {
                        await Task.Delay(2000);
                        await _client.PrivateMessage(RaceBotId, _cmdAlert);
                    });
                    return;
                }

                // 2. Energy Check -> Start Round
                if (content.Contains("100%")) 
                {
                     Console.WriteLine("🔋 الطاقة كاملة (100%). بدء الجولة...");
                     StartRound();
                }
                // 3. Training Complete -> Restart
                else if (content.Contains("عاد حيوانك لطاقته الكاملة"))
                {
                    Console.WriteLine("💪 اكتمل التدريب. بدء دورة جديدة...");
                    _currentRound = 0;
                    StartRound();
                }
            }

            public void HandleGroupMessage(Message msg)
            {
                if (msg.GroupId != _targetGroupId) return;

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
                        
                        _enqueueAction(async () =>
                        {
                            await Task.Delay(2000);
                            await _client.GroupMessage(_targetGroupId, _cmdGrind);
                        });
                        return;
                    }

                    _currentRound++;
                    // Note: We don't increment parent play count here to avoid shared state issues, 
                    // or we could expose an event. For now, we focus on isolation.

                    if (_currentRound < _totalRounds)
                    {
                        Console.WriteLine($"🔄 الجولة {_currentRound + 1} من {_totalRounds}. تكرار السباق...");
                         _enqueueAction(async () =>
                        {
                            await Task.Delay(2000);
                            await _client.GroupMessage(_targetGroupId, _cmdGrind);
                        });
                    }
                    else
                    {
                        Console.WriteLine("🛑 انتهت جميع الجولات.");
                        
                        if (_isTrainingEnabled && _totalRounds < 5)
                        {
                            int percentageNeeded = 100 - (_totalRounds * 20);
                            string trainCmd = $"{_cmdTrain} {percentageNeeded}";
                            
                            Console.WriteLine($"🏋️ إرسال أمر التدريب: {trainCmd}");
                            
                            _enqueueAction(async () =>
                            {
                                await _client.PrivateMessage(RaceBotId, trainCmd);
                            });
                        }
                        else
                        {
                            Console.WriteLine("⚠️ لا يوجد تدريب مطلوب. انتظار...");
                        }
                    }
                }
            }

            private void StartRound()
            {
                if (string.IsNullOrEmpty(_targetGroupId)) return;

                _enqueueAction(async () =>
                {
                    Console.WriteLine($"🏎️ إرسال أمر السباق للمجموعة {_targetGroupId}...");
                    try 
                    {
                        if (int.TryParse(_targetGroupId, out int gid))
                        {
                            await _client.Emit(new Packet("group join", new { id = gid, password = "" }));
                            await Task.Delay(500);
                            await _client.GroupMessage(_targetGroupId, _cmdGrind);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ خطأ أثناء بدء جولة السباق: {ex.Message}");
                    }
                });
            }
        }
    }
}
