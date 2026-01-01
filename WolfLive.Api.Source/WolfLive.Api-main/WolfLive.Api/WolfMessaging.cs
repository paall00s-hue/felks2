using System.Linq;
using System.Threading.Tasks;

namespace WolfLive.Api
{
	using Delegates;
	using Models;
	using System;
	using System.Collections.Generic;

	public interface IWolfMessaging
	{
		event MessageCarrier OnMessage;
		event MessageCarrier<User> OnPrivateMessage;
		event MessageCarrier<GroupUser> OnGroupMessage;

		Task Initialize();

		Task<bool> GroupMessageSubscribe(params string[] groupIds);

		Task<bool> PrivateMessageSubscribe();

		Task<Message> NextMessge(Func<Message, bool> predicate);
	}

	public class WolfMessaging : IWolfMessaging
	{
		private const string CMD_GROUP_MSG_SUB = "message group subscribe";
		private const string CMD_PRIVATE_MSG_SUB = "message private subscribe";
		private const string CMD_MESG_SEND = "message send";

		private readonly IWolfClient _client;
		private readonly List<MessageTask> _messageTasks = new List<MessageTask>();

		public event MessageCarrier OnMessage = delegate { };
		public event MessageCarrier<User> OnPrivateMessage = delegate { };
		public event MessageCarrier<GroupUser> OnGroupMessage = delegate { };

		public WolfMessaging(IWolfClient client)
		{
			_client = client;
		}

		public async Task Initialize()
		{
			_client.On<WolfMessage>(CMD_MESG_SEND, Client_OnMessage);

			await GroupMessageSubscribe();
			await PrivateMessageSubscribe();
		}

		private async void Client_OnMessage(WolfMessage wolfMessage)
		{
			try
			{
				var msg = new Message(wolfMessage);
				OnMessage(_client, msg);

				ProcessTasks(msg);

				if (msg.IsGroup)
				{
					try
					{
						var gu = await _client.GetGroupUser(msg.GroupId, msg.UserId);
						OnGroupMessage(_client, msg, gu);
					}
					catch (Exception ex)
					{
						// Reduced logging verbosity to avoid console flooding during rate limits
						// Console.WriteLine($"[WolfLive.Api] Warning: Failed to fetch group user info (proceeding with null): {ex.Message}");
						// Fire event with null user to allow bot logic to continue
						OnGroupMessage(_client, msg, null); 
					}
					return;
				}

				try
				{
					var u = await _client.GetUser(msg.UserId);
					OnPrivateMessage(_client, msg, u);
				}
				catch (Exception ex)
				{
					// Console.WriteLine($"[WolfLive.Api] Warning: Failed to fetch private user info (proceeding with null): {ex.Message}");
					OnPrivateMessage(_client, msg, null);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[WolfLive.Api] Critical Error in Client_OnMessage: {ex.Message}");
			}
		}

		private void ProcessTasks(Message message)
		{
			foreach(var task in _messageTasks.ToArray())
			{
				if (!task.Predicate(message))
					continue;

				task.TaskSource.SetResult(message);
				_messageTasks.Remove(task);
				return;
			}
		}

		public Task<bool> GroupMessageSubscribe(params string[] groupIds)
		{
			if (groupIds == null || groupIds.Length <= 0)
				return _client.Emit(new Packet(CMD_GROUP_MSG_SUB, new { }, 4));

			if (groupIds.Length > 1)
				return _client.Emit(new Packet(CMD_GROUP_MSG_SUB, new
				{
					idList = groupIds.Select(t => int.Parse(t)).ToArray()
				}, 4));

			return _client.Emit(new Packet(CMD_GROUP_MSG_SUB, new
			{
				id = int.Parse(groupIds[0])
			}, 4));
		}

		public Task<bool> PrivateMessageSubscribe()
		{
			return _client.Emit(new Packet(CMD_PRIVATE_MSG_SUB, new { }, 4));
		}

		public Task<Message> NextMessge(Func<Message, bool> predicate)
		{
			var tsc = new TaskCompletionSource<Message>();
			_messageTasks.Add(new MessageTask
			{
				Predicate = predicate,
				TaskSource = tsc
			});

			return tsc.Task;
		}
	}
}
