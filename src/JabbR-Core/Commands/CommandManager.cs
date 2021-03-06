﻿using System;
using System.Linq;
using System.Reflection;
using JabbR_Core.Services;
using System.Collections.Generic;
using JabbR_Core.Data.Repositories;
using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;
//using Microsoft.Extensions.DependencyModel;
using System.IO;

namespace JabbR_Core.Commands
{
    public class CommandManager
    {
        private readonly string _clientId;
        private readonly string _userAgent;
        private readonly string _userId;
        private readonly string _roomName;
        private readonly INotificationService _notificationService;
        private readonly IChatService _chatService;
        private readonly ICache _cache;
        private readonly IJabbrRepository _repository;

        private static Dictionary<string, ICommand> _commandCache;
        private static readonly Lazy<IList<ICommand>> _commands = new Lazy<IList<ICommand>>(GetCommands);

        public CommandManager(string clientId,
                              string userId,
                              string roomName,
                              IChatService service,
                              IJabbrRepository repository,
                              ICache cache,
                              INotificationService notificationService)
            : this(clientId, null, userId, roomName, service, repository, cache, notificationService)
        {
        }

        public CommandManager(string clientId,
                              string userAgent,
                              string userId,
                              string roomName,
                              IChatService service,
                              IJabbrRepository repository,
                              ICache cache,
                              INotificationService notificationService)
        {
            _clientId = clientId;
            _userAgent = userAgent;
            _userId = userId;
            _roomName = roomName;
            _chatService = service;
            _repository = repository;
            _cache = cache;
            _notificationService = notificationService;
        }

        public string ParseCommand(string commandString, out string[] args)
        {
            var parts = commandString.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                args = new string[0];
                return null;
            }

            args = parts.Skip(1).ToArray();
            return parts[0];
        }
        public bool TryHandleCommand(string command)
        {
            command = command.Trim();
            if (!Regex.IsMatch(command, @"^\/[A-Za-z0-9?]+?"))
            {
                return false;
            }

            string[] args;
            var commandName = ParseCommand(command, out args);
            return TryHandleCommand(commandName, args);
        }

        public bool TryHandleCommand(string commandName, string[] args)
        {
            if (String.IsNullOrEmpty(commandName))
            {
                return false;
            }

            commandName = commandName.Trim();
            if (commandName.StartsWith("/"))
            {
                return false;
            }

            var context = new CommandContext
            {
                Cache = _cache,
                NotificationService = _notificationService,
                Repository = _repository,
                Service = _chatService
            };

            var callerContext = new CallerContext
            {
                ClientId = _clientId,
                UserId = _userId,
                UserAgent = _userAgent,
                RoomName = _roomName,
            };

            ICommand command;
            try
            {
                MatchCommand(commandName, out command);
            }
            catch (CommandNotFoundException)
            {
                throw new HubException(String.Format(LanguageResources.CommandNotFound, commandName));
            }
            catch (CommandAmbiguityException e)
            {
                throw new HubException(String.Format(LanguageResources.CommandAmbiguous, commandName, String.Join(", ", e.Ambiguities)));
            }

            command.Execute(context, callerContext, args);

            return true;
        }

        public void MatchCommand(string commandName, out ICommand command)
        {
            if (_commandCache == null)
            {
               var commands = from c in _commands.Value
                               let commandAttribute = c.GetType().GetTypeInfo().GetCustomAttributes<CommandAttribute>()
                                                       .FirstOrDefault()
                               where commandAttribute != null
                               select new
                               {
                                   Name = commandAttribute.CommandName,
                                   
                                   Command = c
                               };

                _commandCache = commands.ToDictionary(c => c.Name,
                                                      c => c.Command,
                                                      StringComparer.OrdinalIgnoreCase);
            }

            IList<string> candidates = null;
            

            var exactMatches = _commandCache.Keys.Where(comm => comm.Equals(commandName, StringComparison.OrdinalIgnoreCase))
                                                 .ToList();

            if (exactMatches.Count == 1)
            {
                candidates = exactMatches;
            }
            else
            {
                candidates = _commandCache.Keys.Where(comm => comm.StartsWith(commandName, StringComparison.OrdinalIgnoreCase))
                                               .ToList();
            }

            switch (candidates.Count)
            {
                case 1:
                    _commandCache.TryGetValue(candidates[0], out command);
                    commandName = candidates[0];
                    break;
                case 0:
                    throw new CommandNotFoundException();
                default:
                    throw new CommandAmbiguityException(candidates);
            }
        }

        public static IList<ICommand> GetCommands()
        {

           IEnumerable<ICommand> commandsList = typeof(CommandManager).GetTypeInfo().Assembly.GetExportedTypes()
                .Where(o => o.GetTypeInfo().IsSubclassOf(typeof(UserCommand)))
                .Select(t => (ICommand)Activator.CreateInstance(t));
            return commandsList.ToList();

        }

        public static IEnumerable<CommandMetaData> GetCommandsMetaData()
        {
            var commands = from c in _commands.Value
                           let commandAttribute = c.GetType().GetTypeInfo()
                                                   .GetCustomAttributes(true)
                                                   .OfType<CommandAttribute>()
                                                   .FirstOrDefault()
                           where commandAttribute != null
                           select new CommandMetaData
                           {
                               Name = commandAttribute.CommandName,
                               //Description = commandAttribute.Description,
                               Arguments = commandAttribute.Arguments,
                               Group = commandAttribute.Group,
                               ConfirmMessage = commandAttribute.ConfirmMessage
                           };
            return commands;
        }
    }
}