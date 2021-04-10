using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Utilities.Linguistic;
using System;
using System.Threading;
using System.Threading.Tasks;
using Phoenix.Bot.Utilities.Dialogs;
using Microsoft.Extensions.Configuration;
using Phoenix.Bot.Utilities.State;
using Phoenix.DataHandle.Repositories;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Main;
using Bot.Builder.Community.Storage.EntityFramework;
using System.Linq;
using System.Collections.Generic;

namespace Phoenix.Bot.Bots
{
    public class DialogBot<T> : ActivityHandler where T : Dialog
    {
        private readonly IConfiguration configuration;
        private readonly BotState conversationState;
        private readonly BotState userState;
        private readonly AspNetUserRepository userRepository;
        private readonly BotDataContext botDataContext;

        protected readonly Dialog Dialog;

        public DialogBot(IConfiguration configuration, ConversationState conversationState, UserState userState, 
            PhoenixContext phoenixContext, BotDataContext botDataContext,
            T dialog)
        {
            this.configuration = configuration;
            this.conversationState = conversationState;
            this.userState = userState;

            this.userRepository = new AspNetUserRepository(phoenixContext);
            this.botDataContext = botDataContext;

            this.Dialog = dialog;
        }

        public override async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default)
        {
            if (turnContext.Activity.Text == null)
                turnContext.Activity.Text = string.Empty;
            await base.OnTurnAsync(turnContext, cancellationToken);

            try
            {
                // Save any state changes that might have occured during the turn.
                await conversationState.SaveChangesAsync(turnContext, false, cancellationToken);
                await userState.SaveChangesAsync(turnContext, false, cancellationToken);
            }
            catch (Exception) { }
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            string mess = turnContext.Activity.Text;
            var cmd = Command.NoCommand;

            if (CommandHandle.IsCommand(mess))
                CommandHandle.TryGetCommand(mess, out cmd);
            else
                CommandHandle.TryInferCommand(mess, out cmd);

            var conversationDataAccessor = conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
            var mainStateAccessor = conversationState.CreateProperty<DialogState>(nameof(DialogState));

            if (cmd > 0)
            {
                // Reset the dialog state
                try
                {
                    await mainStateAccessor.DeleteAsync(turnContext, cancellationToken);
                    await conversationDataAccessor.DeleteAsync(turnContext, cancellationToken);
                }
                catch (Exception)
                {
                    cmd = Command.Reset;
                }

                switch (cmd)
                {
                    case Command.GetStarted:
                    case Command.Greeting:
                        await turnContext.SendActivityAsync(MessageFactory.ContentUrl(
                            url: await DialogsHelper.CreateGifUrlAsync("g", "hi", 10, new Random().Next(10), configuration["GiphyKey"]),
                            contentType: "image/gif",
                            text: "Γεια σου!! 😊"));
                        break;
                    case Command.Reset:
                        await conversationState.DeleteAsync(turnContext, cancellationToken);
                        break;
                    case Command.Logout:
                        var user = userRepository.FindUserFromLogin(turnContext.Activity.ChannelId.ToLoginProvider(), turnContext.Activity.From.Id);
                        var deactivatedLogins = userRepository.Logout(user.Id, logoutAffiliatedUsers: true);

                        // Delete conversation data of user and all their affiliated users
                        List<BotDataEntity> botDataToRemove = new();
                        var botConversationData = botDataContext.BotDataEntity.
                            Where(bd => bd.RealId.Contains("conversations"));

                        foreach (var login in deactivatedLogins)
                            botDataToRemove.AddRange(botConversationData.
                                Where(bd => bd.RealId.Contains(login.ProviderDisplayName) && bd.RealId.Contains(login.ProviderKey)));

                        botDataContext.RemoveRange(botDataToRemove);
                        botDataContext.SaveChanges();

                        await conversationState.ClearStateAsync(turnContext, cancellationToken);
                        await userState.ClearStateAsync(turnContext, cancellationToken);

                        break;
                }
            }

            
            var conversationData = await conversationDataAccessor.GetAsync(turnContext, null, cancellationToken);
            conversationData.Command = cmd;
            await conversationDataAccessor.SetAsync(turnContext, conversationData, cancellationToken);

            await Dialog.RunAsync(turnContext, conversationState.CreateProperty<DialogState>(nameof(DialogState)), cancellationToken);
        }
    }
}
