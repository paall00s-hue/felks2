using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBotController
{
    public class TelegramController : IDisposable
    {
        private readonly TelegramBotClient _botClient;
        private readonly BotManager _botManager;
        private readonly ConcurrentDictionary<long, UserSession> _userSessions;
        private readonly CancellationTokenSource _cts;
        private bool _isDisposed;
        
        public TelegramController(string botToken)
        {
            _botClient = new TelegramBotClient(botToken);
            _botManager = new BotManager();
            _userSessions = new ConcurrentDictionary<long, UserSession>();
            _cts = new CancellationTokenSource();
            
            _botManager.OnBotEvent += HandleBotEvent;
            _botManager.OnNotification += HandleNotification;
            
            Console.WriteLine("🤖 بوت التليجرام جاهز");
        }
        
        public async Task StartAsync()
        {
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };
            
            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: _cts.Token
            );
            
            var me = await _botClient.GetMe();
            Console.WriteLine($"✅ بوت التليجرام يعمل: @{me.Username}");
            
            try 
            {
                // تنظيف الملفات المؤقتة عند بدء التشغيل لضمان عدم استهلاك مساحة
                if (Directory.Exists("temp")) Directory.Delete("temp", true);
                var logFiles = Directory.GetFiles(".", "*.log");
                foreach (var file in logFiles)
                {
                    // الاحتفاظ فقط بملف الأخطاء الهام، وحذف الباقي
                    if (!file.EndsWith("error.log"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }

                // تم تحويل البوت للعمل على المكتبة الحقيقية
                // انتظر حتى الإلغاء
                await Task.Delay(-1, _cts.Token);
            }
            catch (TaskCanceledException)
            {
                // تم الإيقاف
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ خطأ في حلقة الانتظار: {ex.Message}");
            }
            
            Console.WriteLine("⚠️ توقف البوت عن العمل.");
        }
        
        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Message is { } message)
                {
                    await HandleMessageAsync(message);
                }
                else if (update.CallbackQuery is { } callbackQuery)
                {
                    await HandleCallbackQueryAsync(callbackQuery);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ خطأ في معالجة التحديث: {ex.Message}");
            }
        }
        
        private async Task HandleMessageAsync(Message message)
        {
            var chatId = message.Chat.Id;
            var userId = message.From.Id;
            
            if (!_userSessions.TryGetValue(userId, out UserSession? session))
            {
                session = new UserSession { UserId = userId, ChatId = chatId, State = SessionState.Start };
                _userSessions[userId] = session;
            }
            
            // معالجة أمر البداية بشكل عام لتصفير الحالة
            if (message.Text?.Trim().Equals("/start", StringComparison.OrdinalIgnoreCase) == true)
            {
                session.State = SessionState.Start;
                await ShowStartMenu(chatId);
                return;
            }
            
            switch (session.State)
            {
                case SessionState.Start:
                    await ShowStartMenu(chatId);
                    break;
                    
                case SessionState.WaitingForEmail:
                    session.Email = message.Text?.Trim();
                    await _botClient.SendMessage(chatId, "🔐 أرسل كلمة المرور:");
                    session.State = SessionState.WaitingForPassword;
                    break;

                case SessionState.WaitingForPassword:
                    session.Password = message.Text.Trim();
                    await _botClient.SendMessage(chatId, "⏳ جاري تسجيل الدخول...");
                    
                    try
                    {
                        var botId = await _botManager.StartBot(session.Email!, session.Password);
                        session.ActiveBotId = botId;
                        
                        if (session.Mode == WorkMode.DeleteMessages)
                        {
                            await _botClient.SendMessage(chatId, "✅ تم تسجيل الدخول بنجاح.\n📂 أدخل رقم المجموعة (Group ID) التي تريد تفعيل الحذف فيها:");
                            session.State = SessionState.WaitingForDeleteGroupId;
                        }
                        else
                        {
                            // Normal Mode
                            await _botClient.SendMessage(chatId, "✅ تم تسجيل الدخول بنجاح!");
                            session.State = SessionState.WaitingForBotSelection;
                            await AskForBotSelection(chatId, session);
                        }
                    }
                    catch (Exception ex)
                    {
                        await _botClient.SendMessage(chatId, $"❌ فشل تسجيل الدخول: {ex.Message}\nحاول مرة أخرى (أدخل البريد):");
                        session.State = SessionState.WaitingForEmail;
                    }
                    break;

                case SessionState.WaitingForBotSelection:
                    await HandleBotSelectionAsync(message.Text, session, chatId);
                    break;

                // --- Account Manager States ---
                case SessionState.Acc_Add_Email:
                    session.TempEmail = message.Text?.Trim();
                    await _botClient.SendMessage(chatId, "🔐 أرسل كلمة المرور:");
                    session.State = SessionState.Acc_Add_Pass;
                    break;

                case SessionState.Acc_Add_Pass:
                    session.TempPassword = message.Text?.Trim();
                    session.State = SessionState.Acc_Add_Type;
                    await AskForBotSelection(chatId, session);
                    break;

                case SessionState.Acc_Add_Group:
                    session.TempGroupId = message.Text?.Trim();
                    // إذا كان البوت هو "وقت"، نحتاج معرف الهدف
                    if (session.TempBotType == "وقت")
                    {
                        await _botClient.SendMessage(chatId, "🎯 أدخل معرف المستخدم المستهدف (Target User ID) للرد عليه، أو أرسل 0 للرد على الجميع:");
                        session.State = SessionState.Acc_Add_TargetUser;
                    }
                    else
                    {
                        session.TempTargetUserId = "0";
                        await StartNewAccount(chatId, userId, session);
                    }
                    break;

                case SessionState.Acc_Add_TargetUser:
                    session.TempTargetUserId = message.Text?.Trim();
                    await StartNewAccount(chatId, userId, session);
                    break;

                // --- States for Group Joiner ---
                case SessionState.WaitingForJoinEmail:
                    if (!message.Text.Contains("#"))
                    {
                        await _botClient.SendMessage(chatId, "❌ يجب أن يحتوي الإيميل على علامة # لاستبدالها بالأرقام.\nمثال: `User#@gmail.com`\nحاول مرة أخرى:");
                        return;
                    }
                    session.JoinEmailPattern = message.Text.Trim();
                    await _botClient.SendMessage(chatId, "🔐 أدخل كلمة المرور الموحدة للحسابات:");
                    session.State = SessionState.WaitingForJoinPassword;
                    break;

                case SessionState.WaitingForJoinPassword:
                    session.JoinPassword = message.Text.Trim();
                    await _botClient.SendMessage(chatId, "🔢 أدخل رقم البداية (مثلاً: 1):");
                    session.State = SessionState.WaitingForJoinStart;
                    break;

                case SessionState.WaitingForJoinStart:
                    if (int.TryParse(message.Text.Trim(), out int startNum))
                    {
                        session.JoinStart = startNum;
                        await _botClient.SendMessage(chatId, "🔢 أدخل رقم النهاية (مثلاً: 30):");
                        session.State = SessionState.WaitingForJoinEnd;
                    }
                    else
                    {
                        await _botClient.SendMessage(chatId, "❌ الرجاء إدخال رقم صحيح.");
                    }
                    break;

                case SessionState.WaitingForJoinEnd:
                    if (int.TryParse(message.Text.Trim(), out int endNum))
                    {
                        session.JoinEnd = endNum;
                        await _botClient.SendMessage(chatId, "🆔 أدخل معرفات المجموعات (IDs) مفصولة بفواصل (مثلاً: 12345,67890):");
                        session.State = SessionState.WaitingForJoinGroups;
                    }
                    else
                    {
                        await _botClient.SendMessage(chatId, "❌ الرجاء إدخال رقم صحيح.");
                    }
                    break;

                case SessionState.WaitingForJoinGroups:
                    var ids = message.Text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (ids.Count == 0)
                    {
                        await _botClient.SendMessage(chatId, "❌ الرجاء إدخال معرف واحد على الأقل.");
                        return;
                    }
                    session.JoinGroups = ids;
                    
                    if (session.IsJoiningMode)
                    {
                        var msgKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("نعم", "join_msg_yes"), InlineKeyboardButton.WithCallbackData("لا", "join_msg_no") }
                        });
                        await _botClient.SendMessage(chatId, "💬 هل تريد إرسال رسالة بعد الانضمام؟", replyMarkup: msgKeyboard);
                        session.State = SessionState.WaitingForJoinMessageOption;
                    }
                    else
                    {
                        await StartJoinerProcess(chatId, session);
                    }
                    break;

                case SessionState.WaitingForJoinMessageContent:
                    session.JoinMessageContent = message.Text;
                    await _botClient.SendMessage(chatId, "🔢 كم مرة تريد تكرار الرسالة؟ (أدخل رقم، مثلاً 3):");
                    session.State = SessionState.WaitingForJoinMessageCount;
                    break;

                case SessionState.WaitingForJoinMessageCount:
                    if (int.TryParse(message.Text, out int count) && count > 0)
                    {
                        session.JoinMessageCount = count;
                        await StartJoinerProcess(chatId, session);
                    }
                    else
                    {
                        await _botClient.SendMessage(chatId, "❌ الرجاء إدخال رقم صحيح أكبر من 0.");
                    }
                    break;

                case SessionState.WaitingForDeleteGroupId:
                    session.TempGroupId = message.Text.Trim();
                    await _botClient.SendMessage(chatId, "🆔 أرسل الـ ID الخاص بالمستخدم الذي تريد حذف رسائله تلقائياً:");
                    session.State = SessionState.WaitingForDeleteUserId;
                    break;

                case SessionState.WaitingForDeleteUserId:
                    session.TempTargetUserId = message.Text.Trim();
                    await _botClient.SendMessage(chatId, "⏱️ أدخل وقت الانتظار قبل الحذف بالثواني (0 - 5):");
                    session.State = SessionState.WaitingForDeleteDelay;
                    break;

                case SessionState.WaitingForDeleteDelay:
                    if (int.TryParse(message.Text.Trim(), out int delaySeconds) && delaySeconds >= 0 && delaySeconds <= 5)
                    {
                        var deleteGroupId = session.TempGroupId;
                        var deleteTargetId = session.TempTargetUserId;

                        if (string.IsNullOrEmpty(deleteGroupId) || string.IsNullOrEmpty(session.ActiveBotId))
                        {
                            await _botClient.SendMessage(chatId, "❌ حدث خطأ، يرجى إعادة المحاولة.");
                            session.State = SessionState.Start;
                            await ShowStartMenu(chatId);
                            return;
                        }

                        var deleteResult = await _botManager.StartAutoDelete(session.ActiveBotId, deleteGroupId, deleteTargetId, delaySeconds);
                        await _botClient.SendMessage(chatId, deleteResult);
                        session.State = SessionState.Start;
                        session.Mode = WorkMode.Normal;
                        await ShowStartMenu(chatId);
                    }
                    else
                    {
                        await _botClient.SendMessage(chatId, "❌ الرجاء إدخال رقم صحيح بين 0 و 5.");
                    }
                    break;
            }
        }

        private async Task StartJoinerProcess(long chatId, UserSession session)
        {
             // Start the process
            var joiner = new GroupJoiner(_botClient, chatId);

            // Capture variables to avoid race conditions when resetting session
            var emailPattern = session.JoinEmailPattern;
            var password = session.JoinPassword;
            var startNum = session.JoinStart;
            var endNum = session.JoinEnd;
            var groups = session.JoinGroups;
            var isJoining = session.IsJoiningMode;
            var msgContent = session.SendMessageAfterJoin ? session.JoinMessageContent : null;
            var msgCount = session.SendMessageAfterJoin ? session.JoinMessageCount : 0;

            // Run in background to not block the bot
            _ = Task.Run(async () => {
                try
                {
                    await joiner.ProcessAccountsAsync(
                        emailPattern, 
                        password, 
                        startNum, 
                        endNum, 
                        groups, 
                        isJoining,
                        msgContent,
                        msgCount
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in background process: {ex.Message}");
                }
                finally
                {
                    await ShowStartMenu(chatId);
                }
            });

            // Reset state
            session.State = SessionState.Start;
            // Reset temp fields
            session.SendMessageAfterJoin = false;
            session.JoinMessageContent = null;
            session.JoinMessageCount = 0;

            await _botClient.SendMessage(chatId, "✅ تم بدء العملية في الخلفية. ستصلك التحديثات.");
        }
        
        private async Task HandleBotSelectionAsync(string? messageText, UserSession session, long chatId)
        {
            // This method was referenced but missing. 
            // It seems it was intended to handle manual text input for bot selection or configuration.
            // For now, we'll just re-show the selection menu if text is sent instead of clicking buttons.
            await AskForBotSelection(chatId, session);
        }

        private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery)
        {
            var chatId = callbackQuery.Message.Chat.Id;
            var userId = callbackQuery.From.Id;
            var data = callbackQuery.Data;
            
            try
            {
                await _botClient.AnswerCallbackQuery(callbackQuery.Id);
            }
            catch
            {
                // Ignore "query is too old" errors
            }
            
            if (!_userSessions.TryGetValue(userId, out UserSession? session))
                return;
            
            if (data == "start" || data == "add_account")
            {
                // التحقق من عدد البوتات النشطة
                int activeCount = _botManager.GetUserBotCount(userId.ToString());
                if (activeCount >= 5)
                {
                    await _botClient.SendMessage(chatId, "⚠️ لقد وصلت للحد الأقصى (5 حسابات). يرجى إيقاف أحد الحسابات لإضافة جديد.");
                    return;
                }

                await _botClient.SendMessage(chatId, "📧 أدخل البريد الإلكتروني للحساب الجديد:");
                session.State = SessionState.Acc_Add_Email;
            }
            else if (data == "list_active")
            {
                await ShowAccountsMenu(chatId);
            }
            else if (data == "bot_سباق")
            {
                try { await _botClient.DeleteMessage(chatId, callbackQuery.Message.MessageId); } catch { }

                // Check if target group ID is configured in monitor_config.json
                string? defaultGroupId = null;
                try
                {
                    if (System.IO.File.Exists("monitor_config.json"))
                    {
                        var json = System.IO.File.ReadAllText("monitor_config.json");
                        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);
                        defaultGroupId = config?.TargetGroupId;
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(defaultGroupId))
                {
                     await _botClient.SendMessage(chatId, "❌ يجب تحديد معرف مجموعة السباق في ملف monitor_config.json أولاً.");
                }

                if (session.State == SessionState.Acc_Add_Type)
                {
                    session.TempBotType = "سباق";
                    session.TempGroupId = defaultGroupId ?? "0";
                    session.TempTargetUserId = "0";
                }
                else
                {
                    session.SelectedBot = "سباق";
                    session.GroupId = defaultGroupId ?? "0";
                    session.TargetUserId = "0";
                }
                
                // Ask for Race options BEFORE starting the bot
                var roundsKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("1", "pre_race_rounds_1"), InlineKeyboardButton.WithCallbackData("2", "pre_race_rounds_2"), InlineKeyboardButton.WithCallbackData("3", "pre_race_rounds_3") },
                    new[] { InlineKeyboardButton.WithCallbackData("4", "pre_race_rounds_4"), InlineKeyboardButton.WithCallbackData("5", "pre_race_rounds_5") }
                });
                await _botClient.SendMessage(chatId, "🏁 اختر عدد الجولات:", replyMarkup: roundsKeyboard);
            }
            else if (data.StartsWith("pre_race_rounds_"))
            {
                session.RaceRounds = int.Parse(data.Substring(16));
                
                var trainingKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("نعم (تدريب)", "pre_race_train_yes"), InlineKeyboardButton.WithCallbackData("لا (بدون)", "pre_race_train_no") }
                });
                
                await _botClient.EditMessageText(chatId, callbackQuery.Message.MessageId, $"🏁 عدد الجولات: {session.RaceRounds}\n🏋️ هل تريد تفعيل التدريب؟", replyMarkup: trainingKeyboard);
            }
            else if (data.StartsWith("pre_race_train_"))
            {
                bool training = data == "pre_race_train_yes";
                
                if (session.State == SessionState.Acc_Add_Type || !string.IsNullOrEmpty(session.TempBotType))
                {
                    await SendAndScheduleDeletion(chatId, $"⏳ جاري تشغيل حساب {session.TempBotType}...", 3000);
            
                    var result = await _botManager.StartBot(
                        session.TempBotType ?? "سباق",
                        session.TempEmail!,
                        session.TempPassword!,
                        session.TempGroupId ?? "0",
                        session.TempTargetUserId ?? "0",
                        userId.ToString()
                    );
                    
                    if (result.Success)
                    {
                        var raceStatus = await _botManager.StartRaceMode(result.BotId, session.RaceRounds, training, session.TempGroupId ?? "0");
                        
                        session.TempEmail = null;
                        session.TempPassword = null;
                        session.TempBotType = null;
                        session.TempGroupId = null;
                        session.TempTargetUserId = null;
                        
                        session.ActiveBotId = result.BotId;
                        session.State = SessionState.Start;
                        
                         await _botClient.EditMessageText(chatId, callbackQuery.Message.MessageId, 
                             $"✅ تم تشغيل الحساب بنجاح!\nالبوت: {result.BotName}\n🚀 السباق: {(raceStatus ? "بدأ" : "فشل البدء")}\nالجولات: {session.RaceRounds}");
                         
                         await ShowAccountsMenu(chatId, userId);
                    }
                    else
                    {
                        await _botClient.EditMessageText(chatId, callbackQuery.Message.MessageId, $"❌ فشل التشغيل: {result.Error}");
                    }
                }
                else
                {
                    await StartSelectedBot(chatId, userId, session);
                    
                    if (session.State == SessionState.BotActive && !string.IsNullOrEmpty(session.ActiveBotId))
                    {
                        var status = await _botManager.StartRaceMode(session.ActiveBotId, session.RaceRounds, training, session.GroupId);
                        
                        if (status)
                        {
                            await _botClient.EditMessageText(chatId, callbackQuery.Message.MessageId, $"🚀 تم بدء السباق!\nالجولات: {session.RaceRounds}\nالتدريب: {(training ? "مفعل" : "غير مفعل")}\nالمجموعة: {session.GroupId}");
                        }
                        else
                        {
                             await _botClient.EditMessageText(chatId, callbackQuery.Message.MessageId, "❌ تم تشغيل البوت ولكن فشل بدء وضع السباق.");
                        }
                    }
                }
            }
            else if (data.StartsWith("bot_"))
            {
                try { await _botClient.DeleteMessage(chatId, callbackQuery.Message.MessageId); } catch { }

                string botType = data.Substring(4);
                
                // If in "Add Account" flow
                if (session.State == SessionState.Acc_Add_Type)
                {
                    session.TempBotType = botType;
                    
                    if (botType == "وقت" || botType == "كتابة" || botType == "عكس" || botType == "أحسب")
                    {
                        // Check if TargetGroupId exists in monitor_config.json
                        string? defaultGroupId = null;
                        try
                        {
                            if (System.IO.File.Exists("monitor_config.json"))
                            {
                                var json = System.IO.File.ReadAllText("monitor_config.json");
                                var config = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);
                                defaultGroupId = config?.TargetGroupId;
                            }
                        }
                        catch { }

                        if (!string.IsNullOrEmpty(defaultGroupId))
                        {
                             session.TempGroupId = defaultGroupId;
                             
                             if (botType == "وقت")
                             {
                                 await _botClient.SendMessage(chatId, "🎯 أدخل معرف المستخدم المستهدف (Target User ID) للرد عليه، أو أرسل 0 للرد على الجميع:");
                                 session.State = SessionState.Acc_Add_TargetUser;
                             }
                             else
                             {
                                 session.TempTargetUserId = "0";
                                 await StartNewAccount(chatId, userId, session);
                             }
                        }
                        else
                        {
                            await _botClient.SendMessage(chatId, "📂 أدخل معرف المجموعة (Group ID):");
                            session.State = SessionState.Acc_Add_Group;
                        }
                    }
                    else
                    {
                        // Monitor/Race/etc don't strictly need group ID to start
                        session.TempGroupId = "0";
                        session.TempTargetUserId = "0";
                        await StartNewAccount(chatId, userId, session);
                    }
                }
                else
                {
                    // Fallback for legacy single-bot flow (if accessed somehow)
                    session.SelectedBot = botType;
                    session.GroupId = "0"; 
                    session.TargetUserId = "0";
                    await StartSelectedBot(chatId, userId, session);
                }
            }
            else if (data.StartsWith("stop_id_"))
            {
                string botId = data.Substring(8);
                
                // Get stats before stopping to retrieve credentials
                var stats = _botManager.GetBotStats(botId);
                
                await _botManager.StopBot(botId);
                await _botClient.SendMessage(chatId, "✅ تم إيقاف البوت بنجاح.");
                
                if (stats != null && !string.IsNullOrEmpty(stats.Email) && !string.IsNullOrEmpty(stats.Password))
                {
                    // Restore credentials to temp session to allow easy restart/change
                    session.TempEmail = stats.Email;
                    session.TempPassword = stats.Password;
                    session.State = SessionState.Acc_Add_Type;
                    
                    await _botClient.SendMessage(chatId, $"🔄 يمكنك الآن اختيار بوت جديد للحساب: {stats.Email}");
                    await AskForBotSelection(chatId, session);
                }
                else
                {
                    await ShowAccountsMenu(chatId, userId);
                }
            }
            else if (data == "stop_bot" && !string.IsNullOrEmpty(session.ActiveBotId))
            {
                await StopActiveBot(chatId, userId, session, true);
            }
            else if (data.StartsWith("race_rounds_"))
            {
                session.RaceRounds = int.Parse(data.Substring(12));
                
                var trainingKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("نعم (تدريب)", "race_train_yes"), InlineKeyboardButton.WithCallbackData("لا (بدون)", "race_train_no") }
                });
                
                await _botClient.EditMessageText(chatId, callbackQuery.Message.MessageId, $"🏁 عدد الجولات: {session.RaceRounds}\n🏋️ هل تريد تفعيل التدريب؟", replyMarkup: trainingKeyboard);
            }
            else if (data.StartsWith("race_train_"))
            {
                bool training = data == "race_train_yes";
                
                // Start the bot first if not running, then start race mode
                if (string.IsNullOrEmpty(session.ActiveBotId))
                {
                     // This shouldn't happen if flow is correct, but let's handle it
                     await _botClient.SendMessage(chatId, "❌ خطأ: البوت غير نشط.");
                     return;
                }
                
                var status = await _botManager.StartRaceMode(session.ActiveBotId, session.RaceRounds, training, session.GroupId);
                
                if (status)
                {
                    await _botClient.EditMessageText(chatId, callbackQuery.Message.MessageId, $"🚀 تم بدء السباق!\nالجولات: {session.RaceRounds}\nالتدريب: {(training ? "مفعل" : "غير مفعل")}\nالمجموعة: {session.GroupId}");
                }
                else
                {
                    await _botClient.EditMessageText(chatId, callbackQuery.Message.MessageId, "❌ فشل بدء وضع السباق.");
                }
            }
            else if (data == "status")
            {
                await ShowBotStatus(chatId, session);
            }
            else if (data == "restart")
            {
                await RestartBot(chatId, userId, session);
            }
            else if (data == "join_groups_mode")
            {
                session.IsJoiningMode = true;
                await _botClient.SendMessage(chatId, "📧 أدخل نمط الإيميل (مع علامة # للرقم المتغير).\nمثال: `Sauud#@gmail.com`", parseMode: ParseMode.Markdown);
                session.State = SessionState.WaitingForJoinEmail;
            }
            else if (data == "leave_groups_mode")
            {
                session.IsJoiningMode = false;
                await _botClient.SendMessage(chatId, "📧 أدخل نمط الإيميل للمغادرة (مع علامة # للرقم المتغير).\nمثال: `Sauud#@gmail.com`", parseMode: ParseMode.Markdown);
                session.State = SessionState.WaitingForJoinEmail;
            }
            else if (data == "join_msg_yes")
            {
                session.SendMessageAfterJoin = true;
                await _botClient.SendMessage(chatId, "📝 أدخل نص الرسالة التي تريد إرسالها:");
                session.State = SessionState.WaitingForJoinMessageContent;
            }
            else if (data == "join_msg_no")
            {
                session.SendMessageAfterJoin = false;
                await StartJoinerProcess(chatId, session);
            }
            else if (data == "delete_messages_mode")
            {
                session.Mode = WorkMode.DeleteMessages;
                await _botClient.SendMessage(chatId, "📧 أدخل البريد الإلكتروني للحساب (يجب أن يكون مشرفاً):");
                session.State = SessionState.WaitingForEmail;
            }
            else if (data == "final_close")
            {
                 // تأكيد تسجيل الخروج
                 var confirmKeyboard = new InlineKeyboardMarkup(new[]
                 {
                     new[]
                     {
                         InlineKeyboardButton.WithCallbackData("✅ نعم، أوقف البوتات وسجل خروج", "confirm_final_close"),
                         InlineKeyboardButton.WithCallbackData("❌ إلغاء", "start_menu")
                     }
                 });

                 await _botClient.SendMessage(
                     chatId,
                     "⚠️ **تنبيه**\n\nهذا الخيار سيقوم بـ:\n1. إيقاف جميع بوتات WolfLive النشطة.\n2. تسجيل الخروج من الحساب الحالي (حذف البريد وكلمة المرور من الذاكرة).\n\nسيظل بوت التيليجرام يعمل لاستقبال أوامر جديدة.\n\nهل أنت متأكد؟",
                     parseMode: ParseMode.Markdown,
                     replyMarkup: confirmKeyboard
                 );
            }
            else if (data == "confirm_final_close")
            {
                 await _botClient.SendMessage(chatId, "🔄 جاري إيقاف البوتات وتنظيف الجلسة...");
                 
                 // 1. إيقاف جميع البوتات
                 await _botManager.StopAllBots(userId.ToString());
                 
                 // 2. حذف الجلسة
                 _userSessions.TryRemove(userId, out _);
                 
                 // 3. لا نحذف ملف التوكن ولا نغلق التطبيق
                 
                 await _botClient.SendMessage(chatId, "✅ تم تسجيل الخروج بنجاح. يمكنك البدء من جديد.");
                 
                 // 4. العودة للقائمة الرئيسية
                 // إنشاء جلسة جديدة فارغة للعودة للبداية
                 var newSession = new UserSession { UserId = userId, ChatId = chatId, State = SessionState.Start };
                 _userSessions.TryAdd(userId, newSession);
                 
                 await ShowStartMenu(chatId);
            }
            else if (data == "start_menu")
            {
                await ShowStartMenu(chatId);
            }
            else if (data == "logout_full")
            {
                 await _botClient.SendMessage(chatId, "🔄 جاري إيقاف العمليات وتسجيل الخروج...");
                 
                 // إيقاف جميع بوتات المستخدم
                 await _botManager.StopAllBots(userId.ToString());
                 
                 // حذف الجلسة بالكامل لضمان عدم بقاء بيانات
                 _userSessions.TryRemove(userId, out _);
                 
                 // إنشاء جلسة جديدة نظيفة
                 var newSession = new UserSession { UserId = userId, ChatId = chatId, State = SessionState.Start };
                 _userSessions.TryAdd(userId, newSession);
                 
                 await _botClient.SendMessage(chatId, "✅ تم الإنهاء الكامل وتسجيل الخروج.\nجاهز لاستقبال حساب جديد.");
                 await ShowStartMenu(chatId);
            }
        }
        
        private async Task ShowStartMenu(long chatId)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("➕ إضافة حساب جديد", "add_account"),
                    InlineKeyboardButton.WithCallbackData("📋 الحسابات النشطة", "list_active")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("👥 الانضمام للمجموعات", "join_groups_mode"),
                    InlineKeyboardButton.WithCallbackData("👋 مغادرة المجموعات", "leave_groups_mode")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🗑️ حذف رسائل مستخدم", "delete_messages_mode")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🚪 إغلاق جميع البوتات", "final_close")
                }
            });
            
            await _botClient.SendMessage(
                chatId,
                "👋 مرحباً بك في لوحة تحكم بوتات WolfLive\nيمكنك إضافة حتى 5 حسابات للعمل في آن واحد.\nاختر من القائمة أدناه:",
                replyMarkup: keyboard
            );
        }
        
        private async Task AskForBotSelection(long chatId, UserSession session)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🧮 أحسب", "bot_أحسب"),
                    InlineKeyboardButton.WithCallbackData("📝 كتابة", "bot_كتابة")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔄 عكس", "bot_عكس"),
                    InlineKeyboardButton.WithCallbackData("⏱️ وقت", "bot_وقت")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🦅 مراقبة المعززات", "bot_مراقبة"),
                    InlineKeyboardButton.WithCallbackData("🏎️ سباق", "bot_سباق")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🚪 إغلاق نهائي", "final_close")
                }
            });
            
            await _botClient.SendMessage(
                chatId: chatId,
                text: "🤖 *اختر البوت:*",
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard
            );
        }
        
        private async Task<bool> StartSelectedBot(long chatId, long userId, UserSession session)
        {
            if (string.IsNullOrEmpty(session.SelectedBot)) return false;
            
            // التأكد من إيقاف أي بوت سابق قبل تشغيل الجديد
            if (!string.IsNullOrEmpty(session.ActiveBotId))
            {
                await SendAndScheduleDeletion(chatId, "⚠️ جاري إيقاف البوت السابق...", 3000);
                await _botManager.StopBot(session.ActiveBotId);
                session.ActiveBotId = null;
            }
            
            await SendAndScheduleDeletion(chatId, $"⏳ جاري تشغيل {session.SelectedBot}...", 3000);
            
            var result = await _botManager.StartBot(
                session.SelectedBot,
                session.Email!,
                session.Password!,
                session.GroupId ?? "0",
                session.TargetUserId ?? "0",
                userId.ToString()
            );
            
            if (result.Success)
            {
                session.ActiveBotId = result.BotId;
                session.State = SessionState.BotActive; // Mark as active
                
                // Show control buttons (Stop)
                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithCallbackData("� إيقاف البوت", "stop_bot"),
                    InlineKeyboardButton.WithCallbackData("� الحالة", "status")
                });
                
                await _botClient.SendMessage(
                    chatId: chatId, 
                    text: $"✅ تم تشغيل {result.BotName} بنجاح!\nالمعرف: {result.BotId}",
                    replyMarkup: keyboard
                );
                return true;
            }
            else
            {
                await _botClient.SendMessage(chatId, $"❌ فشل التشغيل: {result.Error}");
                return false;
            }
        }
        
        private async Task StopActiveBot(long chatId, long userId, UserSession session, bool showMenu = true)
        {
            if (string.IsNullOrEmpty(session.ActiveBotId)) return;
            
            var result = await _botManager.StopBot(session.ActiveBotId);
            if (result.Success)
            {
                session.ActiveBotId = null;
                session.State = SessionState.WaitingForBotSelection;
                await SendAndScheduleDeletion(chatId, "⏹️ تم إيقاف البوت.", 3000);
                
                if (showMenu)
                {
                    await AskForBotSelection(chatId, session);
                }
            }
        }
        
        private async Task ShowBotStatus(long chatId, UserSession session)
        {
            if (string.IsNullOrEmpty(session.ActiveBotId)) return;
            
            var status = _botManager.GetBotStatus(session.ActiveBotId);
            if (status != null)
            {
                await _botClient.SendMessage(chatId, $"📊 *{status.BotName}*\nPlay Count: {status.PlayCount}\nRunning: {status.RunningTime}", parseMode: ParseMode.Markdown);
            }
        }
        
        private async Task RestartBot(long chatId, long userId, UserSession session)
        {
            await StopActiveBot(chatId, userId, session, false);
            await StartSelectedBot(chatId, userId, session);
        }
        
        private async Task StartNewAccount(long chatId, long userId, UserSession session)
        {
            await SendAndScheduleDeletion(chatId, $"⏳ جاري تشغيل حساب {session.TempBotType}...", 3000);
            
            var result = await _botManager.StartBot(
                session.TempBotType,
                session.TempEmail!,
                session.TempPassword!,
                session.TempGroupId ?? "0",
                session.TempTargetUserId ?? "0",
                userId.ToString()
            );

            if (result.Success)
            {
                // Clear temp credentials
                session.TempEmail = null;
                session.TempPassword = null;
                session.TempGroupId = null;
                session.TempTargetUserId = null;
                session.TempBotType = null;
                
                // Set as active for context
                session.ActiveBotId = result.BotId;
                session.State = SessionState.Start;
                
                await _botClient.SendMessage(
                    chatId, 
                    $"✅ تم تشغيل الحساب بنجاح!\nالبوت: {result.BotName}\nالمعرف: {result.BotId}"
                );
                
                // Show Accounts Menu
                await ShowAccountsMenu(chatId, userId);
            }
            else
            {
                await _botClient.SendMessage(chatId, $"❌ فشل التشغيل: {result.Error}\nحاول مرة أخرى.");
                session.State = SessionState.Start;
                await ShowStartMenu(chatId);
            }
        }

        private async Task ShowAccountsMenu(long chatId, long userId = 0)
        {
             if (userId == 0) userId = _userSessions.FirstOrDefault(s => s.Value.ChatId == chatId).Key;
             
             var bots = _botManager.GetUserBots(userId.ToString());
             var buttons = new List<InlineKeyboardButton[]>();
             
             foreach (var bot in bots)
             {
                 buttons.Add(new[] 
                 { 
                     InlineKeyboardButton.WithCallbackData($"🛑 إيقاف {bot.BotName} ({bot.BotId.Substring(bot.BotId.Length-4)})", $"stop_id_{bot.BotId}")
                 });
             }
             
             buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("➕ إضافة حساب جديد", "add_account") });
             buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("🔙 القائمة الرئيسية", "start_menu") });
             
             var keyboard = new InlineKeyboardMarkup(buttons);
             
             string message = bots.Count > 0 ? $"📋 لديك {bots.Count} حسابات نشطة:" : "📋 ليس لديك حسابات نشطة حالياً.";
             
             await _botClient.SendMessage(chatId, message, replyMarkup: keyboard);
        }

        private async void HandleBotEvent(object? sender, BotManager.BotEvent e)
        {
            foreach (var session in _userSessions.Values)
            {
                if (session.ActiveBotId == e.BotId)
                {
                    try { await _botClient.SendMessage(session.ChatId, $"📢 {e.Message}"); } catch { }
                }
            }
        }
        
        private async void HandleNotification(object? sender, BotManager.NotificationEvent e)
        {
            // First try to match by User ID (Support multi-account)
            if (long.TryParse(e.TelegramUserId, out long userId) && _userSessions.TryGetValue(userId, out var userSession))
            {
                 try 
                 { 
                     if (e.Message.Contains("تم إيقاف البوت"))
                     {
                         await SendAndScheduleDeletion(userSession.ChatId, e.Message, 3000);
                     }
                     else
                     {
                         await _botClient.SendMessage(userSession.ChatId, e.Message); 
                     }
                     return;
                 } 
                 catch { }
            }

            // Fallback: match by ActiveBotId (Legacy)
            foreach (var session in _userSessions.Values)
            {
                if (session.ActiveBotId == e.BotId)
                {
                    try 
                    { 
                        // إذا كانت رسالة إيقاف ناجح، نحذفها تلقائياً بعد فترة قصيرة
                        if (e.Message.Contains("تم إيقاف البوت"))
                        {
                            await SendAndScheduleDeletion(session.ChatId, e.Message, 3000);
                        }
                        else
                        {
                            await _botClient.SendMessage(session.ChatId, e.Message); 
                        }
                    } 
                    catch { }
                }
            }
        }
        
        // Helper method to send a message and delete it after a delay
        private async Task SendAndScheduleDeletion(long chatId, string text, int delayMs)
        {
            try
            {
                var msg = await _botClient.SendMessage(chatId, text);
                _ = Task.Run(async () => 
                {
                    try
                    {
                        await Task.Delay(delayMs);
                        await _botClient.DeleteMessage(chatId, msg.MessageId);
                    }
                    catch
                    {
                        // Ignore deletion errors (e.g. message already deleted)
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending temporary message: {ex.Message}");
            }
        }

         private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"❌ Telegram Error: {exception.Message}");
            return Task.CompletedTask;
        }
        
        public void Dispose()
        {
            if (!_isDisposed)
            {
                _cts.Cancel();
                _botManager.Dispose();
                _isDisposed = true;
            }
        }
        
        private class UserSession
        {
            public long UserId { get; set; }
            public long ChatId { get; set; }
            public SessionState State { get; set; }
            public string? Email { get; set; }
            public string? Password { get; set; }
            public string? SelectedBot { get; set; }
            public string? GroupId { get; set; }
            public string? TargetUserId { get; set; }
            public string? ActiveBotId { get; set; }
            public string? TempGroupId { get; set; }
            public string? TempTargetUserId { get; set; }
            public string? TempEmail { get; set; }
            public string? TempPassword { get; set; }
            public string? TempBotType { get; set; }
            public int RaceRounds { get; set; }
            
            // Joiner Fields
            public string? JoinEmailPattern { get; set; }
            public string? JoinPassword { get; set; }
            public int JoinStart { get; set; }
            public int JoinEnd { get; set; }
            public List<string>? JoinGroups { get; set; }
            public bool IsJoiningMode { get; set; }
            public bool SendMessageAfterJoin { get; set; }
            public string? JoinMessageContent { get; set; }
            public int JoinMessageCount { get; set; }
            
            // Mode
            public WorkMode Mode { get; set; }
        }
        
        private enum WorkMode
        {
            Normal,
            DeleteMessages
        }
        
        private enum SessionState
        {
            Start,
            WaitingForEmail,
            WaitingForPassword,
            WaitingForBotSelection,
            BotActive,
            
            // Joiner States
            WaitingForJoinEmail,
            WaitingForJoinPassword,
            WaitingForJoinStart,
            WaitingForJoinEnd,
            WaitingForJoinGroups,
            WaitingForJoinMessageOption,
            WaitingForJoinMessageContent,
            WaitingForJoinMessageCount,
            
            // Account Manager States
            Acc_Add_Email,
            Acc_Add_Pass,
            Acc_Add_Group,
            Acc_Add_TargetUser,
            Acc_Add_Type,

            // Admin States
            WaitingForDeleteGroupId,
            WaitingForDeleteUserId,
            WaitingForDeleteDelay
        }
    }
}
