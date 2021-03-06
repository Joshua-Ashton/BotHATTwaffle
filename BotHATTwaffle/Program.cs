﻿using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using BotHATTwaffle.Modules;
using BotHATTwaffle.Objects.Downloader;

using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Discord.WebSocket;

using Microsoft.Extensions.DependencyInjection;

namespace BotHATTwaffle
{
	public class Program
	{
		public const char COMMAND_PREFIX = '>';

		private CommandService _commands;
		private IServiceProvider _services;
		private DataServices _dataServices;
		private TimerService _timerService;
		private Eavesdropping _eavesdropping;

		internal static DiscordSocketClient Client { get; private set; }

		/// <summary>
		/// The entry point of the program. Creates an asyncronous environment to run the bot.
		/// </summary>
		private static void Main() => new Program().MainAsync().GetAwaiter().GetResult();

		/// <summary>
		/// Logs a message to the standard output stream.
		/// </summary>
		/// <param name="message">The message to log.</param>
		/// <returns>No object or value is returned by this method when it completes.</returns>
		private static Task LogEventHandler(LogMessage message)
		{
			Console.WriteLine(message);

			return Task.CompletedTask;
		}

		/// <summary>
		/// Initialises the bot and its required services and then subscribes to its events.
		/// </summary>
		/// <returns>No object or value is returned by this method when it completes.</returns>
		public async Task MainAsync()
		{
			Console.Title = "BotHATTwaffle";

			// Concurrently writes the standard output stream to a log file.
			const string LOG_PATH = "c:/BotHATTwafflelogs/";
			string logName = $"{DateTime.Now:hh_mmtt-MM_dd_yyyy}.log";
			Directory.CreateDirectory(LOG_PATH);
			var _ = new ConsoleCopy(LOG_PATH + logName);

			// Dependency injection. All objects use constructor injection.
			Client = new DiscordSocketClient();
			_commands = new CommandService();
			_services = new ServiceCollection()
				.AddSingleton(Client)
				.AddSingleton(_commands)
				.AddSingleton<TimerService>()
				.AddSingleton<UtilityService>()
				.AddSingleton<ModerationServices>()
				.AddSingleton<LevelTesting>()
				.AddSingleton<ToolsService>()
				.AddSingleton<Eavesdropping>()
				.AddSingleton<DataServices>()
				.AddSingleton<Random>()
				.AddSingleton<DownloaderService>()
				.AddSingleton(s => new InteractiveService(Client, TimeSpan.FromSeconds(120)))
				.BuildServiceProvider();

			// Retrieves services that this class uses.
			_dataServices = _services.GetRequiredService<DataServices>();
			_timerService = _services.GetRequiredService<TimerService>();
			_eavesdropping = _services.GetRequiredService<Eavesdropping>();

			// Retrieves the bot's token from the config file; effectively exits the program if botToken can't be retrieved.
			// This is the only setting that has to be retreived this way so it can start up properly.
			// Once the guild becomes ready the rest of the settings are fully loaded.
			if (!_dataServices.Config.TryGetValue("botToken", out string botToken)) return;

			// Event subscriptions.
			Client.Log += LogEventHandler;
			Client.UserJoined += _eavesdropping.UserJoin; // When a user joins the server.
			Client.GuildAvailable += GuildAvailableEventHandler; // When a guild is available.

			await InstallCommandsAsync();

			await Client.LoginAsync(TokenType.Bot, botToken);
			await Client.StartAsync();

			// Subscribes to connect/disconnect after logging in because they would otherwise be raised before needed.
			Client.Disconnected += DisconnectedEventHandler;
			Client.Connected += ConnectedEventHandler;

			await Task.Delay(Timeout.Infinite); // Blocks this task until the program is closed.
		}

		/// <summary>
		/// Subscribes to <see cref="DiscordSocketClient.MessageReceived"/> to enable listening for commands and loads all
		/// <see cref="ModuleBase"/>s (which contain commands) in this assembly.
		/// </summary>
		/// <returns>No object or value is returned by this method when it completes.</returns>
		private async Task InstallCommandsAsync()
		{
			// TODO: Event not yet implemented in Discord.Net 1.0.
			// _commands.CommandExecuted += CommandExecutedEventHandler;
			Client.MessageReceived += MessageReceivedEventHandler;

			await _commands.AddModulesAsync(Assembly.GetEntryAssembly());
		}

		/// <summary>
		/// Processes a command.
		/// </summary>
		/// <remarks>
		/// Creates a <see cref="SocketCommandContext"/> from the <see cref="message"/>, executes the command, and finally
		/// simulates raising <see cref="CommandService.CommandExecuted"/> by calling <see cref="CommandExecutedEventHandler"/>
		/// to handle the result of the command's execution.
		/// </remarks>
		/// <param name="message">The message which contains the command.</param>
		/// <param name="argPos">The index of the <see cref="message"/>'s contents at which the command begins.</param>
		/// <returns>No object or value is returned by this method when it completes.</returns>
		private async Task ProcessCommandAsync(SocketUserMessage message, int argPos)
		{
			var context = new SocketCommandContext(Client, message);

			// Executes the command; this is not the return value of the command.
			// Rather, it is an object that contains information about the outcome of the execution.
			IResult result = await _commands.ExecuteAsync(context, argPos, _services);

			await CommandExecutedEventHandler(context, result);
		}

