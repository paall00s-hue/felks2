using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using WolfLive.Api;
using WolfLive.Api.Models;

namespace TelegramBotController
{
    public class GroupJoiner
    {
        private readonly ITelegramBotClient _botClient;
        private readonly long _chatId;

        public GroupJoiner(ITelegramBotClient botClient, long chatId)
        {
            _botClient = botClient;
            _chatId = chatId;
        }

        public async Task ProcessAccountsAsync(string emailPattern, string password, int startNum, int endNum, List<string> groupIds, bool isJoining, string messageContent = null, int messageCount = 0)
        {
            string operationName = isJoining ? "الانضمام إلى" : "مغادرة";
            var statusMessage = await _botClient.SendMessage(_chatId, $"🚀 بدء عملية {operationName} المجموعات...\nمن الحساب {startNum} إلى {endNum}\n👤 الوضع المتوازي: حسابين في آن واحد");

            int total = endNum - startNum + 1;
            int processedCount = 0;
            int successCount = 0;
            int failCount = 0;

            var successList = new ConcurrentBag<string>();
            var failList = new ConcurrentBag<string>();

            // Create range
            var range = Enumerable.Range(startNum, total);

            // Parallel Options
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 2 };

            await Parallel.ForEachAsync(range, parallelOptions, async (i, ct) =>
            {
                Interlocked.Increment(ref processedCount);
                string currentEmail = emailPattern.Replace("#", i.ToString());

                // Update status message (fire and forget to avoid slowing down)
                _ = Task.Run(async () => 
                {
                    try
                    {
                        await _botClient.EditMessageText(
                            _chatId,
                            statusMessage.MessageId,
                            $"🔄 جاري المعالجة ({processedCount}/{total})\n📧 جاري العمل على: {currentEmail}\n✅ نجح: {successCount}\n❌ فشل: {failCount}"
                        );
                    }
                    catch { }
                });

                var client = new WolfClient();
                try
                {
                    // 1. Login
                    bool loginResult = await client.Login(currentEmail, password);

                    if (loginResult)
                    {
                        // 2. Process Groups
                        foreach (var groupId in groupIds)
                        {
                            try
                            {
                                if (isJoining)
                                {
                                    // Join
                                    await client.JoinGroup(groupId);
                                    
                                    // Send Message logic
                                    if (!string.IsNullOrEmpty(messageContent) && messageCount > 0)
                                    {
                                        // Wait a bit for server to register join (Reduced for speed)
                                        await Task.Delay(1500); 

                                        for (int m = 0; m < messageCount; m++)
                                        {
                                            try
                                            {
                                                await client.GroupMessage(groupId, messageContent);
                                                // Console.WriteLine($"💬 Msg {m+1}/{messageCount} | {currentEmail} -> {groupId}");
                                                // Reduced delay between messages for speed
                                                if (m < messageCount - 1) await Task.Delay(500); 
                                            }
                                            catch (Exception msgEx)
                                            {
                                                // Console.WriteLine($"⚠️ Msg Error {currentEmail}: {msgEx.Message}");
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    await client.LeaveGroup(groupId);
                                }
                            }
                            catch (Exception gEx)
                            {
                                 // Console.WriteLine($"⚠️ Group error {groupId} for {currentEmail}: {gEx.Message}");
                            }
                            // Reduced delay between groups
                            await Task.Delay(500);
                        }

                        successList.Add(currentEmail);
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        failList.Add($"{currentEmail} (Login Failed)");
                        Interlocked.Increment(ref failCount);
                        // Console.WriteLine($"❌ Login failed for {currentEmail}");
                    }
                }
                catch (Exception ex)
                {
                    failList.Add($"{currentEmail} (Error: {ex.Message})");
                    Interlocked.Increment(ref failCount);
                    // Console.WriteLine($"❌ Error for {currentEmail}: {ex.Message}");
                }
                finally
                {
                    // 3. Disconnect
                    try { await client.Connection.DisconnectAsync(); } catch { }
                }
            });

            // Final Message
            await _botClient.SendMessage(_chatId, $"✅ **اكتملت المهمة!**\n\n📊 الإحصائيات:\n✅ نجح: {successCount}\n❌ فشل: {failCount}\n\nالعملية: {operationName}\n\n📄 جاري إرسال التقرير...");

            // Generate Report File
            await SendReport(operationName, total, successCount, failCount, successList, failList);
        }

        private async Task SendReport(string operationName, int total, int success, int fail, ConcurrentBag<string> successList, ConcurrentBag<string> failList)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"=== تقرير عملية {operationName} المجموعات (سريع) ===");
                sb.AppendLine($"التاريخ: {DateTime.Now}");
                sb.AppendLine($"العدد الكلي: {total}");
                sb.AppendLine($"الناجحة: {success}");
                sb.AppendLine($"الفاشلة: {fail}");
                sb.AppendLine("--------------------------------------------------");

                sb.AppendLine("\n[الحسابات الناجحة]");
                if (successList.Count > 0)
                {
                    foreach (var item in successList) sb.AppendLine($"✅ {item}");
                }
                else
                {
                    sb.AppendLine("لا يوجد حسابات ناجحة.");
                }

                sb.AppendLine("\n--------------------------------------------------");
                sb.AppendLine("\n[الحسابات الفاشلة]");
                if (failList.Count > 0)
                {
                    foreach (var item in failList) sb.AppendLine($"❌ {item}");
                }
                else
                {
                    sb.AppendLine("لا يوجد حسابات فاشلة.");
                }

                string reportFileName = $"report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                await System.IO.File.WriteAllTextAsync(reportFileName, sb.ToString());

                using (var stream = System.IO.File.OpenRead(reportFileName))
                {
                    await _botClient.SendDocument(
                        chatId: _chatId,
                        document: InputFile.FromStream(stream, reportFileName),
                        caption: "📄 تقرير تفصيلي بالعملية"
                    );
                }

                System.IO.File.Delete(reportFileName);
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(_chatId, $"⚠️ فشل في إرسال ملف التقرير: {ex.Message}");
            }
        }
    }
}