		/// <summary>
		/// Raised when the client connects to Discord.
		/// <para>
		/// Restarts the <see cref="TimerService"/> and logs that the client has conncted.
		/// </para>
		/// </summary>
		/// <returns>No object or value is returned by this method when it completes.</returns>
		private Task ConnectedEventHandler()
		{
			Console.WriteLine($"\n{DateTime.Now}\nCLIENT CONNECTED\n");
			_timerService.Restart();

			return Task.CompletedTask;
		}

		/// <summary>
		/// Raised when the client disconnects from Discord.
		/// <para>
		/// Stops the <see cref="TimerService"/> and logs that the client has disconnected along with exception information.
		/// </para>
		/// </summary>
		/// <param name="e">The exception thrown on disconnect.</param>
		/// <returns>No object or value is returned by this method when it completes.</returns>
		private Task DisconnectedEventHandler(Exception e)
		{
			Console.WriteLine(
				$"\n{DateTime.Now}\nCLIENT DISCONNECTED\nMessage: {e.Message}\n---STACK TRACE---\n{e.StackTrace}\n\n");
			_timerService.Stop();

			return Task.CompletedTask;
		}

		/// <summary>
		/// Raised when the guild (server) becomes avaiable.
		/// <para>
		/// Calls for the configuration to be read from the file.
		/// </para>
		/// </summary>
		/// <remarks>
		/// The configuration is called to be read here because some configuration fields are parsed into objects. Some of this
		/// parsing requires the guild to be available so that names and roles can be retrieved.
		/// Because this bot is intended to be used on only one server, this should only get raised once.
		/// </remarks>
		/// <param name="guild">The guild that has become available.</param>
		/// <returns>No object or value is returned by this method when it completes.</returns>
		private Task GuildAvailableEventHandler(SocketGuild guild)
		{
			_dataServices.ReloadSettings();

			return Task.CompletedTask;
		}

		/// <summary>
		/// Raised when a message is received.
		/// <para>
		/// Listens to all messages with <see cref="Eavesdropping"/> and determines if messages are commands.
		/// </para>
		/// </summary>
		/// <param name="messageParam">The message recieved.</param>
		/// <returns>No object or value is returned by this method when it completes.</returns>
		private async Task MessageReceivedEventHandler(SocketMessage messageParam)
		{
			// Ignores system messages.
			if (!(messageParam is SocketUserMessage message))
				return;

			var argPos = 0; // Integer used to track where the prefix ends and the command begins.

			// Determines if the message is a command based on if it starts with the prefix character or a mention prefix.
			if (message.HasCharPrefix(COMMAND_PREFIX, ref argPos) || message.HasMentionPrefix(Client.CurrentUser, ref argPos))
				await ProcessCommandAsync(message, argPos);

			Task _ = _eavesdropping.Listen(messageParam); // Fired and forgotten.
		}

		/// <summary>
		/// Raised when a command is executed.
		/// <para>
		/// Handles failed executions of commands. The failure is logged and a message may be sent indicating failure.
		/// </para>
		/// </summary>
		/// <remarks>
		/// It is intended to eventully subscribe to the <see cref="CommandService.CommandExecuted"/> event with this handler.
		/// However, it is not yet implemented on Discord.Net 1.0 Therefore, it is raised manually in
		/// <see cref="ProcessCommandAsync"/>. Meanwhile, the <see cref="CommandInfo"/> parameter is excluded from the signature;
		/// no practical way of obtaining it currently exists.
		/// </remarks>
		/// <param name="context">The context in which the command was executed.</param>
		/// <param name="result">The result of the command's execution.</param>
		/// <returns>No object or value is returned by this method when it completes.</returns>
		private async Task CommandExecutedEventHandler(ICommandContext context, IResult result)
		{
			if (result.Error is null) return; // Ignores successful executions.

			Console.ForegroundColor = ConsoleColor.Red;
			var alert = false; // Set to true if the log message should mention the appropriate users to alert them of the error.

			switch (result.ErrorReason)
			{
				case "Unknown command.":
					break;
				case "The input text has too many parameters.":
					await context.Channel.SendMessageAsync(
						$"You provided too many parameters! Please consult `{COMMAND_PREFIX}help " +
						$"{context.Message.Content.Substring(1, context.Message.Content.IndexOf(" ") - 1)}`");

					break;
				case "The input text has too few parameters.":
					await context.Channel.SendMessageAsync(
						$"You provided too few parameters! Please consult `{COMMAND_PREFIX}help " +
						$"{context.Message.Content.Substring(1)}`");

					break;
				default:
					alert = true;
					await context.Channel.SendMessageAsync("Something bad happened! I logged the error for TopHATTwaffle.");

					break;
			}

			await _dataServices.ChannelLog(
				$"An error occurred!\nInvoking command: {context.Message}",
				$"Invoking User: {context.Message.Author}\nChannel: {context.Message.Channel}\nError Reason: {result.ErrorReason}",
				alert);

			Console.ResetColor();
		}
	}
}
